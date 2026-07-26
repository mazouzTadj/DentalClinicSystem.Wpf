using Microsoft.Data.SqlClient;

namespace DentalClinic.Data.DataAccess;

// كل ما يخص النسخ الاحتياطي لقاعدة البيانات - يُستخدم من تطبيق الطبيب فقط (صلاحية إدارية)
public class BackupRepository
{
    private readonly DatabaseHelper _db;
    private readonly string _databaseName;

    public BackupRepository(DatabaseHelper db, string databaseName)
    {
        _db = db;
        _databaseName = databaseName;
    }

    // تنفيذ نسخة احتياطية كاملة عبر أمر SQL Server الأصلي BACKUP DATABASE.
    // ملاحظة مهمة: backupFolderPath هو مسار على جهاز SQL Server نفسه (وليس بالضرورة جهاز العميل الذي يشغّل هذا الكود)
    // لأن SQL Server هو من يكتب الملف فعلياً على قرصه الخاص.
    public (bool Success, string Message, string? FilePath) BackupNow(string backupFolderPath)
    {
        var fileName = $"{_databaseName}_{DateTime.Now:yyyy-MM-dd_HHmmss}.bak";
        var fullPath = System.IO.Path.Combine(backupFolderPath, fileName);

        // اسم قاعدة البيانات يأتي من إعدادات التطبيق (App.config) وليس من إدخال المستخدم مباشرة، فالدمج هنا آمن
        var sql = $"BACKUP DATABASE [{_databaseName}] TO DISK = @FilePath WITH NAME = @BackupName, STATS = 10";

        try
        {
            using var conn = _db.GetConnection();
            using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 300 };
            cmd.Parameters.AddWithValue("@FilePath", fullPath);
            cmd.Parameters.AddWithValue("@BackupName", $"{_databaseName} - Full Backup");
            conn.Open();
            cmd.ExecuteNonQuery();

            return (true, "Backup completed successfully", fullPath);
        }
        catch (Exception ex)
        {
            return (false, "Backup failed: " + ex.Message, null);
        }
    }

    // حذف ملفات النسخ الاحتياطية الأقدم من عدد الأيام المحدَّد (تنظيف تلقائي لمنع امتلاء القرص).
    // تعمل عبر xp_delete_file المنفَّذة داخل SQL Server نفسه، فلا حاجة لصلاحية وصول لملفات الخادم من التطبيق.
    public void CleanupOldBackups(string backupFolderPath, int retainDays)
    {
        const string sql = "EXEC master.dbo.xp_delete_file 0, @FolderPath, N'bak', @CutoffDate";
        var cutoff = DateTime.Now.AddDays(-retainDays);

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@FolderPath", backupFolderPath),
            new SqlParameter("@CutoffDate", cutoff));
    }

    // آخر نسخة احتياطية كاملة ناجحة، من سجل SQL Server الرسمي (msdb) - لا حاجة لجدول خاص بنا لتتبّعها
    public DateTime? GetLastBackupDate()
    {
        const string sql = @"
            SELECT MAX(backup_finish_date)
            FROM msdb.dbo.backupset
            WHERE database_name = @DatabaseName AND type = 'D'";

        var result = _db.ExecuteScalar(sql, new SqlParameter("@DatabaseName", _databaseName));
        return result == null || result == DBNull.Value ? null : Convert.ToDateTime(result);
    }
}
