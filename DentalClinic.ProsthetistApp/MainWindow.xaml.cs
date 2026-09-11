using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.Features;
using DentalClinic.UI.Localization;

namespace DentalClinic.ProsthetistApp;

public partial class MainWindow : Window
{
    private readonly UserAccount _currentUser;
    private readonly DatabaseHelper _db;
    private readonly ProstheticCaseRepository _caseRepo;
    private readonly ProstheticPaymentRepository _paymentRepo;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DentalClinic.UI.Connectivity.ConnectionMonitor _connectionMonitor;
    private DentalClinic.UI.ConnectionLostWindow? _connectionLostWindow;

    // Patch 16 - Fix #3/#16: حارس ذرّي يمنع تراكب دورتي تحديث تلقائي (Tick بينما دورة سابقة لم
    // تنتهِ بعد، أو تراكب مع التحميل الابتدائي عند Loaded) - 0 = خامل، 1 = نشط حالياً. لا علاقة له
    // بالتحديثات اليدوية (نقر زر/فلترة/بحث) التي تبقى تماماً كما كانت (LoadCases()/LoadDashboard()
    // المتزامنتين الأصليتين).
    private int _autoRefreshInProgress;

    // "آخر 50 حالة" افتراضياً (راجع البند 11 في المتطلبات) - يزداد بزر "تحميل المزيد" بدل صفحات منفصلة،
    // أبسط للمستخدم من Pager كامل مع الحفاظ على نفس فكرة "لا تُحذف الحالات القديمة، فقط تُحمَّل تدريجياً"
    private int _pageSize = 50;

    // الطبيب يملك صلاحية كاملة دائماً على نظام الترميم (البند 41 في المتطلبات) - لا حاجة لأي
    // صلاحية Prosthetics.* دقيقة مُمنوحة له صراحة، ولا فائدة من "حالاتي فقط" بالنسبة له أصلاً
    // (الطبيب ليس مُسنَداً إليه أي حالة كمرمم - AssignedProsthetistUserID لا يشير له أبداً في
    // سير العمل الطبيعي، فتصفية "حالاتي" له كانت ستُظهر قائمة فارغة دائماً لولا هذا الاستثناء).
    // ⚠️ IsMainDoctor مُشترَطة هنا أيضاً كطبقة حماية مستقلة - رغم أن LoginWindow يرفض أي طبيب غير
    // رئيسي قبل الوصول لهذه النافذة أصلاً، لا نعتمد على نقطة تحقق واحدة فقط (دفاع متعدد الطبقات).
    private bool IsDoctorAccount => _currentUser.Role == UserRole.Doctor && _currentUser.IsMainDoctor;

    // null = "الكل"؛ قيمة = مرمم واحد محدَّد فقط (نفسه دائماً هنا، ما لم يكن طبيباً)
    private int? EffectiveProsthetistId =>
        IsDoctorAccount || (CanViewAllCases && ViewAllCasesToggle.IsChecked == true) ? null : _currentUser.UserID;

    private bool CanViewAllCases => IsDoctorAccount || _currentUser.HasProstheticPermission(ProstheticPermissionKeys.ViewAllCases);
    private bool CanViewFinance => IsDoctorAccount || _currentUser.HasProstheticPermission(ProstheticPermissionKeys.ViewFinance);
    // Patch 8: نفس نمط CanViewAllCases/CanViewFinance أعلاه بالضبط - كانت هاتان الصلاحيتان (من
    // الـ21 صلاحية المخطَّطة أصلاً) موجودتين في القاعدة بلا أي أثر فعلي على أي شاشة حتى الآن.
    // EditStage تُخوِّل ضمنياً رؤيتها أيضاً (لا معنى لتعديل حقل مخفي - راجع نفس المنطق في
    // ProstheticCaseEditWindow._canViewStage).
    private bool CanViewPatientInfo => IsDoctorAccount || _currentUser.HasProstheticPermission(ProstheticPermissionKeys.ViewPatient);
    private bool CanViewStage => IsDoctorAccount
        || _currentUser.HasProstheticPermission(ProstheticPermissionKeys.ViewStage)
        || _currentUser.HasProstheticPermission(ProstheticPermissionKeys.EditStage);

    public ObservableCollection<ProstheticCaseRowViewModel> CaseRows { get; } = new();

