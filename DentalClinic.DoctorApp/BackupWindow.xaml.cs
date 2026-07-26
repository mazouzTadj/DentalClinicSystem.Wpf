using System.Configuration;
using System.Windows;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;

namespace DentalClinic.DoctorApp;

public partial class BackupWindow : Window
{
    private readonly BackupRepository _backupRepo;
    private readonly string? _folderPath;
    private readonly int _retainDays;

    public BackupWindow()
    {
        InitializeComponent();

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _backupRepo = new BackupRepository(db, "DentalClinicDB");

        _folderPath = ConfigurationManager.AppSettings["BackupFolderPath"];
        _retainDays = int.TryParse(ConfigurationManager.AppSettings["BackupRetainDays"], out var d) ? d : 14;

        FolderPathText.Text = string.IsNullOrWhiteSpace(_folderPath)
            ? "Not configured - add BackupFolderPath to App.config"
            : _folderPath;

        RetentionText.Text = $"Backups older than {_retainDays} days are deleted automatically.";

        Loaded += (s, e) => RefreshLastBackupText();
    }

    private void RefreshLastBackupText()
    {
        try
        {
            var last = _backupRepo.GetLastBackupDate();
            LastBackupText.Text = last.HasValue
                ? last.Value.ToString("yyyy-MM-dd HH:mm")
                : "No backup found yet";
        }
        catch (Exception ex)
        {
            LastBackupText.Text = "Could not read backup history: " + ex.Message;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void BackupNowButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(_folderPath))
        {
            StatusText.Text = "Backup folder is not configured. Add BackupFolderPath to App.config first.";
            return;
        }

        BackupNowButton.IsEnabled = false;
        BackupNowButton.Content = "Backing up...";

        try
        {
            var (success, message, _) = _backupRepo.BackupNow(_folderPath);

            if (success)
            {
                _backupRepo.CleanupOldBackups(_folderPath, _retainDays);
                StatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
                StatusText.Text = "Backup completed successfully.";
                RefreshLastBackupText();
            }
            else
            {
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
                StatusText.Text = message;
            }
        }
        finally
        {
            BackupNowButton.IsEnabled = true;
            BackupNowButton.Content = "Backup Now";
        }
    }
}
