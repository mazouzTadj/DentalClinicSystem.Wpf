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

// نافذة "إحصائيات الترميم" (Patch 11.1 + 11.2) - قراءة فقط بالكامل، لا يوجد أي زر تعديل هنا إطلاقاً.
// ⚠️ كل الأرقام هنا من ProstheticStatisticsRepository حصراً (ProstheticCases/ProstheticSessions/
// ProstheticPayments/ProstheticStages/ProstheticCaseHistory للمشتقات الزمنية) - لا صلة بفاينانس
// العيادة العام أو FinancialRepository.
public partial class ProstheticStatisticsWindow : Window
{
    // خيار عرض مستقل للفلتر حتى لا يعتمد عرض ComboBox على ToString() أو على
    // القالب الداخلي لـ UserAccount. هذا يضمن أن اسم المرمم فقط هو الذي يظهر للمستخدم.
    private sealed class ProsthetistFilterOption
    {
        public int UserID { get; init; }
        public string FullName { get; init; } = string.Empty;
        public bool IsAll { get; init; }
    }

    // تحت هذا العدد من الأيام لا تُعتبر الحالة "متأخرة" بصرياً في تبويب "الحالات المتأخرة" - مجرَّد
    // خط فاصل بصري (تلوين الصف)، وليس فلتر إخفاء: كل الحالات المفتوحة تظهر دائماً مرتَّبة من الأقدم،
    // هذا الرقم فقط يُبرز الأسوأ. قيمة قابلة للتعديل بسهولة من هنا فقط.
    private const int OverdueWarningDays = 14;
    private const int OverdueMaxResults = 50;

    private readonly UserAccount _currentUser;
    private readonly ProstheticStatisticsRepository _statsRepo;
    private readonly UserRepository _userRepo;

    // نفس معايير IsDoctorAccount/CanViewAllCases/CanViewFinance المعتمدة في ProsthetistApp.MainWindow
    // بالحرف - لا صلاحية جديدة أُضيفت لهذه الشاشة، فقط إعادة استخدام لما هو موجود بالفعل
    private bool IsDoctorAccount => _currentUser.Role == UserRole.Doctor && _currentUser.IsMainDoctor;
    private bool CanViewAllCases => IsDoctorAccount || _currentUser.HasProstheticPermission(ProstheticPermissionKeys.ViewAllCases);
    private bool CanViewFinance => IsDoctorAccount || _currentUser.HasProstheticPermission(ProstheticPermissionKeys.ViewFinance);
    // Patch 11.2: نفس معيار _canViewStage في ProstheticCaseEditWindow بالحرف - يحكم ظهور تبويب
    // "المراحل" بالكامل (كل محتواه عن المرحلة) + عمود "المرحلة الحالية" في تبويب "الحالات المتأخرة"
    private bool CanViewStage => IsDoctorAccount
        || _currentUser.HasProstheticPermission(ProstheticPermissionKeys.ViewStage)
        || _currentUser.HasProstheticPermission(ProstheticPermissionKeys.EditStage);

    public ObservableCollection<StageBarRowViewModel> StageBarRows { get; } = new();
    public ObservableCollection<ArchDistributionRowViewModel> ArchDistributionRows { get; } = new();
    public ObservableCollection<ComparisonRowViewModel> ComparisonRows { get; } = new();
    public ObservableCollection<OverdueRowViewModel> OverdueRows { get; } = new();

    public ProstheticStatisticsWindow(UserAccount currentUser)
    {
        // Set the user before InitializeComponent so any XAML-created event cannot observe
        // an uninitialized account while the window is being constructed.
        _currentUser = currentUser;
        InitializeComponent();

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _statsRepo = new ProstheticStatisticsRepository(db);
        _userRepo = new UserRepository(db);

        StageBarsList.ItemsSource = StageBarRows;
        ArchDistributionList.ItemsSource = ArchDistributionRows;
        ComparisonGrid.ItemsSource = ComparisonRows;
        OverdueGrid.ItemsSource = OverdueRows;

        // بطاقات القيمة/المدفوعات/المتبقي/متوسط الحالة مالية بالكامل - نفس تقييد لوحة الفاينانس في
        // ProsthetistApp.MainWindow (ViewFinance) وليس صلاحية جديدة
        FinanceCardsPanel.Visibility = CanViewFinance ? Visibility.Visible : Visibility.Collapsed;
        // Patch 12: نفس صلاحية FinanceCardsPanel بالضبط - مصاريف المرمم والدخل الصافي أرقام مالية أيضاً
        NetIncomeCardsPanel.Visibility = CanViewFinance ? Visibility.Visible : Visibility.Collapsed;

        // تبويب "المراحل" بالكامل + عمود المرحلة في "الحالات المتأخرة" مقيَّدان بـViewStage/EditStage
        StagesTabItem.Visibility = CanViewStage ? Visibility.Visible : Visibility.Collapsed;
        // DataGridColumn is intentionally not named in XAML; resolve it from the collection.
        SetOverdueStageColumnVisibility(CanViewStage);

        LoadProsthetistFilter();
        UpdateComparisonTabState();

        // الفترة الافتراضية عند الفتح: هذا الشهر (كما في التصميم المتَّفق عليه)
        Loaded += (s, e) => ApplyMonth();
    }