    // DataGridColumn is not a FrameworkElement and should not be given x:Name in XAML.
    // Resolve columns through the DataGrid collection instead; this keeps XAML code-behind
    // generation stable and avoids cascading CS0103/InitializeComponent errors after UI edits.
    private void SetCaseColumnVisibility(int columnIndex, bool visible)
    {
        if (columnIndex < 0 || columnIndex >= CasesGrid.Columns.Count) return;
        CasesGrid.Columns[columnIndex].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public MainWindow(UserAccount currentUser)
    {
        _currentUser = currentUser;
        InitializeComponent();
        Title = LocalizationManager.T("App_ProsthetistTitleWithNameFormat", _currentUser.FullName);

        CasesGrid.ItemsSource = CaseRows;

        // "عرض كل الحالات" لا معنى له للطبيب (يرى الكل دائماً بلا حاجة لتبديل) ولا لمن لا يملك
        // الصلاحية أصلاً (سيبقى دائماً "حالاتي فقط" له - لا فائدة من إظهاره معطَّلاً)
        ViewAllCasesToggle.Visibility = CanViewAllCases && !IsDoctorAccount ? Visibility.Visible : Visibility.Collapsed;
        // عمود "المرمم المسؤول" مفيد فقط عند إمكانية رؤية أكثر من مرمم؛ في نطاق "حالاتي فقط" هو دائماً نفس الاسم
        SetCaseColumnVisibility(3, CanViewAllCases); // Prosthetist
        // Patch 8: تفعيل فعلي لـProsthetics.ViewPatient وProsthetics.ViewStage على مستوى قائمة
        // الانتظار نفسها - قبل هذا الباتش كان أي مرمم يرى اسم/هاتف المريض والمرحلة دائماً بصرف
        // النظر عن صلاحياته الممنوحة فعلياً، لأن هاتين الصلاحيتين لم تُطبَّقا في أي شاشة إطلاقاً
        SetCaseColumnVisibility(1, CanViewPatientInfo); // Patient name
        SetCaseColumnVisibility(2, CanViewPatientInfo); // Patient phone
        SetCaseColumnVisibility(5, CanViewStage);       // Stage
        // Patch 9: يعكس فعلياً أن البحث بالاسم/الهاتف مُعطَّل الآن على مستوى الاستعلام نفسه لمن
        // لا يملك ViewPatient (راجع GetCases(allowPatientSearch:) أدناه) - وليس فقط إخفاء العمودين
        SearchBox.ToolTip = LocalizationManager.T(CanViewPatientInfo
            ? "ProsthMain_SearchPlaceholder"
            : "ProsthMain_SearchPlaceholderCaseNumberOnly");
        FinanceCardsPanel.Visibility = CanViewFinance ? Visibility.Visible : Visibility.Collapsed;
        // Patch 11.1: نفس صلاحية لوحة الفاينانس بالضبط - إحصائيات الترميم تحتوي أرقاماً مالية
        // (قيمة الحالات/المدفوعات/المتبقي)، فمن المنطقي أن تخضع لنفس القيد بلا صلاحية جديدة
        StatisticsButton.Visibility = CanViewFinance ? Visibility.Visible : Visibility.Collapsed;
        // Patch 12: نفس صلاحية ViewFinance أيضاً - راجع تعليق الزر في XAML
        ExpensesButton.Visibility = CanViewFinance ? Visibility.Visible : Visibility.Collapsed;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        _db = new DatabaseHelper(connectionString);
        _caseRepo = new ProstheticCaseRepository(_db);
        _paymentRepo = new ProstheticPaymentRepository(_db);

        // مراقبة الاتصال بالخادم في الخلفية (لا تُجمِّد الواجهة أبداً - راجع ConnectionMonitor) +
        // مؤشر الحالة في الشريط العلوي + نافذة إشعار تلقائية عند الانقطاع
        _connectionMonitor = new DentalClinic.UI.Connectivity.ConnectionMonitor(_db);
        _connectionMonitor.StatusChanged += OnConnectionStatusChanged;
        ConnectionStatusIndicatorControl.RetryRequested += (s, e) => _connectionMonitor.CheckNow();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        // Patch 16 - Fix #3: الـTick لم يعد يُنفِّذ أي استدعاء Repository/ADO.NET مباشرة على UI
        // Thread - يُفوِّض العمل بالكامل لـRunAutomaticRefreshAsync (Task.Run في الخلفية ثم تحديث
        // الواجهة بعد الاكتمال فقط).
        _refreshTimer.Tick += (s, e) =>
        {
            // لا فائدة من محاولة استعلام جديد ونحن نعلم أصلاً أن الاتصال منقطع - كانت هذه المحاولات
            // المتكررة كل 6 ثوانٍ هي بالضبط سبب التجمُّد المتكرر (كل محاولة تنتظر كامل مهلة الاتصال
            // قبل أن تفشل). ConnectionMonitor يعمل بمعزل عن هذا المؤقّت ويكتشف عودة الاتصال بنفسه.
            if (!_connectionMonitor.IsConnected) return;
            RunAutomaticRefreshAsync();
        };

        Loaded += (s, e) =>
        {
            // التحميل الابتدائي أيضاً بات يمر عبر نفس المسار الآمن في الخلفية (البند 8) - النافذة
            // تبقى مستجيبة فوراً حتى لو كان السيرفر غير متاح أصلاً عند فتح التطبيق.
            _refreshTimer.Start();
            RunAutomaticRefreshAsync();
        };
        Closed += (s, e) =>
        {
            _refreshTimer.Stop();
            _connectionMonitor.Dispose();
        };
    }

    // Patch 16: نقطة الدخول الوحيدة للتحديث التلقائي/الدوري للوحة الفاينانس وقائمة الحالات معاً
    // (Tick + التحميل الابتدائي عند Loaded) - async void مقصودة هنا فقط لأنها معالج حدث فعلياً (نمط
    // قياسي ومقبول في WPF)؛ كل استثناء داخلها مُعالَج بالكامل ضمن LoadDashboardAsync/LoadCasesAsync
    // فلا يتسرّب أي استثناء غير مُعالَج للخارج. الحارس الذرّي (_autoRefreshInProgress) يمنع اجتماع
    // أكثر من تحديث تلقائي واحد نشط بنفس اللحظة.
    private async void RunAutomaticRefreshAsync()
    {
        if (Interlocked.CompareExchange(ref _autoRefreshInProgress, 1, 0) != 0) return;
        try
        {
            await LoadDashboardAsync();
            await LoadCasesAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _autoRefreshInProgress, 0);
        }
    }

