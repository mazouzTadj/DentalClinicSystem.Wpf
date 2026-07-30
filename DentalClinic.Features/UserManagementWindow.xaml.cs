using System.Collections.ObjectModel;
using System.Configuration;
using System.Windows;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;

namespace DentalClinic.Features;

// شاشة إدارة المستخدمين - يجب ألا تُفتح إلا ممن يملك صلاحية UserPermission.ManageUsers
public partial class UserManagementWindow : Window
{
    private readonly UserRepository _userRepo;
    private readonly UserAccount _currentUser;

    public ObservableCollection<UserRowViewModel> Users { get; } = new();

    public UserManagementWindow(UserAccount currentUser)
    {
        // فحص دفاعي إضافي (Defense in Depth): حتى لو فُتحت هذه الشاشة من مكان لم نتوقعه مستقبلاً،
        // لن تُنفَّذ أي عملية إدارية ما لم يكن المستخدم الحالي يملك صلاحية ManageUsers فعلاً.
        if (!currentUser.HasPermission(UserPermission.ManageUsers))
        {
            throw new UnauthorizedAccessException("Only a user with the 'Manage Users' permission can open User Management.");
        }

        _currentUser = currentUser;
        InitializeComponent();
        UsersGrid.ItemsSource = Users;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _userRepo = new UserRepository(db);

        Loaded += (s, e) => LoadUsers();
    }

    private void LoadUsers()
    {
        try
        {
            var users = _userRepo.GetAllUsers();
            Users.Clear();
            foreach (var u in users)
            {
                Users.Add(new UserRowViewModel(u));
            }
            StatusText.Text = $"{users.Count} user(s)";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not load users: " + ex.Message;
        }
    }

    private void AddUserButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new UserEditWindow(existingUser: null, loggedInUser: _currentUser) { Owner = this };
        if (window.ShowDialog() == true)
        {
            LoadUsers();
        }
    }

    private void EditUserButton_Click(object sender, RoutedEventArgs e) => EditSelected();

    private void UsersGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => EditSelected();

    private void EditSelected()
    {
        if (UsersGrid.SelectedItem is not UserRowViewModel selected)
        {
            MessageBox.Show("Please select a user from the list first", "Notice",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new UserEditWindow(existingUser: selected.User, loggedInUser: _currentUser) { Owner = this };
        if (window.ShowDialog() == true)
        {
            LoadUsers();
        }
    }

    private void ToggleActiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not UserRowViewModel selected)
        {
            MessageBox.Show("Please select a user from the list first", "Notice",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // يمنع المدير من تعطيل حسابه الخاص أثناء استخدامه (لتفادي إقفال وصوله للنظام بالخطأ)
        if (selected.UserID == _currentUser.UserID)
        {
            MessageBox.Show("You cannot deactivate your own account while logged in", "Not Allowed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // يمنع تعطيل آخر حساب متبقٍ يملك صلاحية ManageUsers (وإلا يُقفل الجميع من هذه الشاشة نهائياً)
        if (selected.User.IsActive
            && selected.User.HasPermission(UserPermission.ManageUsers)
            && _userRepo.CountActiveSuperAdmins(excludeUserId: selected.UserID) == 0)
        {
            MessageBox.Show("You cannot deactivate the last remaining Super Admin in the system", "Not Allowed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newState = !selected.User.IsActive;
        var actionWord = newState ? "activate" : "deactivate";

        var confirm = MessageBox.Show(
            $"Are you sure you want to {actionWord} the account of \"{selected.FullName}\"?",
            "Confirm",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _userRepo.SetActive(selected.UserID, newState);
            LoadUsers();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to update account status: " + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
