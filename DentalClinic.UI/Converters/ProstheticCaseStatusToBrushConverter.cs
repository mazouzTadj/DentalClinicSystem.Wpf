using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DentalClinic.Data.Models;

namespace DentalClinic.UI.Converters;

// يحوّل حالة ملف الترميم (ProstheticCaseStatus: Open/Completed/Cancelled) إلى لون شارة (Badge) -
// نفس الفكرة والأسلوب البصري تمامًا مثل StatusToBrushConverter المستخدَم لحالة الزيارة (VisitStatus)
// في تطبيق الطبيب، لكن لثلاث حالات فقط. يعمل على القيمة الأصلية (النص الثابت بالإنجليزية المخزَّن
// في القاعدة) وليس النص المترجَم، حتى يبقى اللون صحيحاً بغض النظر عن اللغة المختارة.
public class ProstheticCaseStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value as string;

        return status switch
        {
            ProstheticCaseStatus.Completed => new SolidColorBrush(Color.FromRgb(0x22, 0xA0, 0x6B)), // أخضر
            ProstheticCaseStatus.Cancelled => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),  // أحمر
            ProstheticCaseStatus.Open => new SolidColorBrush(Color.FromRgb(0x2E, 0x86, 0xDE)),       // أزرق (قيد العمل)
            _ => new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
