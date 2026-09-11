using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

// تسجيل الدفعات الفعلية - يسمح بتسديد نفس الفاتورة على أكثر من دفعة عبر زيارات متعددة
public class PaymentRepository
{
    private readonly DatabaseHelper _db;
    private readonly DoctorCommissionService _commissionService;

    public PaymentRepository(DatabaseHelper db)
    {
        _db = db;
        _commissionService = new DoctorCommissionService(db);
    }

    // نقطة التسجيل الوحيدة لأي دفعة فعلية تُحصَّل في النظام - لذلك هي المكان الصحيح لتفعيل
    // نظام تقسيم إيرادات الأطباء تلقائياً (بدون أي تدخل يدوي من الطبيب الرئيسي)
    public void AddPayment(int sessionId, decimal amount, int receivedByUserId, string? notes = null)
    {
        const string sql = @"
            INSERT INTO Payments (SessionID, Amount, ReceivedByUserID, Notes)
            VALUES (@SessionID, @Amount, @ReceivedByUserID, @Notes)";

        var paymentId = _db.ExecuteInsertAndGetId(sql,
            new SqlParameter("@SessionID", sessionId),
            new SqlParameter("@Amount", amount),
            new SqlParameter("@ReceivedByUserID", receivedByUserId),
            new SqlParameter("@Notes", (object?)notes ?? DBNull.Value));

        // Payments هو المصدر الفعلي للدفعات، بينما MedicalSessions.PaidAmount يُستخدم
        // في حساب Outstanding Balances. يجب مزامنة PaidAmount بعد كل دفعة حتى يختفي
        // الدين فوراً من Finance ولا يمكن تسجيل نفس الرصيد مرة ثانية.
        RecalculateSessionPaidAmount(sessionId);

        // إن كان الطبيب صاحب هذه الجلسة ليس الطبيب الرئيسي، يُنشأ تلقائياً مصروف عمولة بنسبته
        // (افتراضياً 50%) يُخصَم من صافي ربح العيادة - المبلغ الكامل يبقى محسوباً في الواردات كما هو.
        try
        {
            _commissionService.RecordCommissionForPayment(sessionId, paymentId, amount);
        }
        catch
        {
            // لا نفشل عملية تحصيل الدفعة نفسها بسبب خطأ في حساب العمولة (مثلاً جلسة بدون طبيب صالح) -
            // الدفعة أهم وقد سُجِّلت بالفعل؛ يمكن مراجعة العمولات لاحقاً من لوحة الفاينانس عند الحاجة.
        }
    }

    // إصلاح: كان نفس هذا الاستعلام مكرراً حرفياً في 3 أماكن مختلفة عبر NurseApp
    // (MainWindow, PatientSearchWindow مرتين). أصبح الآن مصدراً واحداً موثوقاً في طبقة البيانات.
    // كل معرّفات المرضى الذين لديهم جلسة واحدة على الأقل غير مسدَّدة بالكامل.
    public HashSet<int> GetUnpaidPatientIds()
    {
        const string sql = "SELECT DISTINCT PatientID FROM MedicalSessions WHERE TotalPrice - PaidAmount - WriteOffAmount > 0";
        var table = _db.ExecuteQuery(sql);

        var set = new HashSet<int>();
        foreach (DataRow row in table.Rows)
        {
            set.Add(Convert.ToInt32(row["PatientID"]));
        }
        return set;
    }

    // نفس الفكرة لكن مُقيَّدة بمجموعة معرّفات مرضى محددة (تُستخدم في شاشات البحث لتفادي جلب كل الجدول)
    public HashSet<int> GetUnpaidPatientIds(IEnumerable<int> patientIds)
    {
        var idList = patientIds.Distinct().ToList();
        var set = new HashSet<int>();
        if (idList.Count == 0) return set;

        var parameters = new SqlParameter[idList.Count];
        var placeholders = new string[idList.Count];
        for (int i = 0; i < idList.Count; i++)
        {
            placeholders[i] = $"@P{i}";
            parameters[i] = new SqlParameter($"@P{i}", idList[i]);
        }

        string sql = $"SELECT DISTINCT PatientID FROM MedicalSessions WHERE PatientID IN ({string.Join(",", placeholders)}) AND TotalPrice - PaidAmount - WriteOffAmount > 0";
        var table = _db.ExecuteQuery(sql, parameters);

        foreach (DataRow row in table.Rows)
        {
            set.Add(Convert.ToInt32(row["PatientID"]));
        }
        return set;
    }

