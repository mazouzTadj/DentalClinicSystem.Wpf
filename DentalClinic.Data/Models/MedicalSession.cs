namespace DentalClinic.Data.Models;

// الملف الطبي السري للجلسة - هذا الكيان يُستخدم فقط داخل تطبيق الطبيب
public class MedicalSession
{
    public int SessionID { get; set; }
    public int? VisitID { get; set; }
    public int PatientID { get; set; }
    public int DoctorID { get; set; }
    public DateTime SessionDateTime { get; set; }
    public string? ChiefComplaint { get; set; }
    public string? Diagnosis { get; set; }
    public string? TreatmentPerformed { get; set; }
    public string? Medication { get; set; }
    public decimal TotalPrice { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount => TotalPrice - PaidAmount;
    public string? Notes { get; set; }
}
