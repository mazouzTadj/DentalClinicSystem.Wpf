using System.Configuration;
using System.Windows;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;

namespace DentalClinic.NurseApp;

public partial class LoginWindow : Window
{
    public UserAccount? LoggedInUser { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
    }

    // يسمح بسحب النافذة من الشريط العلوي المخصّص لأننا ألغينا شريط العنوان الافتراضي
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

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ErrorText.Text = "Please enter username and password";
            return;
        }

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var userRepo = new UserRepository(db);

            var user = userRepo.Authenticate(username, password, UserRole.Nurse, out string error);

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
            ErrorText.Text = "Could not connect to the database: " + ex.Message;
        }
    }
}
