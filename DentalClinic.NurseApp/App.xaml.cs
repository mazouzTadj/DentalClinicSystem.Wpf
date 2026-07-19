using System.Windows;

namespace DentalClinic.NurseApp;

// نتحكم يدوياً في بدء التشغيل بدل StartupUri:
// نعرض شاشة الدخول أولاً، ولا نفتح الشاشة الرئيسية إلا بعد نجاح تسجيل الدخول.
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // نمنع أي إغلاق تلقائي للتطبيق قبل أن نقرر نحن متى ينتهي
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var loginWindow = new LoginWindow();
        var loginResult = loginWindow.ShowDialog();

        if (loginResult == true && loginWindow.LoggedInUser != null)
        {
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
}
