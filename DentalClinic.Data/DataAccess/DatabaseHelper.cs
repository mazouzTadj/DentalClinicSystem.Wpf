using Microsoft.Data.SqlClient;
using System.Data;

namespace DentalClinic.Data.DataAccess;

// طبقة وصول بيانات عامة (ADO.NET) - تُستخدم من قبل كل الـ Repositories
// التي سنبنيها في خطوة لاحقة (PatientRepository, QueueRepository, SessionRepository ...)
public class DatabaseHelper
{
    private readonly string _connectionString;

    // مهلة اتصال قصيرة نسبياً (بدل افتراضي SqlClient وهو 15 ثانية) - الأولوية الآن لفشل سريع
    // وإظهار حالة الاتصال للمستخدم بدل تجميد الواجهة بالكامل لفترة طويلة عند انقطاع الشبكة/تعطُّل
    // السيرفر (مشكلة تجمُّد تطبيق الاستقبال المُبلَّغ عنها - راجع ConnectionMonitor في
    // DentalClinic.UI للمراقبة المستمرة في الخلفية، وConnectionLostWindow للإشعار التلقائي).
    // تُطبَّق هذه القيمة على GetConnection نفسها، فتشمل تلقائياً حتى الكود الذي يفتح Transaction
    // يدوياً عبر SqlCommand مباشرة (conn, tx) بدل دوال Execute* أدناه - أي محاولة اتصال جديدة في
    // كامل المشروع تستفيد من نفس هذا التقصير، بصرف النظر عن الأسلوب المستخدَم لتنفيذ الاستعلام.
    private const int ConnectTimeoutSeconds = 5;

    // مهلة تنفيذ أمر واحد (SELECT/INSERT/UPDATE/DELETE) - تُطبَّق على دوال Execute* في هذا الملف،
    // وأيضاً (Patch 16) على كل SqlCommand مباشر في بقية الـRepositories (المُنشأة عادة داخل معاملة
    // يدوية conn/tx) عبر الإشارة إليها كمصدر تقصير مركزي واحد: DatabaseHelper.CommandTimeoutSeconds.
    // هذا يمنع أي أمر تفاعلي/سريري/مالي عادي من التقصير الصامت إلى مهلة SqlCommand الافتراضية
    // (30 ثانية). الاستثناء الوحيد المقصود هو BackupRepository (300 ثانية - عملية طويلة بطبيعتها).
    public const int CommandTimeoutSeconds = 8;

    public DatabaseHelper(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ConnectTimeout = ConnectTimeoutSeconds
        };
        _connectionString = builder.ConnectionString;
    }

    public SqlConnection GetConnection() => new SqlConnection(_connectionString);

    // اختبار الاتصال بقاعدة البيانات - يُستخدم عند فتح التطبيق أو من شاشة الإعدادات، وأيضاً من
    // ConnectionMonitor للفحص الدوري في الخلفية (بمهلة override أقصر عادة عبر connectTimeoutOverrideSeconds)
    public bool TestConnection(out string errorMessage, int? connectTimeoutOverrideSeconds = null)
    {
        errorMessage = string.Empty;
        try
        {
            var connectionString = _connectionString;
            if (connectTimeoutOverrideSeconds.HasValue)
            {
                var builder = new SqlConnectionStringBuilder(_connectionString)
                {
                    ConnectTimeout = connectTimeoutOverrideSeconds.Value
                };
                connectionString = builder.ConnectionString;
            }

            using var conn = new SqlConnection(connectionString);
            conn.Open();
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    // لتنفيذ أوامر INSERT / UPDATE / DELETE
    public int ExecuteNonQuery(string commandText, params SqlParameter[] parameters)
    {
        using var conn = GetConnection();
        using var cmd = new SqlCommand(commandText, conn) { CommandTimeout = CommandTimeoutSeconds };
        if (parameters.Length > 0) cmd.Parameters.AddRange(parameters);
        conn.Open();
        ApplyAuditContext(conn);
        return cmd.ExecuteNonQuery();
    }

    // لتنفيذ INSERT مع إرجاع المعرّف الجديد المُولَّد تلقائياً
    public int ExecuteInsertAndGetId(string commandText, params SqlParameter[] parameters)
    {
        using var conn = GetConnection();
        using var cmd = new SqlCommand(commandText + "; SELECT CAST(SCOPE_IDENTITY() AS INT);", conn) { CommandTimeout = CommandTimeoutSeconds };
        if (parameters.Length > 0) cmd.Parameters.AddRange(parameters);
        conn.Open();
        ApplyAuditContext(conn);
        var result = cmd.ExecuteScalar();
        return result != null ? Convert.ToInt32(result) : 0;
    }

    // لتنفيذ استعلامات SELECT وإرجاع النتائج كجدول بيانات
    public DataTable ExecuteQuery(string commandText, params SqlParameter[] parameters)
    {
        using var conn = GetConnection();
        using var cmd = new SqlCommand(commandText, conn) { CommandTimeout = CommandTimeoutSeconds };
        if (parameters.Length > 0) cmd.Parameters.AddRange(parameters);
        conn.Open();
        ApplyAuditContext(conn);
        using var adapter = new SqlDataAdapter(cmd);
        var table = new DataTable();
        adapter.Fill(table);
        return table;
    }

    // لاستعلام يرجع قيمة واحدة فقط (COUNT, EXISTS, وغيرها)
    public object? ExecuteScalar(string commandText, params SqlParameter[] parameters)
    {
        using var conn = GetConnection();
        using var cmd = new SqlCommand(commandText, conn) { CommandTimeout = CommandTimeoutSeconds };
        if (parameters.Length > 0) cmd.Parameters.AddRange(parameters);
        conn.Open();
        ApplyAuditContext(conn);
        return cmd.ExecuteScalar();
    }

    private static void ApplyAuditContext(SqlConnection conn)
    {
        if (!AuditContext.CurrentUserId.HasValue) return;

        using var cmd = new SqlCommand(
            "EXEC sys.sp_set_session_context @key = N'AuditUserId', @value = @UserId;", conn)
        {
            CommandTimeout = CommandTimeoutSeconds
        };
        cmd.Parameters.AddWithValue("@UserId", AuditContext.CurrentUserId.Value);
        cmd.ExecuteNonQuery();
    }
}