    private void SetOverdueStageColumnVisibility(bool visible)
    {
        // OverdueGrid columns: CaseNumber(0), Prosthetist(1), Stage(2), CreatedAt(3), DaysOpen(4).
        if (OverdueGrid.Columns.Count > 2)
            OverdueGrid.Columns[2].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // بلا ViewAllCases: القائمة تُقفَل على حساب المرمم نفسه فقط (نفس قفل EffectiveProsthetistId
    // في MainWindow) - لا خيار "كل المرممين" يظهر إطلاقاً، ولا إمكانية اختيار مرمم آخر
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
            if (!CanViewAllCases) return _currentUser.UserID; // مقفل دائماً على نفسه
            var selected = ProsthetistFilterCombo.SelectedItem as ProsthetistFilterOption;
            return selected == null || selected.IsAll ? (int?)null : selected.UserID;
        }
    }

    // تبويب "مقارنة المرممين" له معنى فقط عند اختيار "كل المرممين" - عند اختيار مرمم واحد بالتحديد
    // تختفي المقارنة (يبقى العنصر النائب برسالة توضيحية) ويظهر تحليله في تبويب "ملخص" فقط، تماماً
    // كما طُلب صراحةً
    private void UpdateComparisonTabState()
    {
        var showComparison = SelectedProsthetistId == null; // null = "كل المرممين" مُختار
        ComparisonHintText.Visibility = showComparison ? Visibility.Collapsed : Visibility.Visible;
        ComparisonGridBorder.Visibility = showComparison ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ProsthetistFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateComparisonTabState();
        // نافذة قد لا تكون انتهت من التحميل الأول بعد (SelectedIndex يُضبَط برمجياً في LoadProsthetistFilter)
        if (IsLoaded) LoadStatistics();
    }

    private void TodayButton_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        SetRange(today, today);
        LoadStatistics();
    }

    private void WeekButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyWeek();
        LoadStatistics();
    }

    private void MonthButton_Click(object sender, RoutedEventArgs e) => ApplyMonth();

    private void ApplyWeek()
    {
        // بداية الأسبوع = السبت (بداية أسبوع العمل المعتادة محلياً)؛ اختيار ثابت وواضح، قابل
        // للتعديل بسهولة هنا فقط إن كان التقويم المعتمد في العيادة مختلفاً
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
        LoadStatistics();
    }

    private void SetRange(DateTime from, DateTime to)
    {
        FromDatePicker.SelectedDate = from;
        ToDatePicker.SelectedDate = to;
    }

    private void ApplyFilterButton_Click(object sender, RoutedEventArgs e) => LoadStatistics();

    private void RefreshDataButton_Click(object sender, RoutedEventArgs e) => LoadStatistics();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void LoadStatistics()
    {
        ErrorText.Text = "";
        try
        {
            var fromDate = (FromDatePicker.SelectedDate ?? DateTime.Today).Date;
            // نطاق نصف مفتوح [from, to) - "إلى" المُختارة نفسها يوم كامل تدخل ضمن الفترة
            var toDateExclusive = (ToDatePicker.SelectedDate ?? DateTime.Today).Date.AddDays(1);

            if (toDateExclusive <= fromDate)
            {
                ErrorText.Text = LocalizationManager.T("ProsthStats_InvalidRange");
                return;
            }

            var prosthetistId = SelectedProsthetistId;
            var summary = _statsRepo.GetSummary(prosthetistId, fromDate, toDateExclusive);

            PeriodText.Text = LocalizationManager.T("ProsthStats_PeriodFormat", fromDate.ToString("yyyy-MM-dd"), toDateExclusive.AddDays(-1).ToString("yyyy-MM-dd"));

            RenderSummaryTab(summary);
            RenderStagesTab(summary);
            RenderComparisonTab(fromDate, toDateExclusive);
            RenderOverdueTab(prosthetistId);
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("ProsthCase_LoadErrorFormat", ex.Message);
        }
    }

    private void RenderSummaryTab(ProstheticStatisticsSummary summary)
    {
        TotalCasesText.Text = summary.TotalCases.ToString();
        CompletedCasesText.Text = summary.CompletedCases.ToString();
        ActiveCasesText.Text = summary.ActiveCases.ToString();
        SessionCountText.Text = summary.SessionCount.ToString();

        TotalCaseValueText.Text = MoneyFormatter.Format(summary.TotalCaseValue);
        TotalPaymentsText.Text = MoneyFormatter.Format(summary.TotalPayments);
        TotalOutstandingText.Text = MoneyFormatter.Format(summary.TotalOutstanding);
        AverageCaseValueText.Text = MoneyFormatter.Format(summary.AverageCaseValue);

        // Patch 12: مصاريف المرمم + الدخل الصافي - لون الدخل الصافي يتغيّر حسب الإشارة (أخضر إن
        // موجب، أحمر إن سالب) بعكس بقية البطاقات ذات اللون الثابت، لأن سالب هنا فعلاً إنذار مالي
        TotalExpensesText.Text = MoneyFormatter.Format(summary.TotalExpenses);
        NetIncomeText.Text = MoneyFormatter.Format(summary.NetIncome);
        NetIncomeText.Foreground = new SolidColorBrush(summary.NetIncome >= 0
            ? Color.FromRgb(0x27, 0xAE, 0x60)
            : Color.FromRgb(0xE7, 0x4C, 0x3C));

        // نسبة الإنجاز (Patch 11.2) - مشتقة من CompletedCases/TotalCases الموجودين أصلاً، عرض فقط
        var rate = summary.CompletionRatePercent;
        CompletionRateText.Text = $"{rate:0.#}%";
        CompletionRateBar.Width = Math.Max(0, 300.0 * rate / 100.0);
        AverageCompletionDaysText.Text = summary.AverageCompletionDays.HasValue
            ? LocalizationManager.T("ProsthStats_AvgCompletionDaysFormat", summary.AverageCompletionDays.Value.ToString("0.#"))
            : LocalizationManager.T("ProsthStats_AvgCompletionDaysUnavailable");

        // توزيع Upper/Lower/Both كنسب مئوية (Patch 11.2) - من نفس العدَّادات الخام الموجودة أصلاً في 11.1
        ArchDistributionRows.Clear();
        var archTotal = summary.UpperOnlyCases + summary.LowerOnlyCases + summary.BothArchCases;
        ArchDistributionRows.Add(new ArchDistributionRowViewModel(LocalizationManager.T("ProsthStats_Upper"), summary.UpperOnlyCases, archTotal));
        ArchDistributionRows.Add(new ArchDistributionRowViewModel(LocalizationManager.T("ProsthStats_Lower"), summary.LowerOnlyCases, archTotal));
        ArchDistributionRows.Add(new ArchDistributionRowViewModel(LocalizationManager.T("ProsthStats_Both"), summary.BothArchCases, archTotal));
    }

    private void RenderStagesTab(ProstheticStatisticsSummary summary)
    {
        StageBarRows.Clear();
        if (!CanViewStage) return; // التبويب أصلاً مخفي بالكامل، لا داعي لبناء صفوف لن تُعرَض

        var totalCases = summary.CasesByStage.Sum(s => s.Count);
        var maxCount = summary.CasesByStage.Count == 0 ? 0 : summary.CasesByStage.Max(s => s.Count);
        foreach (var stage in summary.CasesByStage)
        {
            StageBarRows.Add(new StageBarRowViewModel(stage, maxCount, totalCases, isMostCrowded: maxCount > 0 && stage.Count == maxCount));
        }
        StageEmptyText.Visibility = StageBarRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderComparisonTab(DateTime fromDate, DateTime toDateExclusive)
    {
        ComparisonRows.Clear();
        // يُحمَّل فقط عند اختيار "كل المرممين" - استعلام مقارنة كامل لا فائدة منه لمرمم واحد محدَّد
        if (SelectedProsthetistId != null) return;

        var rows = _statsRepo.GetProsthetistComparison(fromDate, toDateExclusive);
        foreach (var row in rows) ComparisonRows.Add(new ComparisonRowViewModel(row));
    }

    private void RenderOverdueTab(int? prosthetistId)
    {
        OverdueRows.Clear();
        var rows = _statsRepo.GetOverdueCases(prosthetistId, OverdueMaxResults);
        foreach (var row in rows) OverdueRows.Add(new OverdueRowViewModel(row, OverdueWarningDays));
    }
}

