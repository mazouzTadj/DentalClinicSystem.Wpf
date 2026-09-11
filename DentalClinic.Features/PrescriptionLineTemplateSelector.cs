using System.Windows;
using System.Windows.Controls;

namespace DentalClinic.Features;

// يختار قالب العرض المناسب لكل سطر في الوصفة: قالب دواء عادي (اسم + جرعة + عدد علب)
// أو قالب شهادة طبية/عطلة مرضية (فقرة نصية واحدة فقط) - بحسب PrescriptionLineViewModel.IsCertificate
public class PrescriptionLineTemplateSelector : DataTemplateSelector
{
    public DataTemplate? MedicationTemplate { get; set; }
    public DataTemplate? CertificateTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is PrescriptionLineViewModel { IsCertificate: true })
        {
            return CertificateTemplate;
        }
        return MedicationTemplate;
    }
}
