using System.Configuration;
using System.Windows;
using System.Windows.Threading;
using DentalClinic.UI.Diagnostics;
using DentalClinic.Data.DataAccess;
using DentalClinic.Features;
using DentalClinic.UI.Localization;

namespace DentalClinic.ProsthetistApp;

// نتحكم يدوياً في بدء التشغيل بدل StartupUri - نفس نمط DoctorApp/NurseApp تماماً.
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterGlobalExceptionHandlers();
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


    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLogger.LogUnhandledException("DispatcherUnhandledException", e.Exception);

        MessageBox.Show(
            "An unexpected error occurred in the application.\n\nThe details were saved to the local application log. Please contact support if the problem continues.",
            "Dental Clinic System",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // An unhandled UI exception can leave the window/application state inconsistent.
        // We log it and terminate cleanly instead of silently continuing in an unknown state.
        e.Handled = true;
        Shutdown(-1);
    }

    private static void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            CrashLogger.LogUnhandledException("AppDomain.UnhandledException", exception);
        else
            CrashLogger.Log("AppDomain.UnhandledException", e.ExceptionObject?.ToString() ?? "Unknown unhandled exception.");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLogger.LogUnhandledException("TaskScheduler.UnobservedTaskException", e.Exception);
        e.SetObserved();
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
                DatabaseMigrationRunner.EnsureCurrent(db);

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
