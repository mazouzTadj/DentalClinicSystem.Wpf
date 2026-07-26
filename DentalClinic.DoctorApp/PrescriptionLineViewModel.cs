namespace DentalClinic.DoctorApp;

// سطر واحد ضمن الوصفة الطبية - يُقرأ فقط عند توليد الـ PDF، لذا لا حاجة لـ INotifyPropertyChanged
public class PrescriptionLineViewModel
{
    public string MedicationName { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
}
