using System.Configuration;
using System.Globalization;
using System.Windows;
using DentalClinic.Data.DataAccess;

namespace DentalClinic.DoctorApp;

// لوحة الإحصائيات والتقارير المالية - يجب ألا تُفتح إلا من المدير العام (IsAdmin = true)
public partial class FinancialDashboardWindow : Window
{
    private readonly FinancialRepository _financialRepo;

    public FinancialDashboardWindow()
    {
        InitializeComponent();

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _financialRepo = new FinancialRepository(db);

        Loaded += (s, e) => LoadDashboard();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadDashboard();

    private void LoadDashboard()
    {
        try
        {
            LoadRevenueSummary();
            LoadDailyChart();
            LoadTopTreatments();
            LoadOutstandingBalances();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load the dashboard: " + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadRevenueSummary()
    {
        var summary = _financialRepo.GetRevenueSummary();
        TodayRevenueText.Text = summary.TodayRevenue.ToString("N2", CultureInfo.InvariantCulture);
        MonthRevenueText.Text = summary.MonthRevenue.ToString("N2", CultureInfo.InvariantCulture);
        YearRevenueText.Text = summary.YearRevenue.ToString("N2", CultureInfo.InvariantCulture);
        OutstandingText.Text = summary.TotalOutstanding.ToString("N2", CultureInfo.InvariantCulture);
    }

    private void LoadDailyChart()
    {
        const int days = 14;
        var counts = _financialRepo.GetDailyPatientCounts(days);
        var countByDate = counts.ToDictionary(c => c.Date.Date, c => c.Count);

        // نملأ الأيام التي لا تحتوي زيارات بصفر حتى يبقى الرسم البياني متصلاً بلا فجوات
        var fullRange = Enumerable.Range(0, days)
            .Select(offset => DateTime.Now.Date.AddDays(-(days - 1) + offset))
            .Select(date => new { Date = date, Count = countByDate.TryGetValue(date, out var c) ? c : 0 })
            .ToList();

        var maxCount = Math.Max(1, fullRange.Max(x => x.Count));
        const double maxBarHeight = 130;

        DailyChartItems.ItemsSource = fullRange.Select(x => new BarChartItem
        {
            Label = x.Date.ToString("MM/dd"),
            ValueText = x.Count.ToString(),
            BarHeight = x.Count == 0 ? 3 : Math.Max(6, (x.Count / (double)maxCount) * maxBarHeight)
        }).ToList();
    }

    private void LoadTopTreatments()
    {
        var treatments = _financialRepo.GetTopTreatments(6);

        if (treatments.Count == 0)
        {
            NoTreatmentsText.Visibility = Visibility.Visible;
            TreatmentBarItems.ItemsSource = null;
            return;
        }

        NoTreatmentsText.Visibility = Visibility.Collapsed;
        var maxCount = Math.Max(1, treatments.Max(t => t.Count));
        const double maxBarWidth = 220;

        TreatmentBarItems.ItemsSource = treatments.Select(t => new HorizontalBarItem
        {
            Label = t.TreatmentText,
            ValueText = t.Count.ToString(),
            BarWidth = Math.Max(10, (t.Count / (double)maxCount) * maxBarWidth)
        }).ToList();
    }

    private void LoadOutstandingBalances()
    {
        var balances = _financialRepo.GetOutstandingBalances();
        OutstandingGrid.ItemsSource = balances.Select(b => new OutstandingBalanceRowViewModel(b)).ToList();
    }
}