// ===== ViewModels - Patch 11.1 + 11.2 =====

// صف عرض واحد لمرحلة واحدة في تبويب "المراحل" - يحوِّل العدد إلى عرض شريط + نسبة مئوية + متوسط
// أيام في المرحلة + شارة "الأكثر ازدحاماً" (Patch 11.2 - كل هذا إضافة فوق نفس بيانات 11.1، وليس
// استعلاماً مكرَّراً)
public class StageBarRowViewModel
{
    public string StageName { get; }
    public double BarWidth { get; }
    public string CountAndPercentText { get; }
    public string AvgDaysText { get; }
    public Visibility IsMostCrowdedVisibility { get; }

    private const double MaxBarWidth = 220;

    public StageBarRowViewModel(ProstheticStageCount stage, int maxCount, int totalCases, bool isMostCrowded)
    {
        StageName = stage.StageName;
        BarWidth = maxCount == 0 ? 0 : Math.Max(4, MaxBarWidth * stage.Count / maxCount);

        var percent = totalCases == 0 ? 0 : (double)stage.Count / totalCases * 100.0;
        CountAndPercentText = LocalizationManager.T("ProsthStats_StageCountPercentFormat", stage.Count, percent.ToString("0.#"));

        AvgDaysText = stage.AverageDaysInStage.HasValue
            ? LocalizationManager.T("ProsthStats_AvgDaysInStageFormat", stage.AverageDaysInStage.Value.ToString("0.#"))
            : "";

        IsMostCrowdedVisibility = isMostCrowded ? Visibility.Visible : Visibility.Collapsed;
    }
}