    // يُستدعى دائماً على UI Thread (ConnectionMonitor يضمن ذلك) - آمن تحديث عناصر الواجهة مباشرة هنا
    private void OnConnectionStatusChanged(bool connected)
    {
        ConnectionStatusIndicatorControl.SetConnected(connected);

        if (!connected)
        {
            if (_connectionLostWindow == null)
            {
                _connectionLostWindow = new DentalClinic.UI.ConnectionLostWindow { Owner = this };
                _connectionLostWindow.Show();
            }
        }
        else
        {
            _connectionLostWindow?.Close();
            _connectionLostWindow = null;
        }
    }

    // Patch 16: حاوية بيانات بسيطة لنقل نتيجة GetFinanceSummary من خيط الخلفية إلى UI Thread - بلا
    // أي مرجع لعناصر واجهة WPF.
    private sealed class DashboardLoadResult
    {
        public ProstheticFinanceSummary Summary { get; init; } = null!;
    }

    // القراءة الفعلية من القاعدة هنا فقط (بلا أي لمسة لعناصر واجهة WPF بخلاف قراءة EffectiveProsthetistId
    // نفسها التي يجب أخذها على UI Thread قبل الاستدعاء - راجع LoadDashboardAsync)، لتُشغَّل بأمان على
    // خيط خلفية من المسار التلقائي، بينما يبقى المسار اليدوي (LoadDashboard) يستدعيها مباشرة على UI Thread.
    private DashboardLoadResult LoadDashboardCore(int? effectiveProsthetistId)
    {
        var summary = _paymentRepo.GetFinanceSummary(effectiveProsthetistId);
        return new DashboardLoadResult { Summary = summary };
    }

    private void ApplyDashboardResult(DashboardLoadResult result)
    {
        var summary = result.Summary;
        TodayRevenueText.Text = summary.TodayRevenue.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
        MonthRevenueText.Text = summary.MonthRevenue.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
        YearRevenueText.Text = summary.YearRevenue.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
        OutstandingText.Text = summary.TotalOutstandingBalance.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
    }

