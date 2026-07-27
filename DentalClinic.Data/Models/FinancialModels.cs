namespace DentalClinic.Data.Models;

public class RevenueSummary
{
    public decimal TodayRevenue { get; set; }
    public decimal MonthRevenue { get; set; }
    public decimal YearRevenue { get; set; }
    public decimal TotalOutstanding { get; set; }
}

public class OutstandingBalanceRow
{
    public int PatientID { get; set; }
    public string PatientFullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public decimal TotalOwed { get; set; }
    public DateTime LastVisit { get; set; }
}

public class DailyPatientCount
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

public class TreatmentFrequency
{
    public string TreatmentText { get; set; } = string.Empty;
    public int Count { get; set; }
}
public class ExpenseSummary
{
    public decimal TodayExpense { get; set; }
    public decimal MonthExpense { get; set; }
    public decimal YearExpense { get; set; }
}

public class ExpenseRow
{
    public int ExpenseID { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General / Other";
    public DateTime ExpenseDate { get; set; }

    public string DateText => ExpenseDate.ToString("yyyy-MM-dd");
}

public class NetProfitSummary
{
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal NetProfit => TotalIncome - TotalExpense;
}
public class FinancialChartItem
{
    public string Label { get; set; } = string.Empty;
    public decimal Income { get; set; }
    public decimal Expense { get; set; }

    // These will be calculated dynamically to set the visual height of the bars
    public double IncomeBarHeight { get; set; }
    public double ExpenseBarHeight { get; set; }

    // Tooltips for when you hover over the chart with the mouse
    public string IncomeTooltip => $"Income: {Income:N2}";
    public string ExpenseTooltip => $"Expense: {Expense:N2}";
}