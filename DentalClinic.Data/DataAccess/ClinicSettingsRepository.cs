using System.Data;
using Microsoft.Data.SqlClient;

namespace DentalClinic.Data.DataAccess;

// جدول إعدادات عامة بسيط (Key/Value) للعيادة ككل. يُستخدم حالياً فقط لتخزين نسبة عمولة
// الأطباء الافتراضية (DoctorCommissionPercent)، لكنه مصمَّم ليتّسع لأي إعداد عام آخر مستقبلاً.
public class ClinicSettingsRepository
{
    private readonly DatabaseHelper _db;

    public const string DoctorCommissionPercentKey = "DoctorCommissionPercent";
    public const string PrimaryDoctorUserIdKey = "PrimaryDoctorUserId";
    private const decimal DefaultDoctorCommissionPercent = 50m;

    public ClinicSettingsRepository(DatabaseHelper db)
    {
        _db = db;
        EnsureTableExists();
    }

    private void EnsureTableExists()
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ClinicSettings')
            BEGIN
                CREATE TABLE ClinicSettings (
                    SettingKey NVARCHAR(100) NOT NULL PRIMARY KEY,
                    SettingValue NVARCHAR(200) NOT NULL
                );
            END";
        _db.ExecuteNonQuery(sql);
    }

    public string? GetValue(string key)
    {
        const string sql = "SELECT SettingValue FROM ClinicSettings WHERE SettingKey = @Key";
        var result = _db.ExecuteScalar(sql, new SqlParameter("@Key", key));
        return result == null || result == DBNull.Value ? null : result.ToString();
    }

    public void SetValue(string key, string value)
    {
        const string sql = @"
            IF EXISTS (SELECT * FROM ClinicSettings WHERE SettingKey = @Key)
                UPDATE ClinicSettings SET SettingValue = @Value WHERE SettingKey = @Key
            ELSE
                INSERT INTO ClinicSettings (SettingKey, SettingValue) VALUES (@Key, @Value)";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@Key", key),
            new SqlParameter("@Value", value));
    }

    public void DeleteValue(string key)
    {
        const string sql = "DELETE FROM ClinicSettings WHERE SettingKey = @Key";
        _db.ExecuteNonQuery(sql, new SqlParameter("@Key", key));
    }

    // تعيين صريح للطبيب الرئيسي من طرف المستخدم (شاشة الإعدادات) - يتجاوز "أول طبيب مسجَّل".
    // إن كانت القيمة null، يُحذف الإعداد بالكامل فتعود القاعدة التلقائية (أول طبيب مسجَّل) للعمل.
    public int? GetExplicitPrimaryDoctorId()
    {
        var raw = GetValue(PrimaryDoctorUserIdKey);
        return raw != null && int.TryParse(raw, out var id) ? id : (int?)null;
    }

    public void SetExplicitPrimaryDoctorId(int? userId)
    {
        if (userId.HasValue)
        {
            SetValue(PrimaryDoctorUserIdKey, userId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            DeleteValue(PrimaryDoctorUserIdKey);
        }
    }

    // النسبة المئوية العامة الافتراضية لعمولة الأطباء (تُستخدم لأي طبيب ليس له نسبة مخصَّصة). 50% افتراضياً.
    public decimal GetDefaultDoctorCommissionPercent()
    {
        var raw = GetValue(DoctorCommissionPercentKey);
        if (raw != null && decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return DefaultDoctorCommissionPercent;
    }

    public void SetDefaultDoctorCommissionPercent(decimal percent)
    {
        SetValue(DoctorCommissionPercentKey, percent.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
