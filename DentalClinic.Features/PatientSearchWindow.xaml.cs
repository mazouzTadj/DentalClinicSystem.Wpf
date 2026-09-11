using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class PatientSearchWindow : Window
{
    private readonly UserAccount _currentUser;
    public ObservableCollection<PatientSearchRowViewModel> Results { get; } = new();

    // null = بلا أي تقييد رؤية (الطبيب الرئيسي بوضعه الافتراضي) - غير null = القائمة المسموحة + الغير مُسنَدين دائماً
    // (راجع PatientRepository.Search للتفاصيل الكاملة)
    private List<int>? _allowedDoctorUserIds;
    private bool _includeUnassigned = true;
    private readonly bool _isPrimaryDoctor;

    private class DoctorFilterOption
    {
        public int? UserID { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }

    public PatientSearchWindow(UserAccount currentUser)
    {
        _currentUser = currentUser;
        InitializeComponent();
        ResultsGrid.ItemsSource = Results;

        // عمود فتح الملف الطبي بأكمله يُخفى لمن لا يملك صلاحية OpenPatientFile
        OpenFileColumn.Visibility = _currentUser.HasPermission(UserPermission.OpenPatientFile)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // عمود حذف المريض نهائياً بأكمله يُخفى لمن لا يملك صلاحية DeletePatients
        DeleteColumn.Visibility = _currentUser.HasPermission(UserPermission.DeletePatients)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // عمود تعديل بيانات المريض بأكمله يُخفى لمن لا يملك صلاحية EditPatients
        EditColumn.Visibility = _currentUser.HasPermission(UserPermission.EditPatients)
            ? Visibility.Visible
            : Visibility.Collapsed;

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var userRepo = new UserRepository(db);
            var commissionService = new DoctorCommissionService(db);

            if (_currentUser.Role == UserRole.Doctor)
            {
                var primaryDoctorId = commissionService.GetPrimaryDoctorUserId();
                _isPrimaryDoctor = primaryDoctorId.HasValue && primaryDoctorId.Value == _currentUser.UserID;

                if (_isPrimaryDoctor)
                {
                    // بلا أي تقييد افتراضياً + إظهار فلتر إضافي خاص فيه لتضييق النتائج على طبيب معيَّن عند الحاجة
                    _allowedDoctorUserIds = null;
                    _includeUnassigned = true;

                    var options = new List<DoctorFilterOption> { new() { UserID = null, DisplayName = LocalizationManager.T("Search_DoctorFilterAll") } };
                    options.AddRange(userRepo.GetAllDoctors().Select(d => new DoctorFilterOption { UserID = d.UserID, DisplayName = d.FullName }));

                    DoctorFilterBox.ItemsSource = options;
                    DoctorFilterBox.SelectedIndex = 0;
                    DoctorFilterRow.Visibility = Visibility.Visible;
                }
                else
                {
                    // طبيب ثانوي: مرضاه فقط + الغير مُسنَدين لأي طبيب بعد
                    _allowedDoctorUserIds = new List<int> { _currentUser.UserID };
                    _includeUnassigned = true;
                }
            }
            else
            {
                // ممرضة: فقط الأطباء اللي منحها الطبيب الرئيسي صلاحية رؤية قوائمهم + الغير مُسنَدين دائماً
                var accessRepo = new NurseDoctorAccessRepository(db);
                _allowedDoctorUserIds = accessRepo.GetAllowedDoctorIds(_currentUser.UserID);
                _includeUnassigned = true;
            }
        }
        catch
        {
            // فشل الاتصال هنا نادر جداً (لو حصل، سيظهر بوضوح لاحقاً عند الضغط "بحث" نفسها) - نطبّق
            // أشد تقييد ممكن احتياطاً بدل ترك النافذة بلا أي تقييد بالخطأ
            _allowedDoctorUserIds = new List<int>();
            _includeUnassigned = true;
        }
    }

    private void OpenPatientFileButton_Click(object sender, RoutedEventArgs e)
    {
        // فحص دفاعي إضافي: حتى لو ظهر الزر بطريقة غير متوقعة، لن يُفتح الملف إلا لمن يملك الصلاحية فعلاً
        if (!_currentUser.HasPermission(UserPermission.OpenPatientFile))
        {
            MessageBox.Show(LocalizationManager.T("Main_NoPermissionOpenFile"), LocalizationManager.T("Common_AccessDenied"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (sender is not Button { Tag: PatientSearchRowViewModel selectedRow }) return;

        // نفس شاشة الملف الطبي المستخدَمة في تطبيق الطبيب (PatientFileWindow) - الآن كلاهما في نفس المشروع المشترك DentalClinic.Features
        var window = new PatientFileWindow(selectedRow.PatientID, null, _currentUser)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SearchButton_Click(sender, e);
    }

    // بمجرد اختيار طبيب معيَّن من الفلتر، تظهر مرضاه فوراً بلا حاجة للضغط على "بحث" يدوياً.
    // نتجاهل عمداً اختيار "كل الأطباء" هنا (بما فيه الاختيار الافتراضي عند فتح النافذة أول مرة)
    // حتى لا تظهر رسالة "أدخل اسماً أو اختر فلتر" فوراً عند فتح الشاشة بلا أي تفاعل من المستخدم
    private void DoctorFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DoctorFilterBox.SelectedItem is DoctorFilterOption { UserID: not null })
        {
            SearchButton_Click(sender, e);
        }
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Foreground = (Brush)FindResource("ErrorBrush");
        StatusText.Text = string.Empty;

        var term = SearchBox.Text.Trim();

        // نقرأ Tag (قيمة ثابتة: Any/Male/Female، All/Unpaid/Paid) بدل Content (النص المترجَم المعروض)
        // حتى تعمل المقارنة بشكل صحيح بغض النظر عن لغة الواجهة الحالية
        string? genderTag = (GenderFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        string? gender = genderTag == "Male" ? "Male" : genderTag == "Female" ? "Female" : null;

        string? paymentFilter = (PaymentFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        // فلتر الطبيب (الرئيسي فقط) يُعتبر معياراً كافياً وحده للبحث، مثله مثل الجنس أو حالة الدفع
        var doctorFilterSelected = _isPrimaryDoctor && DoctorFilterBox.SelectedItem is DoctorFilterOption { UserID: not null };

        // السماح بالبحث إذا تم إدخال نص، أو اختيار جنس، أو فلتر حالة دفع معين، أو فلتر طبيب معيَّن
        if (string.IsNullOrWhiteSpace(term) && gender == null && (paymentFilter == null || paymentFilter == "All") && !doctorFilterSelected)
        {
            StatusText.Text = LocalizationManager.T("Search_EnterTermOrFilter");
            return;
        }

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var patientRepo = new PatientRepository(db);
            var paymentRepo = new PaymentRepository(db);

            // للطبيب الرئيسي فقط: لو اختار طبيباً معيَّناً من الفلتر الإضافي، نضيّق النتائج عليه حصراً
            // (بلا الغير مُسنَدين - هذا فلتر استعراضي دقيق، وليس قاعدة رؤية عامة)
            List<int>? effectiveAllowedIds = _allowedDoctorUserIds;
            bool effectiveIncludeUnassigned = _includeUnassigned;

            if (_isPrimaryDoctor && DoctorFilterBox.SelectedItem is DoctorFilterOption { UserID: not null } selectedFilter)
            {
                effectiveAllowedIds = new List<int> { selectedFilter.UserID!.Value };
                effectiveIncludeUnassigned = false;
            }

            // نمرر null للعمر الأدنى والأقصى
            var matches = patientRepo.Search(term, gender, null, null, effectiveAllowedIds, effectiveIncludeUnassigned);

            // جلب قائمة المعرفات للمرضى الذين يملكون ديوناً غير مسددة (عبر PaymentRepository الموحَّد الآن)
            var unpaidPatientIds = paymentRepo.GetUnpaidPatientIds(matches.Select(p => p.PatientID));

            Results.Clear();
            foreach (var p in matches)
            {
                bool owesMoney = unpaidPatientIds.Contains(p.PatientID);

                // 💳 تصفية النتائج بناءً على الخيار المحدد في فلتر الدفع
                if (paymentFilter == "Unpaid" && !owesMoney)
                    continue;
                if (paymentFilter == "Paid" && owesMoney)
                    continue;

                Results.Add(new PatientSearchRowViewModel(p, owesMoney));
            }

            if (Results.Count == 0)
            {
                StatusText.Text = LocalizationManager.T("Search_NoMatchFound");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationManager.T("Search_ErrorWhileSearchingFormat", ex.Message);
        }
    }

    private void CollectPaymentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PatientSearchRowViewModel selectedRow }) return;

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var paymentRepo = new PaymentRepository(db);

            var sessionId = paymentRepo.GetLatestUnpaidSessionId(selectedRow.PatientID);

            if (sessionId.HasValue)
            {
                var paymentWindow = new CollectPaymentWindow(sessionId.Value, selectedRow.FullName, _currentUser)
                {
                    Owner = this
                };

                if (paymentWindow.ShowDialog() == true)
                {
                    var unpaidIds = paymentRepo.GetUnpaidPatientIds(new[] { selectedRow.PatientID });
                    selectedRow.HasUnpaidBalance = unpaidIds.Contains(selectedRow.PatientID);
                }
            }
            else
            {
                MessageBox.Show(LocalizationManager.T("Main_NoUnpaidSessionFormat", selectedRow.FullName), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Main_ErrorCheckingPaymentFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddToQueueButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = string.Empty;

        if (ResultsGrid.SelectedItem is not PatientSearchRowViewModel selected)
        {
            StatusText.Foreground = (Brush)FindResource("ErrorBrush");
            StatusText.Text = LocalizationManager.T("Main_SelectPatientFirst");
            return;
        }

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var queueRepo = new QueueRepository(db);

            var (success, message, _) = queueRepo.AddToQueue(selected.PatientID, _currentUser.UserID);
            StatusText.Foreground = success ? new SolidColorBrush(Color.FromRgb(0x22, 0xA0, 0x6B)) : (Brush)FindResource("ErrorBrush");
            StatusText.Text = message;

            if (success)
            {
                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            StatusText.Foreground = (Brush)FindResource("ErrorBrush");
            StatusText.Text = LocalizationManager.T("Main_ErrorPrefixFormat", ex.Message);
        }
    }

    // تعديل البيانات الأساسية لمريض موجود (تصحيح خطأ إدخال) - محمي بصلاحية EditPatients
    private void EditPatientButton_Click(object sender, RoutedEventArgs e)
    {
        // فحص دفاعي إضافي: حتى لو ظهر الزر بطريقة غير متوقعة، لن تُفتح شاشة التعديل إلا لمن يملك الصلاحية فعلاً
        if (!_currentUser.HasPermission(UserPermission.EditPatients))
        {
            MessageBox.Show(LocalizationManager.T("Main_NoPermissionEditPatient"), LocalizationManager.T("Common_AccessDenied"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (sender is not Button { Tag: PatientSearchRowViewModel selectedRow }) return;

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var patientRepo = new PatientRepository(db);

            var patient = patientRepo.GetById(selectedRow.PatientID, _allowedDoctorUserIds, _includeUnassigned);
            if (patient == null) return; // نادراً: حُذف المريض من مكان آخر، أو لم يعد ظاهراً له (تغيّر إسناد الطبيب)

            var window = new AddPatientWindow(_currentUser, patient) { Owner = this };
            if (window.ShowDialog() == true)
            {
                SearchButton_Click(sender, e); // إعادة تنفيذ نفس البحث الحالي لتحديث الاسم/الهاتف المعروضين في الجدول فوراً
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
        if (sender is not Button { Tag: PatientSearchRowViewModel selectedRow }) return;

        // فحص دفاعي إضافي: حتى لو ظهر الزر بطريقة غير متوقعة، لن يُنفَّذ الحذف إلا لمن يملك الصلاحية فعلاً
        if (!_currentUser.HasPermission(UserPermission.DeletePatients))
        {
            MessageBox.Show(LocalizationManager.T("Main_NoPermissionDeletePatient"), LocalizationManager.T("Common_AccessDenied"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            LocalizationManager.T("Main_ConfirmDeletePatientMessageFormat", selectedRow.FullName),
            LocalizationManager.T("Main_ConfirmDeletePatientTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var patientRepo = new PatientRepository(db);

            patientRepo.PermanentlyDelete(selectedRow.PatientID);
            Results.Remove(selectedRow);

            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xA0, 0x6B));
            StatusText.Text = LocalizationManager.T("Main_PatientDeletedFormat", selectedRow.FullName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Main_CouldNotDeletePatientFormat", ex.Message),
                LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}