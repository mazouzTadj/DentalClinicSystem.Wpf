using System.Data;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

public class DiagnosisPresetRepository
{
    private readonly DatabaseHelper _db;

    public DiagnosisPresetRepository(DatabaseHelper db)
    {
        _db = db;
        EnsureTableExists();
    }

    // إنشاء الجدول تلقائياً إن لم يكن موجوداً - نفس نمط MedicationPresetRepository/TreatmentPresets (migration آمنة)
    private void EnsureTableExists()
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DiagnosisPresets')
            BEGIN
                CREATE TABLE DiagnosisPresets (
                    DiagnosisID INT IDENTITY(1,1) PRIMARY KEY,
                    DiagnosisName NVARCHAR(200) NOT NULL,
                    IsActive BIT NOT NULL DEFAULT 1
                );
            END";
        _db.ExecuteNonQuery(sql);
    }

    // القائمة السريعة للتشخيصات النشطة - تُستخدم في شاشة ملف المريض (نافذة اختيار التشخيص)
    public List<DiagnosisPreset> GetActivePresets()
    {
        const string sql = @"
            SELECT DiagnosisID, DiagnosisName, IsActive
            FROM DiagnosisPresets
            WHERE IsActive = 1
            ORDER BY DiagnosisName";

        var table = _db.ExecuteQuery(sql);
        var result = new List<DiagnosisPreset>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(new DiagnosisPreset
            {
                DiagnosisID = (int)row["DiagnosisID"],
                DiagnosisName = row["DiagnosisName"].ToString()!,
                IsActive = (bool)row["IsActive"]
            });
        }
        return result;
    }

    // إضافة تشخيص جديد للقائمة السريعة
    public void AddPreset(string name)
    {
        const string sql = "INSERT INTO DiagnosisPresets (DiagnosisName) VALUES (@Name)";
        _db.ExecuteNonQuery(sql, new SqlParameter("@Name", name));
    }

    // تعديل اسم تشخيص موجود في القائمة السريعة
    public void UpdatePreset(int diagnosisId, string name)
    {
        const string sql = "UPDATE DiagnosisPresets SET DiagnosisName = @Name WHERE DiagnosisID = @ID";
        _db.ExecuteNonQuery(sql, new SqlParameter("@Name", name), new SqlParameter("@ID", diagnosisId));
    }

    // حذف ناعم (Soft Delete) بنفس نمط باقي القوائم الجاهزة - يبقي السجل التاريخي في الجلسات القديمة سليماً
    public void DeactivatePreset(int diagnosisId)
    {
        const string sql = "UPDATE DiagnosisPresets SET IsActive = 0 WHERE DiagnosisID = @ID";
        _db.ExecuteNonQuery(sql, new SqlParameter("@ID", diagnosisId));
    }
}
