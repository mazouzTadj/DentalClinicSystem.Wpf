namespace DentalClinic.Data.Models;

// شهادة طبية / عطلة مرضية جاهزة في القائمة السريعة - نفس فكرة MedicationPreset تماماً،
// لكن بدل جرعة/مدة نخزّن هنا نص الفقرة الجاهز (DefaultText) الذي يُطبع مباشرة في الوصفة
public class CertificatePreset
{
    public int CertificateID { get; set; }
    public string CertificateName { get; set; } = string.Empty;
    public string? DefaultText { get; set; }
    public bool IsActive { get; set; } = true;
}
