using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;

namespace DentalClinic.Features;

public partial class UserEditWindow : Window
{
    private readonly UserRepository _userRepo;
    private readonly UserAccount? _existingUser; // null = وضع الإضافة، غير null = وضع التعديل
    private readonly UserAccount _loggedInUser;   // من فتح شاشة إدارة المستخدمين حالياً (للفحوصات الأمنية)

    public UserEditWindow(UserAccount? existingUser, UserAccount loggedInUser)
    {
        _existingUser = existingUser;
        _loggedInUser = loggedInUser;
        InitializeComponent();

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _userRepo = new UserRepository(db);

        if (_existingUser == null)
        {
            // وضع الإضافة: لا داعي لخيار تفعيل/تعطيل الحساب، فهو نشط دائماً عند الإنشاء
            ActivePanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            TitleText.Text = "Edit User";
            PasswordLabel.Text = "New Password (leave blank to keep current)";

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

            foreach (ComboBoxItem item in RoleBox.Items)
            {
                if ((string)item.Content == (_existingUser.Role == UserRole.Doctor ? "Doctor" : "Nurse"))
                {
                    item.IsSelected = true;
                }
            }
        }
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
            ErrorText.Text = "Full name and username are required";
            return;
        }

        if (_existingUser == null && string.IsNullOrWhiteSpace(password))
        {
            ErrorText.Text = "Password is required for a new account";
            return;
        }

        var role = (RoleBox.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Nurse"
            ? UserRole.Nurse
            : UserRole.Doctor;

        var selectedPermissions = CollectSelectedPermissions();

        // فحص أمان 1: منع المستخدم من إزالة صلاحية إدارة المستخدمين عن حسابه الخاص أثناء استخدامه،
        // وإلا قد يُقفل بالخطأ من شاشة الإدارة بلا رجعة.
        if (_existingUser != null
            && _existingUser.UserID == _loggedInUser.UserID
            && _existingUser.HasPermission(UserPermission.ManageUsers)
            && !selectedPermissions.HasFlag(UserPermission.ManageUsers))
        {
            ErrorText.Text = "You cannot remove your own 'Manage Users' permission while logged in";
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
                ErrorText.Text = "You cannot remove the last remaining Super Admin in the system";
                return;
            }
        }

        try
        {
            var excludeId = _existingUser?.UserID;
            if (_userRepo.UsernameExists(username, excludeId))
            {
                ErrorText.Text = "This username is already taken";
                return;
            }

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

                _userRepo.AddUser(newUser, password);
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

                // كلمة المرور تُغيَّر فقط إن كُتبت فعلاً - تركها فارغة يعني الإبقاء على القديمة
                if (!string.IsNullOrWhiteSpace(password))
                {
                    _userRepo.UpdatePassword(_existingUser.UserID, password);
                }
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = "An error occurred while saving: " + ex.Message;
        }
    }
}
