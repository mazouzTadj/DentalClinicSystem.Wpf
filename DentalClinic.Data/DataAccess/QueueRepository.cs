using Microsoft.Data.SqlClient;
using System.Data;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

public class QueueRepository
{
    private readonly DatabaseHelper _db;

    public QueueRepository(DatabaseHelper db)
    {
        _db = db;
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

    // قائمة انتظار اليوم كاملة - تُستخدم من تطبيقَي الممرضة والطبيب معاً
    public List<VisitQueueItem> GetTodayQueue()
    {
        const string sql = @"
            SELECT q.VisitID, q.PatientID, p.FullName AS PatientFullName, q.VisitDate,
                   q.CheckInTime, q.Status, q.CreatedByUserID, q.StatusUpdatedAt, q.StatusUpdatedByUserID,
                   q.ScheduledDate -- أضفنا هذا السطر
            FROM VisitQueue q
            INNER JOIN Patients p ON p.PatientID = q.PatientID
            WHERE q.VisitDate = CAST(GETDATE() AS DATE)
            ORDER BY q.CheckInTime ASC";

        var table = _db.ExecuteQuery(sql);
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
                // جلبنا وقت الموعد المجدول
                ScheduledDate = row["ScheduledDate"] == DBNull.Value ? null : (DateTime?)row["ScheduledDate"]
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

    // 1. إضافة موعد مستقبلي
    public bool ScheduleAppointment(int patientId, DateTime scheduledDate, int createdByUserId)
    {
        const string insertSql = @"
            INSERT INTO VisitQueue (PatientID, VisitDate, ScheduledDate, Status, CreatedByUserID)
            VALUES (@PatientID, @VisitDate, @ScheduledDate, 'Scheduled', @CreatedByUserID)";

        try
        {
            _db.ExecuteNonQuery(insertSql,
                new SqlParameter("@PatientID", patientId),
                new SqlParameter("@VisitDate", scheduledDate.Date), // نحفظ التاريخ فقط للبحث
                new SqlParameter("@ScheduledDate", scheduledDate),  // نحفظ التاريخ والوقت معاً
                new SqlParameter("@CreatedByUserID", createdByUserId));
            return true;
        }
        catch
        {
            return false;
        }
    }

    // جلب قائمة المرضى ليوم محدد (تُستخدم لفلترة الأيام المستقبلية)
    public List<VisitQueueItem> GetQueueByDate(DateTime selectedDate)
    {
        const string sql = @"
            SELECT q.VisitID, q.PatientID, p.FullName AS PatientFullName, q.VisitDate,
                   q.CheckInTime, q.Status, q.CreatedByUserID, q.StatusUpdatedAt, q.StatusUpdatedByUserID,
                   q.ScheduledDate -- أضفنا هذا السطر
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
                // جلبنا وقت الموعد المجدول
                ScheduledDate = row["ScheduledDate"] == DBNull.Value ? null : (DateTime?)row["ScheduledDate"]
            });
        }
        return result;
    }
}
