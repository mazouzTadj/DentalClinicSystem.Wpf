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
}
