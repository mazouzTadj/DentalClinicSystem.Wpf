using Microsoft.Data.SqlClient;
using System.Data;
using System.Linq;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

public class QueueRepository
{
    private readonly DatabaseHelper _db;

    public QueueRepository(DatabaseHelper db)
    {
        _db = db;
        EnsurePlannedTreatmentColumnExists();
    }

    // عمود العلاج المخطَّط للموعد القادم - migration آمنة لقواعد البيانات القديمة
    private void EnsurePlannedTreatmentColumnExists()
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'PlannedTreatment' AND Object_ID = Object_ID(N'VisitQueue'))
            BEGIN
                ALTER TABLE VisitQueue ADD PlannedTreatment NVARCHAR(500) NULL;
            END";
        _db.ExecuteNonQuery(sql);
    }

    // إضافة مريض لقائمة انتظار اليوم - يمنع تكرار نفس المريض إن كانت له زيارة لم تكتمل اليوم بعد
    public (bool Success, string Message, int VisitID) AddToQueue(int patientId, int createdByUserId)
    {
        const string checkSql = @"
            SELECT COUNT(*) FROM VisitQueue
            WHERE PatientID = @PatientID
              AND VisitDate = CAST(GETDATE() AS DATE)
              AND Status IN (N'Waiting', N'InTreatment')";

        var existing = _db.ExecuteScalar(checkSql, new SqlParameter("@PatientID", patientId));
        if (existing != null && Convert.ToInt32(existing) > 0)
        {
            return (false, "This patient is already in today's queue", 0);
        }

        const string insertSql = @"
            INSERT INTO VisitQueue (PatientID, CreatedByUserID)
            VALUES (@PatientID, @CreatedByUserID)";

        var visitId = _db.ExecuteInsertAndGetId(insertSql,
            new SqlParameter("@PatientID", patientId),
            new SqlParameter("@CreatedByUserID", createdByUserId));

        return (true, "Added to the queue", visitId);
    }

    // قائمة انتظار اليوم كاملة بلا أي تقييد - راجع GetTodayQueue(allowedDoctorUserIds, includeUnassigned)
    // أدناه للنسخة المُقيَّدة (وهي المُستخدَمة فعلياً من كل الشاشات بعد ميزة فصل الأطباء)
    public List<VisitQueueItem> GetTodayQueue() => GetTodayQueue(null, true);

    // allowedDoctorUserIds/includeUnassigned: نفس المنطق تماماً المشروح في PatientRepository.Search -
    //   null => بلا تقييد (يُستخدم فقط داخلياً/للتوافق مع الخلف)
    //   قائمة (حتى فارغة) => يظهر فقط زيارات المرضى المُسنَدين لأحد هذي الأطباء + الغير مُسنَدين إن سُمح بهم
    public List<VisitQueueItem> GetTodayQueue(List<int>? allowedDoctorUserIds, bool includeUnassigned)
    {
        var sql = @"
            SELECT q.VisitID, q.PatientID, p.FullName AS PatientFullName, q.VisitDate,
                   q.CheckInTime, q.Status, q.CreatedByUserID, q.StatusUpdatedAt, q.StatusUpdatedByUserID,
                   
                   -- هنا السر: نبحث عن أقرب موعد مستقبلي لهذا المريض
                   (SELECT TOP 1 ScheduledDate 
                    FROM VisitQueue f 
                    WHERE f.PatientID = q.PatientID 
                      AND f.VisitDate > q.VisitDate 
                      AND f.Status = 'Scheduled' 
                    ORDER BY f.ScheduledDate ASC) AS FutureScheduledDate,

                   (SELECT TOP 1 VisitID
                    FROM VisitQueue f
                    WHERE f.PatientID = q.PatientID
                      AND f.VisitDate > q.VisitDate
                      AND f.Status = 'Scheduled'
                    ORDER BY f.ScheduledDate ASC) AS FutureVisitID,

                   (SELECT TOP 1 PlannedTreatment
                    FROM VisitQueue f
                    WHERE f.PatientID = q.PatientID
                      AND f.VisitDate > q.VisitDate
                      AND f.Status = 'Scheduled'
                    ORDER BY f.ScheduledDate ASC) AS FuturePlannedTreatment

            FROM VisitQueue q
            INNER JOIN Patients p ON p.PatientID = q.PatientID
            WHERE q.VisitDate = CAST(GETDATE() AS DATE)";

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

        // ترتيب الطابور: المرضى النشطون (انتظار/قيد المعالجة/موعد) أولاً حسب وقت الحضور كالمعتاد،
        // ثم من انتهت معالجته (Completed) أو أُلغيت زيارته (Cancelled) يُدفَع تلقائياً لأسفل القائمة
        // بمجرد تغيير حالته - دون الحاجة لأي إجراء يدوي من الطبيب لإعادة الترتيب
        sql += @"
            ORDER BY
                CASE WHEN q.Status IN ('Completed', 'Cancelled') THEN 1 ELSE 0 END ASC,
                q.CheckInTime ASC";

        var table = _db.ExecuteQuery(sql, parameters.ToArray());
        var result = new List<VisitQueueItem>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(new VisitQueueItem
            {
                VisitID = (int)row["VisitID"],
                PatientID = (int)row["PatientID"],
                PatientFullName = row["PatientFullName"].ToString()!,
                VisitDate = (DateTime)row["VisitDate"],
                CheckInTime = (DateTime)row["CheckInTime"],
                Status = Enum.Parse<VisitStatus>(row["Status"].ToString()!),
                CreatedByUserID = (int)row["CreatedByUserID"],
                StatusUpdatedAt = row["StatusUpdatedAt"] as DateTime?,
                StatusUpdatedByUserID = row["StatusUpdatedByUserID"] as int?,
                // نقرأ الموعد المستقبلي بدلاً من الحالي
                ScheduledDate = row["FutureScheduledDate"] == DBNull.Value ? null : (DateTime?)row["FutureScheduledDate"],
                FutureVisitID = row["FutureVisitID"] == DBNull.Value ? null : (int?)Convert.ToInt32(row["FutureVisitID"]),
                FuturePlannedTreatment = row["FuturePlannedTreatment"] as string
            });
        }
        return result;
    }

    // تغيير حالة الزيارة إلى "ملغاة" - تُستخدم من تطبيقَي الممرضة والطبيب معاً
    public bool CancelVisit(int visitId, int cancelledByUserId)
    {
        try
        {
            UpdateStatus(visitId, VisitStatus.Cancelled, cancelledByUserId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // إصلاح: لم يكن هناك أي طريقة لتحويل موعد محجوز (Scheduled) إلى "في الانتظار" (Waiting) عند
    // وصول المريض فعلياً يوم موعده - فكانت الزيارة تبقى عالقة بحالة Scheduled بلا أي إجراء ممكن من
    // الواجهة (لا "بدء معالجة"، ولا "إلغاء"). هذه الدالة تسجّل "تسجيل الحضور" الفعلي: تحدّث الحالة إلى
    // Waiting وتضبط CheckInTime على الوقت الحالي (بدل وقت الحجز الأصلي) لضمان ترتيب صحيح في قائمة الانتظار.
    public bool CheckInScheduledVisit(int visitId, int checkedInByUserId)
    {
        const string sql = @"
            UPDATE VisitQueue
            SET Status = @Status, CheckInTime = GETDATE(),
                StatusUpdatedAt = GETDATE(), StatusUpdatedByUserID = @UpdatedByUserID
            WHERE VisitID = @VisitID AND Status = 'Scheduled'";

        try
        {
            var rowsAffected = _db.ExecuteNonQuery(sql,
                new SqlParameter("@Status", VisitStatus.Waiting.ToString()),
                new SqlParameter("@UpdatedByUserID", checkedInByUserId),
                new SqlParameter("@VisitID", visitId));
            return rowsAffected > 0;
        }
        catch
        {
            return false;
        }
    }

    // تغيير حالة الزيارة - سيُستخدم من تطبيق الطبيب في الخطوة القادمة
    public void UpdateStatus(int visitId, VisitStatus newStatus, int updatedByUserId)
    {
        const string sql = @"
            UPDATE VisitQueue
            SET Status = @Status, StatusUpdatedAt = GETDATE(), StatusUpdatedByUserID = @UpdatedByUserID
            WHERE VisitID = @VisitID";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@Status", newStatus.ToString()),
            new SqlParameter("@UpdatedByUserID", updatedByUserId),
            new SqlParameter("@VisitID", visitId));
    }
    // --------------------------------------------------------
    // الميزات الجديدة: حجز المواعيد المستقبلية
    // --------------------------------------------------------

    // 1. إضافة موعد مستقبلي - تاريخ فقط بلا وقت (حُذف حقل الوقت بناءً على طلب العميل)، مع علاج
    // مخطَّط اختياري. لا يمرّ هذا المسار بأي موافقة - الطبيب هو من يحجز مباشرة عبر ScheduleAppointmentDialog
    public bool ScheduleAppointment(int patientId, DateTime scheduledDate, string? plannedTreatment, int createdByUserId)
    {
        const string insertSql = @"
            INSERT INTO VisitQueue (PatientID, VisitDate, ScheduledDate, PlannedTreatment, Status, CreatedByUserID)
            VALUES (@PatientID, @VisitDate, @ScheduledDate, @PlannedTreatment, 'Scheduled', @CreatedByUserID)";

        try
        {
            _db.ExecuteNonQuery(insertSql,
                new SqlParameter("@PatientID", patientId),
                new SqlParameter("@VisitDate", scheduledDate.Date),
                new SqlParameter("@ScheduledDate", scheduledDate.Date), // تاريخ فقط الآن - لا وقت مخزَّن
                new SqlParameter("@PlannedTreatment", (object?)plannedTreatment ?? DBNull.Value),
                new SqlParameter("@CreatedByUserID", createdByUserId));
            return true;
        }
        catch
        {
            return false;
        }
    }

    // من يستطيع تعديل/حذف موعد مستقبلي مباشرة (بلا مرور بمسار طلبات الممرضة)؟ الطبيب دائماً، أو
    // مرمم مُنِح صراحة Prosthetics.ManageAppointments (ميزة إضافة نظام حجز المواعيد لتطبيق المرمم) -
    // نفس نمط ProstheticPermissionGuard تماماً، لكن هنا محلياً لأن QueueRepository مشتركة بين
    // الطبيب/الممرضة/المرمم ولا تعتمد على ProstheticPermissionGuard في بقية دوالها.
    private static bool CanManageAppointmentsDirectly(UserAccount actingUser) =>
        actingUser.Role == UserRole.Doctor
        || (actingUser.Role == UserRole.Prosthetist && actingUser.HasProstheticPermission(ProstheticPermissionKeys.ManageAppointments));

    // تعديل موعد مستقبلي مباشرة من تطبيق الطبيب أو تطبيق المرمم (بصلاحية ManageAppointments) فقط.
    // لا يستخدم مسار طلبات الممرضة. الحارس داخل طبقة البيانات إلزامي حتى لا يمكن استدعاء العملية
    // من واجهة غير مخوّلة.
    public bool UpdateFutureAppointment(int visitId, DateTime scheduledDate, string? plannedTreatment, UserAccount actingUser)
    {
        if (!CanManageAppointmentsDirectly(actingUser))
            throw new UnauthorizedAccessException("Only doctors, or prosthetists granted Prosthetics.ManageAppointments, can directly edit future appointments.");

        if (scheduledDate.Date <= DateTime.Today)
            throw new ArgumentException("The appointment date must be in the future.", nameof(scheduledDate));

        const string sql = @"
            UPDATE VisitQueue
            SET VisitDate = @ScheduledDate,
                ScheduledDate = @ScheduledDate,
                PlannedTreatment = @PlannedTreatment
            WHERE VisitID = @VisitID
              AND Status = 'Scheduled'
              AND CAST(ScheduledDate AS DATE) > CAST(GETDATE() AS DATE)";

        var rows = _db.ExecuteNonQuery(sql,
            new SqlParameter("@ScheduledDate", scheduledDate.Date),
            new SqlParameter("@PlannedTreatment", (object?)plannedTreatment ?? DBNull.Value),
            new SqlParameter("@VisitID", visitId));

        return rows > 0;
    }

    // حذف الموعد المستقبلي نفسه فقط. لا يحذف المريض ولا أي زيارة طبية أخرى.
    public bool DeleteFutureAppointment(int visitId, UserAccount actingUser)
    {
        if (!CanManageAppointmentsDirectly(actingUser))
            throw new UnauthorizedAccessException("Only doctors, or prosthetists granted Prosthetics.ManageAppointments, can directly delete future appointments.");

        const string sql = @"
            DELETE FROM VisitQueue
            WHERE VisitID = @VisitID
              AND Status = 'Scheduled'
              AND CAST(ScheduledDate AS DATE) > CAST(GETDATE() AS DATE)";

        var rows = _db.ExecuteNonQuery(sql, new SqlParameter("@VisitID", visitId));
        return rows > 0;
    }

    // قائمة المرضى المحجوزين بنفس اليوم المحدَّد + علاجهم المخطَّط - تُعرض حياً بنافذة الحجز حتى يشوف
    // الطبيب حِمل ذلك اليوم قبل ما يوافق على حجز إضافي (قد يتجاوز وقت العمل المتاح)
    public List<(string PatientFullName, string? PlannedTreatment)> GetScheduledPatientsForDate(DateTime date)
    {
        const string sql = @"
            SELECT p.FullName, v.PlannedTreatment
            FROM VisitQueue v
            INNER JOIN Patients p ON p.PatientID = v.PatientID
            WHERE v.VisitDate = CAST(@Date AS DATE) AND v.Status = 'Scheduled'
            ORDER BY p.FullName";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@Date", date.Date));
        var result = new List<(string, string?)>();

        foreach (DataRow row in table.Rows)
        {
            result.Add((row["FullName"].ToString()!, row["PlannedTreatment"] as string));
        }
        return result;
    }

    // موعد مستقبلي واحد بعينه (عبر VisitID الخاص به هو، وليس زيارة اليوم) - تُستخدم لتعبئة نافذة "تعديل الموعد"
    public (DateTime ScheduledDate, string? PlannedTreatment)? GetScheduledVisit(int visitId)
    {
        const string sql = "SELECT ScheduledDate, PlannedTreatment FROM VisitQueue WHERE VisitID = @VisitID AND Status = 'Scheduled'";
        var table = _db.ExecuteQuery(sql, new SqlParameter("@VisitID", visitId));
        if (table.Rows.Count == 0) return null;

        var row = table.Rows[0];
        return ((DateTime)row["ScheduledDate"], row["PlannedTreatment"] as string);
    }

    // جلب قائمة المرضى ليوم محدد (تُستخدم لفلترة الأيام المستقبلية)
    public List<VisitQueueItem> GetQueueByDate(DateTime selectedDate)
    {
        const string sql = @"
            SELECT q.VisitID, q.PatientID, p.FullName AS PatientFullName, q.VisitDate,
                   q.CheckInTime, q.Status, q.CreatedByUserID, q.StatusUpdatedAt, q.StatusUpdatedByUserID,
                   
                   -- هنا السر: نبحث عن أقرب موعد مستقبلي لهذا المريض
                   (SELECT TOP 1 ScheduledDate 
                    FROM VisitQueue f 
                    WHERE f.PatientID = q.PatientID 
                      AND f.VisitDate > q.VisitDate 
                      AND f.Status = 'Scheduled' 
                    ORDER BY f.ScheduledDate ASC) AS FutureScheduledDate

            FROM VisitQueue q
            INNER JOIN Patients p ON p.PatientID = q.PatientID
            WHERE q.VisitDate = CAST(@SelectedDate AS DATE)
            ORDER BY q.CheckInTime ASC";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@SelectedDate", selectedDate.Date));
        var result = new List<VisitQueueItem>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(new VisitQueueItem
            {
                VisitID = (int)row["VisitID"],
                PatientID = (int)row["PatientID"],
                PatientFullName = row["PatientFullName"].ToString()!,
                VisitDate = (DateTime)row["VisitDate"],
                CheckInTime = row["CheckInTime"] == DBNull.Value ? DateTime.MinValue : (DateTime)row["CheckInTime"],
                Status = Enum.Parse<VisitStatus>(row["Status"].ToString()!),
                CreatedByUserID = (int)row["CreatedByUserID"],
                StatusUpdatedAt = row["StatusUpdatedAt"] as DateTime?,
                StatusUpdatedByUserID = row["StatusUpdatedByUserID"] as int?,
                // نقرأ الموعد المستقبلي بدلاً من الحالي
                ScheduledDate = row["FutureScheduledDate"] == DBNull.Value ? null : (DateTime?)row["FutureScheduledDate"]
            });
        }
        return result;
    }

    // --------------------------------------------------------
    // ميزة جديدة: عرض موحَّد لمواعيد اليوم والأيام القادمة
    // --------------------------------------------------------

    // كل المواعيد المستقبلية (Status='Scheduled') لمريض واحد بعينه - تُستخدم في تبويب "المواعيد"
    // داخل ملف حالة الترميم (ProstheticCaseEditWindow)، حيث قد يملك المريض أكثر من موعد قادم واحد.
    public List<AppointmentListItem> GetFutureAppointmentsForPatient(int patientId)
    {
        const string sql = @"
            SELECT VisitID, PatientID, ScheduledDate, PlannedTreatment
            FROM VisitQueue
            WHERE PatientID = @PatientID
              AND Status = 'Scheduled'
              AND ScheduledDate IS NOT NULL
            ORDER BY ScheduledDate ASC";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@PatientID", patientId));
        var result = new List<AppointmentListItem>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(new AppointmentListItem
            {
                VisitID = (int)row["VisitID"],
                PatientID = (int)row["PatientID"],
                ScheduledDate = (DateTime)row["ScheduledDate"],
                PlannedTreatment = row["PlannedTreatment"] as string
            });
        }
        return result;
    }

    // مواعيد اليوم + كل الأيام القادمة (بلا سقف زمني - "القادمة" كما وردت في المتطلبات)، مع اسم
    // المريض جاهزاً للعرض مباشرة. نفس منطق تقييد allowedDoctorUserIds/includeUnassigned المستخدَم
    // في GetTodayQueue بالضبط (راجع التعليق هناك) - يسمح لتطبيق الطبيب بعرض مواعيد مرضاه فقط.
    // null لكليهما => بلا أي تقييد (تُستخدم من تطبيق المرمم عبر GetUpcomingAppointmentsForPatients
    // بدلاً من ذلك، وليس من هنا مباشرة، إلا إن أراد الطبيب رؤية الكل).
    public List<AppointmentListItem> GetUpcomingAppointments(List<int>? allowedDoctorUserIds, bool includeUnassigned)
    {
        var sql = @"
            SELECT q.VisitID, q.PatientID, p.FullName AS PatientFullName, q.ScheduledDate, q.PlannedTreatment
            FROM VisitQueue q
            INNER JOIN Patients p ON p.PatientID = q.PatientID
            WHERE q.Status = 'Scheduled'
              AND q.ScheduledDate IS NOT NULL
              AND CAST(q.ScheduledDate AS DATE) >= CAST(GETDATE() AS DATE)";

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

        sql += " ORDER BY q.ScheduledDate ASC, p.FullName ASC";

        var table = _db.ExecuteQuery(sql, parameters.ToArray());
        return MapAppointmentRows(table);
    }

    // نفس فكرة GetUpcomingAppointments أعلاه، لكن مُقيَّدة بمجموعة PatientID محدَّدة صراحةً - تُستخدم
    // من تطبيق المرمم (لا مفهوم "طبيب مُسنَد" هناك؛ بدلاً من ذلك نُقيِّد بمرضى الحالات الظاهرة له
    // فعلياً، راجع ProstheticCaseRepository.GetVisiblePatientIds). قائمة فارغة => لا نتائج (وليس "الكل").
    public List<AppointmentListItem> GetUpcomingAppointmentsForPatients(IReadOnlyCollection<int> patientIds)
    {
        if (patientIds == null || patientIds.Count == 0) return new List<AppointmentListItem>();

        var placeholders = patientIds.Select((id, i) => $"@P{i}").ToList();
        var parameters = patientIds.Select((id, i) => new SqlParameter($"@P{i}", id)).ToArray();

        var sql = $@"
            SELECT q.VisitID, q.PatientID, p.FullName AS PatientFullName, q.ScheduledDate, q.PlannedTreatment
            FROM VisitQueue q
            INNER JOIN Patients p ON p.PatientID = q.PatientID
            WHERE q.Status = 'Scheduled'
              AND q.ScheduledDate IS NOT NULL
              AND CAST(q.ScheduledDate AS DATE) >= CAST(GETDATE() AS DATE)
              AND q.PatientID IN ({string.Join(",", placeholders)})
            ORDER BY q.ScheduledDate ASC, p.FullName ASC";

        var table = _db.ExecuteQuery(sql, parameters);
        return MapAppointmentRows(table);
    }

    private static List<AppointmentListItem> MapAppointmentRows(DataTable table)
    {
        var result = new List<AppointmentListItem>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(new AppointmentListItem
            {
                VisitID = (int)row["VisitID"],
                PatientID = (int)row["PatientID"],
                PatientFullName = row["PatientFullName"].ToString()!,
                ScheduledDate = (DateTime)row["ScheduledDate"],
                PlannedTreatment = row["PlannedTreatment"] as string
            });
        }
        return result;
    }
}
