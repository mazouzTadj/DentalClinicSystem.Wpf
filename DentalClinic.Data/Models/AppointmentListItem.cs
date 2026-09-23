namespace DentalClinic.Data.Models;

// صف واحد يمثل موعداً مستقبلياً (VisitQueue بحالة Scheduled) - يُستخدم في شاشة "مواعيد اليوم
// والقادمة" المشتركة (AppointmentsOverviewWindow) وفي تبويب "المواعيد" داخل ملف حالة الترميم
// (ProstheticCaseEditWindow). للقراءة فقط - كل عملية تعديل/حذف فعلية تمر عبر QueueRepository
// مباشرة بالـVisitID.
public class AppointmentListItem
{
    public int VisitID { get; set; }
    public int PatientID { get; set; }
    public string PatientFullName { get; set; } = string.Empty;
    public DateTime ScheduledDate { get; set; }
    public string? PlannedTreatment { get; set; }
}
