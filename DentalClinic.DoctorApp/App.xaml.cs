using System.Configuration;
using System.IO;
using System.Windows;
using DentalClinic.Data.DataAccess;
using DentalClinic.Features;
using DentalClinic.UI.Localization;

namespace DentalClinic.DoctorApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // يجب أن يكون أول شيء يحدث: يحمّل قاموس النصوص واتجاه الواجهة (RTL/LTR) المناسبين
        // قبل إنشاء أي نافذة، لأن StaticResource في كل XAML يُحلّ عند InitializeComponent مباشرة.
        LocalizationManager.Initialize();

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var checkSucceeded = TryHasAnyUsers(out var hasAnyUsers);

        if (!TryCheckLicense())
        {
            Shutdown();
            return;
        }

        if (checkSucceeded && !hasAnyUsers)
        {
            // قاعدة بيانات جديدة فارغة تماماً (لا يوجد أي مستخدم بعد) - على الأغلب أول تشغيل
            // عند عميل جديد. نعرض شاشة إنشاء أول حساب Super Admin بدل شاشة الدخول العادية.
            var setupWindow = new FirstRunSetupWindow();
            var setupResult = setupWindow.ShowDialog();

            if (setupResult != true || setupWindow.CreatedUser == null)
            {
                Shutdown();
                return;
            }
            // بعد إنشاء الحساب مباشرة إلى شاشة تسجيل الدخول العادية ليدخل به صاحب العيادة
        }

        var loginWindow = new LoginWindow();
        var loginResult = loginWindow.ShowDialog();

        if (loginResult == true && loginWindow.LoggedInUser != null)
        {
            AuditContext.SetCurrentUser(loginWindow.LoggedInUser.UserID);
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

    // فحص آمن: إن فشل الاتصال بالقاعدة لأي سبب (سيرفر غير جاهز بعد، connection string خاطئ،
    // أو حتى القاعدة نفسها CREATE DATABASE غير منفَّذة بعد) نتجاهل الخطأ هنا تماماً ونكمل لشاشة
    // تسجيل الدخول العادية، التي ستُظهر رسالة الخطأ بوضوح بنفسها عند محاولة الدخول. لا نريد شاشة
    // "الإعداد الأول" أن تظهر بالخطأ لعميل قاعدته فعلاً تحتوي مستخدمين، لمجرد أن الاتصال فشل مؤقتاً.
    private static bool TryHasAnyUsers(out bool hasAnyUsers)
    {
        hasAnyUsers = true; // افتراض آمن عند الفشل: نتصرف كأن القاعدة ليست فارغة (لا نعرض شاشة الإعداد)
        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);

            // أول تشغيل على قاعدة بيانات فارغة تماماً (أُنشئت بأمر CREATE DATABASE فقط، بدون أي
            // جداول بعد) - ننشئ البنية الكاملة تلقائياً هنا قبل أي شيء آخر
            if (!SchemaInitializer.SchemaExists(db))
                SchemaInitializer.CreateSchema(db);
            else
                SchemaInitializer.EnsureSchemaUpgrades(db);

            var userRepo = new UserRepository(db);
            hasAnyUsers = userRepo.AnyUsersExist();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // فحص الترخيص يعمل بمبدأ الفشل المغلق: لا يُسمح بتشغيل التطبيق ما لم نستطع
    // التحقق بنجاح من ترخيص صالح. هذا يمنع تجاوز الترخيص عند تعذر الاتصال بالقاعدة
    // أو عند حدوث خطأ غير متوقع أثناء التحقق.
    private static bool TryCheckLicense()
    {
        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);

            // يُنشئ InstallationId فقط إن لم يكن موجوداً بعد - آمن الاستدعاء دائماً (idempotent)،
            // يُصلح تلقائياً أي قاعدة كانت موجودة من قبل هذا الباتش ولم تمرّ بـ SchemaInitializer.CreateSchema
            LicenseValidator.EnsureInstallationId(db);

            if (LicenseValidator.IsLicensed(db, out var installationId)) return true;

            var licenseWindow = new LicenseRequiredWindow(db, installationId);
            return licenseWindow.ShowDialog() == true;
        }
        catch
        {
            MessageBox.Show(
                "Unable to verify the license. Check the database connection and try again.",
                "License verification",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private static string? LoadSavedBackupFolder()
    {
        try
        {
            var file = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DentalClinicSystem",
                "backup-folder.txt");
            return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private void RunDailyBackupIfNeeded()
    {
        var folderPath = LoadSavedBackupFolder() ?? ConfigurationManager.AppSettings["BackupFolderPath"];
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
