using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class FinancialDashboardWindow : Window
{
    private readonly FinancialRepository _financialRepo;
    private readonly DoctorCommissionService _commissionService;
    private readonly UserAccount? _currentUser;

    // currentUser اختياري للحفاظ على التوافق مع أي استدعاء قديم، لكنه ضروري لإظهار زر
    // "إعدادات العمولات" فقط لمن يملك صلاحية إدارة المستخدمين (المدير العام/الطبيب الرئيسي عادةً)
    public FinancialDashboardWindow(UserAccount? currentUser = null)
    {
        InitializeComponent();
        _currentUser = currentUser;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"]?.ConnectionString
                               ?? "Server=.;Database=DentalClinicDB;Trusted_Connection=True;TrustServerCertificate=True;";
        var db = new DatabaseHelper(connectionString);
        _financialRepo = new FinancialRepository(db);
        _commissionService = new DoctorCommissionService(db);

        if (_currentUser != null && _currentUser.HasPermission(UserPermission.ManageUsers))
        {
            BtnCommissionSettings.Visibility = Visibility.Visible;
        }

        // ضبط تواريخ التصفية المخصصة الافتراضية
        DpStartDate.SelectedDate = DateTime.Now.AddMonths(-1);
        DpEndDate.SelectedDate = DateTime.Now;

        Loaded += async (s, e) => await LoadDashboardAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadDashboardAsync();

    // -------------------------------------------------------------
    // التحكم في التبويبات (Tabs)
    // -------------------------------------------------------------
    private async void TabButton_Click(object sender, RoutedEventArgs e)
    {
        var clickedBtn = sender as Button;

        // إعادة الأزرار لشكلها العادي
        BtnTabIncome.Style = (Style)FindResource("SecondaryButtonStyle");
        BtnTabExpenses.Style = (Style)FindResource("SecondaryButtonStyle");
        BtnTabNetProfit.Style = (Style)FindResource("SecondaryButtonStyle");

        // إخفاء كل الشاشات
        IncomeGrid.Visibility = Visibility.Collapsed;
        ExpensesGrid.Visibility = Visibility.Collapsed;
        NetProfitGrid.Visibility = Visibility.Collapsed;

        // تفعيل الزر المضغوط وإظهار شاشته
        if (clickedBtn != null)
        {
            clickedBtn.Style = (Style)FindResource("PrimaryButtonStyle");

            if (clickedBtn.Name == "BtnTabIncome")
                IncomeGrid.Visibility = Visibility.Visible;
            else if (clickedBtn.Name == "BtnTabExpenses")
                ExpensesGrid.Visibility = Visibility.Visible;
            else if (clickedBtn.Name == "BtnTabNetProfit")
                NetProfitGrid.Visibility = Visibility.Visible;
        }

        await LoadDashboardAsync(); // تحديث البيانات عند التبديل
    }

    // -------------------------------------------------------------
    // جلب وتوزيع البيانات بشكل غير متزامن مع مؤشر التحميل
    // -------------------------------------------------------------
    private async Task LoadDashboardAsync()
    {
        ShowLoading();
        try
        {
            if (IncomeGrid.Visibility == Visibility.Visible)
            {
                await Task.Run(() =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        LoadRevenueSummary();
                        LoadDailyChart();
                        LoadDoctorStats();
                        LoadOutstandingBalances();
                    });
                });
            }
            else if (ExpensesGrid.Visibility == Visibility.Visible)
            {
                await Task.Run(() =>
                {
                    Dispatcher.Invoke(() => LoadExpensesSummary());
                });
            }
            else if (NetProfitGrid.Visibility == Visibility.Visible)
            {
                await Task.Run(() =>
                {
                    Dispatcher.Invoke(() => LoadNetProfit());
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Fin_LoadErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            HideLoading();
        }
    }

    private void ShowLoading() => LoadingOverlay.Visibility = Visibility.Visible;
    private void HideLoading() => LoadingOverlay.Visibility = Visibility.Collapsed;

    // --- توابع المداخيل ---
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

        var fullRange = Enumerable.Range(0, days)
            .Select(offset => DateTime.Now.Date.AddDays(-(days - 1) + offset))
            .Select(date => new { Date = date, Count = countByDate.TryGetValue(date, out var c) ? c : 0 })
            .ToList();

        DailyChartEmptyState.Visibility = fullRange.All(x => x.Count == 0) ? Visibility.Visible : Visibility.Collapsed;

        var maxCount = Math.Max(1, fullRange.Max(x => x.Count));
        const double maxBarHeight = 130;

        DailyChartItems.ItemsSource = fullRange.Select(x => new BarChartItem
        {
            Label = x.Date.ToString("MM/dd"),
            ValueText = x.Count.ToString(),
            BarHeight = x.Count == 0 ? 3 : Math.Max(6, (x.Count / (double)maxCount) * maxBarHeight)
        }).ToList();
    }

    // لوحة "إحصائيات الأطباء" (حلّت محل "أكثر العلاجات شيوعًا"): عدد المرضى والدخل ونصيب
    // العمولة لكل طبيب، مع تمييز الطبيب الرئيسي بتاج 👑
    private void LoadDoctorStats()
    {
        var stats = _commissionService.GetDoctorStatisticsWithCommission();
        if (stats == null || stats.Count == 0)
        {
            NoDoctorStatsText.Visibility = Visibility.Visible;
            DoctorStatsItems.ItemsSource = null;
            return;
        }

        NoDoctorStatsText.Visibility = Visibility.Collapsed;
        const double maxBarWidth = 220;
        var maxIncome = (double)Math.Max(1, stats.Max(s => s.GrossIncome));

        DoctorStatsItems.ItemsSource = stats
            .Select(s => new DoctorStatRowViewModel(s, maxIncome, maxBarWidth))
            .ToList();
    }

    // فتح شاشة إعدادات نسبة عمولة الأطباء (متاحة فقط لمن يملك صلاحية ManageUsers - انظر المُنشئ)
    private void CommissionSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWin = new CommissionSettingsWindow(_commissionService) { Owner = this };
        if (settingsWin.ShowDialog() == true)
        {
            _ = LoadDashboardAsync();
        }
    }

    private void LoadOutstandingBalances()
    {
        var balances = _financialRepo.GetOutstandingBalances();
        OutstandingGrid.ItemsSource = balances.Select(b => new OutstandingBalanceRowViewModel(b)).ToList();
    }

    // --- توابع المصاريف والصافي والمخطط البياني ---
    private void LoadExpensesSummary()
    {
        var summary = _financialRepo.GetExpenseSummary();
        TodayExpenseText.Text = summary.TodayExpense.ToString("N2", CultureInfo.InvariantCulture);
        MonthExpenseText.Text = summary.MonthExpense.ToString("N2", CultureInfo.InvariantCulture);
        YearExpenseText.Text = summary.YearExpense.ToString("N2", CultureInfo.InvariantCulture);

        var expenses = _financialRepo.GetRecentExpenses();
        ExpensesDataGrid.ItemsSource = expenses;
    }

    // حذف مصروف (بعد تأكيد المستخدم) - يعمل مع المصاريف اليدوية وكذلك عمولات الأطباء التلقائية،
    // لتصحيح أي خطأ (مبلغ خاطئ، طبيب خاطئ، إدخال مكرر...) دون الحاجة للوصول لقاعدة البيانات مباشرة
    private void DeleteExpenseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ExpenseRow expense) return;

        var message = expense.IsAutoGenerated
            ? LocalizationManager.T("Expense_DeleteConfirmAutoMessageFormat", expense.Description, expense.Amount.ToString("N2", CultureInfo.InvariantCulture))
            : LocalizationManager.T("Expense_DeleteConfirmMessageFormat", expense.Description, expense.Amount.ToString("N2", CultureInfo.InvariantCulture));

        var confirm = MessageBox.Show(message, LocalizationManager.T("Expense_DeleteConfirmTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _financialRepo.DeleteExpense(expense.ExpenseID);
            LoadExpensesSummary();
            LoadNetProfit();
            LoadDoctorStats();
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Expense_DeleteErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadNetProfit()
    {
        var net = _financialRepo.GetMonthNetProfit();
        NetIncomeText.Text = net.TotalIncome.ToString("N2", CultureInfo.InvariantCulture);
        NetExpenseText.Text = net.TotalExpense.ToString("N2", CultureInfo.InvariantCulture);
        NetProfitText.Text = net.NetProfit.ToString("N2", CultureInfo.InvariantCulture);

        NetProfitText.Foreground = net.NetProfit >= 0 ? new SolidColorBrush(Color.FromRgb(46, 125, 50)) : new SolidColorBrush(Colors.Red);

        // تحميل بيانات المخطط البياني للفترة الافتراضية (Weekly)
        LoadChartData("Weekly");
    }

    private void AddExpenseButton_Click(object sender, RoutedEventArgs e)
    {
        var addWin = new AddExpenseWindow();
        addWin.Owner = this;
        if (addWin.ShowDialog() == true)
        {
            _ = LoadDashboardAsync();
        }
    }

    // حدث الضغط على أزرار التبديل للفترات الزمانية السريعة
    private void ChartPeriod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button clickedButton)
        {
            BtnChartWeekly.Style = (Style)FindResource("SecondaryButtonStyle");
            BtnChartMonthly.Style = (Style)FindResource("SecondaryButtonStyle");
            BtnChartYearly.Style = (Style)FindResource("SecondaryButtonStyle");

            clickedButton.Style = (Style)FindResource("PrimaryButtonStyle");

            if (clickedButton == BtnChartWeekly)
                LoadChartData("Weekly");
            else if (clickedButton == BtnChartMonthly)
                LoadChartData("Monthly");
            else if (clickedButton == BtnChartYearly)
                LoadChartData("Yearly");
        }
    }

    // حدث تطبيق فلتر التواريخ المخصص
    private void ApplyDateRange_Click(object sender, RoutedEventArgs e)
    {
        if (!DpStartDate.SelectedDate.HasValue || !DpEndDate.SelectedDate.HasValue)
        {
            MessageBox.Show(LocalizationManager.T("Fin_SelectBothDates"), LocalizationManager.T("Fin_DateRangeErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DateTime startDate = DpStartDate.SelectedDate.Value.Date;
        DateTime endDate = DpEndDate.SelectedDate.Value.Date;

        if (startDate > endDate)
        {
            MessageBox.Show(LocalizationManager.T("Fin_StartAfterEnd"), LocalizationManager.T("Fin_DateRangeErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnChartWeekly.Style = (Style)FindResource("SecondaryButtonStyle");
        BtnChartMonthly.Style = (Style)FindResource("SecondaryButtonStyle");
        BtnChartYearly.Style = (Style)FindResource("SecondaryButtonStyle");

        LoadCustomDateRangeChartData(startDate, endDate);
    }

    private void LoadChartData(string period)
    {
        var chartData = _financialRepo.GetFinancialChartData(period) ?? new List<FinancialChartItem>();
        UpdateProfitChartUI(chartData);
    }

    private void LoadCustomDateRangeChartData(DateTime startDate, DateTime endDate)
    {
        var allData = _financialRepo.GetFinancialChartData("Monthly") ?? new List<FinancialChartItem>();
        UpdateProfitChartUI(allData);
    }

    private void UpdateProfitChartUI(List<FinancialChartItem> chartData)
    {
        ProfitChartItems.ItemsSource = chartData;
        ProfitChartEmptyState.Visibility = (chartData == null || chartData.Count == 0) ? Visibility.Visible : Visibility.Collapsed;

        if (chartData != null && chartData.Count > 0)
        {
            decimal totalIncome = chartData.Sum(x => x.Income);
            decimal totalExpense = chartData.Sum(x => x.Expense);
            decimal netProfit = totalIncome - totalExpense;

            NetIncomeText.Text = totalIncome.ToString("N2", CultureInfo.InvariantCulture);
            NetExpenseText.Text = totalExpense.ToString("N2", CultureInfo.InvariantCulture);
            NetProfitText.Text = netProfit.ToString("N2", CultureInfo.InvariantCulture);

            NetProfitText.Foreground = netProfit >= 0
                ? new SolidColorBrush(Color.FromRgb(46, 125, 50))
                : new SolidColorBrush(Colors.Red);
        }
    }

    // -------------------------------------------------------------
    // تصدير التقارير المالية (Export to CSV / Excel)
    // -------------------------------------------------------------
    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                DefaultExt = "csv"
            };

            if (IncomeGrid.Visibility == Visibility.Visible)
            {
                saveFileDialog.FileName = $"Income_Outstanding_Report_{DateTime.Now:yyyyMMdd}.csv";
                if (saveFileDialog.ShowDialog() == true)
                {
                    ExportOutstandingToCsv(saveFileDialog.FileName);
                }
            }
            else if (ExpensesGrid.Visibility == Visibility.Visible)
            {
                saveFileDialog.FileName = $"Expenses_Report_{DateTime.Now:yyyyMMdd}.csv";
                if (saveFileDialog.ShowDialog() == true)
                {
                    ExportExpensesToCsv(saveFileDialog.FileName);
                }
            }
            else if (NetProfitGrid.Visibility == Visibility.Visible)
            {
                saveFileDialog.FileName = $"NetProfit_Summary_{DateTime.Now:yyyyMMdd}.csv";
                if (saveFileDialog.ShowDialog() == true)
                {
                    ExportNetProfitToCsv(saveFileDialog.FileName);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Fin_ExportErrorFormat", ex.Message), LocalizationManager.T("Fin_ExportErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportExpensesToCsv(string filePath)
    {
        var items = ExpensesDataGrid.ItemsSource as System.Collections.IEnumerable;
        if (items == null) return;

        var csv = new StringBuilder();
        csv.AppendLine("Date,Category,Description,Amount");

        foreach (dynamic item in items)
        {
            csv.AppendLine($"\"{item.DateText}\",\"{item.Category}\",\"{item.Description}\",{item.Amount}");
        }

        File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        MessageBox.Show(LocalizationManager.T("Fin_ExpensesExportedMsg"), LocalizationManager.T("Fin_ExportDoneTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExportOutstandingToCsv(string filePath)
    {
        var items = OutstandingGrid.ItemsSource as IEnumerable<OutstandingBalanceRowViewModel>;
        if (items == null) return;

        var csv = new StringBuilder();
        csv.AppendLine("Patient,Phone,Last Visit,Amount Owed");

        foreach (var item in items)
        {
            csv.AppendLine($"\"{item.PatientFullName}\",\"{item.PhoneNumber}\",\"{item.LastVisitText}\",\"{item.TotalOwedText}\"");
        }

        File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        MessageBox.Show(LocalizationManager.T("Fin_OutstandingExportedMsg"), LocalizationManager.T("Fin_ExportDoneTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExportNetProfitToCsv(string filePath)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Metric,Amount");
        csv.AppendLine($"Total Income,{NetIncomeText.Text}");
        csv.AppendLine($"Total Expenses,{NetExpenseText.Text}");
        csv.AppendLine($"Net Profit,{NetProfitText.Text}");

        File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        MessageBox.Show(LocalizationManager.T("Fin_NetProfitExportedMsg"), LocalizationManager.T("Fin_ExportDoneTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }
}