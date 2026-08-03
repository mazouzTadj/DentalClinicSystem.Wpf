using System.IO;
using System.Windows;

namespace DentalClinic.Features;

// يقرأ صورة الشعار المضمَّنة في مشروع DentalClinic.UI المشترك كمصفوفة بايتات،
// لاستخدامها داخل مستندات QuestPDF (التي تحتاج بايتات وليس مسار WPF من نوع pack://)
internal static class LogoLoader
{
    public static byte[]? TryLoadLogoBytes() => TryLoadAssetBytes("logo_icon.png");

    // شعار السن بخط رفيع بسيط (بلا خلفية ملوّنة) - أقرب بصرياً لرأسية "الوصفة الطبية" الورقية التقليدية
    public static byte[]? TryLoadOutlineToothLogoBytes() => TryLoadAssetBytes("logo_icon1.png");

    private static byte[]? TryLoadAssetBytes(string fileName)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/DentalClinic.UI;component/Assets/{fileName}");
            var resourceInfo = Application.GetResourceStream(uri);
            if (resourceInfo == null) return null;

            using var ms = new MemoryStream();
            resourceInfo.Stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            // فشل تحميل الشعار لا يجب أن يمنع توليد المستند إطلاقاً - سيظهر بدون صورة فقط
            return null;
        }
    }
}
