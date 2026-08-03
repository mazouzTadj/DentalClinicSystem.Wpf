using System.Data;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

public class MedicationPresetRepository
{
    private readonly DatabaseHelper _db;

    public MedicationPresetRepository(DatabaseHelper db)
    {
        _db = db;
        EnsureTableExists();
    }

    // إنشاء الجدول تلقائياً إن لم يكن موجوداً - نفس نمط باقي المستودعات في هذا المشروع (migration آمنة)
    private void EnsureTableExists()
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MedicationPresets')
            BEGIN
                CREATE TABLE MedicationPresets (
                    MedicationID INT IDENTITY(1,1) PRIMARY KEY,
                    MedicationName NVARCHAR(200) NOT NULL,
                    DefaultDosage NVARCHAR(200) NULL,
                    DefaultDuration NVARCHAR(200) NULL,
                    IsActive BIT NOT NULL DEFAULT 1
                );
            END";
        _db.ExecuteNonQuery(sql);
    }

    // القائمة السريعة للأدوية النشطة - تُستخدم في شاشة تحرير الوصفة الطبية
    public List<MedicationPreset> GetActivePresets()
    {
        const string sql = @"
            SELECT MedicationID, MedicationName, DefaultDosage, DefaultDuration, IsActive
            FROM MedicationPresets
            WHERE IsActive = 1
            ORDER BY MedicationName";

        var table = _db.ExecuteQuery(sql);
        var result = new List<MedicationPreset>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(new MedicationPreset
            {
                MedicationID = (int)row["MedicationID"],
                MedicationName = row["MedicationName"].ToString()!,
                DefaultDosage = row["DefaultDosage"] as string,
                DefaultDuration = row["DefaultDuration"] as string,
                IsActive = (bool)row["IsActive"]
            });
        }
        return result;
    }

    // إضافة دواء جديد للقائمة السريعة (اختياري - يسمح للطبيب بتوسيع القائمة لاحقاً)
    public void AddPreset(string name, string? dosage, string? duration)
    {
        const string sql = @"
            INSERT INTO MedicationPresets (MedicationName, DefaultDosage, DefaultDuration)
            VALUES (@Name, @Dosage, @Duration)";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@Name", name),
            new SqlParameter("@Dosage", (object?)dosage ?? DBNull.Value),
            new SqlParameter("@Duration", (object?)duration ?? DBNull.Value));
    }

    // تعديل دواء موجود في القائمة السريعة (اسمه/جرعته الافتراضية/مدته الافتراضية)
    public void UpdatePreset(int medicationId, string name, string? dosage, string? duration)
    {
        const string sql = @"
            UPDATE MedicationPresets
            SET MedicationName = @Name, DefaultDosage = @Dosage, DefaultDuration = @Duration
            WHERE MedicationID = @ID";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@Name", name),
            new SqlParameter("@Dosage", (object?)dosage ?? DBNull.Value),
            new SqlParameter("@Duration", (object?)duration ?? DBNull.Value),
            new SqlParameter("@ID", medicationId));
    }

    // حذف ناعم (Soft Delete) بنفس نمط TreatmentPresets - يبقي السجل التاريخي في الجلسات القديمة سليماً
    public void DeactivatePreset(int medicationId)
    {
        const string sql = "UPDATE MedicationPresets SET IsActive = 0 WHERE MedicationID = @ID";
        _db.ExecuteNonQuery(sql, new SqlParameter("@ID", medicationId));
    }
}
