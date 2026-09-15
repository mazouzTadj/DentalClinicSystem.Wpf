using System.Configuration;
using System.Windows;
using DentalClinic.Data.DataAccess;
using DentalClinic.Features;
using DentalClinic.UI.Localization;

namespace DentalClinic.NurseApp;

// نتحكم يدوياً في بدء التشغيل بدل StartupUri:
// نعرض شاشة الدخول أولاً، ولا نفتح الشاشة الرئيسية إلا بعد نجاح تسجيل الدخول.
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // يجب أن يكون أول شيء يحدث: يحمّل قاموس النصوص واتجاه الواجهة (RTL/LTR) المناسبين
        // قبل إنشاء أي نافذة، لأن StaticResource في كل XAML يُحلّ عند InitializeComponent مباشرة.
        LocalizationManager.Initialize();

        // نمنع أي إغلاق تلقائي للتطبيق قبل أن نقرر نحن متى ينتهي
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
    // التحقق بنجاح من ترخيص صالح. NurseApp لا ينشئ البنية بنفسه، ولذلك فإن قاعدة
    // غير مهيأة أو اتصالاً فاشلاً يمنعان التشغيل بدلاً من تجاوز الحماية.
    private static bool TryCheckLicense()
    {
        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);

            if (SchemaInitializer.SchemaExists(db))
                SchemaInitializer.EnsureSchemaUpgrades(db);

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
}
