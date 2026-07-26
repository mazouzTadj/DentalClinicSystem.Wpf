using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DentalClinic.UI.Converters;

// يحوّل نص الحالة (Waiting / InTreatment / Completed / Cancelled / Scheduled) إلى لون شارة (Badge)
public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value?.ToString() ?? string.Empty;

        return status switch
        {
            "Waiting" => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),       // كهرماني
            "In Treatment" or "InTreatment" => new SolidColorBrush(Color.FromRgb(0x2E, 0x86, 0xDE)), // أزرق
            "Completed" => new SolidColorBrush(Color.FromRgb(0x22, 0xA0, 0x6B)),     // أخضر
            "Cancelled" => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),     // أحمر
            "Scheduled" => new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6)),     // بنفسجي عصري للمواعيد المجدولة 💜
            _ => new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}