    // المسار اليدوي الأصلي - يبقى متزامناً بالحرف كما كان (نقطة النداء اليدوية: بعد تعديل حالة من
    // نافذة التفاصيل، قد يتغيّر السعر فيتأثر الرصيد المستحق الظاهر في البطاقات). لم يتغيّر سلوكه.
    private void LoadDashboard()
    {
        if (!CanViewFinance) return;

        try
        {
            var result = LoadDashboardCore(EffectiveProsthetistId);
            ApplyDashboardResult(result);
        }
        catch
        {
            // فشل مؤقت بالاتصال - البطاقات تبقى بآخر قيمة معروضة، ستُحاول المحاولة القادمة تلقائياً بعد 6 ثوانٍ
        }
    }

    // Patch 16 - Fix #3: المسار التلقائي/الدوري فقط - نفس Core بالضبط لكن عبر Task.Run في الخلفية،
    // ثم تطبيق النتيجة بعد await. عطل اتصال حقيقي يُبلَّغ فوراً لـConnectionMonitor (Fix #4).
    private async Task LoadDashboardAsync()
    {
        if (!CanViewFinance) return;

        try
        {
            var effectiveProsthetistId = EffectiveProsthetistId; // قراءة تعتمد على عناصر واجهة - يجب أخذها هنا على UI Thread
            var result = await Task.Run(() => LoadDashboardCore(effectiveProsthetistId));
            ApplyDashboardResult(result);
        }
        catch (Exception ex)
        {
            _connectionMonitor.ReportConnectionFailure(ex);
        }
    }

    // Patch 16: حاوية بيانات بسيطة لنقل نتيجة GetCases من خيط الخلفية إلى UI Thread.
    private sealed class CasesLoadResult
    {
        public List<ProstheticCase> Items { get; init; } = new();
        public int TotalCount { get; init; }
    }

    // القراءة الفعلية من القاعدة هنا فقط (بلا أي لمسة لعناصر واجهة WPF) - المعطيات (فلتر
    // الحالة/نص البحث/معرِّف المرمم) تُقرأ من عناصر الواجهة قبل الاستدعاء دائماً على UI Thread
    // (راجع LoadCases وLoadCasesAsync)، فتصل هنا كقيم عادية آمنة للاستخدام من أي خيط.
    private CasesLoadResult LoadCasesCore(int? effectiveProsthetistId, string? statusFilter, string? searchText, int pageSize, bool allowPatientSearch)
    {
        var (items, totalCount) = _caseRepo.GetCases(effectiveProsthetistId, statusFilter, searchText, pageNumber: 1, pageSize: pageSize,
            allowPatientSearch: allowPatientSearch);
        return new CasesLoadResult { Items = items, TotalCount = totalCount };
    }

    private void ApplyCasesResult(CasesLoadResult result)
    {
        // نحافظ على تحديد الصف الحالي (بمعرِّف الحالة) عبر التحديثات الدورية، بدل أن يقفز التمرير/التحديد
        // في كل مرة (كل 6 ثوانٍ) - القائمة نفسها تُعاد بناؤها بالكامل هنا لأن ProstheticCaseRowViewModel
        // (مشتركة مع PatientFileWindow) لا تدعم INotifyPropertyChanged للتحديث الجزئي حالياً.
        var selectedCaseId = (CasesGrid.SelectedItem as ProstheticCaseRowViewModel)?.Case.CaseID;

        CaseRows.Clear();
        foreach (var c in result.Items)
        {
            CaseRows.Add(new ProstheticCaseRowViewModel(c));
        }

        if (selectedCaseId.HasValue)
        {
            var toReselect = CaseRows.FirstOrDefault(r => r.Case.CaseID == selectedCaseId.Value);
            if (toReselect != null) CasesGrid.SelectedItem = toReselect;
        }

        LoadMoreButton.Visibility = result.TotalCount > CaseRows.Count ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = LocalizationManager.T("ProsthMain_ShowingCountFormat", CaseRows.Count, result.TotalCount);
    }

