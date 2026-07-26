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
