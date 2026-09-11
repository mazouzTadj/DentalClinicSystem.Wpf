using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.Features; // الشاشات المشتركة (PatientFileWindow, FinancialDashboardWindow, BackupWindow, ...)
using DentalClinic.UI.Localization;

namespace DentalClinic.NurseApp;

public partial class MainWindow : Window
{
    private readonly UserAccount _currentUser;
    private readonly DatabaseHelper _db;
    private readonly QueueRepository _queueRepo;
    private readonly PaymentRepository _paymentRepo;
    private readonly SessionRepository _sessionRepo;
    private readonly PatientRepository _patientRepo;
    private readonly UserRepository _userRepo;
    private readonly NurseDoctorAccessRepository _accessRepo;
    private readonly AppointmentChangeRequestRepository _requestRepo;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DentalClinic.UI.Connectivity.ConnectionMonitor _connectionMonitor;
    private DentalClinic.UI.ConnectionLostWindow? _connectionLostWindow;

    // null = عرض "غير مُسنَدين" (الوضع الافتراضي، متاح دائماً)؛ غير null = عرض قائمة طبيب معيَّن فقط
    private int? _selectedDoctorFilterId;

    // Patch 16 - Fix #3/#16: حارس ذرّي يمنع تراكب دورتي تحديث تلقائي (سواء بين Tick ولاحقه، أو بين
    // التحميل الابتدائي عند Loaded ودورة Tick الأولى إن كانت قاعدة البيانات بطيئة جداً في الرد) -
    // 0 = لا يوجد تحديث تلقائي نشط، 1 = تحديث تلقائي نشط حالياً. لا علاقة له بالتحديثات اليدوية
    // (نقر زر/إغلاق نافذة فرعية) التي تبقى كما كانت تماماً (LoadQueue() المتزامنة الأصلية).
    private int _autoRefreshInProgress;

    private class DoctorQueueOption
    {
        public int? UserID { get; set; } // null = خيار "غير مُسنَدين"
        public string DisplayName { get; set; } = string.Empty;
        public System.Windows.Media.Brush StatusBrush { get; set; } = System.Windows.Media.Brushes.Transparent;
    }

    public ObservableCollection<QueueRowViewModel> QueueRows { get; } = new();

