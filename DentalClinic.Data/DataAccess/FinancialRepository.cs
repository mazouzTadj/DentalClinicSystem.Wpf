using System.Data;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

// كل استعلامات اللوحة المالية - يُستخدم من تطبيق الطبيب فقط (صلاحية المدير العام تحديداً)
public class FinancialRepository
{
    private readonly DatabaseHelper _db;

    public FinancialRepository(DatabaseHelper db)
    {
        _db = db;
    }

    // إجمالي المداخيل (المبالغ المُحصَّلة فعلياً PaidAmount، وليس TotalPrice) - يومي/شهري/سنوي + إجمالي المتبقي
    public RevenueSummary GetRevenueSummary()
    {
        const string sql = @"
            SELECT
                ISNULL(SUM(CASE WHEN CAST(SessionDateTime AS DATE) = CAST(GETDATE() AS DATE) THEN PaidAmount ELSE 0 END), 0) AS TodayRevenue,
                ISNULL(SUM(CASE WHEN YEAR(SessionDateTime) = YEAR(GETDATE()) AND MONTH(SessionDateTime) = MONTH(GETDATE()) THEN PaidAmount ELSE 0 END), 0) AS MonthRevenue,
                ISNULL(SUM(CASE WHEN YEAR(SessionDateTime) = YEAR(GETDATE()) THEN PaidAmount ELSE 0 END), 0) AS YearRevenue,
                ISNULL(SUM(TotalPrice - PaidAmount), 0) AS TotalOutstanding
            FROM MedicalSessions";

        var table = _db.ExecuteQuery(sql);
        var row = table.Rows[0];

        return new RevenueSummary
        {
            TodayRevenue = (decimal)row["TodayRevenue"],
            MonthRevenue = (decimal)row["MonthRevenue"],
            YearRevenue = (decimal)row["YearRevenue"],
            TotalOutstanding = (decimal)row["TotalOutstanding"]
        };
    }

