namespace DentalClinic.Data.Models;

public enum VisitStatus
{
    Waiting,
    InTreatment,
    Completed,
    Cancelled
}

// عنصر في قائمة الانتظار اليومية - يظهر عند الممرضة والطبيب معاً
public class VisitQueueItem
{
    public int VisitID { get; set; }
    public int PatientID { get; set; }
    public string PatientFullName { get; set; } = string.Empty; // من JOIN مع جدول المرضى للعرض فقط
    public DateTime VisitDate { get; set; }
    public DateTime CheckInTime { get; set; }
    public VisitStatus Status { get; set; }
    public int CreatedByUserID { get; set; }
    public DateTime? StatusUpdatedAt { get; set; }
    public int? StatusUpdatedByUserID { get; set; }
}