    public MainWindow(UserAccount currentUser)
    {
        _currentUser = currentUser;
        InitializeComponent();
        Title = LocalizationManager.T("App_NurseTitleWithNameFormat", _currentUser.FullName);

        // كل زر يظهر فقط إذا كان المستخدم الحالي يملك الصلاحية المقابلة له - مستقلة تماماً عن الدور (Doctor/Nurse)
        OpenPatientFileButton.Visibility = ToVisibility(_currentUser.HasPermission(UserPermission.OpenPatientFile));
        RegisterPatientButton.Visibility = ToVisibility(_currentUser.HasPermission(UserPermission.RegisterPatients));
        CollectPaymentTopButton.Visibility = ToVisibility(_currentUser.HasPermission(UserPermission.CollectPayments));
        ManageTreatmentsButton.Visibility = ToVisibility(_currentUser.HasPermission(UserPermission.ManageTreatments));
        BackupButton.Visibility = ToVisibility(_currentUser.HasPermission(UserPermission.AccessBackup));
        ManageUsersButton.Visibility = ToVisibility(_currentUser.HasPermission(UserPermission.ManageUsers));
        FinancialDashboardButton.Visibility = ToVisibility(_currentUser.HasPermission(UserPermission.AccessFinance));
        DeletePatientColumn.Visibility = ToVisibility(_currentUser.HasPermission(UserPermission.DeletePatients));
        EditPatientColumn.Visibility = ToVisibility(_currentUser.HasPermission(UserPermission.EditPatients));

        QueueGrid.ItemsSource = QueueRows;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        _db = new DatabaseHelper(connectionString);
        _queueRepo = new QueueRepository(_db);
        _paymentRepo = new PaymentRepository(_db);
        _sessionRepo = new SessionRepository(_db);
        _patientRepo = new PatientRepository(_db);
        _userRepo = new UserRepository(_db);
        _accessRepo = new NurseDoctorAccessRepository(_db);
        _requestRepo = new AppointmentChangeRequestRepository(_db);

        // مراقبة الاتصال بالخادم في الخلفية (لا تُجمِّد الواجهة أبداً - راجع ConnectionMonitor) +
        // مؤشر الحالة في الشريط العلوي + نافذة إشعار تلقائية عند الانقطاع. هذا يعالج مباشرة مشكلة
        // "تجمُّد تطبيق الاستقبال" المُبلَّغ عنها: كانت أعراضها تجمُّد الواجهة بالكامل كل دورة تحديث
        // (المؤقّت أدناه) طوال مهلة الاتصال الافتراضية عند انقطاع الشبكة، بلا أي إشعار للمستخدم.
        _connectionMonitor = new DentalClinic.UI.Connectivity.ConnectionMonitor(_db);
        _connectionMonitor.StatusChanged += OnConnectionStatusChanged;
        ConnectionStatusIndicatorControl.RetryRequested += (s, e) => _connectionMonitor.CheckNow();
        Closed += (s, e) => _connectionMonitor.Dispose();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        // Patch 16 - Fix #3: الـTick نفسه لم يعد يُنفِّذ أي استدعاء Repository/ADO.NET مباشرة على
        // UI Thread - كل ما يفعله الآن هو تفويض العمل لـRunAutomaticRefreshAsync، التي تُشغِّل
        // الجزء الفعلي من الاستعلام عبر Task.Run في الخلفية ثم تُحدِّث عناصر الواجهة بعد اكتمالها
        // فقط (بعد await، على نفس UI Thread تلقائياً بلا أي Dispatcher.Invoke إضافي).
        _refreshTimer.Tick += (s, e) =>
        {
            // لا فائدة من محاولة استعلام جديد كل 4 ثوانٍ ونحن نعلم أصلاً أن الاتصال منقطع - كل
            // محاولة كهذه كانت هي بالضبط سبب التجمُّد المتكرر سابقاً. ConnectionMonitor نفسه (يعمل
            // في الخلفية بمعزل عن هذا المؤقّت) هو من يكتشف عودة الاتصال ويُحدِّث المؤشر؛ حينها تعود
            // هذه الدورة لتعمل تلقائياً كالمعتاد.
            if (!_connectionMonitor.IsConnected) return;
            RunAutomaticRefreshAsync();
        };

        Loaded += (s, e) =>
        {
            // التحميل الابتدائي أيضاً بات يمر عبر نفس المسار الآمن في الخلفية (Fix الخاص بالبند 8) -
            // النافذة تبقى مستجيبة فوراً حتى لو كان السيرفر غير متاح أصلاً عند فتح التطبيق.
            _refreshTimer.Start();
            RunAutomaticRefreshAsync();
        };
        Closed += (s, e) => _refreshTimer.Stop();
    }

