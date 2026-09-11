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
using DentalClinic.Features; // الشاشات المشتركة (PatientFileWindow, AddPatientWindow, CollectPaymentWindow, ...)
using DentalClinic.UI.Localization;

namespace DentalClinic.DoctorApp;

public partial class MainWindow : Window
{
    private readonly UserAccount _currentUser;
    private readonly QueueRepository _queueRepo;
    private readonly PaymentRepository _paymentRepo;
    private readonly PatientRepository _patientRepo;
    private readonly UserRepository _userRepo;
    private readonly AppointmentChangeRequestRepository _requestRepo;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _heartbeatTimer;
    private readonly DatabaseHelper _db;
    private readonly DentalClinic.UI.Connectivity.ConnectionMonitor _connectionMonitor;
    private DentalClinic.UI.ConnectionLostWindow? _connectionLostWindow;

    // Patch 16 - Fix #3/#16: حارس ذرّي يمنع تراكب دورتي تحديث تلقائي (Tick يعمل بينما دورة سابقة
    // لم تنتهِ بعد، أو تراكب مع التحميل الابتدائي عند Loaded) - 0 = خامل، 1 = نشط حالياً. لا علاقة
    // له بالتحديثات اليدوية (نقر زر) التي تبقى تماماً كما كانت (LoadQueue()/RefreshPendingAppointmentRequests()
    // المتزامنتين الأصليتين).
    private int _autoRefreshInProgress;

    // معرّفات الطلبات المعلَّقة اللي شافها هذا الطبيب أصلاً - يُستخدم لتشغيل الصوت فقط عند وصول
    // طلب جديد لم يظهر من قبل، بدل تكرار الصوت مع كل استعلام دوري (كل 4 ثوانٍ) طالما الطلب لا يزال معلَّقاً
    private readonly HashSet<int> _seenPendingRequestIds = new();

    public ObservableCollection<QueueRowViewModel> QueueRows { get; } = new();

