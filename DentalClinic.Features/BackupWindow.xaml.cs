using System.Configuration;
using System.Windows;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

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
            ? LocalizationManager.T("Backup_NotConfigured")
            : _folderPath;

        RetentionText.Text = LocalizationManager.T("Backup_RetentionFormat", _retainDays);

        // إصلاح: الزر لم يكن يحمل أي نص عند فتح النافذة أول مرة (كان فارغاً حتى أول نقرة)
        BackupNowButton.Content = LocalizationManager.T("Backup_NowButton");

        Loaded += (s, e) => RefreshLastBackupText();
    }

    private void RefreshLastBackupText()
    {
        try
        {
            var last = _backupRepo.GetLastBackupDate();
            LastBackupText.Text = last.HasValue
                ? last.Value.ToString("yyyy-MM-dd HH:mm")
                : LocalizationManager.T("Backup_NoBackupYet");
        }
        catch (Exception ex)
        {
            LastBackupText.Text = LocalizationManager.T("Backup_ReadHistoryErrorFormat", ex.Message);
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
            StatusText.Text = LocalizationManager.T("Backup_FolderNotConfigured");
            return;
        }

        BackupNowButton.IsEnabled = false;
        BackupNowButton.Content = LocalizationManager.T("Backup_InProgress");

        try
        {
            var (success, message, _) = _backupRepo.BackupNow(_folderPath);

            if (success)
            {
                _backupRepo.CleanupOldBackups(_folderPath, _retainDays);
                StatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
                StatusText.Text = LocalizationManager.T("Backup_Success");
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
            BackupNowButton.Content = LocalizationManager.T("Backup_NowButton");
        }
    }
}
