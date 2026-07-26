using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;

namespace DentalClinic.DoctorApp;

public partial class UserEditWindow : Window
{
    private readonly UserRepository _userRepo;
    private readonly UserAccount? _existingUser; // null = وضع الإضافة، غير null = وضع التعديل

    public UserEditWindow(UserAccount? existingUser)
    {
        _existingUser = existingUser;
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
            IsAdminCheck.IsChecked = _existingUser.IsAdmin;
            IsActiveCheck.IsChecked = _existingUser.IsActive;

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
                    IsAdmin = IsAdminCheck.IsChecked == true,
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
                    IsAdmin = IsAdminCheck.IsChecked == true,
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
