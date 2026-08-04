using System.Configuration;
using System.Windows;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.DoctorApp;

// تظهر فقط عند أول تشغيل على قاعدة بيانات جديدة فارغة تماماً (لا يوجد أي مستخدم بعد - راجع
// App.xaml.cs → AnyUsersExist()). تُنشئ أول حساب Super Admin (دور Doctor + كل الصلاحيات السبعة
// مفعّلة)، وبعدها ينتقل التشغيل مباشرة لشاشة تسجيل الدخول العادية ليدخل به صاحب العيادة.
// لا يوجد زر إغلاق هنا عمداً: بدون حساب واحد على الأقل لا يمكن لأي أحد الدخول للتطبيق إطلاقاً.
public partial class FirstRunSetupWindow : Window
{
    public UserAccount? CreatedUser { get; private set; }

    public FirstRunSetupWindow()
    {
        InitializeComponent();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var fullName = FullNameBox.Text.Trim();
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        var confirmPassword = ConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(username))
        {
            ErrorText.Text = LocalizationManager.T("FirstRun_FullNameUsernameRequired");
            return;
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
        {
            ErrorText.Text = LocalizationManager.T("FirstRun_PasswordTooShort");
            return;
        }

        if (password != confirmPassword)
        {
            ErrorText.Text = LocalizationManager.T("FirstRun_PasswordMismatch");
            return;
        }

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var userRepo = new UserRepository(db);

            if (userRepo.UsernameExists(username))
            {
                ErrorText.Text = LocalizationManager.T("UserEdit_UsernameTaken");
                return;
            }

            // أول حساب في النظام = مدير عام كامل الصلاحيات (نفس مستوى "جلولي زيد عبد الصمد")
            var allPermissions = UserPermission.ManageUsers | UserPermission.AccessFinance
                | UserPermission.OpenPatientFile | UserPermission.AccessBackup
                | UserPermission.ManageTreatments | UserPermission.CollectPayments
                | UserPermission.RegisterPatients;

            var newUser = new UserAccount
            {
                FullName = fullName,
                Username = username,
                Role = UserRole.Doctor,
                Permissions = allPermissions,
                IsActive = true
            };

            userRepo.AddUser(newUser, password);

            CreatedUser = newUser;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("Login_ConnectionErrorFormat", ex.Message);
        }
    }
}
