using Microsoft.Data.SqlClient;
using System.Data;
using System.Linq;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

// كل ما يخص الملف الطبي السري - يُستخدم من تطبيق الطبيب فقط
public class SessionRepository
{
    private readonly DatabaseHelper _db;

    public SessionRepository(DatabaseHelper db)
    {
        _db = db;
    }

    // تسجيل جلسة طبية جديدة (تشخيص / معالجة / دواء / سعر)
    public int Add(MedicalSession session)
    {
        const string sql = @"
            INSERT INTO MedicalSessions
                (VisitID, PatientID, DoctorID, ChiefComplaint, Diagnosis, TreatmentPerformed, Medication, Certificate, TotalPrice, PaidAmount, WriteOffAmount, Notes)
            VALUES
                (@VisitID, @PatientID, @DoctorID, @ChiefComplaint, @Diagnosis, @TreatmentPerformed, @Medication, @Certificate, @TotalPrice, @PaidAmount, @WriteOffAmount, @Notes)";

        return _db.ExecuteInsertAndGetId(sql,
            new SqlParameter("@VisitID", (object?)session.VisitID ?? DBNull.Value),
            new SqlParameter("@PatientID", session.PatientID),
            new SqlParameter("@DoctorID", session.DoctorID),
            new SqlParameter("@ChiefComplaint", (object?)session.ChiefComplaint ?? DBNull.Value),
            new SqlParameter("@Diagnosis", (object?)session.Diagnosis ?? DBNull.Value),
            new SqlParameter("@TreatmentPerformed", (object?)session.TreatmentPerformed ?? DBNull.Value),
            new SqlParameter("@Medication", (object?)session.Medication ?? DBNull.Value),
            new SqlParameter("@Certificate", (object?)session.Certificate ?? DBNull.Value),
            new SqlParameter("@TotalPrice", session.TotalPrice),
            new SqlParameter("@PaidAmount", session.PaidAmount),
            new SqlParameter("@WriteOffAmount", session.WriteOffAmount),
            new SqlParameter("@Notes", (object?)session.Notes ?? DBNull.Value));
    }

