using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.Features;
using DentalClinic.UI.Localization;

namespace DentalClinic.ProsthetistApp;

public partial class LoginWindow : Window
{
    public UserAccount? LoggedInUser { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();

        var remembered = LoginPreferences.LoadRememberedUsername("Prosthetist");
        if (!string.IsNullOrWhiteSpace(remembered))
        {
            UsernameBox.Text = remembered;
            RememberMeCheckBox.IsChecked = true;
            RememberPasswordCheckBox.IsEnabled = true;

            var rememberedPassword = LoginPreferences.LoadRememberedPassword("Prosthetist");
            if (!string.IsNullOrEmpty(rememberedPassword))
            {
                PasswordBox.Password = rememberedPassword;
                RememberPasswordCheckBox.IsChecked = true;
            }

            PasswordBox.Focus();
        }
    }

    private void RememberMeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var isChecked = RememberMeCheckBox.IsChecked == true;
        RememberPasswordCheckBox.IsEnabled = isChecked;
        if (!isChecked) RememberPasswordCheckBox.IsChecked = false;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LoginButton_Click(sender, e);
        }
    }

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

    private void EnglishLangButton_Click(object sender, RoutedEventArgs e) => TrySwitchLanguage(AppLanguage.English);

    private void ArabicLangButton_Click(object sender, RoutedEventArgs e) => TrySwitchLanguage(AppLanguage.Arabic);

    private void TrySwitchLanguage(AppLanguage newLanguage)
    {
        if (newLanguage == LocalizationManager.CurrentLanguage)
        {
            return;
        }

        var confirm = MessageBox.Show(
            LocalizationManager.T("Login_RestartMessage"),
            LocalizationManager.T("Login_RestartTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        LocalizationManager.SaveLanguagePreference(newLanguage);
        LocalizationManager.RestartApplication();
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ErrorText.Text = LocalizationManager.T("Login_EnterCredentials");
            return;
        }

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var userRepo = new UserRepository(db);

            // ⚠️ يقبل حساب "مرمم" أو حساب "طبيب" معاً - الطبيب يجب أن يمتلك صلاحية كاملة على نظام
            // الترميم دون الحاجة لإنشاء حساب مرمم منفصل له (راجع MainWindow: أي حساب Role=Doctor
            // يتجاوز كل فحوصات الصلاحيات الدقيقة تلقائياً هناك، تماماً كما يحدث فعلاً في
            // ProstheticCaseEditWindow منذ الباتش 3).
            var user = userRepo.Authenticate(username, password, new[] { UserRole.Prosthetist, UserRole.Doctor }, out string error);

            if (user == null)
            {
                ErrorText.Text = error;
                return;
            }

            // ⚠️ التحقق الأمني النهائي - من قاعدة البيانات مباشرة، وليس اعتماداً على إخفاء الزر في
            // DoctorApp فقط. أي حساب Role=Doctor لا يحمل IsMainDoctor=1 يُرفَض هنا صراحةً، حتى لو
            // شغَّل ProsthetistApp.exe مباشرة متجاوزاً واجهة تطبيق الطبيب بالكامل. حسابات المرممين
            // (Prosthetist) لا تتأثر بهذا الفحص إطلاقاً - التطبيق مخصَّص لها أصلاً بلا أي قيد إضافي.
            if (user.Role == UserRole.Doctor && !user.IsMainDoctor)
            {
                ErrorText.Text = LocalizationManager.T("Login_ProsthetistDoctorNotAuthorized");
                return;
            }

            // ⚠️ خطوة جوهرية خاصة بتطبيق المرمم فقط: صلاحيات الترميم الدقيقة (Permissions/UserPermissions)
            // لا تُملأ تلقائياً عند Authenticate (تعمداً - راجع تعليق UserAccount.ProstheticPermissionKeys:
            // لا داعي لإثقال تسجيل دخول الطبيب/الممرضة بها). هنا تحديداً، حيث تُستخدَم فعلياً في كل
            // شاشة لاحقة بالتطبيق (MainWindow وProstheticCaseEditWindow)، يجب تحميلها صراحة قبل المتابعة.
            try
            {
                var permissionRepo = new PermissionRepository(db);
                user.ProstheticPermissionKeys = permissionRepo.GetGrantedKeys(user.UserID);
            }
            catch (Exception ex)
            {
                ErrorText.Text = LocalizationManager.T("Login_ConnectionErrorFormat", ex.Message);
                return;
            }

            LoggedInUser = user;
            var rememberUsername = RememberMeCheckBox.IsChecked == true ? username : null;
            var rememberPassword = RememberMeCheckBox.IsChecked == true && RememberPasswordCheckBox.IsChecked == true
                ? password
                : null;
            LoginPreferences.SaveRememberedCredentials("Prosthetist", rememberUsername, rememberPassword);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("Login_ConnectionErrorFormat", ex.Message);
        }
    }
}