    // Patch 16: نقطة الدخول الوحيدة للتحديث التلقائي/الدوري (Tick + التحميل الابتدائي عند Loaded) -
    // async void مقصودة هنا فقط لأنها معالج حدث فعلياً (نمط قياسي ومقبول في WPF)، وكل استثناء
    // داخلها مُعالَج بالكامل ضمن LoadQueueAsync/RefreshDoctorViewOptionsAsync فلا يتسرّب أي استثناء
    // غير مُعالَج للخارج. الحارس الذرّي (_autoRefreshInProgress) يمنع اجتماع أكثر من تحديث تلقائي
    // واحد نشط في نفس اللحظة (Fix #3 - البند 16: منع تراكب التحديثات التلقائية).
    private async void RunAutomaticRefreshAsync()
    {
        if (Interlocked.CompareExchange(ref _autoRefreshInProgress, 1, 0) != 0) return;
        try
        {
            await LoadQueueAsync();
            await RefreshDoctorViewOptionsAsync(); // النقطة الخضراء (متصل الآن) تحتاج تحديث دوري أيضاً
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

    // يعيد بناء قائمة "قائمة انتظار مين" من جديد: "غير مُسنَدين" دائماً أولاً، ثم كل طبيب مسموح
    // لهذه الممرضة رؤية قائمته (حسب ما مُنح لها من الطبيب الرئيسي)، مع نقطة خضراء لمن هو متصل الآن.
    // يحافظ على الاختيار الحالي إن كان لا يزال ضمن القائمة الجديدة (مثلاً لو سُحبت الصلاحية أثناء العمل).
    //
    // Patch 16 - Fix #3: هذه العملية باتت آلية بالكامل (تُستدعى فقط من RunAutomaticRefreshAsync
    // الدوري/الابتدائي - لا يوجد أي زر أو مسار يدوي يستدعيها مباشرة)، فحُوِّلت مباشرة إلى async
    // Task مع Task.Run لجزء القراءة من القاعدة، بدل الإبقاء على نسخة متزامنة إضافية بلا داعٍ.
    private async Task RefreshDoctorViewOptionsAsync()
    {
        try
        {
            var (allDoctors, previouslySelected) = await Task.Run(() =>
            {
                var allowedIds = _accessRepo.GetAllowedDoctorIds(_currentUser.UserID);
                var doctors = _userRepo.GetAllDoctors().Where(d => allowedIds.Contains(d.UserID)).ToList();
                return (doctors, _selectedDoctorFilterId);
            });

            var options = new List<DoctorQueueOption>
            {
                new() { UserID = null, DisplayName = LocalizationManager.T("Main_UnassignedPatients") }
            };
            options.AddRange(allDoctors.Select(d => new DoctorQueueOption
            {
                UserID = d.UserID,
                DisplayName = d.FullName,
                StatusBrush = d.IsOnline
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xA0, 0x6B))
                    : System.Windows.Media.Brushes.Transparent
            }));

            DoctorViewSelector.ItemsSource = options;
            var stillValid = options.Any(o => o.UserID == previouslySelected);
            DoctorViewSelector.SelectedIndex = stillValid ? options.FindIndex(o => o.UserID == previouslySelected) : 0;
        }
        catch (Exception ex)
        {
            // فشل مؤقت بالاتصال - القائمة الحالية (إن وُجدت) تبقى كما هي، ستُحاول المحاولة القادمة
            // تلقائياً بعد 4 ثوانٍ. عطل اتصال حقيقي يُبلَّغ فوراً لـConnectionMonitor (Fix #4) بدل
            // انتظار دورة الفحص الدوري التالية له وحده.
            _connectionMonitor.ReportConnectionFailure(ex);
        }
    }

