using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

// طبقة وصول لحالات الترميم (ProstheticCases) وسجل تاريخها (ProstheticCaseHistory).
// ملاحظة Concurrency: كل عملية تُغيّر حقلاً مهمًا (مرحلة/تعيين/حالة) تُنفَّذ ضمن معاملة واحدة
// مع تسجيل History بنفس اللحظة، بنفس مستوى الحماية المعتمد في بقية المشروع (لا يوجد RowVersion
// في أي جدول آخر بالمشروع الحالي أيضًا) - آخر عملية حفظ تفوز (last-write-wins)، لكن يبقى كل
// تغيير مسجَّلاً في History بحيث لا يضيع أي أثر حتى عند تعارض عمليتين متزامنتين.
public class ProstheticCaseRepository
{
    private readonly DatabaseHelper _db;

    public ProstheticCaseRepository(DatabaseHelper db)
    {
        _db = db;
    }

    private const string BaseSelect = @"
        SELECT c.CaseID, c.CaseNumber, c.PatientID, p.FullName AS PatientFullName, p.PhoneNumber AS PatientPhoneNumber,
               c.WorkTypeID, wt.WorkTypeName, c.IncludesUpper, c.IncludesLower, c.ToothScope,
               c.AssignedProsthetistUserID, u1.FullName AS AssignedProsthetistName,
               c.StageID, st.StageName, c.CaseStatus, c.TotalAgreedPrice, c.Notes,
               c.CreatedByUserID, u2.FullName AS CreatedByUserName, c.CreatedAt,
               ISNULL((SELECT SUM(pp.Amount) FROM dbo.ProstheticPayments pp WHERE pp.CaseID = c.CaseID), 0) AS TotalPaid
        FROM dbo.ProstheticCases c
        INNER JOIN dbo.Patients p ON p.PatientID = c.PatientID
        LEFT JOIN dbo.ProstheticWorkTypes wt ON wt.WorkTypeID = c.WorkTypeID
        LEFT JOIN dbo.ProstheticStages st ON st.StageID = c.StageID
        LEFT JOIN dbo.Users u1 ON u1.UserID = c.AssignedProsthetistUserID
        LEFT JOIN dbo.Users u2 ON u2.UserID = c.CreatedByUserID";

