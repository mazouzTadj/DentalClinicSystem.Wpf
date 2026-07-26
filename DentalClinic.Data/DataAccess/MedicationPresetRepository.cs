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
}
