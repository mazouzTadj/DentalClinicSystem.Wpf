namespace DentalClinic.Features;

// سطر واحد ضمن الوصفة الطبية - يُقرأ فقط عند توليد الـ PDF، لذا لا حاجة لـ INotifyPropertyChanged
public class PrescriptionLineViewModel
{
    public string MedicationName { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;

    // عدد العلب - حقل يملأه المستخدم قبل الطباعة، يظهر في الوصفة المطبوعة في موضع "المربع الأصفر".
    // القيمة الافتراضية ثابتة دائماً "1BTS" بغض النظر عن لغة واجهة البرنامج (عربي/إنجليزي) - هذا مقصود
    // بناءً على طلب صريح، وليس نصاً مترجَماً؛ يبقى قابلاً للتعديل يدوياً قبل الطباعة عند الحاجة.
    public string BoxCount { get; set; } = "1BTS";

    // سطر شهادة طبية / عطلة مرضية بدل دواء: يُستخدم فقط حقل MedicationName (كفقرة نصية كاملة)،
    // بينما تبقى الجرعة وعدد العلب فارغين دائماً ولا يظهران لا في الواجهة ولا في الـPDF
    public bool IsCertificate { get; set; } = false;
}
