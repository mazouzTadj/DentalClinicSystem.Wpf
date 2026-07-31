using System.Collections.ObjectModel;
using System.Configuration;
using System.Windows;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

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
            StatusText.Text = LocalizationManager.T("UserMgmt_CountFormat", users.Count);
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationManager.T("UserMgmt_LoadErrorFormat", ex.Message);
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
            MessageBox.Show(LocalizationManager.T("UserMgmt_SelectUserFirst"), LocalizationManager.T("Common_Notice"),
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
            MessageBox.Show(LocalizationManager.T("UserMgmt_SelectUserFirst"), LocalizationManager.T("Common_Notice"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // يمنع المدير من تعطيل حسابه الخاص أثناء استخدامه (لتفادي إقفال وصوله للنظام بالخطأ)
        if (selected.UserID == _currentUser.UserID)
        {
            MessageBox.Show(LocalizationManager.T("UserMgmt_CannotDeactivateSelf"), LocalizationManager.T("UserMgmt_NotAllowedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // يمنع تعطيل آخر حساب متبقٍ يملك صلاحية ManageUsers (وإلا يُقفل الجميع من هذه الشاشة نهائياً)
        if (selected.User.IsActive
            && selected.User.HasPermission(UserPermission.ManageUsers)
            && _userRepo.CountActiveSuperAdmins(excludeUserId: selected.UserID) == 0)
        {
            MessageBox.Show(LocalizationManager.T("UserMgmt_CannotDeactivateLastAdmin"), LocalizationManager.T("UserMgmt_NotAllowedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newState = !selected.User.IsActive;
        var actionWord = LocalizationManager.T(newState ? "UserMgmt_ConfirmActivate" : "UserMgmt_ConfirmDeactivate");

        var confirm = MessageBox.Show(
            LocalizationManager.T("UserMgmt_ConfirmToggleFormat", actionWord, selected.FullName),
            LocalizationManager.T("Common_Confirm"),
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
            MessageBox.Show(LocalizationManager.T("UserMgmt_UpdateStatusErrorFormat", ex.Message), LocalizationManager.T("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
