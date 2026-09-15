using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

// نافذة "مصاريف الترميم" (Patch 12) - نظير تبويب المصاريف في FinancialDashboardWindow (فاينانس
// العيادة العام في تطبيق الطبيب)، لكن هنا على جدول ProstheticExpenses المستقل تماماً (راجع تعليق
// ProstheticExpenseRepository حول الفصل المالي). تعرض قائمة/إجمالي مصاريف المرمم + الدخل الصافي
// لنفس الفترة/الفلتر المُختار، وتفتح AddProstheticExpenseWindow لإضافة مصروف جديد.
public partial class ProstheticExpensesWindow : Window
{
    // نفس خيار العرض المستقل المستخدَم في ProstheticStatisticsWindow - يضمن أن اسم المرمم فقط
    // هو الذي يظهر للمستخدم، بمعزل عن أي قالب افتراضي لـUserAccount
    private sealed class ProsthetistFilterOption
    {
        public int UserID { get; init; }
        public string FullName { get; init; } = string.Empty;
        public bool IsAll { get; init; }
    }

    private readonly UserAccount _currentUser;
    private readonly DatabaseHelper _db;
    private readonly ProstheticExpenseRepository _expenseRepo;
    private readonly UserRepository _userRepo;

    // نفس معايير IsDoctorAccount/CanViewAllCases/CanViewFinance المعتمدة في ProstheticStatisticsWindow
    // بالحرف - لا صلاحية جديدة لمجرد رؤية القائمة والإجمالي/الدخل الصافي
    private bool IsDoctorAccount => _currentUser.Role == UserRole.Doctor && _currentUser.IsMainDoctor;
    private bool CanViewAllCases => IsDoctorAccount || _currentUser.HasProstheticPermission(ProstheticPermissionKeys.ViewAllCases);
    private bool CanAddExpense => IsDoctorAccount || _currentUser.HasProstheticPermission(ProstheticPermissionKeys.AddExpense);
    private bool CanEditExpense => IsDoctorAccount || _currentUser.HasProstheticPermission(ProstheticPermissionKeys.EditExpense);
    private bool CanDeleteExpense => IsDoctorAccount || _currentUser.HasProstheticPermission(ProstheticPermissionKeys.DeleteExpense);

    public ObservableCollection<ProstheticExpenseRow> ExpenseRows { get; } = new();

    public ProstheticExpensesWindow(UserAccount currentUser)
    {
        _currentUser = currentUser;
        InitializeComponent();

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        _db = new DatabaseHelper(connectionString);
        _expenseRepo = new ProstheticExpenseRepository(_db);
        _userRepo = new UserRepository(_db);

        ExpensesGrid.ItemsSource = ExpenseRows;

        AddExpenseButton.Visibility = CanAddExpense ? Visibility.Visible : Visibility.Collapsed;
        EditColumn.Visibility = CanEditExpense ? Visibility.Visible : Visibility.Collapsed;
        DeleteColumn.Visibility = CanDeleteExpense ? Visibility.Visible : Visibility.Collapsed;

        LoadProsthetistFilter();

        // نفس الفترة الافتراضية المعتمدة في ProstheticStatisticsWindow: هذا الشهر
        Loaded += (s, e) => ApplyMonth();
    }

    // بلا ViewAllCases: القائمة تُقفَل على حساب المرمم نفسه فقط - لا خيار "كل المرممين" يظهر إطلاقاً
    private void LoadProsthetistFilter()
    {
        var items = new List<ProsthetistFilterOption>();

        if (CanViewAllCases)
        {
            items.Add(new ProsthetistFilterOption
            {
                UserID = 0,
                FullName = LocalizationManager.T("ProsthStats_AllProsthetists"),
                IsAll = true
            });

            items.AddRange(_userRepo.GetAllProsthetists(activeOnly: false)
                .Select(p => new ProsthetistFilterOption
                {
                    UserID = p.UserID,
                    FullName = string.IsNullOrWhiteSpace(p.FullName) ? p.Username : p.FullName
                }));
        }
        else
        {
            items.Add(new ProsthetistFilterOption
            {
                UserID = _currentUser.UserID,
                FullName = string.IsNullOrWhiteSpace(_currentUser.FullName) ? _currentUser.Username : _currentUser.FullName
            });
        }

        ProsthetistFilterCombo.ItemsSource = items;
        ProsthetistFilterCombo.SelectedIndex = 0;
        ProsthetistFilterCombo.IsEnabled = CanViewAllCases;
    }