    // إنشاء حالة ترميم جديدة (من ملف المريض في تطبيق الطبيب) - CaseNumber يُولَّد تلقائيًا من
    // SQL SEQUENCE مستقل (راجع SchemaInitializer)، فلا يمكن أبدًا أن يتكرر أو يُعاد استخدامه.
    // Patch 14: actingUser للتحقق من Prosthetics.CreateCase فقط (دفاع في العمق) - لا علاقة له
    // بـCreatedByUserID المخزَّن في الحالة نفسها (يبقى newCase.CreatedByUserID كما كان).
    public int CreateCase(ProstheticCase newCase, UserAccount actingUser)
    {
        ProstheticPermissionGuard.Ensure(actingUser, ProstheticPermissionKeys.CreateCase);

        const string sql = @"
            INSERT INTO dbo.ProstheticCases
                (CaseNumber, PatientID, WorkTypeID, IncludesUpper, IncludesLower, ToothScope,
                 AssignedProsthetistUserID, StageID, CaseStatus, TotalAgreedPrice, Notes, CreatedByUserID, CreatedAt)
            VALUES
                (NEXT VALUE FOR dbo.ProstheticCaseNumberSeq, @PatientID, @WorkTypeID, @IncludesUpper, @IncludesLower, @ToothScope,
                 @ProsthetistID, @StageID, @CaseStatus, @TotalAgreedPrice, @Notes, @CreatedByUserID, GETDATE())";

        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using var cmd = new SqlCommand(sql + "; SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, tx) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds };
            cmd.Parameters.AddWithValue("@PatientID", newCase.PatientID);
            cmd.Parameters.AddWithValue("@WorkTypeID", (object?)newCase.WorkTypeID ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IncludesUpper", newCase.IncludesUpper);
            cmd.Parameters.AddWithValue("@IncludesLower", newCase.IncludesLower);
            cmd.Parameters.AddWithValue("@ToothScope", (object?)newCase.ToothScope ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ProsthetistID", (object?)newCase.AssignedProsthetistUserID ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@StageID", (object?)newCase.StageID ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CaseStatus", newCase.CaseStatus);
            cmd.Parameters.AddWithValue("@TotalAgreedPrice", newCase.TotalAgreedPrice);
            cmd.Parameters.AddWithValue("@Notes", (object?)newCase.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedByUserID", newCase.CreatedByUserID);

            var newId = Convert.ToInt32(cmd.ExecuteScalar());

            InsertHistory(conn, tx, newId, newCase.CreatedByUserID, "Created", null,
                $"Case created" + (newCase.AssignedProsthetistUserID.HasValue ? " and assigned" : ""));

            tx.Commit();
            return newId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // Patch 15: قراءة حالة واحدة - محمية الآن بطبقتين (دفاع في العمق، يماثل تمامًا ما طبَّقناه على
    // الكتابة في Patch 14، لكن هنا للقراءة):
    //   1) وصول على مستوى الحالة (Row-level): هل يحق لهذا المستخدم رؤية هذه الحالة على الإطلاق؟
    //      نفس القاعدة المطبَّقة أصلاً على قائمة الانتظار (GetCases) - الطبيب دائمًا، أو من يملك
    //      ViewAllCases، أو المرمم المسؤول عن هذه الحالة تحديدًا. غير ذلك: رفض كامل.
    //   2) تنقيح حقلي (Field-level): حتى لمن يملك حق رؤية الحالة، حقلا اسم/هاتف المريض ومعلومات
    //      المرحلة يُخفيان (تُعاد قيم فارغة/null) ما لم يملك ViewPatient أو ViewStage/EditStage على
    //      التوالي - هذا التنقيح كان يحدث سابقًا فقط داخل الواجهة (راجع Patch 8/9)؛ أصبح الآن
    //      مطبَّقاً في مصدر البيانات نفسه، فلا يعود استدعاء هذه الدالة مباشرة (متجاوزاً الواجهة)
    //      كافياً لتسريب هذه الحقول. لا تغيير على سلوك أي شاشة موجودة: كل شاشة تحسب صلاحياتها
    //      بنفس القاعدة أصلاً (HasEditRight) ولا تعرض هذه الحقول إلا حين تكون غير مُنقَّحة أصلاً.
    public ProstheticCase? GetById(int caseId, UserAccount actingUser)
    {
        var sql = BaseSelect + " WHERE c.CaseID = @CaseID";
        var table = _db.ExecuteQuery(sql, new SqlParameter("@CaseID", caseId));
        if (table.Rows.Count == 0) return null;

        var result = MapRow(table.Rows[0]);
        EnsureCanAccessCase(result, actingUser);
        RedactRestrictedFields(result, actingUser);
        return result;
    }

    // كل حالات الترميم لمريض معيَّن (قد تكون عدة حالات في نفس الوقت) - تُستخدم حصرياً من
    // ProstheticCaseWindow المفتوحة عبر ملف المريض (PatientFileWindow في DoctorApp/NurseApp).
    // Patch 15: عمداً بلا حراسة قراءة إضافية هنا (بخلاف GetById/GetHistory أدناه) - السياق مختلف
    // جوهرياً: من يفتح هذه الشاشة (طبيب أو ممرضة) يملك أصلاً وصولاً كاملاً شرعياً لملف هذا المريض
    // تحديداً (فتح الملف نفسه محكوم بصلاحية OpenPatientFile المنفصلة تماماً)، فإخفاء اسم/هاتف
    // المريض هنا لا معنى له (معروفان أصلاً من كون الملف نفسه مفتوحاً) ولا يضيف أي حماية حقيقية -
    // على عكس تطبيق المرمم حيث قد يستدعي مستخدم واحد حالة تخص مرمماً/مريضاً آخر لا علاقة له به.
    // تطبيق نفس تصفية GetById هنا كان سيُفرِغ هذه القائمة تمامًا لأي ممرضة (لا تملك عادة أي صلاحية
    // Prosthetics.*)، وهو تراجع حقيقي في السلوك لم يُطلَب ولا مبرَّر أمنياً - فتُرِك هذا المسار كما كان.
    public List<ProstheticCase> GetByPatient(int patientId)
    {
        var sql = BaseSelect + " WHERE c.PatientID = @PatientID ORDER BY c.CreatedAt DESC";
        return MapAll(_db.ExecuteQuery(sql, new SqlParameter("@PatientID", patientId)));
    }

    // ===================== Patch 15: حراسة القراءة (Read-side guards) =====================

    private static bool CanAccessCase(ProstheticCase c, UserAccount actingUser)
    {
        if (actingUser.Role == UserRole.Doctor) return true;
        if (actingUser.HasProstheticPermission(ProstheticPermissionKeys.ViewAllCases)) return true;
        return c.AssignedProsthetistUserID.HasValue && c.AssignedProsthetistUserID.Value == actingUser.UserID;
    }

    private static void EnsureCanAccessCase(ProstheticCase c, UserAccount actingUser)
    {
        if (!CanAccessCase(c, actingUser))
        {
            throw new UnauthorizedAccessException("You don't have access to this prosthetic case.");
        }
    }

    private static bool CanViewPatientInfo(UserAccount actingUser) =>
        actingUser.Role == UserRole.Doctor || actingUser.HasProstheticPermission(ProstheticPermissionKeys.ViewPatient);

    private static bool CanViewStageInfo(UserAccount actingUser) =>
        actingUser.Role == UserRole.Doctor
        || actingUser.HasProstheticPermission(ProstheticPermissionKeys.ViewStage)
        || actingUser.HasProstheticPermission(ProstheticPermissionKeys.EditStage);

    // تُصفِّر/تُفرِغ فقط الحقلين المشمولين فعلياً بصلاحية عرض مستقلة (ViewPatient/ViewStage) -
    // بقية حقول الحالة (نوع العمل، السعر، الملاحظات، اسم المرمم المسؤول، حالة الملف) لم تكن
    // محكومة بأي صلاحية عرض مستقلة في أي Patch سابق (راجع Patch 8/9)، فتبقى كما هي هنا أيضاً -
    // هذه الدالة تُطابق فقط ما كانت الواجهة تُخفيه فعلياً، ولا تُضيف أي تقييد جديد لم يكن موجوداً.
    private static void RedactRestrictedFields(ProstheticCase c, UserAccount actingUser)
    {
        if (!CanViewPatientInfo(actingUser))
        {
            c.PatientFullName = string.Empty;
            c.PatientPhoneNumber = string.Empty;
        }
        if (!CanViewStageInfo(actingUser))
        {
            c.StageID = null;
            c.StageName = null;
        }
    }

    // قائمة انتظار المرمم / الطبيب مع فلاتر + بحث + Pagination (لا حذف أبدًا - راجع البند 11).
    // prosthetistUserId: null = كل المرممين (الطبيب الرئيسي فقط)؛ قيمة = فقط حالات مرمم معيَّن.
    // allowPatientSearch (Patch 9): إن كان المستخدم بلا Prosthetics.ViewPatient، لا نسمح للبحث
    // بمطابقة الاسم/الهاتف إطلاقًا - وإلا يستطيع "تجربة" اسم أو رقم هاتف ومعرفة ضمنيًا وجود
    // تطابق من عدد النتائج، رغم أن الأعمدة نفسها مخفية عنه في الواجهة (تسرّب معلومة جزئي عبر
    // قناة جانبية). يبقى البحث برقم الحالة متاحًا دائمًا لأنه ليس بيانات مريض.
    public (List<ProstheticCase> Items, int TotalCount) GetCases(
        int? prosthetistUserId, string? statusFilter, string? searchText, int pageNumber, int pageSize,
        bool allowPatientSearch = true)
    {
        var where = new StringBuilder(" WHERE 1 = 1");
        if (prosthetistUserId.HasValue) where.Append(" AND c.AssignedProsthetistUserID = @ProsthetistID");
        if (!string.IsNullOrWhiteSpace(statusFilter)) where.Append(" AND c.CaseStatus = @Status");
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            where.Append(allowPatientSearch
                ? " AND (p.FullName LIKE @Search OR p.PhoneNumber LIKE @Search OR CAST(c.CaseNumber AS NVARCHAR(20)) LIKE @Search)"
                : " AND CAST(c.CaseNumber AS NVARCHAR(20)) LIKE @Search");
        }

        // ⚠️ كائن SqlParameter لا يمكن إضافته لأكثر من SqlCommand واحد - إعادة استخدام نفس الكائنات
        // بين استعلام العدّ (Count) واستعلام الصفحة (Paged) كانت تُسبِّب استثناء "already contained
        // by another SqlParameterCollection" في ثاني استعلام دائماً (صامت، يلتقطه try/catch في
        // الواجهة، فتظهر القائمة فارغة بلا أي فلتر يعمل بشكل ملحوظ). لذلك نبني مصفوفة معاملات
        // جديدة تماماً في كل استدعاء لهذه الدالة المحلية، لا نُعيد استخدام أي كائن بين الاستعلامَين.
        SqlParameter[] BuildFilterParameters()
        {
            var list = new List<SqlParameter>();
            if (prosthetistUserId.HasValue) list.Add(new SqlParameter("@ProsthetistID", prosthetistUserId.Value));
            if (!string.IsNullOrWhiteSpace(statusFilter)) list.Add(new SqlParameter("@Status", statusFilter));
            if (!string.IsNullOrWhiteSpace(searchText)) list.Add(new SqlParameter("@Search", $"%{searchText}%"));
            return list.ToArray();
        }

        var countSql = @"
            SELECT COUNT(*) FROM dbo.ProstheticCases c
            INNER JOIN dbo.Patients p ON p.PatientID = c.PatientID" + where;
        var totalCount = Convert.ToInt32(_db.ExecuteScalar(countSql, BuildFilterParameters()));

        var pageSql = BaseSelect + where +
            " ORDER BY c.CreatedAt DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
        var pagedParameters = BuildFilterParameters().ToList();
        pagedParameters.Add(new SqlParameter("@Offset", (pageNumber - 1) * pageSize));
        pagedParameters.Add(new SqlParameter("@PageSize", pageSize));

        var items = MapAll(_db.ExecuteQuery(pageSql, pagedParameters.ToArray()));
        return (items, totalCount);
    }

    // آخر 50 حالة افتراضيًا (لوحة تحكم المرمم) - اختصار مباشر فوق GetCases
    public List<ProstheticCase> GetRecent(int? prosthetistUserId, int count = 50)
    {
        var (items, _) = GetCases(prosthetistUserId, statusFilter: null, searchText: null, pageNumber: 1, pageSize: count);
        return items;
    }

    // نقل الحالة من مرمم إلى آخر (أو تعيينها لأول مرة) - لا يُغيّر CaseNumber ولا الجلسات ولا
    // المعلومات المالية إطلاقًا (تحديث عمود واحد فقط + تسجيل History)
    // Patch 14: يتحقق من Prosthetics.TransferCase تحديدًا - وليس EditCase العامة
    public void ReassignProsthetist(int caseId, int? newProsthetistUserId, int changedByUserId, string? oldName, string? newName, UserAccount actingUser)
    {
        ProstheticPermissionGuard.Ensure(actingUser, ProstheticPermissionKeys.TransferCase);

        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var cmd = new SqlCommand(
                "UPDATE dbo.ProstheticCases SET AssignedProsthetistUserID = @NewID WHERE CaseID = @CaseID", conn, tx) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds })
            {
                cmd.Parameters.AddWithValue("@NewID", (object?)newProsthetistUserId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CaseID", caseId);
                cmd.ExecuteNonQuery();
            }

            InsertHistory(conn, tx, caseId, changedByUserId, "ProsthetistReassigned", oldName, newName);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // Patch 14: يتحقق من Prosthetics.EditStage تحديدًا - وليس EditCase العامة. ViewStage تبقى
    // صلاحية منفصلة تمامًا (لا علاقة لها بالتعديل هنا، كما صُمِّم في Patch 8).
    public void UpdateStage(int caseId, int? newStageId, int changedByUserId, string? oldStageName, string? newStageName, UserAccount actingUser)
    {
        ProstheticPermissionGuard.Ensure(actingUser, ProstheticPermissionKeys.EditStage);

        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var cmd = new SqlCommand(
                "UPDATE dbo.ProstheticCases SET StageID = @NewID WHERE CaseID = @CaseID", conn, tx) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds })
            {
                cmd.Parameters.AddWithValue("@NewID", (object?)newStageId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CaseID", caseId);
                cmd.ExecuteNonQuery();
            }

            InsertHistory(conn, tx, caseId, changedByUserId, "StageChanged", oldStageName, newStageName);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // Patch 14: لا يوجد أي مستدعٍ فعلي لهذه الدالة في المشروع حاليًا (بحث شامل لم يجد أي Call Site) -
    // تركتها كما هي وظيفيًا وأضفت فقط actingUser للتحقق من Prosthetics.EditCase (نفس صلاحية بقية
    // حقول "معلومات الحالة" العامة - راجع نفس المنطق المطبَّق في History منذ Patch 10)، تحسبًا
    // لاستخدامها مستقبلًا دون أن تبقى غير محمية.
    public void UpdateStatus(int caseId, string newStatus, int changedByUserId, string? oldStatus, UserAccount actingUser)
    {
        ProstheticPermissionGuard.Ensure(actingUser, ProstheticPermissionKeys.EditCase);

        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var cmd = new SqlCommand(
                "UPDATE dbo.ProstheticCases SET CaseStatus = @NewStatus WHERE CaseID = @CaseID", conn, tx) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds })
            {
                cmd.Parameters.AddWithValue("@NewStatus", newStatus);
                cmd.Parameters.AddWithValue("@CaseID", caseId);
                cmd.ExecuteNonQuery();
            }

            InsertHistory(conn, tx, caseId, changedByUserId, "StatusChanged", oldStatus, newStatus);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // تعديل معلومات الحالة العامة (نوع العمل، Upper/Lower، ملاحظات، السعر المتفق عليه).
    // ⚠️ لا تُسجَّل أي حركة في History ولا يُنفَّذ أي UPDATE فعلي إن لم يتغيّر أي حقل فعلياً -
    // نقرأ القيم الحالية من القاعدة داخل نفس المعاملة ونقارنها حقلاً حقلاً قبل أي كتابة، بنفس
    // دقة ReassignProsthetist/UpdateStage أدناه (سطر History منفصل لكل حقل تغيّر تحديداً، وليس
    // سطراً عاماً واحداً)، حتى يبقى سجل التدقيق نظيفاً وذا معنى فعلي.
    // Patch 14: يتحقق من Prosthetics.EditCase تحديدًا (دفاع في العمق) - يحدث قبل أي قراءة/مقارنة/
    // كتابة، فمحاولة استدعاء الدالة بلا صلاحية لا تُنتج حتى SELECT على القاعدة.
    public void UpdateCaseInfo(ProstheticCase updated, int changedByUserId, UserAccount actingUser)
    {
        ProstheticPermissionGuard.Ensure(actingUser, ProstheticPermissionKeys.EditCase);

        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            int? currentWorkTypeId;
            bool currentIncludesUpper, currentIncludesLower;
            string? currentToothScope;
            decimal currentPrice;
            string? currentNotes;

            using (var selectCmd = new SqlCommand(
                @"SELECT WorkTypeID, IncludesUpper, IncludesLower, ToothScope, TotalAgreedPrice, Notes
                  FROM dbo.ProstheticCases WHERE CaseID = @CaseID", conn, tx) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds })
            {
                selectCmd.Parameters.AddWithValue("@CaseID", updated.CaseID);
                using var reader = selectCmd.ExecuteReader();
                if (!reader.Read())
                    throw new InvalidOperationException($"Prosthetic case #{updated.CaseID} not found.");

                currentWorkTypeId = reader["WorkTypeID"] as int?;
                currentIncludesUpper = (bool)reader["IncludesUpper"];
                currentIncludesLower = (bool)reader["IncludesLower"];
                currentToothScope = reader["ToothScope"] as string;
                currentPrice = Convert.ToDecimal(reader["TotalAgreedPrice"]);
                currentNotes = reader["Notes"] as string;
            }

            var changes = new List<(string ActionType, string? OldValue, string? NewValue)>();

            if (currentWorkTypeId != updated.WorkTypeID)
                changes.Add(("WorkTypeChanged", currentWorkTypeId?.ToString(), updated.WorkTypeID?.ToString()));

            if (currentIncludesUpper != updated.IncludesUpper || currentIncludesLower != updated.IncludesLower)
                changes.Add(("ArchChanged", ArchLabel(currentIncludesUpper, currentIncludesLower), ArchLabel(updated.IncludesUpper, updated.IncludesLower)));

            if (!string.Equals(currentToothScope, updated.ToothScope, StringComparison.Ordinal))
                changes.Add(("ToothScopeChanged", currentToothScope, updated.ToothScope));

            if (currentPrice != updated.TotalAgreedPrice)
                changes.Add(("PriceChanged",
                    currentPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    updated.TotalAgreedPrice.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            if (!string.Equals(currentNotes, updated.Notes, StringComparison.Ordinal))
                changes.Add(("NotesChanged", currentNotes, updated.Notes));

            if (changes.Count == 0)
            {
                // لا تغيير فعلي - لا UPDATE ولا History. المعاملة لم تُعدِّل أي بيانات أصلاً، فالـCommit هنا شكلي فقط
                tx.Commit();
                return;
            }

            const string updateSql = @"
                UPDATE dbo.ProstheticCases
                SET WorkTypeID = @WorkTypeID, IncludesUpper = @IncludesUpper, IncludesLower = @IncludesLower,
                    ToothScope = @ToothScope, TotalAgreedPrice = @TotalAgreedPrice, Notes = @Notes
                WHERE CaseID = @CaseID";

            using (var cmd = new SqlCommand(updateSql, conn, tx) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds })
            {
                cmd.Parameters.AddWithValue("@WorkTypeID", (object?)updated.WorkTypeID ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IncludesUpper", updated.IncludesUpper);
                cmd.Parameters.AddWithValue("@IncludesLower", updated.IncludesLower);
                cmd.Parameters.AddWithValue("@ToothScope", (object?)updated.ToothScope ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TotalAgreedPrice", updated.TotalAgreedPrice);
                cmd.Parameters.AddWithValue("@Notes", (object?)updated.Notes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CaseID", updated.CaseID);
                cmd.ExecuteNonQuery();
            }

            foreach (var change in changes)
            {
                InsertHistory(conn, tx, updated.CaseID, changedByUserId, change.ActionType, change.OldValue, change.NewValue);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static string ArchLabel(bool includesUpper, bool includesLower) => (includesUpper, includesLower) switch
    {
        (true, true) => "Upper+Lower",
        (true, false) => "Upper",
        (false, true) => "Lower",
        _ => "-"
    };

    // Patch 15: نفس تبويب History بالضبط (Patch 10) - يتطلب Prosthetics.EditCase (أو الطبيب) لرؤية
    // السجل كاملاً، بالإضافة لوصول على مستوى الحالة نفسها (معيَّن لهذا المستخدم، أو ViewAllCases،
    // أو طبيب) - نفس قاعدة GetById أعلاه. ثم تُصفَّى حركات StageChanged تحديداً بصلاحية
    // ViewStage/EditStage - حرفياً نفس منطق LoadHistory في الواجهة (لا تغيير في القاعدة، فقط نقلها
    // لتطبَّق حتى عند استدعاء هذه الدالة مباشرة بتجاوز الواجهة).
    public List<ProstheticCaseHistoryEntry> GetHistory(int caseId, UserAccount actingUser)
    {
        if (actingUser.Role != UserRole.Doctor && !actingUser.HasProstheticPermission(ProstheticPermissionKeys.EditCase))
        {
            throw new UnauthorizedAccessException("You don't have permission to view this case's history.");
        }

        var assignedResult = _db.ExecuteScalar(
            "SELECT AssignedProsthetistUserID FROM dbo.ProstheticCases WHERE CaseID = @CaseID",
            new SqlParameter("@CaseID", caseId));
        int? assignedProsthetistUserId = assignedResult == null || assignedResult == DBNull.Value
            ? null : Convert.ToInt32(assignedResult);

        if (actingUser.Role != UserRole.Doctor
            && !actingUser.HasProstheticPermission(ProstheticPermissionKeys.ViewAllCases)
            && assignedProsthetistUserId != actingUser.UserID)
        {
            throw new UnauthorizedAccessException("You don't have access to this prosthetic case.");
        }

        const string sql = @"
            SELECT h.HistoryID, h.CaseID, h.ChangedByUserID, u.FullName AS ChangedByUserName,
                   h.ChangedAt, h.ActionType, h.OldValue, h.NewValue, h.Notes
            FROM dbo.ProstheticCaseHistory h
            LEFT JOIN dbo.Users u ON u.UserID = h.ChangedByUserID
            WHERE h.CaseID = @CaseID
            ORDER BY h.ChangedAt DESC";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@CaseID", caseId));
        var result = new List<ProstheticCaseHistoryEntry>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(new ProstheticCaseHistoryEntry
            {
                HistoryID = (int)row["HistoryID"],
                CaseID = (int)row["CaseID"],
                ChangedByUserID = (int)row["ChangedByUserID"],
                ChangedByUserName = row["ChangedByUserName"] as string,
                ChangedAt = (DateTime)row["ChangedAt"],
                ActionType = row["ActionType"].ToString()!,
                OldValue = row["OldValue"] as string,
                NewValue = row["NewValue"] as string,
                Notes = row["Notes"] as string
            });
        }

        var canViewStage = actingUser.Role == UserRole.Doctor
            || actingUser.HasProstheticPermission(ProstheticPermissionKeys.ViewStage)
            || actingUser.HasProstheticPermission(ProstheticPermissionKeys.EditStage);
        if (!canViewStage)
        {
            result = result.Where(e => e.ActionType != "StageChanged").ToList();
        }

        return result;
    }

    private static void InsertHistory(SqlConnection conn, SqlTransaction tx, int caseId, int changedByUserId,
        string actionType, string? oldValue, string? newValue)
    {
        using var cmd = new SqlCommand(@"
            INSERT INTO dbo.ProstheticCaseHistory (CaseID, ChangedByUserID, ChangedAt, ActionType, OldValue, NewValue)
            VALUES (@CaseID, @ChangedByUserID, GETDATE(), @ActionType, @OldValue, @NewValue)", conn, tx) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds };
        cmd.Parameters.AddWithValue("@CaseID", caseId);
        cmd.Parameters.AddWithValue("@ChangedByUserID", changedByUserId);
        cmd.Parameters.AddWithValue("@ActionType", actionType);
        cmd.Parameters.AddWithValue("@OldValue", (object?)oldValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@NewValue", (object?)newValue ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static List<ProstheticCase> MapAll(DataTable table)
    {
        var result = new List<ProstheticCase>();
        foreach (DataRow row in table.Rows)
            result.Add(MapRow(row));
        return result;
    }

    private static ProstheticCase MapRow(DataRow row) => new ProstheticCase
    {
        CaseID = (int)row["CaseID"],
        CaseNumber = (int)row["CaseNumber"],
        PatientID = (int)row["PatientID"],
        PatientFullName = row["PatientFullName"].ToString()!,
        PatientPhoneNumber = row["PatientPhoneNumber"].ToString()!,
        WorkTypeID = row["WorkTypeID"] as int?,
        WorkTypeName = row["WorkTypeName"] as string,
        IncludesUpper = (bool)row["IncludesUpper"],
        IncludesLower = (bool)row["IncludesLower"],
        ToothScope = row["ToothScope"] as string,
        AssignedProsthetistUserID = row["AssignedProsthetistUserID"] as int?,
        AssignedProsthetistName = row["AssignedProsthetistName"] as string,
        StageID = row["StageID"] as int?,
        StageName = row["StageName"] as string,
        CaseStatus = row["CaseStatus"].ToString()!,
        TotalAgreedPrice = Convert.ToDecimal(row["TotalAgreedPrice"]),
        TotalPaid = Convert.ToDecimal(row["TotalPaid"]),
        Notes = row["Notes"] as string,
        CreatedByUserID = (int)row["CreatedByUserID"],
        CreatedByUserName = row["CreatedByUserName"] as string,
        CreatedAt = (DateTime)row["CreatedAt"]
    };

    // حذف حالة ترميم نهائياً من قاعدة البيانات - عملية لا يمكن التراجع عنها (Patch 12، إصلاح ثغرة
    // وظيفية: كانت صلاحية Prosthetics.DeleteCase معرَّفة في القاعدة منذ Patch 1.1 بلا أي Repository
    // أو UI فعلي يستخدمها إطلاقاً). نفس نمط PatientRepository.PermanentlyDelete بالحرف:
    //   - ترتيب الحذف يتبع سلسلة المفاتيح الأجنبية يدوياً (لا يوجد ON DELETE CASCADE في المخطط
    //     عمداً): ProstheticPayments -> ProstheticSessions -> ProstheticCaseHistory -> ProstheticCases،
    //     كل هذا داخل معاملة واحدة (إما يُحذف كل شيء بنجاح أو لا يتغيّر شيء إطلاقاً).
    //   - مصير Sessions/Payments/History: تُحذف نهائياً مع الحالة نفسها، ولا تبقى معلَّقة (Orphaned)
    //     بلا Case مرتبطة بها - القرار المتّسق مع حذف المريض نفسه (يمحو تاريخه الطبي بالكامل أيضاً)،
    //     وليس Soft-Delete: طالما الحالة اختفت، لا معنى لإبقاء جلساتها/مدفوعاتها/سجل تدقيقها منفصلة.
    //     رقم الحالة (CaseNumber) لا يتأثر ولا يُعاد استخدامه أبداً لأنه مصدره SQL SEQUENCE مستقل
    //     تماماً عن وجود الصف نفسه (راجع القسم 4 من وثيقة التصميم).
    //   - دفاع في العمق (Defense in Depth - نقطة أثارها تقرير الاختبار): هذه أول دالة في هذا
    //     الـRepository تتحقق من الصلاحية بنفسها بدل الاعتماد كلياً على فحص الواجهة، لأنها الأخطر
    //     (حذف نهائي)، وأُنشئت جديدة بالكامل في هذا الباتش فلا خطر Regression من تعديل توقيعها.
    //     بقية عمليات الكتابة الحساسة (Add/Edit/DeletePayment، Add/Edit/DeleteSession، تعديل
    //     المرحلة/السعر/المرمم المسؤول) ما تزال تعتمد فقط على فحص الواجهة كما كانت - تحصينها بنفس
    //     الأسلوب يحتاج تعديل تواقيع عدة دوال موجودة فعلياً وناجحة، وأفضّل أن يكون قراراً منفصلاً
    //     صريحاً (راجع الـREADME) بدل تضمينه ضمنياً هنا.
    public void PermanentlyDelete(int caseId, UserAccount actingUser)
    {
        if (actingUser.Role != UserRole.Doctor && !actingUser.HasProstheticPermission(ProstheticPermissionKeys.DeleteCase))
        {
            throw new UnauthorizedAccessException("You don't have permission to delete this prosthetic case.");
        }

        using var conn = _db.GetConnection();
        conn.Open();
        using var transaction = conn.BeginTransaction();

        try
        {
            void Exec(string sql)
            {
                using var cmd = new SqlCommand(sql, conn, transaction) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds };
                cmd.Parameters.AddWithValue("@CaseID", caseId);
                cmd.ExecuteNonQuery();
            }

            Exec("DELETE FROM dbo.ProstheticPayments WHERE CaseID = @CaseID");
            Exec("DELETE FROM dbo.ProstheticSessions WHERE CaseID = @CaseID");
            Exec("DELETE FROM dbo.ProstheticCaseHistory WHERE CaseID = @CaseID");
            Exec("DELETE FROM dbo.ProstheticCases WHERE CaseID = @CaseID");

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
