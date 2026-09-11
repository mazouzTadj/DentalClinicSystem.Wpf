using System.Configuration;
using System.IO;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class BackupWindow : Window
{
    private readonly BackupRepository _backupRepo;
    private readonly int _retainDays;

    public BackupWindow()
    {
        InitializeComponent();

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _backupRepo = new BackupRepository(db, "DentalClinicDB");

        var savedFolderPath = LoadSavedBackupFolder();
        _retainDays = int.TryParse(ConfigurationManager.AppSettings["BackupRetainDays"], out var d) ? d : 14;

        FolderPathText.Text = savedFolderPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(FolderPathText.Text))
            FolderPathText.Text = ConfigurationManager.AppSettings["BackupFolderPath"] ?? string.Empty;

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

    private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationManager.T("Backup_SelectFolder"),
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(FolderPathText.Text) && Directory.Exists(FolderPathText.Text))
            dialog.InitialDirectory = FolderPathText.Text;

        if (dialog.ShowDialog(this) == true)
            FolderPathText.Text = dialog.FolderName;
    }

    private void SaveFolderButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = string.Empty;
        var folder = FolderPathText.Text.Trim();

        if (string.IsNullOrWhiteSpace(folder))
        {
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            StatusText.Text = LocalizationManager.T("Backup_FolderNotConfigured");
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            SaveBackupFolder(folder);
            StatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
            StatusText.Text = LocalizationManager.T("Backup_FolderSaved");
        }
        catch (Exception ex)
        {
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            StatusText.Text = LocalizationManager.T("Backup_FolderSaveErrorFormat", ex.Message);
        }
    }

    private void BackupNowButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = string.Empty;

        var folder = FolderPathText.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            StatusText.Text = LocalizationManager.T("Backup_FolderNotConfigured");
            return;
        }

        // حفظ المسار تلقائياً قبل تنفيذ النسخة حتى يستخدمه البرنامج في المرات القادمة.
        try
        {
            SaveBackupFolder(folder);
        }
        catch (Exception ex)
        {
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            StatusText.Text = LocalizationManager.T("Backup_FolderSaveErrorFormat", ex.Message);
            return;
        }

        BackupNowButton.IsEnabled = false;
        BackupNowButton.Content = LocalizationManager.T("Backup_InProgress");

        try
        {
            var (success, message, _) = _backupRepo.BackupNow(folder);

            if (success)
            {
                _backupRepo.CleanupOldBackups(folder, _retainDays);
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

    private static string? LoadSavedBackupFolder()
    {
        try
        {
            var file = GetBackupFolderSettingsFile();
            return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveBackupFolder(string folder)
    {
        var file = GetBackupFolderSettingsFile();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, folder);
    }

    private static string GetBackupFolderSettingsFile()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DentalClinicSystem");
        return Path.Combine(directory, "backup-folder.txt");
    }
}
