using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace DentalClinic.UI.Localization;

public enum AppLanguage
{
    Arabic,
    English
}

// يدير كل ما يخص اللغة: القراءة/الحفظ، تحميل قاموس النصوص المناسب، واتجاه الواجهة (RTL/LTR).
// التبديل يتطلب إعادة تشغيل التطبيق عمداً (وليس فورياً) - أبسط وأكثر أماناً من إعادة بناء
// كل نافذة مفتوحة حياً في نفس اللحظة.
public static class LocalizationManager
{
    // ملف تفضيل اللغة مشترك بين تطبيقي الطبيب والاستقبال (في مجلد واحد على مستوى المستخدم)
    // حتى يبقى الاختيار متسقاً بين التطبيقين إن كان يستخدمهما نفس الشخص على نفس الجهاز.
    private static readonly string SettingsFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DentalClinicSystem");

    private static readonly string SettingsFilePath = Path.Combine(SettingsFolder, "language.txt");

    public static AppLanguage CurrentLanguage { get; private set; } = AppLanguage.Arabic;

    // يقرأ اللغة المحفوظة (أو العربية افتراضياً لأول تشغيل) ويدمج قاموس النصوص المناسب
    // في موارد التطبيق. يجب استدعاؤها في بداية App.OnStartup قبل إنشاء أي نافذة.
    public static void Initialize()
    {
        CurrentLanguage = ReadSavedLanguage();
        ApplyResourceDictionary(CurrentLanguage);
    }

    private static AppLanguage ReadSavedLanguage()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var saved = File.ReadAllText(SettingsFilePath).Trim();
                if (Enum.TryParse<AppLanguage>(saved, ignoreCase: true, out var parsed))
                {
                    return parsed;
                }
            }
        }
        catch
        {
            // أي خطأ قراءة (صلاحيات، ملف تالف...) يُتجاهَل ونستخدم القيمة الافتراضية
        }

        return AppLanguage.Arabic;
    }

    // يحفظ تفضيل اللغة الجديد على القرص فقط - لا يُطبَّق على الواجهة الحالية،
    // يتطلب إعادة تشغيل التطبيق (استخدم RestartApplication بعدها).
    public static void SaveLanguagePreference(AppLanguage language)
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            File.WriteAllText(SettingsFilePath, language.ToString());
        }
        catch
        {
            // فشل الحفظ لا يجب أن يمنع المستخدم من المتابعة - سيُطلَب منه فقط الاختيار مجدداً لاحقاً
        }
    }

    private static void ApplyResourceDictionary(AppLanguage language)
    {
        var fileName = language == AppLanguage.Arabic ? "Strings.ar.xaml" : "Strings.en.xaml";
        var uri = new Uri($"pack://application:,,,/DentalClinic.UI;component/Localization/{fileName}");

        var dictionary = new ResourceDictionary { Source = uri };
        Application.Current.Resources.MergedDictionaries.Add(dictionary);
    }

    // دالة مساعدة لجلب نص مترجَم من الكود الخلفي (MessageBox، رسائل الأخطاء، إلخ)
    // بدل تكرار البحث في الموارد يدوياً في كل ملف. تُرجع المفتاح نفسه إن لم يوجد (لسهولة اكتشاف نقص الترجمة).
    public static string T(string key)
    {
        if (Application.Current?.TryFindResource(key) is string value)
        {
            return value;
        }
        return key;
    }

    // نسخة تدعم التنسيق (String.Format) للنصوص التي تحتاج قيماً متغيرة، مثل "عدد المرضى اليوم: {0}"
    public static string T(string key, params object[] args)
    {
        var format = T(key);
        try
        {
            return string.Format(format, args);
        }
        catch
        {
            return format; // إن كان القالب غير متوافق مع عدد المعطيات، نعرض النص كما هو بدل رمي استثناء
        }
    }

    // يعيد تشغيل نفس ملف exe الحالي (DoctorApp.exe أو NurseApp.exe) بنفس المسار، ثم يُغلق العملية الحالية.
    public static void RestartApplication()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                Process.Start(exePath);
            }
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }
}
