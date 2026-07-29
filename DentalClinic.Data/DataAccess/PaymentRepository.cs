using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;

namespace DentalClinic.Data.DataAccess;

// تسجيل الدفعات الفعلية - يسمح بتسديد نفس الفاتورة على أكثر من دفعة عبر زيارات متعددة
public class PaymentRepository
{
    private readonly DatabaseHelper _db;

    public PaymentRepository(DatabaseHelper db)
    {
        _db = db;
    }

    public void AddPayment(int sessionId, decimal amount, int receivedByUserId, string? notes = null)
    {
        const string sql = @"
            INSERT INTO Payments (SessionID, Amount, ReceivedByUserID, Notes)
            VALUES (@SessionID, @Amount, @ReceivedByUserID, @Notes)";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@SessionID", sessionId),
            new SqlParameter("@Amount", amount),
            new SqlParameter("@ReceivedByUserID", receivedByUserId),
            new SqlParameter("@Notes", (object?)notes ?? DBNull.Value));
    }

    // إصلاح: كان نفس هذا الاستعلام مكرراً حرفياً في 3 أماكن مختلفة عبر NurseApp
    // (MainWindow, PatientSearchWindow مرتين). أصبح الآن مصدراً واحداً موثوقاً في طبقة البيانات.
    // كل معرّفات المرضى الذين لديهم جلسة واحدة على الأقل غير مسدَّدة بالكامل.
    public HashSet<int> GetUnpaidPatientIds()
    {
        const string sql = "SELECT DISTINCT PatientID FROM MedicalSessions WHERE TotalPrice > PaidAmount";
        var table = _db.ExecuteQuery(sql);

        var set = new HashSet<int>();
        foreach (DataRow row in table.Rows)
        {
            set.Add(Convert.ToInt32(row["PatientID"]));
        }
        return set;
    }

    // نفس الفكرة لكن مُقيَّدة بمجموعة معرّفات مرضى محددة (تُستخدم في شاشات البحث لتفادي جلب كل الجدول)
    public HashSet<int> GetUnpaidPatientIds(IEnumerable<int> patientIds)
    {
        var idList = patientIds.Distinct().ToList();
        var set = new HashSet<int>();
        if (idList.Count == 0) return set;

        var parameters = new SqlParameter[idList.Count];
        var placeholders = new string[idList.Count];
        for (int i = 0; i < idList.Count; i++)
        {
            placeholders[i] = $"@P{i}";
            parameters[i] = new SqlParameter($"@P{i}", idList[i]);
        }

        string sql = $"SELECT DISTINCT PatientID FROM MedicalSessions WHERE PatientID IN ({string.Join(",", placeholders)}) AND TotalPrice > PaidAmount";
        var table = _db.ExecuteQuery(sql, parameters);

        foreach (DataRow row in table.Rows)
        {
            set.Add(Convert.ToInt32(row["PatientID"]));
        }
        return set;
    }

    // آخر جلسة غير مسدَّدة بالكامل لمريض معيّن - تُستخدم لفتح شاشة تحصيل الدفعة مباشرة على الجلسة الصحيحة
    public int? GetLatestUnpaidSessionId(int patientId)
    {
        const string sql = @"
            SELECT TOP 1 SessionID
            FROM MedicalSessions
            WHERE PatientID = @PatientID AND TotalPrice > PaidAmount
            ORDER BY SessionDateTime DESC";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@PatientID", patientId));
        return table.Rows.Count > 0 ? Convert.ToInt32(table.Rows[0]["SessionID"]) : null;
    }
}