    public MainWindow(UserAccount currentUser)
    {
        _currentUser = currentUser;
        InitializeComponent();
        Title = LocalizationManager.T("App_DoctorTitleWithNameFormat", _currentUser.FullName);

        // كل زر يظهر فقط إذا كان المستخدم الحالي يملك الصلاحية المقابلة له - مستقلة تماماً عن الدور (Doctor/Nurse)
        RegisterPatientButton.Visibility     = ToVisibility(_currentUser.HasPermission(UserPermission.RegisterPatients));
        OpenPatientFileButton.Visibility     = ToVisibility(_currentUser.HasPermission(UserPermission.OpenPatientFile));
        CollectPaymentButton.Visibility      = ToVisibility(_currentUser.HasPermission(UserPermission.CollectPayments));
        ManageTreatmentsButton.Visibility    = ToVisibility(_currentUser.HasPermission(UserPermission.ManageTreatments));
        BackupButton.Visibility              = ToVisibility(_currentUser.HasPermission(UserPermission.AccessBackup));
        ManageUsersButton.Visibility         = ToVisibility(_currentUser.HasPermission(UserPermission.ManageUsers));
        // ⚠️ شرط مزدوج مقصود (Main Doctor + Permission) - ManageUsers وحدها لم تعد كافية: أي طبيب
        // آخر (غير رئيسي) قد يملك ManageUsers أيضاً (مثلاً مدير عيادة مساعد) دون أن يكون مخوَّلاً
        // للدخول لتطبيق المرمم بحساب طبيب كامل الصلاحيات.
        OpenProsthetistAppButton.Visibility  = ToVisibility(_currentUser.IsMainDoctor && _currentUser.HasPermission(UserPermission.ManageUsers));
        // Patch 13: IsMainDoctor فقط - بلا أي صلاحية إضافية (لا ManageUsers ولا غيرها)، تنفيذاً
        // لطلب صريح باستخدام آلية IsMainDoctor وحدها لتحديد من يدير هذه القوائم
        OpenLookupManagementButton.Visibility = ToVisibility(_currentUser.IsMainDoctor);
        FinancialDashboardButton.Visibility  = ToVisibility(_currentUser.HasPermission(UserPermission.AccessFinance));
        DeletePatientColumn.Visibility       = ToVisibility(_currentUser.HasPermission(UserPermission.DeletePatients));
        EditPatientColumn.Visibility         = ToVisibility(_currentUser.HasPermission(UserPermission.EditPatients));

        QueueGrid.ItemsSource = QueueRows;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _db = db;
        _queueRepo = new QueueRepository(db);
        _paymentRepo = new PaymentRepository(db);
        _patientRepo = new PatientRepository(db);
        _userRepo = new UserRepository(db);
        _requestRepo = new AppointmentChangeRequestRepository(db);

        // مراقبة الاتصال بالخادم في الخلفية (لا تُجمِّد الواجهة أبداً - راجع ConnectionMonitor) +
        // مؤشر الحالة في الشريط العلوي + نافذة إشعار تلقائية عند الانقطاع
        _connectionMonitor = new DentalClinic.UI.Connectivity.ConnectionMonitor(_db);
        _connectionMonitor.StatusChanged += OnConnectionStatusChanged;
        ConnectionStatusIndicatorControl.RetryRequested += (s, e) => _connectionMonitor.CheckNow();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        // Patch 16 - Fix #3: الـTick لم يعد يُنفِّذ أي استدعاء Repository/ADO.NET مباشرة على UI
        // Thread - يُفوِّض العمل بالكامل لـRunAutomaticQueueRefreshAsync (Task.Run في الخلفية ثم
        // تحديث الواجهة بعد الاكتمال فقط).
        _refreshTimer.Tick += (s, e) =>
        {
            // لا فائدة من محاولة استعلام جديد ونحن نعلم أصلاً أن الاتصال منقطع - كانت هذه المحاولات
            // المتكررة كل 4 ثوانٍ هي بالضبط سبب التجمُّد المتكرر (كل محاولة تنتظر كامل مهلة الاتصال
            // قبل أن تفشل). ConnectionMonitor يعمل بمعزل عن هذا المؤقّت ويكتشف عودة الاتصال بنفسه.
            if (!_connectionMonitor.IsConnected) return;
            RunAutomaticQueueRefreshAsync();
        };

        // "نبضة" دورية تُثبت أن هذا الطبيب متصل الآن فعلياً - أساس النقطة الخضراء في شاشات الممرضة.
        // فترة 45 ثانية + عتبة "متصل" 90 ثانية بالسيرفر (راجع UserRepository) تعني أن الحالة تصحّح
        // نفسها تلقائياً خلال دقيقة ونصف تقريباً حتى لو أُغلق التطبيق بشكل غير سليم (طاح، انقطاع كهرباء)
        _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        _heartbeatTimer.Tick += (s, e) =>
        {
            if (!_connectionMonitor.IsConnected) return; // نفس منطق تفادي المحاولة المحكوم عليها بالفشل مسبقاً
            _ = SendHeartbeatAsync();
        };

        Loaded += (s, e) =>
        {
            // التحميل الابتدائي أيضاً بات يمر عبر نفس المسار الآمن في الخلفية (البند 8) - النافذة
            // تبقى مستجيبة فوراً حتى لو كان السيرفر غير متاح أصلاً عند فتح التطبيق.
            _refreshTimer.Start();
            _heartbeatTimer.Start();
            RunAutomaticQueueRefreshAsync();
            _ = SendHeartbeatAsync(); // نبضة فورية عند فتح التطبيق - لا ننتظر أول Tick بعد 45 ثانية
        };
        Closed += (s, e) =>
        {
            _refreshTimer.Stop();
            _heartbeatTimer.Stop();
            _connectionMonitor.Dispose();
        };
    }

