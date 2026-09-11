using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class UserEditWindow : Window
{
    private readonly UserRepository _userRepo;
    private readonly NurseDoctorAccessRepository _accessRepo;
    private readonly PermissionRepository _permissionRepo;
    private readonly UserAccount? _existingUser; // null = وضع الإضافة، غير null = وضع التعديل
    private readonly UserAccount _loggedInUser;   // من فتح شاشة إدارة المستخدمين حالياً (للفحوصات الأمنية)

    // من يستطيع تعيين/تغيير "الطبيب الرئيسي"؟ إما الطبيب الرئيسي الحالي نفسه (تسليم الرئاسة)، أو
    // أي شخص يملك ManageUsers إن لم يوجد أي طبيب رئيسي معيَّن بعد إطلاقاً (بوتستراب التعيين الأول -
    // راجع UserRepository.AnyMainDoctorExists). خلاف ذلك، تظهر الخانة معطَّلة (قراءة فقط) مع تلميح واضح.
    private readonly bool _canEditMainDoctor;

    private class DoctorAccessOption
    {
        public int UserID { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public bool IsChecked { get; set; }
    }

    // صف واحد من صلاحيات المرمم الديناميكية - مبني من PermissionRepository.GetDefinitionsByCategory،
    // وليس من قائمة مكتوبة في الكود، حتى تنعكس أي صلاحية تُضاف مستقبلاً للقاعدة تلقائياً هنا
    private class ProsthPermissionOption
    {
        public string PermissionKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsChecked { get; set; }
    }

    public UserEditWindow(UserAccount? existingUser, UserAccount loggedInUser)
    {
        _existingUser = existingUser;
        _loggedInUser = loggedInUser;
        InitializeComponent();

        // نفس إصلاح PatientFileWindow: لا تسمح للنافذة أن تتجاوز الشاشات الصغيرة
        var maxAvailableHeight = SystemParameters.WorkArea.Height - 20;
        if (Height > maxAvailableHeight) Height = maxAvailableHeight;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _userRepo = new UserRepository(db);
        _accessRepo = new NurseDoctorAccessRepository(db);
        _permissionRepo = new PermissionRepository(db);

        _canEditMainDoctor = _loggedInUser.IsMainDoctor || !_userRepo.AnyMainDoctorExists();

        LoadDoctorAccessOptions();
        LoadProstheticPermissionOptions();

        if (_existingUser == null)
        {
            // وضع الإضافة: لا داعي لخيار تفعيل/تعطيل الحساب، فهو نشط دائماً عند الإنشاء
            ActivePanel.Visibility = Visibility.Collapsed;
            TitleText.Text = LocalizationManager.T("UserEdit_TitleAdd");
            DoctorAccessSection.Visibility = Visibility.Collapsed; // "Doctor" هو الاختيار الافتراضي عند الإضافة
            ProstheticPermissionsSection.Visibility = Visibility.Collapsed;

            // "Doctor" هو الاختيار الافتراضي عند الإضافة، فقسم الطبيب الرئيسي يظهر مباشرة (غير محدَّد افتراضياً)
            MainDoctorSection.Visibility = Visibility.Visible;
            IsMainDoctorCheck.IsEnabled = _canEditMainDoctor;
            MainDoctorReadOnlyHintText.Visibility = _canEditMainDoctor ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            TitleText.Text = LocalizationManager.T("UserEdit_TitleEdit");
            PasswordLabel.Text = LocalizationManager.T("UserEdit_PasswordKeepCurrent");

            FullNameBox.Text = _existingUser.FullName;
            UsernameBox.Text = _existingUser.Username;
            PhoneBox.Text = _existingUser.PhoneNumber ?? string.Empty;
            IsActiveCheck.IsChecked = _existingUser.IsActive;

            PermManageUsersCheck.IsChecked  = _existingUser.HasPermission(UserPermission.ManageUsers);
            PermFinanceCheck.IsChecked      = _existingUser.HasPermission(UserPermission.AccessFinance);
            PermPatientFileCheck.IsChecked  = _existingUser.HasPermission(UserPermission.OpenPatientFile);
            PermBackupCheck.IsChecked       = _existingUser.HasPermission(UserPermission.AccessBackup);
            PermTreatmentsCheck.IsChecked   = _existingUser.HasPermission(UserPermission.ManageTreatments);
            PermPaymentsCheck.IsChecked     = _existingUser.HasPermission(UserPermission.CollectPayments);
            PermRegisterPatientsCheck.IsChecked = _existingUser.HasPermission(UserPermission.RegisterPatients);
            PermEditPatientsCheck.IsChecked = _existingUser.HasPermission(UserPermission.EditPatients);
            PermDeletePatientsCheck.IsChecked = _existingUser.HasPermission(UserPermission.DeletePatients);

            DoctorAccessSection.Visibility = _existingUser.Role == UserRole.Nurse ? Visibility.Visible : Visibility.Collapsed;
            BitmaskPermissionsSection.Visibility = _existingUser.Role == UserRole.Prosthetist ? Visibility.Collapsed : Visibility.Visible;
            ProstheticPermissionsSection.Visibility = _existingUser.Role == UserRole.Prosthetist ? Visibility.Visible : Visibility.Collapsed;

            MainDoctorSection.Visibility = _existingUser.Role == UserRole.Doctor ? Visibility.Visible : Visibility.Collapsed;
            IsMainDoctorCheck.IsChecked = _existingUser.IsMainDoctor;
            IsMainDoctorCheck.IsEnabled = _canEditMainDoctor;
            MainDoctorReadOnlyHintText.Visibility = _canEditMainDoctor ? Visibility.Collapsed : Visibility.Visible;

            // نطابق عبر Tag (القيمة الثابتة Doctor/Nurse/Prosthetist) بدل Content (النص المترجَم المعروض)
            // هذا يصحح خللاً سابقاً كان يمنع تحديد الدور الصحيح تلقائياً عند فتح شاشة التعديل
            var targetRoleTag = _existingUser.Role switch
            {
                UserRole.Doctor => "Doctor",
                UserRole.Nurse => "Nurse",
                UserRole.Prosthetist => "Prosthetist",
                _ => "Doctor"
            };
            foreach (ComboBoxItem item in RoleBox.Items)
            {
                if ((string?)item.Tag == targetRoleTag)
                {
                    item.IsSelected = true;
                }
            }
        }
    }

    // يبني قائمة صلاحيات الترميم الـ21 ديناميكياً من جدول Permissions (فئة Prosthetics) - وليست
    // مكتوبة يدوياً هنا، فتنعكس أي صلاحية تُضاف/تُحذف من القاعدة مستقبلاً بدون أي تعديل كود.
    // DisplayName يُترجَم عبر مفتاح "ProsthPerm_" + اسم الصلاحية بدون البادئة (Prosthetics.)، مع
    // العودة لـ DisplayName الإنجليزي المخزَّن في القاعدة كـ fallback إن غاب مفتاح الترجمة (LocalizationManager.T
    // تُرجع المفتاح نفسه عند الغياب - نفحص ذلك ونستخدم DisplayName بدلاً منه في هذه الحالة فقط).
    private void LoadProstheticPermissionOptions()
    {
        try
        {
            var grantedKeys = _existingUser != null
                ? _permissionRepo.GetGrantedKeys(_existingUser.UserID)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var definitions = _permissionRepo.GetDefinitionsByCategory("Prosthetics");

            var options = definitions.Select(def =>
            {
                var localizationKey = "ProsthPerm_" + def.PermissionKey.Substring("Prosthetics.".Length);
                var translated = LocalizationManager.T(localizationKey);
                var displayName = translated == localizationKey ? def.DisplayName : translated; // fallback عند غياب الترجمة

                return new ProsthPermissionOption
                {
                    PermissionKey = def.PermissionKey,
                    DisplayName = displayName,
                    IsChecked = grantedKeys.Contains(def.PermissionKey)
                };
            }).ToList();

            ProstheticPermissionsList.ItemsSource = options;
            NoProsthPermsHintText.Visibility = options.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            // فشل نادر بالاتصال وقت فتح النافذة - القائمة تبقى فارغة، والحفظ الفعلي سيُظهر الخطأ الحقيقي لاحقاً إن استمر
        }
    }

    // يبني قائمة كل الأطباء النشطين مع علامة "محدَّد" لمن سبق ومُنحت هذه الممرضة صلاحية عليه
    // (فارغة بالكامل لمستخدم جديد - الوضع الافتراضي دائماً "بلا أي صلاحية" إلى أن يُمنح صراحةً)
    private void LoadDoctorAccessOptions()
    {
        try
        {
            var allowedIds = _existingUser != null
                ? _accessRepo.GetAllowedDoctorIds(_existingUser.UserID)
                : new List<int>();

            var options = _userRepo.GetAllDoctors()
                .Select(d => new DoctorAccessOption
                {
                    UserID = d.UserID,
                    DisplayName = d.FullName,
                    IsChecked = allowedIds.Contains(d.UserID)
                })
                .ToList();

            DoctorAccessList.ItemsSource = options;
            NoDoctorsHintText.Visibility = options.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            // فشل نادر بالاتصال وقت فتح النافذة - القائمة تبقى فارغة، والحفظ الفعلي سيُظهر الخطأ الحقيقي لاحقاً إن استمر
        }
    }

    // يُظهر/يُخفي الأقسام المرتبطة بالدور فوراً عند تبديل الدور، حتى قبل الحفظ
    private void RoleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DoctorAccessSection == null || ProstheticPermissionsSection == null || BitmaskPermissionsSection == null || MainDoctorSection == null)
            return; // يُستدعى مرة أولى أثناء InitializeComponent قبل تجهيز بقية العناصر

        var roleTag = (RoleBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        DoctorAccessSection.Visibility = roleTag == "Nurse" ? Visibility.Visible : Visibility.Collapsed;
        ProstheticPermissionsSection.Visibility = roleTag == "Prosthetist" ? Visibility.Visible : Visibility.Collapsed;
        BitmaskPermissionsSection.Visibility = roleTag == "Prosthetist" ? Visibility.Collapsed : Visibility.Visible;
        MainDoctorSection.Visibility = roleTag == "Doctor" ? Visibility.Visible : Visibility.Collapsed;
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

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // مزامنة مستمرة بين PasswordBox (المخفي) و TextBox (الظاهر) بغضّ النظر عن أيهما مرئي حالياً،
    // حتى تبقى PasswordBox.Password دائماً المصدر الصحيح المستخدَم عند الحفظ دون أي تعديل آخر
    private bool _syncingPassword;

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPassword) return;
        _syncingPassword = true;
        PasswordVisibleBox.Text = PasswordBox.Password;
        _syncingPassword = false;
    }

    private void PasswordVisibleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingPassword) return;
        _syncingPassword = true;
        PasswordBox.Password = PasswordVisibleBox.Text;
        _syncingPassword = false;
    }

    private void ShowPasswordToggle_Checked(object sender, RoutedEventArgs e)
    {
        PasswordVisibleBox.Visibility = Visibility.Visible;
        PasswordBox.Visibility = Visibility.Collapsed;
        ShowPasswordToggle.Content = "🙈";
        PasswordVisibleBox.Focus();
        PasswordVisibleBox.CaretIndex = PasswordVisibleBox.Text.Length;
    }

    private void ShowPasswordToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        PasswordBox.Visibility = Visibility.Visible;
        PasswordVisibleBox.Visibility = Visibility.Collapsed;
        ShowPasswordToggle.Content = "👁";
        PasswordBox.Focus();
    }

    // تجميع كل الصلاحيات المختارة في الواجهة إلى قيمة واحدة من النوع UserPermission
    private UserPermission CollectSelectedPermissions()
    {
        var permissions = UserPermission.None;
        if (PermManageUsersCheck.IsChecked == true) permissions |= UserPermission.ManageUsers;
        if (PermFinanceCheck.IsChecked == true)     permissions |= UserPermission.AccessFinance;
        if (PermPatientFileCheck.IsChecked == true) permissions |= UserPermission.OpenPatientFile;
        if (PermBackupCheck.IsChecked == true)      permissions |= UserPermission.AccessBackup;
        if (PermTreatmentsCheck.IsChecked == true)  permissions |= UserPermission.ManageTreatments;
        if (PermPaymentsCheck.IsChecked == true)    permissions |= UserPermission.CollectPayments;
        if (PermRegisterPatientsCheck.IsChecked == true) permissions |= UserPermission.RegisterPatients;
        if (PermEditPatientsCheck.IsChecked == true) permissions |= UserPermission.EditPatients;
        if (PermDeletePatientsCheck.IsChecked == true) permissions |= UserPermission.DeletePatients;
        return permissions;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var fullName = FullNameBox.Text.Trim();
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(username))
        {
            ErrorText.Text = LocalizationManager.T("UserEdit_FullNameUsernameRequired");
            return;
        }

        if (_existingUser == null && string.IsNullOrWhiteSpace(password))
        {
            ErrorText.Text = LocalizationManager.T("UserEdit_PasswordRequiredForNew");
            return;
        }

        // نقرأ Tag (القيمة الثابتة Doctor/Nurse/Prosthetist) بدل Content (النص المترجَم) حتى يعمل
        // الاختيار بشكل صحيح بغض النظر عن لغة الواجهة الحالية
        var role = (RoleBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
        {
            "Nurse" => UserRole.Nurse,
            "Prosthetist" => UserRole.Prosthetist,
            _ => UserRole.Doctor
        };

        // المرمم له نظام صلاحيات منفصل تماماً (UserPermissions الدقيقة) - لا معنى لأي صلاحية Bitmask
        // له (تُحفَظ دائماً None)، حتى لو كانت خانات الاختيار القديمة لا تزال محتوية على قيم سابقة
        // من تبديل الدور جيئة وذهاباً داخل نفس فتح النافذة
        var selectedPermissions = role == UserRole.Prosthetist ? UserPermission.None : CollectSelectedPermissions();

        // فحص أمان 1: منع المستخدم من إزالة صلاحية إدارة المستخدمين عن حسابه الخاص أثناء استخدامه،
        // وإلا قد يُقفل بالخطأ من شاشة الإدارة بلا رجعة.
        if (_existingUser != null
            && _existingUser.UserID == _loggedInUser.UserID
            && _existingUser.HasPermission(UserPermission.ManageUsers)
            && !selectedPermissions.HasFlag(UserPermission.ManageUsers))
        {
            ErrorText.Text = LocalizationManager.T("UserEdit_CannotRemoveOwnManageUsers");
            return;
        }

        // فحص أمان 2: منع إزالة آخر صلاحية "مدير عام" متبقية في كامل النظام (حتى لو لم تكن هي حسابك الخاص)
        if (_existingUser != null
            && _existingUser.HasPermission(UserPermission.ManageUsers)
            && !selectedPermissions.HasFlag(UserPermission.ManageUsers))
        {
            var remainingAdmins = _userRepo.CountActiveSuperAdmins(excludeUserId: _existingUser.UserID);
            if (remainingAdmins == 0)
            {
                ErrorText.Text = LocalizationManager.T("UserEdit_CannotRemoveLastAdmin");
                return;
            }
        }

        // فحص أمان 3 (Patch 6.2): لا يمكن إلغاء صفة "الطبيب الرئيسي" عن هذا الحساب دون تعيين بديل -
        // القرار المُتَّفق عليه هو "طبيب رئيسي واحد دائماً"، فلا نسمح بالعودة لصفر عبر تعطيل الخانة
        // فقط. التسليم الصحيح الوحيد هو تحديد طبيب آخر كرئيسي مباشرة (يُلغي هذا تلقائياً ضمن نفس
        // Transaction - راجع UserRepository.SetAsMainDoctor/ClearMainDoctor لنفس القيد على مستوى البيانات.
        if (role == UserRole.Doctor
            && _existingUser?.IsMainDoctor == true
            && IsMainDoctorCheck.IsChecked != true)
        {
            ErrorText.Text = LocalizationManager.T("UserEdit_CannotClearMainDoctor");
            return;
        }

        try
        {
            var excludeId = _existingUser?.UserID;
            if (_userRepo.UsernameExists(username, excludeId))
            {
                ErrorText.Text = LocalizationManager.T("UserEdit_UsernameTaken");
                return;
            }

            int savedUserId;

            if (_existingUser == null)
            {
                // إضافة مستخدم جديد
                var newUser = new UserAccount
                {
                    FullName = fullName,
                    Username = username,
                    Role = role,
                    Permissions = selectedPermissions,
                    PhoneNumber = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim()
                };

                savedUserId = _userRepo.AddUser(newUser, password);
            }
            else
            {
                // تعديل مستخدم موجود
                var updatedUser = new UserAccount
                {
                    UserID = _existingUser.UserID,
                    FullName = fullName,
                    Username = username,
                    Role = role,
                    Permissions = selectedPermissions,
                    IsActive = IsActiveCheck.IsChecked == true,
                    PhoneNumber = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim()
                };

                _userRepo.UpdateUser(updatedUser);
                savedUserId = _existingUser.UserID;

                // كلمة المرور تُغيَّر فقط إن كُتبت فعلاً - تركها فارغة يعني الإبقاء على القديمة
                if (!string.IsNullOrWhiteSpace(password))
                {
                    _userRepo.UpdatePassword(_existingUser.UserID, password);
                }
            }

            // قوائم الأطباء المسموح لهذه الممرضة رؤيتها - تُحفَظ فقط إن كان الدور النهائي "ممرضة"
            // (لو حُوِّل الحساب من ممرضة إلى طبيب، نترك أي صلاحيات قديمة كما هي بلا أثر عملي، فهي غير مقروءة لطبيب أصلاً)
            if (role == UserRole.Nurse && DoctorAccessList.ItemsSource is IEnumerable<DoctorAccessOption> doctorOptions)
            {
                var grantedDoctorIds = doctorOptions.Where(o => o.IsChecked).Select(o => o.UserID);
                _accessRepo.SetAllowedDoctorIds(savedUserId, grantedDoctorIds);
            }

            // صلاحيات المرمم الدقيقة - تُحفَظ فقط إن كان الدور النهائي "مرمم" (نفس منطق قوائم
            // الأطباء أعلاه: لو حُوِّل الحساب لاحقاً لدور آخر، تبقى المنح القديمة في UserPermissions
            // بلا أثر عملي، فهي غير مقروءة إلا لتطبيق المرمم تحديداً)
            if (role == UserRole.Prosthetist && ProstheticPermissionsList.ItemsSource is IEnumerable<ProsthPermissionOption> prosthOptions)
            {
                var grantedKeys = prosthOptions.Where(o => o.IsChecked).Select(o => o.PermissionKey);
                _permissionRepo.ReplaceUserPermissions(savedUserId, grantedKeys, _loggedInUser);
            }

            // "الطبيب الرئيسي" - تُنفَّذ فقط لدور Doctor النهائي، وفقط لمن يملك الحق فعلياً
            // (فحص مستقل هنا في الكود الخلفي، وليس فقط تعطيل الخانة في الواجهة - نفس فلسفة الأمان
            // المُتَّبعة في كل شاشات المرمم السابقة). لا يُنفَّذ أي استدعاء إن لم تتغيَّر القيمة فعلياً.
            if (role == UserRole.Doctor && _canEditMainDoctor)
            {
                var isCheckedNow = IsMainDoctorCheck.IsChecked == true;
                var wasMainDoctorBefore = _existingUser?.IsMainDoctor == true;

                if (isCheckedNow && !wasMainDoctorBefore)
                {
                    _userRepo.SetAsMainDoctor(savedUserId, _loggedInUser); // يُلغي أي طبيب رئيسي سابق ذرّياً ضمن Transaction
                }
                // ملاحظة (Patch 6.2): لا يوجد مسار لإلغاء الصفة بلا بديل من هنا بعد الآن - يُمنع صراحة
                // في فحص الأمان 3 أعلاه قبل الحفظ، وأيضاً على مستوى UserRepository.ClearMainDoctor نفسها
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("UserEdit_SaveErrorFormat", ex.Message);
        }
    }
}
