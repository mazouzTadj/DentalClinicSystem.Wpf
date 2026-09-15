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
    private readonly PaymentRepository _paymentRepo;
    private readonly UserAccount? _currentUser;

    // currentUser اختياري للحفاظ على التوافق مع أي استدعاء قديم، لكنه ضروري لإظهار زر
    // "إعدادات العمولات" فقط لمن يملك صلاحية إدارة المستخدمين (المدير العام/الطبيب الرئيسي عادةً)
    public FinancialDashboardWindow(UserAccount? currentUser = null)
    {
        InitializeComponent();
        _currentUser = currentUser;

        // نفس إصلاح PatientFileWindow: لا تسمح للنافذة أن تتجاوز الشاشات الصغيرة
        var maxAvailableHeight = SystemParameters.WorkArea.Height - 20;
        if (Height > maxAvailableHeight) Height = maxAvailableHeight;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"]?.ConnectionString
                               ?? "Server=.;Database=DentalClinicDB;Trusted_Connection=True;TrustServerCertificate=True;";
        var db = new DatabaseHelper(connectionString);
        _financialRepo = new FinancialRepository(db);
        _commissionService = new DoctorCommissionService(db);
        _paymentRepo = new PaymentRepository(db);

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
                        LoadIncomePaymentsGrid();
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
        TodayRevenueText.Text = MoneyFormatter.Format(summary.TodayRevenue);
        MonthRevenueText.Text = MoneyFormatter.Format(summary.MonthRevenue);
        YearRevenueText.Text = MoneyFormatter.Format(summary.YearRevenue);
        OutstandingText.Text = MoneyFormatter.Format(summary.TotalOutstanding);

        SetTrend(TodayRevenueTrendText, summary.TodayRevenue, summary.YesterdayRevenue, "Fin_CompareYesterday");
        SetTrend(MonthRevenueTrendText, summary.MonthRevenue, summary.LastMonthRevenue, "Fin_CompareLastMonth");
        SetTrend(YearRevenueTrendText, summary.YearRevenue, summary.LastYearRevenue, "Fin_CompareLastYear");
    }

    // مؤشر الاتجاه (↑/↓ نسبة مئوية مقارنة بالفترة المناظرة السابقة) - يُستخدم في بطاقات الإيراد
    // وبطاقات صافي الربح معاً. compareLabelKey مفتاح ترجمة لاسم الفترة المقارَنة (أمس/الشهر الماضي/العام الماضي)
    private static void SetTrend(TextBlock target, decimal current, decimal previous, string compareLabelKey)
    {
        var compareLabel = LocalizationManager.T(compareLabelKey);

        if (previous <= 0)
        {
            if (current <= 0)
            {
                target.Text = string.Empty;
                return;
            }
            target.Text = LocalizationManager.T("Fin_TrendNew");
            target.Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x44, 0xAD));
            return;
        }

        var changePercent = Math.Round((current - previous) / previous * 100m, 1);

        if (changePercent == 0)
        {
            target.Text = LocalizationManager.T("Fin_TrendFlatFormat", compareLabel);
            target.Foreground = new SolidColorBrush(Colors.Gray);
        }
        else if (changePercent > 0)
        {
            target.Text = LocalizationManager.T("Fin_TrendUpFormat", changePercent, compareLabel);
            target.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        }
        else
        {
            target.Text = LocalizationManager.T("Fin_TrendDownFormat", Math.Abs(changePercent), compareLabel);
            target.Foreground = new SolidColorBrush(Colors.Red);
        }
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

    private void DoctorStatsButton_Click(object sender, RoutedEventArgs e)
    {
        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"]?.ConnectionString
                               ?? "Server=.;Database=DentalClinicDB;Trusted_Connection=True;TrustServerCertificate=True;";
        var db = new DatabaseHelper(connectionString);
        var window = new DoctorStatisticsWindow(db) { Owner = this };
        window.ShowDialog();
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

    // آخر 50 دفعة (أو أقل عند التصفية باسم مريض) - جدول "المداخيل" الجديد بجانب Outstanding Balances
    private void LoadIncomePaymentsGrid()
    {
        var payments = _paymentRepo.GetRecentPayments(50, IncomeSearchBox.Text);
        IncomePaymentsGrid.ItemsSource = payments.Select(p => new IncomeRowViewModel(p)).ToList();
        IncomeEmptyText.Visibility = payments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void IncomeSearchBox_TextChanged(object sender, TextChangedEventArgs e) => LoadIncomePaymentsGrid();

    private static void SetSelectionFromCheckBox(object sender, bool selected)
    {
        if (sender is not CheckBox checkBox) return;

        switch (checkBox.DataContext)
        {
            case OutstandingBalanceRowViewModel outstanding:
                outstanding.IsSelected = selected;
                break;
            case IncomeRowViewModel income:
                income.IsSelected = selected;
                break;
            case ExpenseRow expense:
                expense.IsSelected = selected;
                break;
        }
    }

    private void OutstandingSelectionCheckBox_Checked(object sender, RoutedEventArgs e)
        => SetSelectionFromCheckBox(sender, true);

    private void OutstandingSelectionCheckBox_Unchecked(object sender, RoutedEventArgs e)
        => SetSelectionFromCheckBox(sender, false);

    private void IncomeSelectionCheckBox_Checked(object sender, RoutedEventArgs e)
        => SetSelectionFromCheckBox(sender, true);

    private void IncomeSelectionCheckBox_Unchecked(object sender, RoutedEventArgs e)
        => SetSelectionFromCheckBox(sender, false);

    private void ExpenseSelectionCheckBox_Checked(object sender, RoutedEventArgs e)
        => SetSelectionFromCheckBox(sender, true);

    private void ExpenseSelectionCheckBox_Unchecked(object sender, RoutedEventArgs e)
        => SetSelectionFromCheckBox(sender, false);

    private void SelectAllOutstandingButton_Click(object sender, RoutedEventArgs e)
    {
        if (OutstandingGrid.ItemsSource is not IEnumerable<OutstandingBalanceRowViewModel> rows) return;
        foreach (var row in rows) row.IsSelected = true;
        OutstandingGrid.Items.Refresh();
    }

    private void ClearOutstandingSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (OutstandingGrid.ItemsSource is not IEnumerable<OutstandingBalanceRowViewModel> rows) return;
        foreach (var row in rows) row.IsSelected = false;
        OutstandingGrid.Items.Refresh();
    }

    private void ZeroSelectedOutstandingButton_Click(object sender, RoutedEventArgs e)
    {
        if (OutstandingGrid.ItemsSource is not IEnumerable<OutstandingBalanceRowViewModel> rows) return;
        var selected = rows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;
        if (_currentUser == null)
        {
            MessageBox.Show(LocalizationManager.T("Fin_ZeroSelectedNeedsUser"), LocalizationManager.T("Common_AccessDenied"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            LocalizationManager.T("Fin_ZeroSelectedConfirmFormat", selected.Count),
            LocalizationManager.T("Fin_ZeroSelectedTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var db = new DatabaseHelper(ConfigurationManager.ConnectionStrings["DentalClinicDB"]?.ConnectionString
                                       ?? "Server=.;Database=DentalClinicDB;Trusted_Connection=True;TrustServerCertificate=True;");
            var paymentRepo = new PaymentRepository(db);
            var recordedPayments = 0;

            foreach (var row in selected)
            {
                // تسجيل الرصيد المتبقي كدفعات فعلية في Payments، وبالتالي يظهر مباشرة في Recent Income.
                recordedPayments += paymentRepo.ZeroOutstandingForPatient(
                    row.PatientID,
                    _currentUser.UserID,
                    "Finance: bulk payment / zero balance");
            }

            // تحديث الجدولين مباشرة حتى لا تبقى واجهة Finance تعرض البيانات القديمة.
            LoadOutstandingBalances();
            LoadIncomePaymentsGrid();
            LoadRevenueSummary();
            OutstandingGrid.UpdateLayout();
            IncomePaymentsGrid.UpdateLayout();

            // إعادة تحميل بقية مؤشرات اللوحة أيضاً.
            _ = LoadDashboardAsync();

            if (recordedPayments == 0)
            {
                MessageBox.Show(
                    LocalizationManager.T("Fin_ZeroSelectedNoPayments"),
                    LocalizationManager.T("Common_Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Fin_LoadErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SelectAllIncomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (IncomePaymentsGrid.ItemsSource is not IEnumerable<IncomeRowViewModel> rows) return;
        foreach (var row in rows) row.IsSelected = true;
        IncomePaymentsGrid.Items.Refresh();
    }

    private void DeleteSelectedIncomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (IncomePaymentsGrid.ItemsSource is not IEnumerable<IncomeRowViewModel> rows) return;
        var selected = rows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        var confirm = MessageBox.Show(
            LocalizationManager.T("Fin_DeleteSelectedConfirmFormat", selected.Count),
            LocalizationManager.T("Fin_DeleteIncomeTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            foreach (var row in selected) _paymentRepo.DeletePayment(row.PaymentID);
            _ = LoadDashboardAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Fin_LoadErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SelectAllExpensesButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExpensesDataGrid.ItemsSource is not IEnumerable<ExpenseRow> rows) return;
        foreach (var row in rows) row.IsSelected = true;
        ExpensesDataGrid.Items.Refresh();
    }

    private void DeleteSelectedExpensesButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExpensesDataGrid.ItemsSource is not IEnumerable<ExpenseRow> rows) return;
        var selected = rows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        var confirm = MessageBox.Show(
            LocalizationManager.T("Fin_DeleteSelectedConfirmFormat", selected.Count),
            LocalizationManager.T("Fin_RecentExpenses"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            foreach (var row in selected) _financialRepo.DeleteExpense(row.ExpenseID);
            LoadExpensesSummary();
            LoadNetProfit();
            LoadDoctorStats();
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Expense_DeleteErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    // تعديل مبلغ دفعة (تصحيح خطأ إدخال فقط) - يُحدِّث تلقائياً المتبقي على الجلسة وعمولة الطبيب لذلك
    // اليوم بالذات، ثم يُعاد تحميل لوحة الفاينانس بالكامل حتى تنعكس كل الأرقام المتأثرة فوراً
    private void EditIncomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IncomeRowViewModel income }) return;

        var editWindow = new EditIncomeWindow(income.PatientFullName, income.Amount) { Owner = this };
        if (editWindow.ShowDialog() != true) return;

        try
        {
            _paymentRepo.UpdatePaymentAmount(income.PaymentID, editWindow.NewAmount);
            _ = LoadDashboardAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Fin_LoadErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // حذف دفعة نهائياً - عملية لا رجعة فيها، بتأكيد صريح. نفس إعادة الحساب التلقائي أعلاه بعد الحذف.
    private void DeleteIncomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IncomeRowViewModel income }) return;

        var confirm = MessageBox.Show(
            LocalizationManager.T("Fin_DeleteIncomeConfirmFormat", income.PatientFullName, income.AmountText),
            LocalizationManager.T("Fin_DeleteIncomeTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _paymentRepo.DeletePayment(income.PaymentID);
            _ = LoadDashboardAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Fin_LoadErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // --- توابع المصاريف والصافي والمخطط البياني ---
    private void LoadExpensesSummary()
    {
        var summary = _financialRepo.GetExpenseSummary();
        TodayExpenseText.Text = MoneyFormatter.Format(summary.TodayExpense);
        MonthExpenseText.Text = MoneyFormatter.Format(summary.MonthExpense);
        YearExpenseText.Text = MoneyFormatter.Format(summary.YearExpense);

        var expenses = _financialRepo.GetRecentExpenses();
        ExpensesDataGrid.ItemsSource = expenses;
    }

    // حذف مصروف (بعد تأكيد المستخدم) - يعمل مع المصاريف اليدوية وكذلك عمولات الأطباء التلقائية،
    // لتصحيح أي خطأ (مبلغ خاطئ، طبيب خاطئ، إدخال مكرر...) دون الحاجة للوصول لقاعدة البيانات مباشرة
    private void EditExpenseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ExpenseRow expense) return;

        var editWin = new AddExpenseWindow(expense) { Owner = this };
        if (editWin.ShowDialog() == true) LoadExpensesSummary();
    }

    private void DeleteExpenseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ExpenseRow expense) return;

        var message = expense.IsAutoGenerated
            ? LocalizationManager.T("Expense_DeleteConfirmAutoMessageFormat", expense.Description, MoneyFormatter.Format(expense.Amount))
            : LocalizationManager.T("Expense_DeleteConfirmMessageFormat", expense.Description, MoneyFormatter.Format(expense.Amount));

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
        // بطاقات اليوم/الشهر/السنة الجديدة (حلّت محل بطاقة "Period Overview") - نفس مصدري البيانات
        // الموجودين أصلاً (ملخص الإيرادات وملخص المصاريف)، بلا حاجة لاستعلام جديد بقاعدة البيانات
        var revenue = _financialRepo.GetRevenueSummary();
        var expense = _financialRepo.GetExpenseSummary();
        var commission = _financialRepo.GetCommissionSummary();

        var todayProfit = revenue.TodayRevenue - expense.TodayExpense;
        var monthProfit = revenue.MonthRevenue - expense.MonthExpense;
        var yearProfit = revenue.YearRevenue - expense.YearExpense;

        SetNetProfitCard(TodayNetProfitText, todayProfit);
        SetNetProfitCard(MonthNetProfitText, monthProfit);
        SetNetProfitCard(YearNetProfitText, yearProfit);

        SetMarginText(TodayMarginText, todayProfit, revenue.TodayRevenue);
        SetMarginText(MonthMarginText, monthProfit, revenue.MonthRevenue);
        SetMarginText(YearMarginText, yearProfit, revenue.YearRevenue);

        SetTrend(TodayNetProfitTrendText, todayProfit, revenue.YesterdayRevenue - expense.YesterdayExpense, "Fin_CompareYesterday");
        SetTrend(MonthNetProfitTrendText, monthProfit, revenue.LastMonthRevenue - expense.LastMonthExpense, "Fin_CompareLastMonth");
        SetTrend(YearNetProfitTrendText, yearProfit, revenue.LastYearRevenue - expense.LastYearExpense, "Fin_CompareLastYear");

        // بند عمولات الأطباء مستقلاً - أرقام هذا الشهر تحديداً (الأكثر تمثيلاً لنمط العمل المعتاد)
        if (expense.MonthExpense > 0 && commission.MonthCommission > 0)
        {
            var sharePercent = Math.Round(commission.MonthCommission / expense.MonthExpense * 100m, 1);
            CommissionBreakdownText.Text = LocalizationManager.T("Fin_CommissionBreakdownFormat",
                MoneyFormatter.Format(commission.MonthCommission), sharePercent);
        }
        else
        {
            CommissionBreakdownText.Text = LocalizationManager.T("Fin_CommissionBreakdownNone");
        }

        // تحميل بيانات المخطط البياني للفترة الافتراضية (Weekly)
        LoadChartData("Weekly");
    }

    // هامش الربح % = صافي الربح / الإيراد الإجمالي لنفس الفترة - يُترك فارغاً إن كان الإيراد صفراً
    // (نسبة مئوية بلا إيراد أصلاً غير ذات معنى، وليست "صفر بالمئة")
    private static void SetMarginText(TextBlock target, decimal profit, decimal revenue)
    {
        if (revenue <= 0)
        {
            target.Text = string.Empty;
            return;
        }

        var marginPercent = Math.Round(profit / revenue * 100m, 1);
        target.Text = LocalizationManager.T("Fin_MarginFormat", marginPercent);
    }

    private static void SetNetProfitCard(TextBlock textBlock, decimal amount)
    {
        textBlock.Text = MoneyFormatter.Format(amount);
        textBlock.Foreground = amount >= 0 ? new SolidColorBrush(Color.FromRgb(46, 125, 50)) : new SolidColorBrush(Colors.Red);
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
        // بطاقات اليوم/الشهر/السنة أعلى التبويب ثابتة ولا تتأثر بفلتر (من-إلى) - هذا الفلتر يتحكم
        // بالرسم البياني تحته فقط (راجع LoadNetProfit لمصدر أرقام البطاقات الثابتة)
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
        csv.AppendLine($"Today's Net Profit,{TodayNetProfitText.Text}");
        csv.AppendLine($"This Month's Net Profit,{MonthNetProfitText.Text}");
        csv.AppendLine($"This Year's Net Profit,{YearNetProfitText.Text}");

        File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        MessageBox.Show(LocalizationManager.T("Fin_NetProfitExportedMsg"), LocalizationManager.T("Fin_ExportDoneTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }
}