    // Patch 16: نقطة الدخول الوحيدة للتحديث التلقائي/الدوري لقائمة الانتظار وطلبات المواعيد
    // المعلَّقة معاً (Tick + التحميل الابتدائي عند Loaded) - async void مقصودة هنا فقط لأنها معالج
    // حدث فعلياً (نمط قياسي ومقبول في WPF)؛ كل استثناء داخلها مُعالَج بالكامل ضمن LoadQueueAsync/
    // RefreshPendingAppointmentRequestsAsync فلا يتسرّب أي استثناء غير مُعالَج للخارج. الحارس
    // الذرّي (_autoRefreshInProgress) يمنع اجتماع أكثر من تحديث تلقائي واحد نشط بنفس اللحظة.
    private async void RunAutomaticQueueRefreshAsync()
    {
        if (Interlocked.CompareExchange(ref _autoRefreshInProgress, 1, 0) != 0) return;
        try
        {
            await LoadQueueAsync();
            await RefreshPendingAppointmentRequestsAsync();
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

    // قائمة الطلبات المعلَّقة المخصَّصة بهذا الطبيب حالياً (نفس نافذة الموافقة تُبنى منها مباشرة بلا استعلام إضافي)
    private List<AppointmentChangeRequest> _currentPendingRequests = new();

    // Patch 16: حاوية بيانات بسيطة لنقل نتيجة GetPendingRequests من خيط الخلفية إلى UI Thread -
    // بلا أي مرجع لعناصر واجهة WPF.
    private sealed class PendingRequestsResult
    {
        public List<AppointmentChangeRequest> RelevantRequests { get; init; } = new();
    }

    // فحص دوري (كل 4 ثوانٍ) لطلبات تعديل المواعيد المعلَّقة الخاصة بمرضى هذا الطبيب فقط (نفس قاعدة رؤية
    // قائمة الانتظار بالضبط: مرضاه + الغير مُسنَدين). القراءة الفعلية من القاعدة هنا فقط (بلا أي لمسة
    // لعناصر واجهة WPF)، لتُشغَّل بأمان على خيط خلفية من المسار التلقائي (راجع
    // RefreshPendingAppointmentRequestsAsync)، بينما يبقى المسار اليدوي (RefreshPendingAppointmentRequests)
    // يستدعيها مباشرة على UI Thread تماماً كسابقاً.
    private PendingRequestsResult RefreshPendingAppointmentRequestsCore()
    {
        var all = _requestRepo.GetPendingRequests();
        var relevant = all
            .Where(r => r.PatientAssignedDoctorUserID == _currentUser.UserID || r.PatientAssignedDoctorUserID == null)
            .ToList();
        return new PendingRequestsResult { RelevantRequests = relevant };
    }

    // يُطبِّق نتيجة جاهزة على عناصر واجهة WPF (زر الجرس + صوت التنبيه لطلب جديد) - يجب أن يُستدعى
    // دائماً على UI Thread فقط. نفس المنطق الأصلي بالضبط.
    private void ApplyPendingRequestsResult(PendingRequestsResult result)
    {
        _currentPendingRequests = result.RelevantRequests;

        if (_currentPendingRequests.Count > 0)
        {
            NotificationBellButton.Content = LocalizationManager.T("Notif_BellButtonFormat", _currentPendingRequests.Count);
            NotificationBellButton.Visibility = Visibility.Visible;
        }
        else
        {
            NotificationBellButton.Visibility = Visibility.Collapsed;
        }

        var hasNewRequest = _currentPendingRequests.Any(r => !_seenPendingRequestIds.Contains(r.RequestID));
        if (hasNewRequest)
        {
            System.Media.SystemSounds.Exclamation.Play();
        }

        _seenPendingRequestIds.Clear();
        foreach (var r in _currentPendingRequests)
        {
            _seenPendingRequestIds.Add(r.RequestID);
        }
    }

    // المسار اليدوي الأصلي - يبقى متزامناً بالحرف كما كان (نقطة النداء الوحيدة اليدوية: فتح/إغلاق
    // نافذة الطلبات المعلَّقة عبر NotificationBellButton_Click). لم يتغيّر سلوكه إطلاقاً هنا.
    private void RefreshPendingAppointmentRequests()
    {
        try
        {
            var result = RefreshPendingAppointmentRequestsCore();
            ApplyPendingRequestsResult(result);
        }
        catch
        {
            // فشل مؤقت بالاتصال - المحاولة القادمة تلقائياً بعد 4 ثوانٍ
        }
    }

    // Patch 16 - Fix #3: المسار التلقائي/الدوري فقط - نفس Core بالضبط لكن عبر Task.Run في الخلفية،
    // ثم تطبيق النتيجة بعد await. عطل اتصال حقيقي يُبلَّغ فوراً لـConnectionMonitor (Fix #4).
    private async Task RefreshPendingAppointmentRequestsAsync()
    {
        try
        {
            var result = await Task.Run(() => RefreshPendingAppointmentRequestsCore());
            ApplyPendingRequestsResult(result);
        }
        catch (Exception ex)
        {
            _connectionMonitor.ReportConnectionFailure(ex);
        }
    }

    private void NotificationBellButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new PendingAppointmentRequestsWindow(_currentUser, _requestRepo, _userRepo, _currentPendingRequests) { Owner = this };
        window.ShowDialog();

        RefreshPendingAppointmentRequests();
        LoadQueue(); // احتمال تغيّر موعد قادم لمريض ظاهر حالياً بقائمة الانتظار
    }

    // Patch 16: نبضة الاتصال الدورية لا يوجد لها أي نداء يدوي على الإطلاق (فقط Tick تلقائي +
    // نبضة فورية عند Loaded) - حُوِّلت مباشرة إلى async Task مع Task.Run بدل الإبقاء على نسخة
    // متزامنة إضافية بلا داعٍ. لا تُحدِّث أي عنصر واجهة على الإطلاق فلا حاجة لأي تطبيق لاحق على UI.
    private async Task SendHeartbeatAsync()
    {
        try
        {
            await Task.Run(() => _userRepo.UpdateHeartbeat(_currentUser.UserID));
        }
        catch (Exception ex)
        {
            // فشل مؤقت بالاتصال لا يستحق مقاطعة الطبيب برسالة خطأ - ستُحاول النبضة التالية بعد 45 ثانية تلقائياً
            _connectionMonitor.ReportConnectionFailure(ex);
        }
    }

    private static Visibility ToVisibility(bool hasPermission) =>
        hasPermission ? Visibility.Visible : Visibility.Collapsed;

    // Button.Click مُعلَّق (attached event) من StackPanel القائمة الجانبية في XAML - يلتقط نقر أي زر
    // بداخلها بعد أن ينفّذ ذلك الزر دالته الخاصة أولاً (RegisterPatientButton_Click، إلخ)، ثم يغلق
    // القائمة تلقائياً. لا علاقة له بمنطق أي زر - فقط سلوك واجهة إضافي.
    private void HamburgerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        HamburgerToggle.IsChecked = false;
    }

