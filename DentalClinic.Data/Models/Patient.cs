namespace DentalClinic.Data.Models;

// بيانات المريض الأساسية فقط - هذا الكيان تراه الممرضة بالكامل
// (لا يحتوي على أي تفاصيل تشخيص أو علاج - تلك موجودة في MedicalSession)
public class Patient
{
    public int PatientID { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? BasicMedicalNotes { get; set; } // حساسية / أمراض مزمنة أساسية فقط
    public int RegisteredByUserID { get; set; }
    public DateTime RegisteredAt { get; set; }
    public bool IsActive { get; set; } = true;
}