    // كل المرضى الذين لديهم مبلغ متبقٍ غير مسدَّد، مرتّبين تنازلياً حسب الأكبر ديناً
    public List<OutstandingBalanceRow> GetOutstandingBalances()
    {
        const string sql = @"
            SELECT p.PatientID, p.FullName AS PatientFullName, p.PhoneNumber,
                   SUM(s.TotalPrice - s.PaidAmount) AS TotalOwed,
                   MAX(s.SessionDateTime) AS LastVisit
            FROM MedicalSessions s
            INNER JOIN Patients p ON p.PatientID = s.PatientID
            GROUP BY p.PatientID, p.FullName, p.PhoneNumber
            HAVING SUM(s.TotalPrice - s.PaidAmount) > 0
            ORDER BY TotalOwed DESC";

        var table = _db.ExecuteQuery(sql);
        var result = new List<OutstandingBalanceRow>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(new OutstandingBalanceRow
            {
                PatientID = (int)row["PatientID"],
                PatientFullName = row["PatientFullName"].ToString()!,
                PhoneNumber = row["PhoneNumber"].ToString()!,
                TotalOwed = (decimal)row["TotalOwed"],
                LastVisit = (DateTime)row["LastVisit"]
            });
        }
        return result;
    }

    // عدد المرضى (الفريدين) لكل يوم خلال آخر N يوماً - أيام بلا زيارات لا تظهر في النتيجة (تُملأ لاحقاً في الواجهة)
    public List<DailyPatientCount> GetDailyPatientCounts(int days = 14)
    {
        const string sql = @"
            SELECT CAST(SessionDateTime AS DATE) AS VisitDay, COUNT(DISTINCT PatientID) AS PatientCount
            FROM MedicalSessions
            WHERE SessionDateTime >= @FromDate
            GROUP BY CAST(SessionDateTime AS DATE)
            ORDER BY VisitDay";

        var fromDate = DateTime.Now.Date.AddDays(-(days - 1));
        var table = _db.ExecuteQuery(sql, new SqlParameter("@FromDate", fromDate));

        var result = new List<DailyPatientCount>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(new DailyPatientCount
            {
                Date = (DateTime)row["VisitDay"],
                Count = (int)row["PatientCount"]
            });
        }
        return result;
    }

    // أكثر النصوص تكراراً في حقل "المعالجة" - تقريب بسيط لأكثر الخدمات طلباً
    // (يعتمد على تطابق نصي حرفي، فيُنصح لاحقاً بحقل نوع علاج منظّم لدقة أعلى - هذا كافٍ كبداية عملية)
    public List<TreatmentFrequency> GetTopTreatments(int top = 5)
    {
        const string sql = @"
            SELECT TOP (@Top) TreatmentPerformed, COUNT(*) AS Cnt
            FROM MedicalSessions
            WHERE TreatmentPerformed IS NOT NULL AND LTRIM(RTRIM(TreatmentPerformed)) <> ''
            GROUP BY TreatmentPerformed
            ORDER BY COUNT(*) DESC";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@Top", top));
        var result = new List<TreatmentFrequency>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(new TreatmentFrequency
            {
                TreatmentText = row["TreatmentPerformed"].ToString()!,
                Count = (int)row["Cnt"]
            });
        }
        return result;
    }
    // --------------------------------------------------------
    // دوال المصاريف (Expenses) الجديدة
    // --------------------------------------------------------

    public void AddExpense(decimal amount, string description, string category, DateTime date)
    {
        const string sql = @"
        INSERT INTO ClinicExpenses (Amount, Description, Category, ExpenseDate) 
        VALUES (@Amount, @Desc, @Cat, @Date)";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@Amount", amount),
            new SqlParameter("@Desc", description),
            new SqlParameter("@Cat", category),
            new SqlParameter("@Date", date));
    }

    public ExpenseSummary GetExpenseSummary()
    {
        const string sql = @"
            SELECT
                ISNULL(SUM(CASE WHEN CAST(ExpenseDate AS DATE) = CAST(GETDATE() AS DATE) THEN Amount ELSE 0 END), 0) AS TodayExpense,
                ISNULL(SUM(CASE WHEN YEAR(ExpenseDate) = YEAR(GETDATE()) AND MONTH(ExpenseDate) = MONTH(GETDATE()) THEN Amount ELSE 0 END), 0) AS MonthExpense,
                ISNULL(SUM(CASE WHEN YEAR(ExpenseDate) = YEAR(GETDATE()) THEN Amount ELSE 0 END), 0) AS YearExpense
            FROM ClinicExpenses";

        var table = _db.ExecuteQuery(sql);
        var row = table.Rows[0];

        return new ExpenseSummary
        {
            TodayExpense = (decimal)row["TodayExpense"],
            MonthExpense = (decimal)row["MonthExpense"],
            YearExpense = (decimal)row["YearExpense"]
        };
    }

    public List<ExpenseRow> GetRecentExpenses(int top = 50)
    {
        const string sql = @"
        SELECT TOP (@Top) ExpenseID, Amount, Description, Category, ExpenseDate 
        FROM ClinicExpenses 
        ORDER BY ExpenseDate DESC";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@Top", top));
        var result = new List<ExpenseRow>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(new ExpenseRow
            {
                ExpenseID = (int)row["ExpenseID"],
                Amount = (decimal)row["Amount"],
                Description = row["Description"].ToString()!,
                Category = row["Category"] != DBNull.Value ? row["Category"].ToString()! : "General / Other",
                ExpenseDate = (DateTime)row["ExpenseDate"]
            });
        }
        return result;
    }

    // دالة لحساب الربح الصافي (مثلاً لهذا الشهر)
    public NetProfitSummary GetMonthNetProfit()
    {
        const string sql = @"
            DECLARE @Income DECIMAL(18,2) = (SELECT ISNULL(SUM(PaidAmount), 0) FROM MedicalSessions WHERE YEAR(SessionDateTime) = YEAR(GETDATE()) AND MONTH(SessionDateTime) = MONTH(GETDATE()));
            DECLARE @Expense DECIMAL(18,2) = (SELECT ISNULL(SUM(Amount), 0) FROM ClinicExpenses WHERE YEAR(ExpenseDate) = YEAR(GETDATE()) AND MONTH(ExpenseDate) = MONTH(GETDATE()));
            SELECT @Income AS TotalIncome, @Expense AS TotalExpense;";

        var table = _db.ExecuteQuery(sql);
        var row = table.Rows[0];

        return new NetProfitSummary
        {
            TotalIncome = (decimal)row["TotalIncome"],
            TotalExpense = (decimal)row["TotalExpense"]
        };
    }
    // دالة جلب وتجميع بيانات المخطط البياني حسب الفترة (Weekly / Monthly / Yearly)
    public List<FinancialChartItem> GetFinancialChartData(string period)
    {
        var result = new List<FinancialChartItem>();

        if (period == "Weekly")
        {
            var startDate = DateTime.Now.Date.AddDays(-6);

            const string incomeSql = @"
            SELECT CAST(SessionDateTime AS DATE) AS TransDate, ISNULL(SUM(PaidAmount), 0) AS Total
            FROM MedicalSessions
            WHERE CAST(SessionDateTime AS DATE) >= @FromDate
            GROUP BY CAST(SessionDateTime AS DATE)";

            const string expenseSql = @"
            SELECT CAST(ExpenseDate AS DATE) AS TransDate, ISNULL(SUM(Amount), 0) AS Total
            FROM ClinicExpenses
            WHERE CAST(ExpenseDate AS DATE) >= @FromDate
            GROUP BY CAST(ExpenseDate AS DATE)";

            var incomeTable = _db.ExecuteQuery(incomeSql, new SqlParameter("@FromDate", startDate));
            var expenseTable = _db.ExecuteQuery(expenseSql, new SqlParameter("@FromDate", startDate));

            var incomeDict = incomeTable.Rows.Cast<DataRow>()
                .ToDictionary(r => ((DateTime)r["TransDate"]).Date, r => (decimal)r["Total"]);
            var expenseDict = expenseTable.Rows.Cast<DataRow>()
                .ToDictionary(r => ((DateTime)r["TransDate"]).Date, r => (decimal)r["Total"]);

            for (int i = 6; i >= 0; i--)
            {
                var date = DateTime.Now.Date.AddDays(-i);
                decimal inc = incomeDict.TryGetValue(date, out var v1) ? v1 : 0;
                decimal exp = expenseDict.TryGetValue(date, out var v2) ? v2 : 0;

                result.Add(new FinancialChartItem
                {
                    Label = date.ToString("ddd"),
                    Income = inc,
                    Expense = exp
                });
            }
        }
        else if (period == "Monthly")
        {
            var startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-5);

            const string incomeSql = @"
            SELECT YEAR(SessionDateTime) AS Yr, MONTH(SessionDateTime) AS Mth, ISNULL(SUM(PaidAmount), 0) AS Total
            FROM MedicalSessions
            WHERE SessionDateTime >= @FromDate
            GROUP BY YEAR(SessionDateTime), MONTH(SessionDateTime)";

            const string expenseSql = @"
            SELECT YEAR(ExpenseDate) AS Yr, MONTH(ExpenseDate) AS Mth, ISNULL(SUM(Amount), 0) AS Total
            FROM ClinicExpenses
            WHERE ExpenseDate >= @FromDate
            GROUP BY YEAR(ExpenseDate), MONTH(ExpenseDate)";

            var incomeTable = _db.ExecuteQuery(incomeSql, new SqlParameter("@FromDate", startDate));
            var expenseTable = _db.ExecuteQuery(expenseSql, new SqlParameter("@FromDate", startDate));

            var incomeDict = incomeTable.Rows.Cast<DataRow>()
                .ToDictionary(r => $"{r["Yr"]}-{r["Mth"]}", r => (decimal)r["Total"]);
            var expenseDict = expenseTable.Rows.Cast<DataRow>()
                .ToDictionary(r => $"{r["Yr"]}-{r["Mth"]}", r => (decimal)r["Total"]);

            for (int i = 5; i >= 0; i--)
            {
                var targetMonth = DateTime.Now.AddMonths(-i);
                string key = $"{targetMonth.Year}-{targetMonth.Month}";

                decimal inc = incomeDict.TryGetValue(key, out var v1) ? v1 : 0;
                decimal exp = expenseDict.TryGetValue(key, out var v2) ? v2 : 0;

                result.Add(new FinancialChartItem
                {
                    Label = targetMonth.ToString("MMM"),
                    Income = inc,
                    Expense = exp
                });
            }
        }
        else if (period == "Yearly")
        {
            int startYear = DateTime.Now.Year - 4;

            const string incomeSql = @"
            SELECT YEAR(SessionDateTime) AS Yr, ISNULL(SUM(PaidAmount), 0) AS Total
            FROM MedicalSessions
            WHERE YEAR(SessionDateTime) >= @StartYear
            GROUP BY YEAR(SessionDateTime)";

            const string expenseSql = @"
            SELECT YEAR(ExpenseDate) AS Yr, ISNULL(SUM(Amount), 0) AS Total
            FROM ClinicExpenses
            WHERE YEAR(ExpenseDate) >= @StartYear
            GROUP BY YEAR(ExpenseDate)";

            var incomeTable = _db.ExecuteQuery(incomeSql, new SqlParameter("@StartYear", startYear));
            var expenseTable = _db.ExecuteQuery(expenseSql, new SqlParameter("@StartYear", startYear));

            var incomeDict = incomeTable.Rows.Cast<DataRow>()
                .ToDictionary(r => (int)r["Yr"], r => (decimal)r["Total"]);
            var expenseDict = expenseTable.Rows.Cast<DataRow>()
                .ToDictionary(r => (int)r["Yr"], r => (decimal)r["Total"]);

            for (int i = 4; i >= 0; i--)
            {
                int year = DateTime.Now.Year - i;
                decimal inc = incomeDict.TryGetValue(year, out var v1) ? v1 : 0;
                decimal exp = expenseDict.TryGetValue(year, out var v2) ? v2 : 0;

                result.Add(new FinancialChartItem
                {
                    Label = year.ToString(),
                    Income = inc,
                    Expense = exp
                });
            }
        }

        // حساب الارتفاعات ديناميكياً للنسب المئوية للأعمدة
        decimal maxAmount = result.Count > 0 ? result.Max(x => Math.Max(x.Income, x.Expense)) : 0;
        double maxBarHeight = 170.0;

        foreach (var item in result)
        {
            item.IncomeBarHeight = maxAmount > 0 ? (double)(item.Income / maxAmount) * maxBarHeight : 0;
            item.ExpenseBarHeight = maxAmount > 0 ? (double)(item.Expense / maxAmount) * maxBarHeight : 0;
        }

        return result;
    }
}
