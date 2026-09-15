using System.Data;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

// كل عمليات مصاريف المرمم (ProstheticExpenses، Patch 12) - جدول مستقل تماماً عن ClinicExpenses
// (مصاريف العيادة العامة في FinancialRepository/تطبيق الطبيب). نفس قاعدة الفصل المالي المطبَّقة
// في كل مكان آخر من نظام الترميم (راجع تعليق ProstheticStatisticsRepository) - لا صلة إطلاقاً
// بـFinancialRepository أو ClinicExpenses هنا.
public class ProstheticExpenseRepository
{
    private readonly DatabaseHelper _db;

    public ProstheticExpenseRepository(DatabaseHelper db)
    {
        _db = db;
        EnsureTableExists(db);
    }

    // Public static حتى تستطيع ProstheticStatisticsRepository استدعاءها أيضاً (الدخل الصافي يعتمد
    // على مجموع هذا الجدول) دون تكرار نص إنشاء الجدول في مكانين - نفس أسلوب الإنشاء الآمن
    // (IF NOT EXISTS) المعتمد في FinancialRepository.EnsureClinicExpensesTableExists.
    public static void EnsureTableExists(DatabaseHelper db)
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProstheticExpenses')
            BEGIN
                CREATE TABLE ProstheticExpenses (
                    ExpenseID INT IDENTITY(1,1) PRIMARY KEY,
                    ProsthetistUserID INT NOT NULL,
                    Amount DECIMAL(18,2) NOT NULL,
                    Description NVARCHAR(500) NOT NULL,
                    Category NVARCHAR(100) NOT NULL DEFAULT N'General / Other',
                    ExpenseDate DATETIME NOT NULL,
                    CreatedByUserID INT NOT NULL,
                    CreatedAt DATETIME NOT NULL DEFAULT GETDATE()
                );
            END";
        db.ExecuteNonQuery(sql);
    }

    // نفس نمط ProstheticPaymentRepository.AddPayment بالحرف - التحقق من الصلاحية هنا في طبقة
    // البيانات (وليس فقط إخفاء الزر في الواجهة)، دفاعاً متعدد الطبقات.
    public void AddExpense(ProstheticExpense expense, UserAccount actingUser)
    {
        ProstheticPermissionGuard.Ensure(actingUser, ProstheticPermissionKeys.AddExpense);

        const string sql = @"
            INSERT INTO ProstheticExpenses (ProsthetistUserID, Amount, Description, Category, ExpenseDate, CreatedByUserID)
            VALUES (@ProsthetistUserID, @Amount, @Desc, @Cat, @Date, @CreatedBy)";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@ProsthetistUserID", expense.ProsthetistUserID),
            new SqlParameter("@Amount", expense.Amount),
            new SqlParameter("@Desc", expense.Description),
            new SqlParameter("@Cat", expense.Category),
            new SqlParameter("@Date", expense.ExpenseDate),
            new SqlParameter("@CreatedBy", actingUser.UserID));
    }

    public void DeleteExpense(int expenseId, UserAccount actingUser)
    {
        ProstheticPermissionGuard.Ensure(actingUser, ProstheticPermissionKeys.DeleteExpense);

        const string sql = "DELETE FROM ProstheticExpenses WHERE ExpenseID = @ExpenseID";
        _db.ExecuteNonQuery(sql, new SqlParameter("@ExpenseID", expenseId));
    }

    // تعديل مصروف موجود - نفس نمط UpdatePayment في ProstheticPaymentRepository بالحرف. لا نُغيّر
    // ProsthetistUserID/CreatedByUserID/CreatedAt هنا عمداً (المرمم صاحب المصروف لا يتغيّر بالتعديل).
    public void UpdateExpense(ProstheticExpense expense, UserAccount actingUser)
    {
        ProstheticPermissionGuard.Ensure(actingUser, ProstheticPermissionKeys.EditExpense);

        const string sql = @"
            UPDATE ProstheticExpenses
            SET Amount = @Amount, Description = @Desc, Category = @Cat, ExpenseDate = @Date
            WHERE ExpenseID = @ExpenseID";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@Amount", expense.Amount),
            new SqlParameter("@Desc", expense.Description),
            new SqlParameter("@Cat", expense.Category),
            new SqlParameter("@Date", expense.ExpenseDate),
            new SqlParameter("@ExpenseID", expense.ExpenseID));
    }

    // قائمة المصاريف ضمن فترة + فلتر مرمم اختياري (null = كل المرممين) - لعرضها في
    // ProstheticExpensesWindow. قراءة فقط، بلا أي تحقق صلاحية هنا (نفس أسلوب باقي دوال القراءة في
    // ProstheticStatisticsRepository - التحقق يقع على الاستدعاء من الواجهة عبر CanViewFinance).
    public List<ProstheticExpenseRow> GetExpenses(int? prosthetistUserId, DateTime fromDate, DateTime toDateExclusive)
    {
        var filter = prosthetistUserId.HasValue ? " AND e.ProsthetistUserID = @ProsthetistID" : "";
        var sql = $@"
            SELECT e.ExpenseID, e.ProsthetistUserID, e.Amount, e.Description, e.Category, e.ExpenseDate,
                   ISNULL(u.FullName, u.Username) AS ProsthetistName
            FROM dbo.ProstheticExpenses e
            LEFT JOIN dbo.Users u ON u.UserID = e.ProsthetistUserID
            WHERE e.ExpenseDate >= @FromDate AND e.ExpenseDate < @ToDate{filter}
            ORDER BY e.ExpenseDate DESC";

        var parameters = new List<SqlParameter> { new("@FromDate", fromDate), new("@ToDate", toDateExclusive) };
        if (prosthetistUserId.HasValue) parameters.Add(new SqlParameter("@ProsthetistID", prosthetistUserId.Value));

        var table = _db.ExecuteQuery(sql, parameters.ToArray());
        var result = new List<ProstheticExpenseRow>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(new ProstheticExpenseRow
            {
                ExpenseID = Convert.ToInt32(row["ExpenseID"]),
                ProsthetistUserID = Convert.ToInt32(row["ProsthetistUserID"]),
                Amount = Convert.ToDecimal(row["Amount"]),
                Description = row["Description"].ToString()!,
                Category = row["Category"].ToString()!,
                ExpenseDate = Convert.ToDateTime(row["ExpenseDate"]),
                ProsthetistName = row["ProsthetistName"] is DBNull ? "-" : row["ProsthetistName"].ToString()!
            });
        }
        return result;
    }

    // إجمالي المصاريف ضمن فترة + فلتر مرمم اختياري - يُستخدم من ProstheticStatisticsRepository
    // لحساب الدخل الصافي (TotalPayments - هذا الرقم). نفس أسلوب GetTotalOutstanding/GetSummary
    // هناك تماماً (استعلام SUM بسيط بلا أي تحقق صلاحية في طبقة القراءة).
    public decimal GetTotalExpenses(int? prosthetistUserId, DateTime fromDate, DateTime toDateExclusive)
    {
        var filter = prosthetistUserId.HasValue ? " AND ProsthetistUserID = @ProsthetistID" : "";
        var sql = $@"
            SELECT ISNULL(SUM(Amount), 0) AS Total
            FROM dbo.ProstheticExpenses
            WHERE ExpenseDate >= @FromDate AND ExpenseDate < @ToDate{filter}";

        var parameters = new List<SqlParameter> { new("@FromDate", fromDate), new("@ToDate", toDateExclusive) };
        if (prosthetistUserId.HasValue) parameters.Add(new SqlParameter("@ProsthetistID", prosthetistUserId.Value));

        return Convert.ToDecimal(_db.ExecuteScalar(sql, parameters.ToArray()));
    }
}
