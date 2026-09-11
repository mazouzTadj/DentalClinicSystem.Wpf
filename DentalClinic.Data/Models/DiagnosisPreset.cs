namespace DentalClinic.Data.Models;

// عنصر بقائمة التشخيصات الجاهزة - يديره الطبيب من شاشة "إدارة العلاجات والأدوية" (نفس مكان
// إدارة العلاجات/الأدوية)، ويختار الطبيب منها في ملف المريض بدل الكتابة الحرة في كل مرة
public class DiagnosisPreset
{
    public int DiagnosisID { get; set; }
    public string DiagnosisName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
