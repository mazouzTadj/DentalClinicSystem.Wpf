using System.Configuration;
using System.Windows;
using DentalClinic.Data.DataAccess;

namespace DentalClinic.DoctorApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var loginWindow = new LoginWindow();
        var loginResult = loginWindow.ShowDialog();

        if (loginResult == true && loginWindow.LoggedInUser != null)
        {
            var mainWindow = new MainWindow(loginWindow.LoggedInUser);
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();

            // نسخة احتياطية تلقائية مرة واحدة يومياً: تعمل بصمت في الخلفية بعد أول تسجيل دخول لليوم،
            // ولا تعطّل عمل الطبيب أو تُظهر أي نافذة إن نجحت أو فشلت.
            RunDailyBackupIfNeeded();
        }
        else
        {
            Shutdown();
        }
    }

    private void RunDailyBackupIfNeeded()
    {
        var folderPath = ConfigurationManager.AppSettings["BackupFolderPath"];
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return; // لم يُعدّ مسار النسخ الاحتياطي بعد - لا شيء نفعله تلقائياً
        }

        var retainDays = int.TryParse(ConfigurationManager.AppSettings["BackupRetainDays"], out var d) ? d : 14;

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
                var db = new DatabaseHelper(connectionString);
                var backupRepo = new BackupRepository(db, "DentalClinicDB");

                var lastBackup = backupRepo.GetLastBackupDate();
                if (lastBackup.HasValue && lastBackup.Value.Date == DateTime.Now.Date)
                {
                    return; // تمت نسخة احتياطية اليوم بالفعل
                }

                var (success, _, _) = backupRepo.BackupNow(folderPath);
                if (success)
                {
                    backupRepo.CleanupOldBackups(folderPath, retainDays);
                }
            }
            catch
            {
                // نتجاهل أي خطأ هنا عمداً: النسخ التلقائي لا يجب أن يقاطع عمل الطبيب أبداً.
                // النسخ اليدوي عبر شاشة "Database Backup" سيُظهر رسالة الخطأ بوضوح إن احتاج الطبيب معرفتها.
            }
        });
    }
}