    // Patch 16: حاوية بيانات بسيطة لنقل نتيجة GetTodayQueue من خيط الخلفية إلى UI Thread - بلا أي
    // مرجع لعناصر واجهة WPF.
    private sealed class QueueLoadResult
    {
        public List<VisitQueueItem> Queue { get; init; } = new();
    }

    // كل طبيب - بمن فيهم الرئيسي - يشوف بقائمة الانتظار مرضاه فقط + الغير مُسنَدين لأحد بعد.
    // مرضى الأطباء الآخرين لا يظهرون هنا إطلاقاً، حتى للرئيسي (يصل لهم فقط عبر البحث الصريح).
    // القراءة الفعلية من القاعدة هنا فقط (بلا أي لمسة لعناصر واجهة WPF)، لتُشغَّل بأمان على خيط
    // خلفية من المسار التلقائي (راجع LoadQueueAsync)، بينما يبقى المسار اليدوي (LoadQueue)
    // يستدعيها مباشرة على UI Thread تماماً كسابقاً.
    private QueueLoadResult LoadQueueCore()
    {
        var queue = _queueRepo.GetTodayQueue(new List<int> { _currentUser.UserID }, true);
        return new QueueLoadResult { Queue = queue };
    }

    // يُطبِّق نتيجة جاهزة على عناصر واجهة WPF - يجب أن يُستدعى دائماً على UI Thread فقط. نفس منطق
    // الدمج (بدل Clear+إعادة الإضافة) والعدّاد الموجودين سابقاً بالضبط.
    private void ApplyQueueResult(QueueLoadResult result)
    {
        var queue = result.Queue;

        // دمج ذكي بدل Clear() + إعادة الإضافة: يحافظ على تحديد الصف الحالي أثناء التحديث التلقائي
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
            var existingRow = QueueRows.FirstOrDefault(r => r.VisitID == item.VisitID);
            if (existingRow != null)
            {
                existingRow.UpdateFrom(item);
            }
            else
            {
                QueueRows.Add(new QueueRowViewModel(item));
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
    }

    // المسار اليدوي الأصلي - يبقى متزامناً بالحرف كما كان (نفس نقاط النداء الموجودة: أزرار، إغلاق
    // نوافذ فرعية). لم يتغيّر سلوكه إطلاقاً في هذا الباتش.
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

    // Patch 16 - Fix #3: المسار التلقائي/الدوري فقط (RunAutomaticQueueRefreshAsync) - نفس
    // LoadQueueCore بالضبط لكن مُنفَّذة على خيط خلفية عبر Task.Run، ثم تطبيق النتيجة على الواجهة
    // بعد await. عطل اتصال حقيقي يُبلَّغ فوراً لـConnectionMonitor.
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

    private void OpenFileButton_Click(object sender, RoutedEventArgs e) => OpenSelectedPatientFile();

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

    // تعديل موعد مستقبلي مباشرة من الطبيب - لا ينشئ طلب موافقة.
    private void EditAppointmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: QueueRowViewModel row } || !row.FutureVisitID.HasValue) return;

        if (_currentUser.Role != UserRole.Doctor)
        {
            MessageBox.Show(LocalizationManager.T("Sched_DoctorOnly"), LocalizationManager.T("Common_AccessDenied"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_requestRepo.HasPendingRequest(row.FutureVisitID.Value))
        {
            MessageBox.Show(LocalizationManager.T("Sched_DirectBlockedPending"), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var dialog = new EditAppointmentDialog(row.FutureVisitID.Value, _currentUser, _queueRepo, _db) { Owner = this };
            if (dialog.ShowDialog() == true) LoadQueue();
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Main_ErrorPrefixFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // حذف الموعد المستقبلي نفسه فقط - لا يمس المريض أو الزيارات الطبية السابقة/الحالية.
    private void DeleteAppointmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: QueueRowViewModel row } || !row.FutureVisitID.HasValue) return;

        if (_currentUser.Role != UserRole.Doctor)
        {
            MessageBox.Show(LocalizationManager.T("Sched_DoctorOnly"), LocalizationManager.T("Common_AccessDenied"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_requestRepo.HasPendingRequest(row.FutureVisitID.Value))
        {
            MessageBox.Show(LocalizationManager.T("Sched_DirectBlockedPending"), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            LocalizationManager.T("Sched_ConfirmDeleteFormat", row.PatientFullName, row.NextAppointmentText),
            LocalizationManager.T("Sched_DeleteAppointmentTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            if (_queueRepo.DeleteFutureAppointment(row.FutureVisitID.Value, _currentUser))
            {
                LoadQueue();
                MessageBox.Show(LocalizationManager.T("Sched_DeleteAppointmentSuccess"), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(LocalizationManager.T("Sched_DirectAppointmentNotFound"), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Main_ErrorPrefixFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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

        var confirm = MessageBox.Show(
            LocalizationManager.T("Main_ConfirmDeletePatientMessageFormat", row.PatientFullName),
            LocalizationManager.T("Main_ConfirmDeletePatientTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
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

    // بحث سريع داخل قائمة اليوم فقط. لا نستخدم CollectionView.Filter حتى لا تختفي بقية الصفوف؛
    // البحث هنا يعمل كـ"محدد سريع" وينقل اختيار DataGrid إلى أول مريض مطابق وكأن الطبيب نقر عليه.
    private void TodayQueueSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;

        var query = TodayQueueSearchBox.Text.Trim();
        ClearTodayQueueSearchButton.Visibility = string.IsNullOrEmpty(query)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (string.IsNullOrEmpty(query))
            return;

        var comparison = StringComparison.CurrentCultureIgnoreCase;

        // نفضّل الاسم الذي يبدأ بالنص المدخل، ثم نبحث عن تطابق جزئي.
        var match = QueueRows.FirstOrDefault(row =>
            row.PatientFullName.StartsWith(query, comparison))
            ?? QueueRows.FirstOrDefault(row =>
                row.PatientFullName.Contains(query, comparison));

        if (match is null)
        {
            QueueGrid.SelectedItem = null;
            return;
        }

        SelectQueuePatient(match);
    }

    private void SelectQueuePatient(QueueRowViewModel patient)
    {
        QueueGrid.SelectedItem = patient;
        QueueGrid.ScrollIntoView(patient);
        // لا ننقل KeyboardFocus إلى الـ DataGrid؛ يجب أن يبقى الطبيب داخل حقل البحث
        // ليتمكن من مواصلة الكتابة والبحث Real-Time دون انقطاع.
    }

    private void ClearTodayQueueSearchButton_Click(object sender, RoutedEventArgs e)
    {
        TodayQueueSearchBox.Clear();
        TodayQueueSearchBox.Focus();
    }

    private void QueueGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedPatientFile();

    private void OpenSelectedPatientFile()
    {
        // فحص دفاعي إضافي: حتى لو ظهر الزر أو استُخدم النقر المزدوج بطريقة غير متوقعة،
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

        // فتح الملف يعني بدء المعالجة فعلياً: ننقل الحالة تلقائياً إلى "قيد المعالجة" إن كانت لا تزال "في الانتظار"
        try
        {
            if (selected.Status == VisitStatus.Waiting)
            {
                _queueRepo.UpdateStatus(selected.VisitID, VisitStatus.InTreatment, _currentUser.UserID);
            }
        }
        catch
        {
            // لا نمنع فتح الملف حتى لو فشل تحديث الحالة هنا
        }

        var window = new PatientFileWindow(selected.PatientID, selected.VisitID, _currentUser) { Owner = this };
        window.ShowDialog();
        LoadQueue();
    }

    // بحث عن مريض بالاسم/الهاتف/الجنس/العمر - نفس الشاشة المستخدَمة في تطبيق الاستقبال، وفيها
    // فلتر "عرض مرضى طبيب معيَّن" الإضافي الخاص بالطبيب الرئيسي فقط
    private void SearchPatientButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new PatientSearchWindow(_currentUser) { Owner = this };
        window.ShowDialog();
        LoadQueue(); // احتمال حصل تعديل/حذف مريض من داخل شاشة البحث - نحدّث القائمة احتياطاً
    }

    private void AdvancedSearchButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new AdvancedSearchWindow(_currentUser) { Owner = this };
        window.ShowDialog();
    }

    // تسجيل مريض جديد - نفس الشاشة المستخدَمة في تطبيق الاستقبال (DentalClinic.Features.AddPatientWindow)
    private void RegisterPatientButton_Click(object sender, RoutedEventArgs e)
    {
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

    // تحصيل دفعة من المريض المحدَّد في قائمة الانتظار - نفس منطق تطبيق الاستقبال بالضبط
    private void CollectPaymentButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentUser.HasPermission(UserPermission.CollectPayments))
        {
            MessageBox.Show(LocalizationManager.T("Main_NoPermissionCollectPayment"), LocalizationManager.T("Common_AccessDenied"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (QueueGrid.SelectedItem is not QueueRowViewModel selected)
        {
            MessageBox.Show(LocalizationManager.T("Main_SelectPatientFromQueueFirst"), LocalizationManager.T("Common_Notice"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var sessionId = _paymentRepo.GetLatestUnpaidSessionId(selected.PatientID);

            if (sessionId.HasValue)
            {
                var paymentWindow = new CollectPaymentWindow(sessionId.Value, selected.PatientFullName, _currentUser)
                {
                    Owner = this
                };
                paymentWindow.ShowDialog();
                LoadQueue();
            }
            else
            {
                MessageBox.Show(LocalizationManager.T("Main_NoUnpaidSessionFormat", selected.PatientFullName),
                    LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Main_ErrorCheckingPaymentFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 🩺 فتح نافذة إدارة العلاجات والأسعار
    private void ManageTreatmentsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentUser.HasPermission(UserPermission.ManageTreatments))
        {
            return;
        }

        var window = new TreatmentManagementWindow { Owner = this };
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

    private void ManageUsersButton_Click(object sender, RoutedEventArgs e)
    {
        // فحص دفاعي إضافي: حتى لو ظهر الزر بطريقة ما، لن تُفتح الشاشة إلا لمن يملك صلاحية ManageUsers فعلاً
        if (!_currentUser.HasPermission(UserPermission.ManageUsers))
        {
            return;
        }

        var window = new UserManagementWindow(_currentUser) { Owner = this };
        window.ShowDialog();
    }

    // يفتح تطبيق المرمم كعملية Windows منفصلة تماماً (تسجيل دخول عادي داخله، لا تمرير جلسة -
    // قرار معماري صريح من بداية تصميم تطبيق المرمم لتفادي أي تعقيد أمني في تمرير بيانات الدخول
    // بين عمليتين). المسار يعتمد على تخطيط التثبيت الفعلي عبر Inno Setup حيث DoctorApp وProsthetistApp
    // مجلدان شقيقان تحت نفس مجلد التثبيت ({app}\DoctorApp و{app}\ProsthetistApp) - لذلك لن يعمل هذا
    // الزر أثناء التطوير المحلي (Visual Studio/dotnet run، حيث لكل مشروع مجلد bin\ منفصل تماماً)
    // إلا بعد نشر (Publish) كلا التطبيقين بنفس تخطيط المُثبِّت، أو بتشغيل .exe المنشوريَن يدوياً
    // بجانب بعضهما بنفس الاسمين. رسالة خطأ واضحة تُشرح ذلك بدل فشل صامت أو استثناء غير مفهوم.
    private void OpenProsthetistAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentUser.IsMainDoctor || !_currentUser.HasPermission(UserPermission.ManageUsers))
        {
            return;
        }

        try
        {
            var doctorAppDirectory = AppContext.BaseDirectory;
            var prosthetistExePath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(doctorAppDirectory, "..", "ProsthetistApp", "ProsthetistApp.exe"));

            if (!System.IO.File.Exists(prosthetistExePath))
            {
                MessageBox.Show(
                    LocalizationManager.T("Main_ProsthetistAppNotFoundFormat", prosthetistExePath),
                    LocalizationManager.T("Common_Notice"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(prosthetistExePath)
            {
                UseShellExecute = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(prosthetistExePath)
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                LocalizationManager.T("Main_ProsthetistAppLaunchErrorFormat", ex.Message),
                LocalizationManager.T("Common_Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // Patch 13: فحص دفاعي إضافي (طبقة ثانية فوق إخفاء الزر) - حتى لو ظهر الزر بطريقة ما، لن تُفتح
    // النافذة إلا لمن هو IsMainDoctor فعلاً؛ والنافذة نفسها تكرر هذا الفحص مجدداً عند الإنشاء
    // (طبقة ثالثة)، وكل عملية كتابة داخلها تتحقق بذاتها في ProstheticLookupRepository (طبقة رابعة)
    private void OpenLookupManagementButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentUser.IsMainDoctor)
        {
            return;
        }

        var window = new ProstheticLookupManagementWindow(_currentUser) { Owner = this };
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

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadQueue();

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

        // إيقاف المؤقت التلقائي فوراً لضمان عدم استهلاك أي موارد أو استعلامات في الخلفية أثناء تبديل الشاشات
        _refreshTimer.Stop();

        // هذه النافذة مسجَّلة كـ Application.MainWindow تحت ShutdownMode.OnMainWindowClose؛ لو أُغلقت الآن
        // مباشرة سيُنهي ذلك التطبيق بالكامل بالخطأ. نمنع الإغلاق التلقائي مؤقتاً حتى نُثبّت نافذة رئيسية بديلة.
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // نُخفي هذه النافذة فوراً بدل تركها ظاهرة خلف شاشة الدخول أثناء إدخال بيانات المستخدم الجديد
        Hide();

        var loginWindow = new LoginWindow();
        var loginResult = loginWindow.ShowDialog();

        if (loginResult == true && loginWindow.LoggedInUser != null)
        {
            var newMainWindow = new MainWindow(loginWindow.LoggedInUser);
            Application.Current.MainWindow = newMainWindow;
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            newMainWindow.Show();

            Close(); // إغلاق آمن الآن بعد وجود نافذة رئيسية بديلة تحمل راية التطبيق
        }
        else
        {
            // لم يسجّل أحد دخولاً جديداً: أنهِ التطبيق بالكامل بدل ترك نافذة يتيمة أو عملية معلَّقة في الخلفية
            Application.Current.Shutdown();
        }
    }
}
