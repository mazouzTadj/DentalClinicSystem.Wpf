using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DentalClinic.Features;

// تخزين تفضيلات تسجيل الدخول محلياً على هذا الجهاز فقط (ليس داخل قاعدة البيانات - هذا إعداد
// واجهة خاص بكل جهاز). يُحفظ في %AppData% (مجلد المستخدم الحالي في ويندوز، لا صلاحيات Admin).
//
// اسم المستخدم يُخزَّن كنص عادي (لا خطورة أمنية حقيقية في ذلك).
//
// كلمة المرور (اختيارية، معطَّلة افتراضياً، المستخدم يُفعّلها صراحةً) تُخزَّن مشفَّرة عبر
// Windows DPAPI (ProtectedData.Protect بنطاق CurrentUser) - أي أنها:
//   - مربوطة بحساب ويندوز الحالي على هذا الجهاز تحديداً؛ لا يمكن فك تشفيرها من جهاز آخر
//     أو حتى من حساب ويندوز آخر على نفس الجهاز، حتى لو نُسخ الملف بالكامل.
//   - هذا هو أسلوب ويندوز القياسي لتخزين أسرار محلية (نفس ما يستخدمه Chrome/Edge لحفظ
//     كلمات المرور محلياً)، وليس تشفيراً مخصَّصاً ضعيفاً.
public static class LoginPreferences
{
    private static string GetPath(string appName)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DentalClinicSystem");
        return Path.Combine(folder, $"login_prefs_{appName}.json");
    }

    private class StoredPrefs
    {
        public string? RememberedUsername { get; set; }
        public string? EncryptedPasswordBase64 { get; set; }
    }

    private static StoredPrefs? LoadRaw(string appName)
    {
        try
        {
            var path = GetPath(appName);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<StoredPrefs>(json);
        }
        catch
        {
            return null;
        }
    }

    public static string? LoadRememberedUsername(string appName) => LoadRaw(appName)?.RememberedUsername;

    // يرجع null إن لم تكن كلمة المرور محفوظة، أو إن فشل فك التشفير لأي سبب (مثلاً نُسخ الملف
    // ليدوياً لجهاز/حساب ويندوز آخر - DPAPI يرفض فك التشفير عمداً في هذه الحالة، وهذا هو المطلوب)
    public static string? LoadRememberedPassword(string appName)
    {
        var raw = LoadRaw(appName);
        if (string.IsNullOrEmpty(raw?.EncryptedPasswordBase64)) return null;

        try
        {
            var encryptedBytes = Convert.FromBase64String(raw.EncryptedPasswordBase64);
            var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch
        {
            return null;
        }
    }

    // username = null يمسح التفضيل بالكامل (اسم المستخدم وكلمة المرور معاً، لأن كلمة المرور
    // بلا اسم مستخدم مرتبط بها لا معنى لها). password = null يُبقي اسم المستخدم فقط دون كلمة مرور.
    public static void SaveRememberedCredentials(string appName, string? username, string? password)
    {
        try
        {
            var path = GetPath(appName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (string.IsNullOrWhiteSpace(username))
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            string? encryptedPasswordBase64 = null;
            if (!string.IsNullOrEmpty(password))
            {
                var passwordBytes = Encoding.UTF8.GetBytes(password);
                var encryptedBytes = ProtectedData.Protect(passwordBytes, null, DataProtectionScope.CurrentUser);
                encryptedPasswordBase64 = Convert.ToBase64String(encryptedBytes);
            }

            var json = JsonSerializer.Serialize(new StoredPrefs
            {
                RememberedUsername = username,
                EncryptedPasswordBase64 = encryptedPasswordBase64
            });
            File.WriteAllText(path, json);
        }
        catch
        {
            // فشل الحفظ لا يجب أن يمنع تسجيل الدخول نفسه من النجاح - نتجاهل بصمت
        }
    }
}
