using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DentalClinic.Data.Models;

namespace DentalClinic.UI.Converters;

// يحوّل حالة الزيارة (VisitStatus) إلى لون شارة (Badge).
// يعمل مباشرة على القيمة الأصلية للـ enum بدل النص المترجَم المعروض للمستخدم،
// حتى يبقى اللون صحيحاً بغض النظر عن اللغة المختارة (عربي/إنجليزي).
public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value is VisitStatus vs ? vs : (VisitStatus?)null;

        return status switch
        {
            VisitStatus.Waiting => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),       // كهرماني
            VisitStatus.InTreatment => new SolidColorBrush(Color.FromRgb(0x2E, 0x86, 0xDE)),   // أزرق
            VisitStatus.Completed => new SolidColorBrush(Color.FromRgb(0x22, 0xA0, 0x6B)),     // أخضر
            VisitStatus.Cancelled => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),     // أحمر
            VisitStatus.Scheduled => new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6)),     // بنفسجي عصري للمواعيد المجدولة 💜
            _ => new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
