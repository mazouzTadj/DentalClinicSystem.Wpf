using System.Configuration;
using System.Windows;
using DentalClinic.Data.DataAccess;
using DentalClinic.Features;
using DentalClinic.UI.Localization;

namespace DentalClinic.ProsthetistApp;

// نتحكم يدوياً في بدء التشغيل بدل StartupUri - نفس نمط DoctorApp/NurseApp تماماً.
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LocalizationManager.Initialize();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (!TryCheckLicense())
        {
            Shutdown();
            return;
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
        }
        else
        {
            Shutdown();
        }
    }

    // فحص الترخيص يعمل بمبدأ الفشل المغلق: لا يُسمح بتشغيل التطبيق ما لم نستطع
    // التحقق بنجاح من ترخيص صالح. تطبيق المرمّم لا ينشئ البنية بنفسه، ولذلك فإن
    // قاعدة غير مهيأة أو اتصالاً فاشلاً يمنعان التشغيل بدلاً من تجاوز الحماية.
    private static bool TryCheckLicense()
    {
        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);

            if (SchemaInitializer.SchemaExists(db))
                SchemaInitializer.EnsureSchemaUpgrades(db);

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
}