    private int? SelectedProsthetistId
    {
        get
        {
            if (!CanViewAllCases) return _currentUser.UserID;
            var selected = ProsthetistFilterCombo.SelectedItem as ProsthetistFilterOption;
            return selected == null || selected.IsAll ? (int?)null : selected.UserID;
        }
    }

    private void ProsthetistFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) LoadData();
    }

    private void TodayButton_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        SetRange(today, today);
        LoadData();
    }

    private void WeekButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyWeek();
        LoadData();
    }

    private void MonthButton_Click(object sender, RoutedEventArgs e) => ApplyMonth();

    private void ApplyWeek()
    {
        var today = DateTime.Today;
        var daysSinceSaturday = ((int)today.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7;
        var startOfWeek = today.AddDays(-daysSinceSaturday);
        SetRange(startOfWeek, today);
    }

    private void ApplyMonth()
    {
        var today = DateTime.Today;
        var startOfMonth = new DateTime(today.Year, today.Month, 1);
        SetRange(startOfMonth, today);
        LoadData();
    }

    private void SetRange(DateTime from, DateTime to)
    {
        FromDatePicker.SelectedDate = from;
        ToDatePicker.SelectedDate = to;
    }

    private void ApplyFilterButton_Click(object sender, RoutedEventArgs e) => LoadData();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void AddExpenseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanAddExpense) return;

        var window = new AddProstheticExpenseWindow(_currentUser, _db, SelectedProsthetistId) { Owner = this };
        if (window.ShowDialog() == true) LoadData();
    }

    private void EditExpenseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditExpense) return;
        if (sender is not Button btn || btn.Tag is not ProstheticExpenseRow expense) return;

        var window = new AddProstheticExpenseWindow(expense, _currentUser, _db) { Owner = this };
        if (window.ShowDialog() == true) LoadData();
    }

    private void DeleteExpenseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanDeleteExpense) return;
        if (sender is not Button btn || btn.Tag is not ProstheticExpenseRow expense) return;

        var message = LocalizationManager.T("Expense_DeleteConfirmMessageFormat", expense.Description, MoneyFormatter.Format(expense.Amount));
        var confirm = MessageBox.Show(message, LocalizationManager.T("Expense_DeleteConfirmTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _expenseRepo.DeleteExpense(expense.ExpenseID, _currentUser);
            LoadData();
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(LocalizationManager.T("ProsthExpense_NoPermission"), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Expense_DeleteErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadData()
    {
        ErrorText.Text = "";
        try
        {
            var fromDate = (FromDatePicker.SelectedDate ?? DateTime.Today).Date;
            var toDateExclusive = (ToDatePicker.SelectedDate ?? DateTime.Today).Date.AddDays(1);

            if (toDateExclusive <= fromDate)
            {
                ErrorText.Text = LocalizationManager.T("ProsthStats_InvalidRange");
                return;
            }

            var prosthetistId = SelectedProsthetistId;

            PeriodText.Text = LocalizationManager.T("ProsthStats_PeriodFormat", fromDate.ToString("yyyy-MM-dd"), toDateExclusive.AddDays(-1).ToString("yyyy-MM-dd"));

            // المدفوعات الواردة فعلياً ضمن الفترة نفسها (وليس رصيد لحظي) + المصاريف + الدخل الصافي:
            // نستدعي ProstheticStatisticsRepository.GetSummary بدل تكرار نفس الاستعلامات هنا
            var statsRepo = new ProstheticStatisticsRepository(_db);
            var summary = statsRepo.GetSummary(prosthetistId, fromDate, toDateExclusive);

            TotalPaymentsText.Text = MoneyFormatter.Format(summary.TotalPayments);
            TotalExpensesText.Text = MoneyFormatter.Format(summary.TotalExpenses);
            NetIncomeText.Text = MoneyFormatter.Format(summary.NetIncome);
            NetIncomeText.Foreground = new SolidColorBrush(summary.NetIncome >= 0
                ? Color.FromRgb(0x27, 0xAE, 0x60)
                : Color.FromRgb(0xE7, 0x4C, 0x3C));

            ExpenseRows.Clear();
            foreach (var row in _expenseRepo.GetExpenses(prosthetistId, fromDate, toDateExclusive))
                ExpenseRows.Add(row);

            EmptyText.Visibility = ExpenseRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("ProsthCase_LoadErrorFormat", ex.Message);
        }
    }
}