    // المسار اليدوي الأصلي - يبقى متزامناً بالحرف كما كان (نفس نقاط النداء الموجودة: فلترة، بحث،
    // "تحميل المزيد"، أزرار، إغلاق نوافذ فرعية). لم يتغيّر سلوكه إطلاقاً في هذا الباتش.
    private void LoadCases()
    {
        try
        {
            var statusTag = (StatusFilterBox.SelectedItem as ComboBoxItem)?.Tag as string;
            var statusFilter = string.IsNullOrEmpty(statusTag) ? null : statusTag;
            var searchText = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim();

            var result = LoadCasesCore(EffectiveProsthetistId, statusFilter, searchText, _pageSize, CanViewPatientInfo);
            ApplyCasesResult(result);
        }
        catch (Exception ex)
        {
            CountText.Text = LocalizationManager.T("ProsthCase_LoadErrorFormat", ex.Message);
        }
    }

    // Patch 16 - Fix #3: المسار التلقائي/الدوري فقط (RunAutomaticRefreshAsync) - نفس LoadCasesCore
    // بالضبط لكن مُنفَّذة على خيط خلفية عبر Task.Run، ثم تطبيق النتيجة على الواجهة بعد await. عطل
    // اتصال حقيقي يُبلَّغ فوراً لـConnectionMonitor.
    private async Task LoadCasesAsync()
    {
        try
        {
            // كل القراءات المعتمدة على عناصر الواجهة (الفلتر/البحث/معرِّف المرمم الفعّال) تُؤخَذ هنا
            // على UI Thread قبل الانتقال للخلفية - آمنة تماماً، لا لمسة لأي عنصر واجهة بعد ذلك حتى await
            var statusTag = (StatusFilterBox.SelectedItem as ComboBoxItem)?.Tag as string;
            var statusFilter = string.IsNullOrEmpty(statusTag) ? null : statusTag;
            var searchText = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim();
            var effectiveProsthetistId = EffectiveProsthetistId;
            var pageSize = _pageSize;
            var allowPatientSearch = CanViewPatientInfo;

            var result = await Task.Run(() => LoadCasesCore(effectiveProsthetistId, statusFilter, searchText, pageSize, allowPatientSearch));
            ApplyCasesResult(result);
        }
        catch (Exception ex)
        {
            _connectionMonitor.ReportConnectionFailure(ex);
            CountText.Text = LocalizationManager.T("ProsthCase_LoadErrorFormat", ex.Message);
        }
    }

    private void ViewAllCasesToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_caseRepo == null) return;
        LoadCases();
    }

    private void StatusFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_caseRepo == null) return; // يُستدعى مرة أولى أثناء InitializeComponent قبل تجهيز بقية الحقول
        LoadCases();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) LoadCases();
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e) => LoadCases();

    private void LoadMoreButton_Click(object sender, RoutedEventArgs e)
    {
        _pageSize += 50;
        LoadCases();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadCases();

    private void StatisticsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new ProstheticStatisticsWindow(_currentUser) { Owner = this };
        window.ShowDialog();
    }

    private void ExpensesButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new ProstheticExpensesWindow(_currentUser) { Owner = this };
        window.ShowDialog();
    }

    // فتح تفاصيل حالة كاملة - نفس النافذة المشتركة المستخدَمة من تطبيق الطبيب، لكن هنا تُطبَّق قيود
    // الصلاحيات الدقيقة داخلها (راجع ProstheticCaseEditWindow: EditCase/TransferCase/EditStage) لأن
    // فاتح النافذة هنا مرمم وليس طبيباً بالضرورة
    private void CasesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CasesGrid.SelectedItem is not ProstheticCaseRowViewModel row) return;

        var window = new ProstheticCaseEditWindow(row.Case, _currentUser) { Owner = this };
        if (window.ShowDialog() == true)
        {
            LoadCases();
            LoadDashboard(); // قد يتغيّر السعر ضمن التعديل، فيتأثر الرصيد المستحق الظاهر في البطاقات
        }
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            LocalizationManager.T("Main_ConfirmLogoutMessage"),
            LocalizationManager.T("Main_ConfirmLogoutTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        _refreshTimer.Stop();

        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Hide();

        var loginWindow = new LoginWindow();
        var loginResult = loginWindow.ShowDialog();

        if (loginResult == true && loginWindow.LoggedInUser != null)
        {
            var newMainWindow = new MainWindow(loginWindow.LoggedInUser);
            Application.Current.MainWindow = newMainWindow;
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            newMainWindow.Show();
            Close();
        }
        else
        {
            Application.Current.Shutdown();
        }
    }
}