    // السجل الطبي الكامل لكل زيارات مريض معيّن (الأحدث أولاً)
    public List<MedicalSession> GetByPatient(int patientId)
    {
        const string sql = @"
            SELECT SessionID, VisitID, PatientID, DoctorID, SessionDateTime, ChiefComplaint,
                   Diagnosis, TreatmentPerformed, Medication, Certificate, TotalPrice, PaidAmount, WriteOffAmount, Notes, RowVersion
            FROM MedicalSessions
            WHERE PatientID = @PatientID
            ORDER BY SessionDateTime DESC";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@PatientID", patientId));
        var result = new List<MedicalSession>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(new MedicalSession
            {
                SessionID = (int)row["SessionID"],
                VisitID = row["VisitID"] as int?,
                PatientID = (int)row["PatientID"],
                DoctorID = (int)row["DoctorID"],
                SessionDateTime = (DateTime)row["SessionDateTime"],
                ChiefComplaint = row["ChiefComplaint"] as string,
                Diagnosis = row["Diagnosis"] as string,
                TreatmentPerformed = row["TreatmentPerformed"] as string,
                Medication = row["Medication"] as string,
                Certificate = row["Certificate"] as string,
                TotalPrice = (decimal)row["TotalPrice"],
                PaidAmount = (decimal)row["PaidAmount"],
                WriteOffAmount = (decimal)row["WriteOffAmount"],
                Notes = row["Notes"] as string,
                RowVersion = (byte[])row["RowVersion"]
            });
        }
        return result;
    }


    public MedicalSession? GetById(int sessionId)
    {
        const string sql = @"
            SELECT SessionID, VisitID, PatientID, DoctorID, SessionDateTime, ChiefComplaint,
                   Diagnosis, TreatmentPerformed, Medication, Certificate, TotalPrice, PaidAmount, WriteOffAmount, Notes, RowVersion
            FROM MedicalSessions
            WHERE SessionID = @SessionID";
        var table = _db.ExecuteQuery(sql, new SqlParameter("@SessionID", sessionId));
        return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
    }

    public MedicalSession? GetByPatientOnDate(int patientId, DateTime date)
    {
        const string sql = @"
            SELECT TOP 1 SessionID, VisitID, PatientID, DoctorID, SessionDateTime, ChiefComplaint,
                   Diagnosis, TreatmentPerformed, Medication, Certificate, TotalPrice, PaidAmount, WriteOffAmount, Notes, RowVersion
            FROM MedicalSessions
            WHERE PatientID = @PatientID
              AND SessionDateTime >= @StartDate
              AND SessionDateTime < @EndDate
            ORDER BY SessionDateTime DESC, SessionID DESC";
        var table = _db.ExecuteQuery(sql,
            new SqlParameter("@PatientID", patientId),
            new SqlParameter("@StartDate", date.Date),
            new SqlParameter("@EndDate", date.Date.AddDays(1)));
        return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
    }

    public void Update(MedicalSession session)
    {
        var current = GetById(session.SessionID) ?? throw new InvalidOperationException("Session not found");
        if (session.TotalPrice < current.PaidAmount + current.WriteOffAmount)
            throw new InvalidOperationException("Total price cannot be less than the amount already paid.");
        if (session.RowVersion.Length == 0)
            throw new InvalidOperationException("Session version is missing.");

        const string sql = @"
            UPDATE MedicalSessions
            SET ChiefComplaint = @ChiefComplaint,
                Diagnosis = @Diagnosis,
                TreatmentPerformed = @TreatmentPerformed,
                Medication = @Medication,
                Certificate = @Certificate,
                TotalPrice = @TotalPrice,
                Notes = @Notes
            WHERE SessionID = @SessionID
              AND RowVersion = @RowVersion";

        var affectedRows = _db.ExecuteNonQuery(sql,
            new SqlParameter("@SessionID", session.SessionID),
            new SqlParameter("@RowVersion", SqlDbType.Timestamp) { Value = session.RowVersion },
            new SqlParameter("@ChiefComplaint", (object?)session.ChiefComplaint ?? DBNull.Value),
            new SqlParameter("@Diagnosis", (object?)session.Diagnosis ?? DBNull.Value),
            new SqlParameter("@TreatmentPerformed", (object?)session.TreatmentPerformed ?? DBNull.Value),
            new SqlParameter("@Medication", (object?)session.Medication ?? DBNull.Value),
            new SqlParameter("@Certificate", (object?)session.Certificate ?? DBNull.Value),
            new SqlParameter("@TotalPrice", session.TotalPrice),
            new SqlParameter("@Notes", (object?)session.Notes ?? DBNull.Value));

        if (affectedRows == 0)
            throw new ConcurrencyConflictException();
    }

    public List<(int SessionID, decimal Amount)> GetUnpaidSessionsForPatient(int patientId)
    {
        const string sql = @"
            SELECT SessionID, (TotalPrice - PaidAmount - WriteOffAmount) AS Remaining
            FROM MedicalSessions
            WHERE PatientID = @PatientID AND TotalPrice - PaidAmount - WriteOffAmount > 0
            ORDER BY SessionDateTime ASC, SessionID ASC";
        var table = _db.ExecuteQuery(sql, new SqlParameter("@PatientID", patientId));
        return table.Rows.Cast<DataRow>()
            .Select(r => (Convert.ToInt32(r["SessionID"]), Convert.ToDecimal(r["Remaining"])))
            .Where(x => x.Item2 > 0)
            .ToList();
    }

    public bool HasPayments(int sessionId)
    {
        const string sql = "SELECT COUNT(1) FROM Payments WHERE SessionID = @SessionID";
        return Convert.ToInt32(_db.ExecuteScalar(sql, new SqlParameter("@SessionID", sessionId))) > 0;
    }

    public void Delete(int sessionId)
    {
        const string sql = @"
            SET XACT_ABORT ON;
            BEGIN TRY
                BEGIN TRANSACTION;

                DELETE FROM ClinicExpenses
                WHERE SourceSessionID = @SessionID
                   OR SourcePaymentID IN (SELECT PaymentID FROM Payments WHERE SessionID = @SessionID);

                DELETE FROM ToothRecords WHERE SessionID = @SessionID;
                DELETE FROM Payments WHERE SessionID = @SessionID;
                DELETE FROM MedicalSessions WHERE SessionID = @SessionID;

                COMMIT TRANSACTION;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH";

        _db.ExecuteNonQuery(sql, new SqlParameter("@SessionID", sessionId));
    }

    public List<string> GetToothNumbersForSession(int sessionId)
    {
        const string sql = "SELECT ToothNumber FROM ToothRecords WHERE SessionID = @SessionID ORDER BY ToothRecordID";
        var table = _db.ExecuteQuery(sql, new SqlParameter("@SessionID", sessionId));
        return table.Rows.Cast<DataRow>().Select(r => r["ToothNumber"].ToString()!).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    public void ReplaceToothRecords(int sessionId, IEnumerable<string> toothNumbers)
    {
        const string sql = @"
            DELETE FROM ToothRecords WHERE SessionID = @SessionID;
            INSERT INTO ToothRecords (SessionID, ToothNumber, ToothCondition, ProcedureNotes)
            SELECT @SessionID, ToothNumber, 'Treated', NULL
            FROM (SELECT DISTINCT value AS ToothNumber FROM STRING_SPLIT(@Teeth, ',')) t
            WHERE NULLIF(LTRIM(RTRIM(ToothNumber)), '') IS NOT NULL";
        _db.ExecuteNonQuery(sql,
            new SqlParameter("@SessionID", sessionId),
            new SqlParameter("@Teeth", string.Join(',', toothNumbers ?? Enumerable.Empty<string>())));
    }

    private static MedicalSession Map(DataRow row) => new()
    {
        SessionID = Convert.ToInt32(row["SessionID"]),
        VisitID = row["VisitID"] == DBNull.Value ? null : (int?)Convert.ToInt32(row["VisitID"]),
        PatientID = Convert.ToInt32(row["PatientID"]),
        DoctorID = Convert.ToInt32(row["DoctorID"]),
        SessionDateTime = Convert.ToDateTime(row["SessionDateTime"]),
        ChiefComplaint = row["ChiefComplaint"] == DBNull.Value ? null : row["ChiefComplaint"].ToString(),
        Diagnosis = row["Diagnosis"] == DBNull.Value ? null : row["Diagnosis"].ToString(),
        TreatmentPerformed = row["TreatmentPerformed"] == DBNull.Value ? null : row["TreatmentPerformed"].ToString(),
        Medication = row["Medication"] == DBNull.Value ? null : row["Medication"].ToString(),
        Certificate = row["Certificate"] == DBNull.Value ? null : row["Certificate"].ToString(),
        TotalPrice = Convert.ToDecimal(row["TotalPrice"]),
        PaidAmount = Convert.ToDecimal(row["PaidAmount"]),
        WriteOffAmount = Convert.ToDecimal(row["WriteOffAmount"]),
        Notes = row["Notes"] == DBNull.Value ? null : row["Notes"].ToString(),
        RowVersion = (byte[])row["RowVersion"]
    };

    // تسجيل مبسّط لسن ضمن جلسة - تمهيداً لمخطط الأسنان التفاعلي (Odontogram) في خطوة لاحقة
    public void AddToothRecord(int sessionId, string toothNumber, string condition, string? notes)
    {
        const string sql = @"
            INSERT INTO ToothRecords (SessionID, ToothNumber, ToothCondition, ProcedureNotes)
            VALUES (@SessionID, @ToothNumber, @ToothCondition, @ProcedureNotes)";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@SessionID", sessionId),
            new SqlParameter("@ToothNumber", toothNumber),
            new SqlParameter("@ToothCondition", condition),
            new SqlParameter("@ProcedureNotes", (object?)notes ?? DBNull.Value));
    }

    // تحديث دفعة/ملاحظات جلسة موجودة بدل إنشاء جلسة جديدة - يُستخدم عندما يكون السعر الإجمالي لم يتغيّر
    // (أي أن الطبيب يسجّل دفعة إضافية لنفس الرصيد القائم، وليس علاجاً/فاتورة جديدة)
    public void UpdateSessionPayment(int sessionId, decimal newPaidAmount, string? chiefComplaint,
        string? diagnosis, string? treatment, string? medication)
    {
        const string sql = @"
            UPDATE MedicalSessions
            SET PaidAmount = @PaidAmount,
                ChiefComplaint = @ChiefComplaint,
                Diagnosis = @Diagnosis,
                TreatmentPerformed = @Treatment,
                Medication = @Medication
            WHERE SessionID = @SessionID";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@PaidAmount", newPaidAmount),
            new SqlParameter("@ChiefComplaint", (object?)chiefComplaint ?? DBNull.Value),
            new SqlParameter("@Diagnosis", (object?)diagnosis ?? DBNull.Value),
            new SqlParameter("@Treatment", (object?)treatment ?? DBNull.Value),
            new SqlParameter("@Medication", (object?)medication ?? DBNull.Value),
            new SqlParameter("@SessionID", sessionId));
    }

    // بحث متقدم متعدد المعايير عبر كل الجلسات (اسم/هاتف، تشخيص، مدى تاريخي، رصيد متبقٍ، رقم سن)
    // كل معيار اختياري تماماً؛ يُبنى شرط WHERE ديناميكياً لكن بمعاملات SQL آمنة دائماً (لا دمج نصي لقيم المستخدم)
    // allowedDoctorUserIds/includeUnassigned: نفس منطق PatientRepository.Search بالضبط - كانت هذي
    // الشاشة ثغرة رؤية حقيقية (نتائج البحث نفسها، مو فقط فتح الملف) قبل هذا الإصلاح
    public List<SessionSearchResult> AdvancedSearch(SessionSearchCriteria criteria, List<int>? allowedDoctorUserIds = null, bool includeUnassigned = true)
    {
        var sql = @"
            SELECT DISTINCT s.SessionID, s.PatientID, p.FullName AS PatientFullName, p.PhoneNumber AS PatientPhone,
                   s.SessionDateTime, s.Diagnosis, s.TreatmentPerformed, s.TotalPrice, s.PaidAmount, s.WriteOffAmount
            FROM MedicalSessions s
            INNER JOIN Patients p ON p.PatientID = s.PatientID
            LEFT JOIN ToothRecords t ON t.SessionID = s.SessionID
            WHERE 1 = 1";

        var parameters = new List<SqlParameter>();

        if (allowedDoctorUserIds != null)
        {
            if (allowedDoctorUserIds.Count == 0)
            {
                sql += includeUnassigned ? " AND p.AssignedDoctorUserID IS NULL" : " AND 1 = 0";
            }
            else
            {
                var placeholders = allowedDoctorUserIds.Select((id, i) => $"@Doc{i}").ToList();
                for (var i = 0; i < allowedDoctorUserIds.Count; i++)
                {
                    parameters.Add(new SqlParameter($"@Doc{i}", allowedDoctorUserIds[i]));
                }

                sql += includeUnassigned
                    ? $" AND (p.AssignedDoctorUserID IN ({string.Join(",", placeholders)}) OR p.AssignedDoctorUserID IS NULL)"
                    : $" AND p.AssignedDoctorUserID IN ({string.Join(",", placeholders)})";
            }
        }

        if (!string.IsNullOrWhiteSpace(criteria.PatientNameOrPhone))
        {
            sql += " AND (p.FullName LIKE @NameOrPhone OR p.PhoneNumber LIKE @NameOrPhone)";
            parameters.Add(new SqlParameter("@NameOrPhone", $"%{criteria.PatientNameOrPhone}%"));
        }

        if (!string.IsNullOrWhiteSpace(criteria.DiagnosisContains))
        {
            sql += " AND s.Diagnosis LIKE @Diagnosis";
            parameters.Add(new SqlParameter("@Diagnosis", $"%{criteria.DiagnosisContains}%"));
        }

        if (criteria.FromDate.HasValue)
        {
            sql += " AND s.SessionDateTime >= @FromDate";
            parameters.Add(new SqlParameter("@FromDate", criteria.FromDate.Value.Date));
        }

        if (criteria.ToDate.HasValue)
        {
            sql += " AND s.SessionDateTime < @ToDate";
            parameters.Add(new SqlParameter("@ToDate", criteria.ToDate.Value.Date.AddDays(1)));
        }

        if (criteria.OnlyWithOutstandingBalance)
        {
            sql += " AND (s.TotalPrice - s.PaidAmount - s.WriteOffAmount) > 0";
        }

        if (!string.IsNullOrWhiteSpace(criteria.ToothNumber))
        {
            sql += " AND t.ToothNumber = @ToothNumber";
            parameters.Add(new SqlParameter("@ToothNumber", criteria.ToothNumber.Trim()));
        }

        sql += " ORDER BY s.SessionDateTime DESC";

        var table = _db.ExecuteQuery(sql, parameters.ToArray());
        var results = new List<SessionSearchResult>();

        foreach (DataRow row in table.Rows)
        {
            results.Add(new SessionSearchResult
            {
                SessionID = (int)row["SessionID"],
                PatientID = (int)row["PatientID"],
                PatientFullName = row["PatientFullName"].ToString()!,
                PatientPhone = row["PatientPhone"].ToString()!,
                SessionDateTime = (DateTime)row["SessionDateTime"],
                Diagnosis = row["Diagnosis"] as string,
                TreatmentPerformed = row["TreatmentPerformed"] as string,
                TotalPrice = (decimal)row["TotalPrice"],
                PaidAmount = (decimal)row["PaidAmount"],
                WriteOffAmount = (decimal)row["WriteOffAmount"]
            });
        }
        return results;
    }
}
