using System.Data;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

// سير العمل: الممرضة تعدّل موعداً مستقبلياً موجوداً → يُنشأ طلب "معلَّق" هنا فقط (الموعد الحقيقي في
// VisitQueue لا يتغيَّر إطلاقاً بعد) → الطبيب يوافق أو يرفض → عند الموافقة فقط يُحدَّث VisitQueue فعلياً.
// حجز موعد جديد من الطبيب نفسه (ScheduleAppointmentDialog) لا يمرّ بهذا المسار مطلقاً - مباشر بلا طلب.
public class AppointmentChangeRequestRepository
{
    private readonly DatabaseHelper _db;

    public AppointmentChangeRequestRepository(DatabaseHelper db)
    {
        _db = db;
        EnsureTableExists();
    }

    private void EnsureTableExists()
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AppointmentChangeRequests')
            BEGIN
                CREATE TABLE AppointmentChangeRequests (
                    RequestID INT IDENTITY(1,1) PRIMARY KEY,
                    VisitID INT NOT NULL,
                    RequestedByUserID INT NOT NULL,
                    RequestedAt DATETIME NOT NULL DEFAULT GETDATE(),
                    NewScheduledDate DATETIME NOT NULL,
                    NewPlannedTreatment NVARCHAR(500) NULL,
                    Status NVARCHAR(20) NOT NULL DEFAULT 'Pending',
                    RejectionReason NVARCHAR(300) NULL,
                    RespondedAt DATETIME NULL,
                    RespondedByUserID INT NULL,
                    NurseNotified BIT NOT NULL DEFAULT 0
                );
            END";
        _db.ExecuteNonQuery(sql);
    }

    // إنشاء طلب تعديل جديد - يفشل عمداً لو فيه طلب معلَّق أصلاً لنفس الموعد (تفادي تضارب طلبين بنفس الوقت)
    public bool CreateChangeRequest(int visitId, DateTime newScheduledDate, string? newPlannedTreatment, int requestedByUserId)
    {
        if (HasPendingRequest(visitId)) return false;

        const string sql = @"
            INSERT INTO AppointmentChangeRequests (VisitID, RequestedByUserID, NewScheduledDate, NewPlannedTreatment, Status)
            VALUES (@VisitID, @RequestedByUserID, @NewScheduledDate, @NewPlannedTreatment, 'Pending')";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@VisitID", visitId),
            new SqlParameter("@RequestedByUserID", requestedByUserId),
            new SqlParameter("@NewScheduledDate", newScheduledDate),
            new SqlParameter("@NewPlannedTreatment", (object?)newPlannedTreatment ?? DBNull.Value));

        return true;
    }

    public bool HasPendingRequest(int visitId)
    {
        const string sql = "SELECT COUNT(*) FROM AppointmentChangeRequests WHERE VisitID = @VisitID AND Status = 'Pending'";
        var count = _db.ExecuteScalar(sql, new SqlParameter("@VisitID", visitId));
        return count != null && Convert.ToInt32(count) > 0;
    }

    // كل معرّفات المواعيد (VisitID) اللي عندها طلب معلَّق حالياً - فحص جماعي واحد بدل استعلام لكل صف
    // بقائمة الممرضة (أداء أفضل عند وجود عدة مرضى بموعد قادم بنفس الوقت)
    public HashSet<int> GetPendingVisitIds()
    {
        const string sql = "SELECT DISTINCT VisitID FROM AppointmentChangeRequests WHERE Status = 'Pending'";
        var table = _db.ExecuteQuery(sql);
        var result = new HashSet<int>();
        foreach (DataRow row in table.Rows)
        {
            result.Add((int)row["VisitID"]);
        }
        return result;
    }

    // كل الطلبات المعلَّقة حالياً بكل العيادة - يُفلترها الطبيب لاحقاً بنفس منطق الرؤية المستخدَم بقائمة
    // الانتظار (AssignedDoctorUserID) لأن هذا الجدول لا يملك عمود طبيب مباشر - نجلبه عبر JOIN
    public List<AppointmentChangeRequest> GetPendingRequests()
    {
        const string sql = @"
            SELECT r.RequestID, r.VisitID, v.PatientID, p.FullName AS PatientFullName, p.AssignedDoctorUserID,
                   r.RequestedByUserID, r.RequestedAt,
                   v.ScheduledDate AS CurrentScheduledDate, v.PlannedTreatment AS CurrentPlannedTreatment,
                   r.NewScheduledDate, r.NewPlannedTreatment
            FROM AppointmentChangeRequests r
            INNER JOIN VisitQueue v ON v.VisitID = r.VisitID
            INNER JOIN Patients p ON p.PatientID = v.PatientID
            WHERE r.Status = 'Pending'
            ORDER BY r.RequestedAt ASC";

        var table = _db.ExecuteQuery(sql);
        var result = new List<AppointmentChangeRequest>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(new AppointmentChangeRequest
            {
                RequestID = (int)row["RequestID"],
                VisitID = (int)row["VisitID"],
                PatientID = (int)row["PatientID"],
                PatientFullName = row["PatientFullName"].ToString()!,
                PatientAssignedDoctorUserID = row["AssignedDoctorUserID"] == DBNull.Value ? null : Convert.ToInt32(row["AssignedDoctorUserID"]),
                RequestedByUserID = (int)row["RequestedByUserID"],
                RequestedAt = (DateTime)row["RequestedAt"],
                CurrentScheduledDate = (DateTime)row["CurrentScheduledDate"],
                CurrentPlannedTreatment = row["CurrentPlannedTreatment"] as string,
                NewScheduledDate = (DateTime)row["NewScheduledDate"],
                NewPlannedTreatment = row["NewPlannedTreatment"] as string,
                Status = AppointmentChangeRequestStatus.Pending
            });
        }
        return result;
    }

    // معرّفات الأطباء المُسنَدين للمرضى أصحاب الطلبات المعلَّقة - يُستخدم لتفعيل صوت التنبيه فقط
    // لمن يعنيه الأمر فعلاً، بدل تنبيه كل الأطباء بكل طلب حتى لو لمريض طبيب آخر
    public int? GetPatientAssignedDoctorForRequest(int requestId)
    {
        const string sql = @"
            SELECT p.AssignedDoctorUserID
            FROM AppointmentChangeRequests r
            INNER JOIN VisitQueue v ON v.VisitID = r.VisitID
            INNER JOIN Patients p ON p.PatientID = v.PatientID
            WHERE r.RequestID = @RequestID";

        var result = _db.ExecuteScalar(sql, new SqlParameter("@RequestID", requestId));
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    // موافقة: يُحدَّث الموعد الحقيقي في VisitQueue فعلياً، ويُغلَق الطلب بحالة "مقبول"
    public void ApproveRequest(int requestId, int respondedByUserId)
    {
        const string getSql = "SELECT VisitID, NewScheduledDate, NewPlannedTreatment FROM AppointmentChangeRequests WHERE RequestID = @RequestID";
        var table = _db.ExecuteQuery(getSql, new SqlParameter("@RequestID", requestId));
        if (table.Rows.Count == 0) return;

        var row = table.Rows[0];
        var visitId = (int)row["VisitID"];
        var newDate = (DateTime)row["NewScheduledDate"];
        var newTreatment = row["NewPlannedTreatment"] as string;

        const string updateVisitSql = @"
            UPDATE VisitQueue
            SET ScheduledDate = @NewDate, VisitDate = @NewVisitDate, PlannedTreatment = @NewTreatment
            WHERE VisitID = @VisitID";
        _db.ExecuteNonQuery(updateVisitSql,
            new SqlParameter("@NewDate", newDate),
            new SqlParameter("@NewVisitDate", newDate.Date),
            new SqlParameter("@NewTreatment", (object?)newTreatment ?? DBNull.Value),
            new SqlParameter("@VisitID", visitId));

        const string updateRequestSql = @"
            UPDATE AppointmentChangeRequests
            SET Status = 'Approved', RespondedAt = GETDATE(), RespondedByUserID = @RespondedByUserID
            WHERE RequestID = @RequestID";
        _db.ExecuteNonQuery(updateRequestSql,
            new SqlParameter("@RespondedByUserID", respondedByUserId),
            new SqlParameter("@RequestID", requestId));
    }

    // رفض: الموعد الحقيقي يبقى كما هو بلا أي تغيير، مع سبب يظهر لاحقاً للممرضة
    public void RejectRequest(int requestId, int respondedByUserId, string? reason)
    {
        const string sql = @"
            UPDATE AppointmentChangeRequests
            SET Status = 'Rejected', RespondedAt = GETDATE(), RespondedByUserID = @RespondedByUserID, RejectionReason = @Reason
            WHERE RequestID = @RequestID";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@RespondedByUserID", respondedByUserId),
            new SqlParameter("@Reason", (object?)reason ?? DBNull.Value),
            new SqlParameter("@RequestID", requestId));
    }

    // طلبات وصل ردّها (موافقة/رفض) ولم تُعرَض للممرضة اللي قدّمتها بعد - تُستخدم لعرض إشعار لها
    public List<AppointmentChangeRequest> GetUnnotifiedResolvedRequests(int nurseUserId)
    {
        const string sql = @"
            SELECT r.RequestID, r.VisitID, v.PatientID, p.FullName AS PatientFullName,
                   r.RequestedByUserID, r.RequestedAt, r.Status, r.RejectionReason,
                   v.ScheduledDate AS CurrentScheduledDate, v.PlannedTreatment AS CurrentPlannedTreatment,
                   r.NewScheduledDate, r.NewPlannedTreatment
            FROM AppointmentChangeRequests r
            INNER JOIN VisitQueue v ON v.VisitID = r.VisitID
            INNER JOIN Patients p ON p.PatientID = v.PatientID
            WHERE r.RequestedByUserID = @NurseUserID AND r.Status IN ('Approved', 'Rejected') AND r.NurseNotified = 0";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@NurseUserID", nurseUserId));
        var result = new List<AppointmentChangeRequest>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(new AppointmentChangeRequest
            {
                RequestID = (int)row["RequestID"],
                VisitID = (int)row["VisitID"],
                PatientID = (int)row["PatientID"],
                PatientFullName = row["PatientFullName"].ToString()!,
                RequestedByUserID = (int)row["RequestedByUserID"],
                RequestedAt = (DateTime)row["RequestedAt"],
                CurrentScheduledDate = (DateTime)row["CurrentScheduledDate"],
                CurrentPlannedTreatment = row["CurrentPlannedTreatment"] as string,
                NewScheduledDate = (DateTime)row["NewScheduledDate"],
                NewPlannedTreatment = row["NewPlannedTreatment"] as string,
                Status = row["Status"].ToString() == "Approved" ? AppointmentChangeRequestStatus.Approved : AppointmentChangeRequestStatus.Rejected,
                RejectionReason = row["RejectionReason"] as string
            });
        }
        return result;
    }

    public void MarkNotified(int requestId)
    {
        const string sql = "UPDATE AppointmentChangeRequests SET NurseNotified = 1 WHERE RequestID = @RequestID";
        _db.ExecuteNonQuery(sql, new SqlParameter("@RequestID", requestId));
    }
}