// صف عرض واحد لتوزيع الفك (Upper/Lower/Both) كنسبة مئوية - Patch 11.2
public class ArchDistributionRowViewModel
{
    public string Label { get; }
    public double BarWidth { get; }
    public string SummaryText { get; }

    private const double MaxBarWidth = 260;

    public ArchDistributionRowViewModel(string label, int count, int total)
    {
        Label = label;
        var percent = total == 0 ? 0 : (double)count / total * 100.0;
        BarWidth = total == 0 ? 0 : Math.Max(4, MaxBarWidth * count / total);
        SummaryText = $"{count} ({percent.ToString("0.#")}%)";
    }
}

// صف عرض واحد في جدول "مقارنة المرممين" - Patch 11.2. القيم المالية جاهزة للعرض هنا (وليس Binding
// إلى decimal خام) حتى تُنسَّق بنفس MoneyFormatter المعتمد في كل مكان آخر في المشروع
public class ComparisonRowViewModel
{
    public string ProsthetistName { get; }
    public int TotalCases { get; }
    public int CompletedCases { get; }
    public int ActiveCases { get; }
    public int SessionCount { get; }
    public string TotalCaseValueText { get; }
    public string TotalPaymentsText { get; }
    public string TotalOutstandingText { get; }
    public string TotalExpensesText { get; }
    public string NetIncomeText { get; }

    public ComparisonRowViewModel(ProstheticComparisonRow row)
    {
        ProsthetistName = row.ProsthetistName;
        TotalCases = row.TotalCases;
        CompletedCases = row.CompletedCases;
        ActiveCases = row.ActiveCases;
        SessionCount = row.SessionCount;
        TotalCaseValueText = MoneyFormatter.Format(row.TotalCaseValue);
        TotalPaymentsText = MoneyFormatter.Format(row.TotalPayments);
        TotalOutstandingText = MoneyFormatter.Format(row.TotalOutstanding);
        TotalExpensesText = MoneyFormatter.Format(row.TotalExpenses);
        NetIncomeText = MoneyFormatter.Format(row.NetIncome);
    }
}

// صف عرض واحد في جدول "الحالات المتأخرة" - Patch 11.2. بلا أي بيانات مريض عمداً (راجع تعليق
// ProstheticOverdueCaseRow في طبقة البيانات)
public class OverdueRowViewModel
{
    public int CaseNumber { get; }
    public string ProsthetistName { get; }
    public string StageName { get; }
    public string CreatedAtText { get; }
    public string DaysOpenText { get; }

    public OverdueRowViewModel(ProstheticOverdueCaseRow row, int warningDays)
    {
        CaseNumber = row.CaseNumber;
        ProsthetistName = row.ProsthetistName;
        StageName = row.StageName;
        CreatedAtText = row.CreatedAt.ToString("yyyy-MM-dd");
        // تمييز بصري بسيط بنص إضافي عند تجاوز العتبة، بدل إخفاء الحالات الأقدَّ - الجدول يبقى دائماً كاملاً
        DaysOpenText = row.DaysOpen >= warningDays
            ? LocalizationManager.T("ProsthStats_DaysOpenWarningFormat", row.DaysOpen)
            : row.DaysOpen.ToString();
    }
}
