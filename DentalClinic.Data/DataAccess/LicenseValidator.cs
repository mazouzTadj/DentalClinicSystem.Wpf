using System.Security.Cryptography;
using System.Text;

namespace DentalClinic.Data.DataAccess;

// ============================================================
// نظام الترخيص: ترخيص واحد يغطي كل أجهزة العيادة (وليس ترخيصاً لكل جهاز على حدة).
//
// الفكرة: عند إنشاء قاعدة البيانات لأول مرة (SchemaInitializer)، يُولَّد معرّف تثبيت
// فريد وثابت (InstallationId - GUID) ويُخزَّن داخل جدول ClinicSettings. هذا المعرّف
// هو "بصمة" تلك القاعدة تحديداً (وبالتالي تلك العيادة، بما أن كل أجهزتها تتصل بنفس
// القاعدة). التطبيق (سواء NurseApp أو DoctorApp) يتحقق فقط من وجود توقيع رقمي صالح
// لهذا المعرّف داخل نفس القاعدة - لا حاجة لأي فحص عتاد إضافي في كل جهاز على حدة.
//
// الأمان: هذا الملف يحتوي فقط المفتاح العام (Public Key) - يمكنه التحقق من توقيع
// موجود، لكن لا يستطيع إطلاقاً توليد توقيع صالح جديد. المفتاح الخاص (Private Key)
// يبقى فقط عند المطوّر على جهازه الشخصي، ولا يُشحن أبداً مع التطبيق. حتى لو حصل
// أي شخص على الكود المصدري الكامل لهذا الملف، لا يستطيع تفعيل ترخيص لعيادة أخرى.
// ============================================================
public static class LicenseValidator
{
    private const string InstallationIdKey = "InstallationId";
    private const string LicenseSignatureKey = "LicenseSignature";

    // ⚠️ مفتاح عام فقط - آمن تماماً تضمينه هنا، هذا هو الغرض من المفاتيح العامة
    private const string PublicKeyPem = @"-----BEGIN RSA PUBLIC KEY-----
MIIBCgKCAQEApnO/YSHTl5b7p1D9pt58wzMH+AHOJwFfnK9DPBaXjScopuJi5KBq
WmWu2P0xim1WJ90Ocd9Yo4NQIuERfzPWRxH0I5Mb95xQdXI0MRyEWzWjnq4pJcZP
0QFkzTMYaeJTuUEGB9wBy4DNVDV3z4EOK7WSR9QhbMYlpDOBg2uAcXf8gTn/qG+q
9z6Ex+DZ09itUTEV6LI5zOx0mjE+csQwa6rfn3M2yFjvPt4AIQrim00bW6CgYMpc
X4If7S26bwehAvZ+kdszW4qb0eFFcKcHEh/0HUvNhoVzghklLxrq69jGe+rhtRUE
XbONLRm2QGxS4ZSFAtSq26HWY2AyKqsMXQIDAQAB
-----END RSA PUBLIC KEY-----";

    // يُستدعى مرة واحدة فقط من SchemaInitializer.CreateSchema عند إنشاء القاعدة لأول مرة
    public static void EnsureInstallationId(DatabaseHelper db)
    {
        var existing = db.ExecuteScalar(
            "SELECT SettingValue FROM ClinicSettings WHERE SettingKey = 'InstallationId'");
        if (existing != null && existing != DBNull.Value) return;

        var newId = Guid.NewGuid().ToString("N");
        db.ExecuteNonQuery(
            "INSERT INTO ClinicSettings (SettingKey, SettingValue) VALUES ('InstallationId', @Value)",
            new Microsoft.Data.SqlClient.SqlParameter("@Value", newId));
    }

    public static string? GetInstallationId(DatabaseHelper db)
    {
        var result = db.ExecuteScalar(
            "SELECT SettingValue FROM ClinicSettings WHERE SettingKey = 'InstallationId'");
        return result == null || result == DBNull.Value ? null : result.ToString();
    }

    // مفتاح الترخيص (توقيع RSA-2048 بصيغة Base64) طوله ~344 حرفاً - أطول من الحد الأصلي
    // nvarchar(200) لعمود SettingValue. نوسّعه هنا تلقائياً بشكل آمن (idempotent - لا يفقد
    // أي بيانات موجودة ولا يُخطئ لو نُفِّذ أكثر من مرة) قبل أي محاولة حفظ.
    private static void EnsureSettingValueColumnWidened(DatabaseHelper db)
    {
        db.ExecuteNonQuery("ALTER TABLE ClinicSettings ALTER COLUMN SettingValue NVARCHAR(1000) NOT NULL");
    }

    // يُستدعى من شاشة "التفعيل" بعد لصق الترخيص المُرسَل من المطوّر
    public static void SetLicenseSignature(DatabaseHelper db, string signatureBase64)
    {
        EnsureSettingValueColumnWidened(db);

        var current = db.ExecuteScalar(
            "SELECT COUNT(*) FROM ClinicSettings WHERE SettingKey = 'LicenseSignature'");
        var exists = current != null && Convert.ToInt32(current) > 0;

        if (exists)
        {
            db.ExecuteNonQuery(
                "UPDATE ClinicSettings SET SettingValue = @Value WHERE SettingKey = 'LicenseSignature'",
                new Microsoft.Data.SqlClient.SqlParameter("@Value", signatureBase64));
        }
        else
        {
            db.ExecuteNonQuery(
                "INSERT INTO ClinicSettings (SettingKey, SettingValue) VALUES ('LicenseSignature', @Value)",
                new Microsoft.Data.SqlClient.SqlParameter("@Value", signatureBase64));
        }
    }

    // التحقق الفعلي: هل يوجد توقيع صالح (موقَّع فعلاً بالمفتاح الخاص) لمعرّف هذه القاعدة؟
    public static bool IsLicensed(DatabaseHelper db, out string installationId)
    {
        installationId = GetInstallationId(db) ?? string.Empty;
        if (string.IsNullOrEmpty(installationId)) return false;

        var signatureResult = db.ExecuteScalar(
            "SELECT SettingValue FROM ClinicSettings WHERE SettingKey = 'LicenseSignature'");
        if (signatureResult == null || signatureResult == DBNull.Value) return false;

        var signatureBase64 = signatureResult.ToString();
        if (string.IsNullOrWhiteSpace(signatureBase64)) return false;

        try
        {
            var signatureBytes = Convert.FromBase64String(signatureBase64.Trim());
            var dataBytes = Encoding.UTF8.GetBytes(installationId);

            using var rsa = RSA.Create();
            rsa.ImportFromPem(PublicKeyPem);

            return rsa.VerifyData(dataBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false; // توقيع تالف أو مُزوَّر بشكل غير صحيح
        }
    }
}
