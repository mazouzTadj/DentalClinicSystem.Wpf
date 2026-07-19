using Microsoft.Data.SqlClient;
using System.Data;

namespace DentalClinic.Data.DataAccess;

// طبقة وصول بيانات عامة (ADO.NET) - تُستخدم من قبل كل الـ Repositories
// التي سنبنيها في خطوة لاحقة (PatientRepository, QueueRepository, SessionRepository ...)
public class DatabaseHelper
{
    private readonly string _connectionString;

    public DatabaseHelper(string connectionString)
    {
        _connectionString = connectionString;
    }

    public SqlConnection GetConnection() => new SqlConnection(_connectionString);

    // اختبار الاتصال بقاعدة البيانات - يُستخدم عند فتح التطبيق أو من شاشة الإعدادات
    public bool TestConnection(out string errorMessage)
    {
        errorMessage = string.Empty;
        try
        {
            using var conn = GetConnection();
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
        using var cmd = new SqlCommand(commandText, conn);
        if (parameters.Length > 0) cmd.Parameters.AddRange(parameters);
        conn.Open();
        return cmd.ExecuteNonQuery();
    }

    // لتنفيذ INSERT مع إرجاع المعرّف الجديد المُولَّد تلقائياً
    public int ExecuteInsertAndGetId(string commandText, params SqlParameter[] parameters)
    {
        using var conn = GetConnection();
        using var cmd = new SqlCommand(commandText + "; SELECT CAST(SCOPE_IDENTITY() AS INT);", conn);
        if (parameters.Length > 0) cmd.Parameters.AddRange(parameters);
        conn.Open();
        var result = cmd.ExecuteScalar();
        return result != null ? Convert.ToInt32(result) : 0;
    }

    // لتنفيذ استعلامات SELECT وإرجاع النتائج كجدول بيانات
    public DataTable ExecuteQuery(string commandText, params SqlParameter[] parameters)
    {
        using var conn = GetConnection();
        using var cmd = new SqlCommand(commandText, conn);
        if (parameters.Length > 0) cmd.Parameters.AddRange(parameters);
        using var adapter = new SqlDataAdapter(cmd);
        var table = new DataTable();
        adapter.Fill(table);
        return table;
    }

    // لاستعلام يرجع قيمة واحدة فقط (COUNT, EXISTS, وغيرها)
    public object? ExecuteScalar(string commandText, params SqlParameter[] parameters)
    {
        using var conn = GetConnection();
        using var cmd = new SqlCommand(commandText, conn);
        if (parameters.Length > 0) cmd.Parameters.AddRange(parameters);
        conn.Open();
        return cmd.ExecuteScalar();
    }
}
