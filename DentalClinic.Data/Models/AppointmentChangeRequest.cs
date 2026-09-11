namespace DentalClinic.Data.Models;

public enum AppointmentChangeRequestStatus
{
    Pending,
    Approved,
    Rejected
}

// طلب تعديل على موعد مستقبلي مقدَّم من الممرضة - لا يُطبَّق فعلياً على الموعد الحقيقي (VisitQueue)
// إلا بعد موافقة الطبيب صراحةً. الحجز الجديد (من الطبيب نفسه) لا يمر بهذا المسار إطلاقاً - مباشر بلا موافقة.
public class AppointmentChangeRequest
{
    public int RequestID { get; set; }
    public int VisitID { get; set; }
    public int PatientID { get; set; }
    public string PatientFullName { get; set; } = string.Empty;
    public int? PatientAssignedDoctorUserID { get; set; } // لفلترة أي طلب يظهر لأي طبيب (نفس قاعدة رؤية قائمة الانتظار)

    public int RequestedByUserID { get; set; }
    public DateTime RequestedAt { get; set; }

    public DateTime CurrentScheduledDate { get; set; }
    public string? CurrentPlannedTreatment { get; set; }

    public DateTime NewScheduledDate { get; set; }
    public string? NewPlannedTreatment { get; set; }

    public AppointmentChangeRequestStatus Status { get; set; }
    public string? RejectionReason { get; set; }
}
