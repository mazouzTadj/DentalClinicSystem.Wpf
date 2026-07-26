namespace DentalClinic.Data.Models;

// دواء جاهز في القائمة السريعة لتسهيل تحرير الوصفات الطبية
public class MedicationPreset
{
    public int MedicationID { get; set; }
    public string MedicationName { get; set; } = string.Empty;
    public string? DefaultDosage { get; set; }
    public string? DefaultDuration { get; set; }
    public bool IsActive { get; set; } = true;
}
