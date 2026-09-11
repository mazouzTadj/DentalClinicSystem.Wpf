using System.Globalization;
using System.Windows.Data;
using DentalClinic.UI.Localization;

namespace DentalClinic.UI.Converters;

// يُستخدَم في الـ XAML مباشرة لأي نص "ديناميكي" (غير قادم من Strings.ar.xaml) قد يحتوي على
// أقواس أو رموز محايدة أخرى، مثل ملاحظات المريض، اسم مركَّب في الكود، وصف عملية مالية...
// مثال:
//   <TextBlock Text="{Binding NoteText, Converter={StaticResource ArabicBidiConverter}}"/>
// مُسجَّل كمورد عام في Theme.xaml بحيث يكون متاحاً في كل نوافذ التطبيق دون تعريفه من جديد.
public class ArabicBidiConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BidiTextFixer.Fix(value as string);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