    // آخر جلسة غير مسدَّدة بالكامل لمريض معيّن - تُستخدم لفتح شاشة تحصيل الدفعة مباشرة على الجلسة الصحيحة
    public int? GetLatestUnpaidSessionId(int patientId)
    {
        const string sql = @"
            SELECT TOP 1 SessionID
            FROM MedicalSessions
            WHERE PatientID = @PatientID AND TotalPrice - PaidAmount - WriteOffAmount > 0
            ORDER BY SessionDateTime DESC";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@PatientID", patientId));
        return table.Rows.Count > 0 ? Convert.ToInt32(table.Rows[0]["SessionID"]) : null;
    }

    // آخر عدد من الدفعات (جدول "المداخيل" في لوحة الفاينانس) - مع فلتر اختياري باسم المريض
    // للوصول لدفعات أقدم من حد count الافتراضي بلا الحاجة لجلب الجدول بالكامل
    public List<IncomeRow> GetRecentPayments(int count, string? patientNameFilter = null)
    {
        var sql = @"
            SELECT TOP (@Count) pay.PaymentID, pay.SessionID, p.FullName AS PatientFullName, pay.Amount, pay.PaymentDate
            FROM Payments pay
            INNER JOIN MedicalSessions s ON s.SessionID = pay.SessionID
            INNER JOIN Patients p ON p.PatientID = s.PatientID
            WHERE 1 = 1";

        var parameters = new List<SqlParameter> { new SqlParameter("@Count", count) };

        if (!string.IsNullOrWhiteSpace(patientNameFilter))
        {
            sql += " AND p.FullName LIKE @Name";
            parameters.Add(new SqlParameter("@Name", $"%{patientNameFilter}%"));
        }

        sql += " ORDER BY pay.PaymentDate DESC";

        var table = _db.ExecuteQuery(sql, parameters.ToArray());
        var result = new List<IncomeRow>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(new IncomeRow
            {
                PaymentID = (int)row["PaymentID"],
                SessionID = (int)row["SessionID"],
                PatientFullName = row["PatientFullName"].ToString()!,
                Amount = (decimal)row["Amount"],
                PaymentDate = (DateTime)row["PaymentDate"]
            });
        }
        return result;
    }

    // معلومات السياق اللازمة لإعادة الحساب بعد تعديل/حذف دفعة: الجلسة (لإعادة حساب المتبقي)،
    // والطبيب والتاريخ (لإعادة حساب عمولة ذلك اليوم بالذات)
    private (int SessionID, int DoctorID, DateTime PaymentDate, decimal Amount) GetPaymentContext(int paymentId)
    {
        const string sql = @"
            SELECT pay.SessionID, s.DoctorID, pay.PaymentDate, pay.Amount
            FROM Payments pay
            INNER JOIN MedicalSessions s ON s.SessionID = pay.SessionID
            WHERE pay.PaymentID = @PaymentID";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@PaymentID", paymentId));
        if (table.Rows.Count == 0)
        {
            throw new InvalidOperationException("Payment not found");
        }

        var row = table.Rows[0];
        return ((int)row["SessionID"], (int)row["DoctorID"], (DateTime)row["PaymentDate"], (decimal)row["Amount"]);
    }

    // إعادة حساب "المتبقي" (TotalPrice - PaidAmount) لجلسة معيّنة من واقع الدفعات الفعلية الحالية،
    // بدل الاعتماد على تحديث تراكمي قد ينحرف مع الوقت
    private void RecalculateSessionPaidAmount(int sessionId)
    {
        const string sql = @"
            UPDATE MedicalSessions
            SET PaidAmount = (SELECT ISNULL(SUM(Amount), 0) FROM Payments WHERE SessionID = @SessionID)
            WHERE SessionID = @SessionID";

        _db.ExecuteNonQuery(sql, new SqlParameter("@SessionID", sessionId));
    }

    // تسوية كامل الرصيد المستحق لمجموعة مرضى عن طريق تسجيل دفعات فعلية في Payments.
    // هذا هو المسار المستخدم من Finance -> Zero Selected حتى يصبح الرصيد 0 ويظهر المبلغ
    // مباشرة في Recent Income. تتم عملية إدخال الدفعات وتحديث PaidAmount داخل Transaction واحدة
    // حتى لا تنجح واجهة المستخدم بصرياً بينما تفشل بعض الجلسات في قاعدة البيانات.
    public int ZeroOutstandingForPatients(IEnumerable<int> patientIds, int receivedByUserId, string? notes = null)
    {
        var ids = patientIds.Distinct().Where(id => id > 0).ToList();
        if (ids.Count == 0) return 0;

        var placeholders = new string[ids.Count];
        var parameters = new List<SqlParameter>();
        for (var i = 0; i < ids.Count; i++)
        {
            placeholders[i] = $"@PatientId{i}";
            parameters.Add(new SqlParameter(placeholders[i], ids[i]));
        }

        const string selectTemplate = @"
            SELECT s.SessionID, s.TotalPrice - s.PaidAmount AS Remaining
            FROM MedicalSessions s
            WHERE s.PatientID IN ({0}) AND s.TotalPrice > s.PaidAmount
            ORDER BY s.SessionDateTime ASC, s.SessionID ASC";

        var selectSql = string.Format(selectTemplate, string.Join(",", placeholders));
        var createdPayments = new List<(int PaymentID, int SessionID, decimal Amount)>();

        using var connection = _db.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            var selectCommand = new SqlCommand(selectSql, connection, transaction) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds };
            selectCommand.Parameters.AddRange(parameters.ToArray());

            var pendingSessions = new List<(int SessionID, decimal Remaining)>();
            using (var reader = selectCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    var sessionId = reader.GetInt32(reader.GetOrdinal("SessionID"));
                    var remaining = reader.GetDecimal(reader.GetOrdinal("Remaining"));
                    if (remaining > 0) pendingSessions.Add((sessionId, remaining));
                }
            }

            foreach (var pending in pendingSessions)
            {
                using var insertCommand = new SqlCommand(@"
                    INSERT INTO Payments (SessionID, Amount, ReceivedByUserID, Notes)
                    VALUES (@SessionID, @Amount, @ReceivedByUserID, @Notes);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);", connection, transaction) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds };

                insertCommand.Parameters.AddWithValue("@SessionID", pending.SessionID);
                insertCommand.Parameters.AddWithValue("@Amount", pending.Remaining);
                insertCommand.Parameters.AddWithValue("@ReceivedByUserID", receivedByUserId);
                insertCommand.Parameters.AddWithValue("@Notes", notes ?? "Finance: zero selected / full payment");

                var paymentId = Convert.ToInt32(insertCommand.ExecuteScalar());
                createdPayments.Add((paymentId, pending.SessionID, pending.Remaining));

                using var updateCommand = new SqlCommand(@"
                    UPDATE MedicalSessions
                    SET PaidAmount = (SELECT ISNULL(SUM(Amount), 0) FROM Payments WHERE SessionID = @SessionID)
                    WHERE SessionID = @SessionID;", connection, transaction) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds };
                updateCommand.Parameters.AddWithValue("@SessionID", pending.SessionID);
                updateCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { /* keep the original exception */ }
            throw;
        }

        // تسجيل العمولات بعد نجاح الـTransaction، حتى لا نترك Finance في حالة نصف مكتملة.
        foreach (var payment in createdPayments)
        {
            try
            {
                _commissionService.RecordCommissionForPayment(payment.SessionID, payment.PaymentID, payment.Amount);
            }
            catch
            {
                // كما في AddPayment: لا نفشل تحصيل الدفعة بسبب مشكلة جانبية في العمولة.
            }
        }

        return createdPayments.Count;
    }

    // توافق مع أي كود قديم يستعمل عملية مريض واحد.
    public int ZeroOutstandingForPatient(int patientId, int receivedByUserId, string? notes = null)
        => ZeroOutstandingForPatients(new[] { patientId }, receivedByUserId, notes);

    // تعديل مبلغ دفعة موجودة (تصحيح خطأ إدخال فقط - لا يمكن تغيير التاريخ أو المريض المرتبط بها).
    // يعيد حساب "المتبقي" على الجلسة وعمولة الطبيب لذلك اليوم بالذات تلقائياً، دون أي تدخل يدوي.
    public void UpdatePaymentAmount(int paymentId, decimal newAmount)
    {
        var (sessionId, doctorId, paymentDate, _) = GetPaymentContext(paymentId);

        const string sql = "UPDATE Payments SET Amount = @Amount WHERE PaymentID = @PaymentID";
        _db.ExecuteNonQuery(sql,
            new SqlParameter("@Amount", newAmount),
            new SqlParameter("@PaymentID", paymentId));

        // تعديل مبلغ الدفعة لا يخلق ديناً جديداً؛ PaidAmount يبقى متوافقاً مع الدفعات الفعلية.
        RecalculateSessionPaidAmount(sessionId);
        _commissionService.RecalculateCommissionForDoctorDate(doctorId, paymentDate);
    }

    // حذف المدخول نهائياً من سجل الإيرادات مع شطب نفس المبلغ من الرصيد المستحق.
    // هذا يمنع عودة المبلغ كدين، ويضمن أن أي دفعة جديدة لاحقاً تُحسب فوق الرصيد الحقيقي
    // بعد الحذف بدلاً من إعادة إدخال الدين القديم.
    public void DeletePayment(int paymentId)
    {
        var (sessionId, doctorId, paymentDate, amount) = GetPaymentContext(paymentId);
        if (amount <= 0)
        {
            _db.ExecuteNonQuery("DELETE FROM Payments WHERE PaymentID = @PaymentID",
                new SqlParameter("@PaymentID", paymentId));
            _commissionService.RecalculateCommissionForDoctorDate(doctorId, paymentDate);
            return;
        }

        const string sql = @"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            DELETE FROM Payments WHERE PaymentID = @PaymentID;

            UPDATE MedicalSessions
            SET WriteOffAmount = WriteOffAmount + @Amount,
                PaidAmount = (SELECT ISNULL(SUM(Amount), 0) FROM Payments WHERE SessionID = @SessionID)
            WHERE SessionID = @SessionID;

            COMMIT TRANSACTION;";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@PaymentID", paymentId),
            new SqlParameter("@SessionID", sessionId),
            new SqlParameter("@Amount", amount));

        _commissionService.RecalculateCommissionForDoctorDate(doctorId, paymentDate);
    }
}
