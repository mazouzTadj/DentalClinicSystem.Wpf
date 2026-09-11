using System.Data;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

// نفس نمط MedicationPresetRepository بالضبط - قائمة سريعة لشهادات طبية/عطل مرضية جاهزة
public class CertificatePresetRepository
{
    private readonly DatabaseHelper _db;

    public CertificatePresetRepository(DatabaseHelper db)
    {
        _db = db;
        EnsureTableExists();
    }

    // إنشاء الجدول تلقائياً إن لم يكن موجوداً - نفس نمط باقي المستودعات في هذا المشروع (migration آمنة)
    private void EnsureTableExists()
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CertificatePresets')
            BEGIN
                CREATE TABLE CertificatePresets (
                    CertificateID INT IDENTITY(1,1) PRIMARY KEY,
                    CertificateName NVARCHAR(200) NOT NULL,
                    DefaultText NVARCHAR(MAX) NULL,
                    IsActive BIT NOT NULL DEFAULT 1
                );
            END";
        _db.ExecuteNonQuery(sql);
    }

    // القائمة السريعة للشهادات/العطل النشطة - تُستخدم في شاشة تحرير الوصفة الطبية وملف المريض
    public List<CertificatePreset> GetActivePresets()
    {
        const string sql = @"
            SELECT CertificateID, CertificateName, DefaultText, IsActive
            FROM CertificatePresets
            WHERE IsActive = 1
            ORDER BY CertificateName";

        var table = _db.ExecuteQuery(sql);
        var result = new List<CertificatePreset>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(new CertificatePreset
            {
                CertificateID = (int)row["CertificateID"],
                CertificateName = row["CertificateName"].ToString()!,
                DefaultText = row["DefaultText"] as string,
                IsActive = (bool)row["IsActive"]
            });
        }
        return result;
    }

    // إضافة شهادة/عطلة جديدة للقائمة السريعة
    public void AddPreset(string name, string? defaultText)
    {
        const string sql = @"
            INSERT INTO CertificatePresets (CertificateName, DefaultText)
            VALUES (@Name, @DefaultText)";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@Name", name),
            new SqlParameter("@DefaultText", (object?)defaultText ?? DBNull.Value));
    }

    // تعديل شهادة/عطلة موجودة في القائمة السريعة
    public void UpdatePreset(int certificateId, string name, string? defaultText)
    {
        const string sql = @"
            UPDATE CertificatePresets
            SET CertificateName = @Name, DefaultText = @DefaultText
            WHERE CertificateID = @ID";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@Name", name),
            new SqlParameter("@DefaultText", (object?)defaultText ?? DBNull.Value),
            new SqlParameter("@ID", certificateId));
    }

    // حذف ناعم (Soft Delete) بنفس نمط باقي القوائم الجاهزة - يبقي السجل التاريخي في الجلسات القديمة سليماً
    public void DeactivatePreset(int certificateId)
    {
        const string sql = "UPDATE CertificatePresets SET IsActive = 0 WHERE CertificateID = @ID";
        _db.ExecuteNonQuery(sql, new SqlParameter("@ID", certificateId));
    }
}
