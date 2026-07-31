using System.Configuration;
using System.Windows;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.DoctorApp;

public partial class LoginWindow : Window
{
    public UserAccount? LoggedInUser { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
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

    private void EnglishLangButton_Click(object sender, RoutedEventArgs e) => TrySwitchLanguage(AppLanguage.English);

    private void ArabicLangButton_Click(object sender, RoutedEventArgs e) => TrySwitchLanguage(AppLanguage.Arabic);

    // يحفظ اللغة الجديدة ويطلب تأكيد إعادة التشغيل، لأن التبديل هنا ليس فورياً بتصميم مقصود
    // (أبسط وأكثر أماناً من إعادة بناء كل نافذة مفتوحة حياً في نفس اللحظة).
    private void TrySwitchLanguage(AppLanguage newLanguage)
    {
        if (newLanguage == LocalizationManager.CurrentLanguage)
        {
            return; // اللغة المختارة هي نفسها الحالية بالفعل - لا داعي لإزعاج المستخدم بسؤال إعادة التشغيل
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

            var user = userRepo.Authenticate(username, password, UserRole.Doctor, out string error);

            if (user == null)
            {
                ErrorText.Text = error;
                return;
            }

            LoggedInUser = user;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("Login_ConnectionErrorFormat", ex.Message);
        }
    }
}
