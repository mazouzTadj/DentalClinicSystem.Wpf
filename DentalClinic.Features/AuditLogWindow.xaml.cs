using System.Collections.ObjectModel;
using System.Configuration;
using System.Windows;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class AuditLogWindow : Window
{
    private readonly AuditLogRepository _auditLogRepo;
    public ObservableCollection<AuditLogEntry> Entries { get; } = new();

    public AuditLogWindow(UserAccount currentUser)
    {
        if (!currentUser.HasPermission(UserPermission.ManageUsers))
            throw new UnauthorizedAccessException("Only a user with the 'Manage Users' permission can view the audit log.");

        InitializeComponent();
        AuditGrid.ItemsSource = Entries;
        var db = new DatabaseHelper(ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString);
        _auditLogRepo = new AuditLogRepository(db);
        Loaded += (_, _) => LoadEntries();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadEntries();

    private void LoadEntries()
    {
        try
        {
            var entries = _auditLogRepo.GetRecent();
            Entries.Clear();
            foreach (var entry in entries) Entries.Add(entry);
            StatusText.Text = LocalizationManager.T("Audit_CountFormat", entries.Count);
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationManager.T("Audit_LoadErrorFormat", ex.Message);
        }
    }
}
