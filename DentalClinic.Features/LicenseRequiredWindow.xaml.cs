using System.Windows;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;

namespace DentalClinic.Features;

// تظهر عند بدء تشغيل أي من التطبيقين (NurseApp أو DoctorApp) إن لم تكن قاعدة العيادة مُفعَّلة
// بعد (لا يوجد توقيع ترخيص صالح لمعرّف التثبيت الخاص بها - راجع LicenseValidator في
// DentalClinic.Data). لا يوجد زر إغلاق يتجاوز الأمر - إغلاق النافذة بدون تفعيل ناجح يعني
// إنهاء التطبيق بالكامل (يتحكم بذلك المستدعي عبر DialogResult).
public partial class LicenseRequiredWindow : Window
{
    private readonly DatabaseHelper _db;

    public LicenseRequiredWindow(DatabaseHelper db, string installationId)
    {
        InitializeComponent();
        _db = db;
        InstallationIdBox.Text = installationId;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CopyIdButton_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(InstallationIdBox.Text); } catch { /* حافظة غير متاحة - نتجاهل بأمان */ }
    }

    private void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var key = LicenseKeyBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(key))
        {
            ErrorText.Text = "Please paste the license key you received.";
            return;
        }

        try
        {
            LicenseValidator.SetLicenseSignature(_db, key);

            if (LicenseValidator.IsLicensed(_db, out _))
            {
                DialogResult = true;
                Close();
            }
            else
            {
                ErrorText.Text = "This license key is invalid for this Installation ID.";
            }
        }
        catch (Exception ex)
        {
            ErrorText.Text = "Could not save the license key: " + ex.Message;
        }
    }
}