    private void DoctorViewSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DoctorViewSelector.SelectedItem is not DoctorQueueOption selected) return;
        if (_selectedDoctorFilterId == selected.UserID) return;

        _selectedDoctorFilterId = selected.UserID;
        LoadQueue();
    }

    private static Visibility ToVisibility(bool hasPermission) =>
        hasPermission ? Visibility.Visible : Visibility.Collapsed;

    // Patch 16 - Fix #3: حاوية بيانات بسيطة لنقل نتيجة استعلامات LoadQueue من خيط الخلفية (حيث
    // تُنفَّذ فعلياً عبر LoadQueueCore) إلى UI Thread (حيث تُطبَّق فعلياً عبر ApplyQueueResult) -
    // لا تحتوي أي مرجع لعناصر واجهة WPF، فآمنة تماماً للبناء والقراءة من أي خيط.
    private sealed class QueueLoadResult
    {
        public List<VisitQueueItem> Queue { get; init; } = new();
        public HashSet<int> UnpaidPatientIds { get; init; } = new();
        public Dictionary<int, bool> HasPrescriptionByVisitId { get; init; } = new();
        public HashSet<int> PendingVisitIds { get; init; } = new();
        public List<AppointmentChangeRequest> ResolvedRequests { get; init; } = new();
    }

    // كل الاستعلامات الفعلية (بلا أي لمسة لعناصر واجهة WPF) - نفس الاستعلامات والمنطق بالضبط
    // الموجودين سابقاً داخل LoadQueue، فقط مفصولين هنا ليمكن تشغيلهم بأمان على خيط خلفية (Task.Run)
    // من المسار التلقائي (LoadQueueAsync)، بينما يبقى المسار اليدوي (LoadQueue) يستدعيها مباشرة
    // على UI Thread تماماً كسابقاً (سلوك متطابق حرفياً لكل نقاط النداء اليدوية الموجودة).
    // ملاحظة: طلبات المواعيد "المحلولة" (Resolved) تُعلَّم كـ"مُطَّلَع عليها" هنا مباشرة (نفس
    // منطق MarkNotified الأصلي بالضبط) بدل تأجيلها لبعد عرض الرسالة، لتفادي أي استدعاء إضافي
    // لقاعدة البيانات بعد العودة لـUI Thread (Nested Task.Run) في المسار التلقائي.
    private QueueLoadResult LoadQueueCore()
    {
        // "غير مُسنَدين" (افتراضي) أو قائمة طبيب واحد محدَّد فقط - أبداً مزيج، وأبداً كل المرضى معاً
        // (راجع DoctorViewSelector أعلاه وRefreshDoctorViewOptionsAsync)
        var queue = _selectedDoctorFilterId == null
            ? _queueRepo.GetTodayQueue(new List<int>(), true)
            : _queueRepo.GetTodayQueue(new List<int> { _selectedDoctorFilterId.Value }, false);

        // جلب قائمة المرضى الذين لديهم ديون غير مسددة (الآن عبر PaymentRepository الموحَّد بدل استعلام محلي مكرر)
        var unpaidPatientIds = new HashSet<int>(_paymentRepo.GetUnpaidPatientIds());

        var hasPrescriptionByVisitId = new Dictionary<int, bool>();
        foreach (var item in queue)
        {
            bool hasPrescription = false;
            try
            {
                var todaySession = _sessionRepo.GetByPatientOnDate(item.PatientID, DateTime.Now.Date);
                hasPrescription = todaySession != null && !string.IsNullOrWhiteSpace(todaySession.Medication);
            }
            catch
            {
                // إذا تعذر جلب الجلسة مؤقتاً، يبقى زر الطباعة معطلاً حتى لا نحاول طباعة وصفة غير مؤكدة.
            }

            hasPrescriptionByVisitId[item.VisitID] = hasPrescription;
        }

        // تحديث حالة "قيد المراجعة" لكل موعد قادم - فحص جماعي واحد بدل استعلام لكل صف
        var pendingVisitIds = new HashSet<int>(_requestRepo.GetPendingVisitIds());

        // نفس منطق CheckForResolvedAppointmentRequests الأصلي بالضبط - القراءة والتعليم كـ"مُطَّلَع
        // عليها" معاً هنا (على خيط الخلفية)، وعرض رسائل النتيجة فعلياً يبقى على UI Thread لاحقاً
        // (راجع ShowResolvedRequestNotifications أدناه)
        var resolvedRequests = _requestRepo.GetUnnotifiedResolvedRequests(_currentUser.UserID);
        foreach (var request in resolvedRequests)
        {
            _requestRepo.MarkNotified(request.RequestID);
        }

        return new QueueLoadResult
        {
            Queue = queue,
            UnpaidPatientIds = unpaidPatientIds,
            HasPrescriptionByVisitId = hasPrescriptionByVisitId,
            PendingVisitIds = pendingVisitIds,
            ResolvedRequests = resolvedRequests
        };
    }

    // يُطبِّق نتيجة جاهزة على عناصر واجهة WPF - يجب أن يُستدعى دائماً على UI Thread فقط. نفس منطق
    // الدمج (بدل Clear+إعادة الإضافة) والعدّاد وعرض/إخفاء لوحة "لا يوجد" الموجودين سابقاً بالضبط.
    private void ApplyQueueResult(QueueLoadResult result)
    {
        var queue = result.Queue;
        var freshIds = new HashSet<int>(queue.Select(q => q.VisitID));

        for (int i = QueueRows.Count - 1; i >= 0; i--)
        {
            if (!freshIds.Contains(QueueRows[i].VisitID))
            {
                QueueRows.RemoveAt(i);
            }
        }

        foreach (var item in queue)
        {
            bool owesMoney = result.UnpaidPatientIds.Contains(item.PatientID);
            bool hasPrescription = result.HasPrescriptionByVisitId.TryGetValue(item.VisitID, out var hp) && hp;

            var existingRow = QueueRows.FirstOrDefault(r => r.VisitID == item.VisitID);

            if (existingRow != null)
            {
                existingRow.UpdateFrom(item);
                existingRow.HasUnpaidBalance = owesMoney; // تحديث حالة الدفع تلقائياً
                existingRow.HasPrescription = hasPrescription;
            }
            else
            {
                var newRow = new QueueRowViewModel(item)
                {
                    HasUnpaidBalance = owesMoney,
                    HasPrescription = hasPrescription
                };
                QueueRows.Add(newRow);
            }
        }

        var waitingCount = queue.Count(q => q.Status == VisitStatus.Waiting);
        var completedCount = queue.Count(q => q.Status == VisitStatus.Completed);
        var cancelledCount = queue.Count(q => q.Status == VisitStatus.Cancelled);
        // "كل الحالات الأخرى" = أي حالة غير الثلاث أعلاه (حالياً: قيد المعالجة + موعد محجوز)
        var otherCount = queue.Count - waitingCount - completedCount - cancelledCount;

        CountText.Text = LocalizationManager.T("Main_PatientsTodayDetailedFormat",
            queue.Count, waitingCount, completedCount, cancelledCount, otherCount);
        EmptyQueuePanel.Visibility = QueueRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var row in QueueRows)
        {
            row.IsAppointmentPending = row.FutureVisitID.HasValue && result.PendingVisitIds.Contains(row.FutureVisitID.Value);
        }

        ShowResolvedRequestNotifications(result.ResolvedRequests);
    }

    // المسار اليدوي الأصلي - يبقى متزامناً بالحرف كما كان (نفس نقاط النداء الموجودة: أزرار، إغلاق
    // نوافذ فرعية، تغيير الفلتر). لم يتغيّر سلوكه إطلاقاً في هذا الباتش (خارج نطاق الإصلاح -
    // البند 2: هذا الإصلاح يخص فقط التحديث التلقائي/الدوري، وليس كل نداء متزامن في المشروع).
    private void LoadQueue()
    {
        try
        {
            var result = LoadQueueCore();
            ApplyQueueResult(result);
        }
        catch (Exception ex)
        {
            CountText.Text = LocalizationManager.T("Main_CouldNotLoadQueueFormat", ex.Message);
        }
    }

    // Patch 16 - Fix #3: المسار التلقائي/الدوري فقط (RunAutomaticRefreshAsync) - نفس LoadQueueCore
    // بالضبط لكن مُنفَّذة على خيط خلفية عبر Task.Run، ثم تطبيق النتيجة على الواجهة بعد await (على
    // UI Thread تلقائياً، بلا Dispatcher.Invoke). عطل اتصال حقيقي يُبلَّغ فوراً لـConnectionMonitor.
    private async Task LoadQueueAsync()
    {
        try
        {
            var result = await Task.Run(() => LoadQueueCore());
            ApplyQueueResult(result);
        }
        catch (Exception ex)
        {
            _connectionMonitor.ReportConnectionFailure(ex);
            CountText.Text = LocalizationManager.T("Main_CouldNotLoadQueueFormat", ex.Message);
        }
    }

    // زر "تعديل" بجانب الموعد القادم - يفتح نافذة تقديم طلب تعديل (يحتاج موافقة الطبيب، لا يُحفظ مباشرة)
    private void EditAppointmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: QueueRowViewModel row } || !row.FutureVisitID.HasValue) return;

        var dialog = new EditAppointmentRequestDialog(row.FutureVisitID.Value, _currentUser.UserID, _queueRepo, _requestRepo, _db)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            LoadQueue();
        }
    }

    // تُعرض للممرضة مرة واحدة فقط: نتيجة رد الطبيب (موافقة/رفض) على طلبات قدَّمتها هي بالذات.
    // Patch 16: القراءة والتعليم كـ"مُطَّلَع عليها" انتقلا إلى LoadQueueCore أعلاه (جزء البيانات) -
    // هذه الدالة الآن للعرض فقط (MessageBox، يجب أن يبقى على UI Thread دائماً).
    private void ShowResolvedRequestNotifications(List<AppointmentChangeRequest> resolved)
    {
        foreach (var request in resolved)
        {
            if (request.Status == AppointmentChangeRequestStatus.Approved)
            {
                MessageBox.Show(
                    LocalizationManager.T("Notif_NurseApprovedFormat", request.PatientFullName),
                    LocalizationManager.T("Notif_NurseResultTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var reason = string.IsNullOrWhiteSpace(request.RejectionReason)
                    ? LocalizationManager.T("Notif_NurseRejectedNoReason")
                    : request.RejectionReason;
                MessageBox.Show(
                    LocalizationManager.T("Notif_NurseRejectedFormat", request.PatientFullName, reason),
                    LocalizationManager.T("Notif_NurseResultTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void PrintPrescriptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: QueueRowViewModel row } || !row.HasPrescription) return;

        try
        {
            var session = _sessionRepo.GetByPatientOnDate(row.PatientID, DateTime.Now.Date);
            if (session == null || string.IsNullOrWhiteSpace(session.Medication))
            {
                row.HasPrescription = false;
                return;
            }

            var patient = _patientRepo.GetById(row.PatientID, null, true);
            if (patient == null) return;

            var medicationRepo = new MedicationPresetRepository(_db);
            var presets = medicationRepo.GetActivePresets();
            var lines = session.Medication
                .Split("; ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name =>
                {
                    var preset = presets.FirstOrDefault(x => x.MedicationName == name);
                    return new PrescriptionLineViewModel
                    {
                        MedicationName = name,
                        Dosage = preset?.DefaultDosage ?? string.Empty
                    };
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.MedicationName))
                .ToList();

            if (lines.Count == 0)
            {
                row.HasPrescription = false;
                return;
            }

            var printers = DentalClinic.Printing.SilentPdfPrinter.GetInstalledPrinterNames();
            if (printers.Count == 0)
            {
                MessageBox.Show("لم يتم العثور على أي طابعة مثبتة على هذا الجهاز.",
                    "الطباعة", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var printerDialog = new Window
            {
                Owner = this,
                Title = "اختيار الطابعة",
                Width = 460,
                Height = 220,
                MinWidth = 460,
                MinHeight = 220,
                MaxWidth = 460,
                MaxHeight = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.White
            };

            var root = new Border
            {
                Background = System.Windows.Media.Brushes.White,
                Padding = new Thickness(24),
                CornerRadius = new CornerRadius(14)
            };

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "اختر الطابعة التي ستتم الطباعة عليها",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.Black
            };
            Grid.SetRow(title, 0);
            layout.Children.Add(title);

            var printerBox = new ComboBox
            {
                ItemsSource = printers,
                SelectedIndex = 0,
                Height = 38,
                FontSize = 13,
                Padding = new Thickness(10, 6, 10, 6),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(printerBox, 2);
            layout.Children.Add(printerBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancelButton = new Button
            {
                Content = "إلغاء",
                Width = 88,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 0, 12, 0)
            };
            cancelButton.Click += (_, _) => printerDialog.DialogResult = false;

            var printButton = new Button
            {
                Content = "طباعة",
                Width = 88,
                Height = 34,
                Padding = new Thickness(12, 0, 12, 0),
                IsDefault = true
            };
            printButton.Click += (_, _) => printerDialog.DialogResult = true;

            buttons.Children.Add(cancelButton);
            buttons.Children.Add(printButton);
            Grid.SetRow(buttons, 4);
            layout.Children.Add(buttons);

            root.Child = layout;
            printerDialog.Content = root;

            if (printerDialog.ShowDialog() != true || printerBox.SelectedItem is not string selectedPrinter || string.IsNullOrWhiteSpace(selectedPrinter))
                return;

            DentalClinic.Features.PrescriptionPdfExporter.GenerateAndPrint(
                patient.FullName, DateTime.Now, lines, null, patient.Age, selectedPrinter);

            MessageBox.Show(LocalizationManager.T("Rx_PrintSuccess"), LocalizationManager.T("Rx_Title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Rx_GenerateFailedFormat", ex.Message),
                LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddPatientButton_Click(object sender, RoutedEventArgs e)
    {
        // فحص دفاعي إضافي: حتى لو ظهر الزر بطريقة غير متوقعة، لن تُفتح الشاشة إلا لمن يملك الصلاحية فعلاً
        if (!_currentUser.HasPermission(UserPermission.RegisterPatients))
        {
            return;
        }

        var window = new AddPatientWindow(_currentUser) { Owner = this };
        if (window.ShowDialog() == true)
        {
            LoadQueue();
        }
    }

    private void SearchPatientButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new PatientSearchWindow(_currentUser) { Owner = this };
        if (window.ShowDialog() == true)
        {
            LoadQueue();
        }
    }

    // 🩺 فتح نافذة إدارة العلاجات والأسعار - نفس الشاشة المستخدَمة في تطبيق الطبيب
    private void ManageTreatmentsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentUser.HasPermission(UserPermission.ManageTreatments))
        {
            return;
        }

        var window = new TreatmentManagementWindow { Owner = this };
        window.ShowDialog();
    }

    private void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentUser.HasPermission(UserPermission.AccessBackup))
        {
            return;
        }

        var window = new BackupWindow { Owner = this };
        window.ShowDialog();
    }

    private void ManageUsersButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentUser.HasPermission(UserPermission.ManageUsers))
        {
            return;
        }

        var window = new UserManagementWindow(_currentUser) { Owner = this };
        window.ShowDialog();
    }

    private void FinancialDashboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentUser.HasPermission(UserPermission.AccessFinance))
        {
            return;
        }

        var window = new FinancialDashboardWindow(_currentUser) { Owner = this };
        window.ShowDialog();
    }

    // فتح الملف الطبي الكامل للمريض المحدَّد في قائمة الانتظار (زر علوي أو نقر مزدوج على الصف)
    private void OpenPatientFileButton_Click(object sender, RoutedEventArgs e) => OpenSelectedPatientFile();

    private void QueueGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedPatientFile();

    private void OpenSelectedPatientFile()
    {
        // فحص دفاعي: حتى لو ظهر الزر أو استُخدم النقر المزدوج بطريقة غير متوقعة،
        // لن يُفتح الملف الطبي إلا لمن يملك صلاحية OpenPatientFile فعلاً.
        if (!_currentUser.HasPermission(UserPermission.OpenPatientFile))
        {
            MessageBox.Show(LocalizationManager.T("Main_NoPermissionOpenFile"), LocalizationManager.T("Common_AccessDenied"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (QueueGrid.SelectedItem is not QueueRowViewModel selected)
        {
            MessageBox.Show(LocalizationManager.T("Main_SelectPatientFirst"), LocalizationManager.T("Common_Notice"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // نفس شاشة الملف الطبي المستخدَمة في المشروع المشترك (DentalClinic.Features.PatientFileWindow)
        var window = new PatientFileWindow(selected.PatientID, selected.VisitID, _currentUser)
        {
            Owner = this
        };
        window.ShowDialog();
        LoadQueue();
    }

    private void CollectPaymentButton_Click(object sender, RoutedEventArgs e)
    {
        // فحص دفاعي: يغطي زر الشريط العلوي وزر كل صف في القائمة معاً، حتى لو ظهر أحدهما بطريقة غير متوقعة
        if (!_currentUser.HasPermission(UserPermission.CollectPayments))
        {
            MessageBox.Show(LocalizationManager.T("Main_NoPermissionCollectPayment"), LocalizationManager.T("Common_AccessDenied"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        QueueRowViewModel? selectedRow = null;

        if (sender is Button { Tag: QueueRowViewModel taggedRow })
        {
            selectedRow = taggedRow;
        }
        else if (QueueGrid.SelectedItem is QueueRowViewModel rowFromGrid)
        {
            selectedRow = rowFromGrid;
        }

        if (selectedRow == null)
        {
            MessageBox.Show(LocalizationManager.T("Main_SelectPatientFromQueueFirst"), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var sessionId = _paymentRepo.GetLatestUnpaidSessionId(selectedRow.PatientID);

            if (sessionId.HasValue)
            {
                var paymentWindow = new CollectPaymentWindow(sessionId.Value, selectedRow.PatientFullName, _currentUser)
                {
                    Owner = this
                };

                if (paymentWindow.ShowDialog() == true)
                {
                    LoadQueue(); // إعادة تحميل القائمة لتتغير حالة الزر فوراً إلى Paid
                }
            }
            else
            {
                MessageBox.Show(LocalizationManager.T("Main_NoUnpaidSessionFormat", selectedRow.PatientFullName), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Main_ErrorCheckingPaymentFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadQueue();

    // إصلاح: زر جديد لتسجيل حضور موعد محجوز (Scheduled) فعلياً عند وصول المريض في يوم موعده
    private void CheckInButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: QueueRowViewModel row })
        {
            return;
        }

        try
        {
            var success = _queueRepo.CheckInScheduledVisit(row.VisitID, _currentUser.UserID);
            if (success)
            {
                LoadQueue();
            }
            else
            {
                MessageBox.Show(LocalizationManager.T("Main_CheckInFailed"), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Main_ErrorPrefixFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelVisitButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: QueueRowViewModel row })
        {
            return;
        }

        var confirm = MessageBox.Show(
            LocalizationManager.T("Main_ConfirmCancelMessageFormat", row.PatientFullName),
            LocalizationManager.T("Main_ConfirmCancelTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var success = _queueRepo.CancelVisit(row.VisitID, _currentUser.UserID);
            if (success)
            {
                LoadQueue();
            }
            else
            {
                MessageBox.Show(LocalizationManager.T("Main_CouldNotCancelVisit"), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Main_ErrorPrefixFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // تعديل البيانات الأساسية لمريض في قائمة الانتظار (تصحيح خطأ إدخال) - محمي بصلاحية EditPatients
    private void EditPatientButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: QueueRowViewModel row })
        {
            return;
        }

        // فحص دفاعي إضافي: حتى لو ظهر الزر بطريقة غير متوقعة، لن تُفتح شاشة التعديل إلا لمن يملك الصلاحية فعلاً
        if (!_currentUser.HasPermission(UserPermission.EditPatients))
        {
            MessageBox.Show(LocalizationManager.T("Main_NoPermissionEditPatient"), LocalizationManager.T("Common_AccessDenied"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var patient = _patientRepo.GetById(row.PatientID);
            if (patient == null) return; // نادراً: حُذف المريض من مكان آخر بين لحظة عرض القائمة ولحظة الضغط على تعديل

            var window = new AddPatientWindow(_currentUser, patient) { Owner = this };
            if (window.ShowDialog() == true)
            {
                LoadQueue();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Main_ErrorPrefixFormat", ex.Message),
                LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // حذف مريض نهائياً من قاعدة البيانات - عملية لا رجعة فيها، محمية بصلاحية DeletePatients وبتأكيد صريح
    private void DeletePatientButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: QueueRowViewModel row })
        {
            return;
        }

        // فحص دفاعي إضافي: حتى لو ظهر الزر بطريقة غير متوقعة، لن يُنفَّذ الحذف إلا لمن يملك الصلاحية فعلاً
        if (!_currentUser.HasPermission(UserPermission.DeletePatients))
        {
            MessageBox.Show(LocalizationManager.T("Main_NoPermissionDeletePatient"), LocalizationManager.T("Common_AccessDenied"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var deleteConfirm = MessageBox.Show(
            LocalizationManager.T("Main_ConfirmDeletePatientMessageFormat", row.PatientFullName),
            LocalizationManager.T("Main_ConfirmDeletePatientTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (deleteConfirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _patientRepo.PermanentlyDelete(row.PatientID);
            LoadQueue();
            MessageBox.Show(LocalizationManager.T("Main_PatientDeletedFormat", row.PatientFullName),
                LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Main_CouldNotDeletePatientFormat", ex.Message),
                LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            LocalizationManager.T("Main_ConfirmLogoutMessage"),
            LocalizationManager.T("Main_ConfirmLogoutTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

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