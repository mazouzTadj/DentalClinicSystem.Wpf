using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class AddPatientWindow : Window
{
    private readonly UserAccount _currentUser;

    public AddPatientWindow(UserAccount currentUser)
    {
        _currentUser = currentUser;
        InitializeComponent();
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

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var fullName = FullNameBox.Text.Trim();
        var phone = PhoneBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            ErrorText.Text = LocalizationManager.T("AddPatient_FullNameRequired");
            return;
        }

        int? age = null;
        if (!string.IsNullOrWhiteSpace(AgeBox.Text))
        {
            if (!int.TryParse(AgeBox.Text.Trim(), out int parsedAge) || parsedAge < 0 || parsedAge > 130)
            {
                ErrorText.Text = LocalizationManager.T("AddPatient_InvalidAge");
                return;
            }
            age = parsedAge;
        }

        // نقرأ Tag (القيمة الثابتة: Male/Female) بدل Content (النص المترجَم) حتى تبقى القيمة المخزَّنة
        // في قاعدة البيانات متسقة بغض النظر عن لغة الواجهة المستخدَمة عند التسجيل
        string? gender = (GenderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var patientRepo = new PatientRepository(db);
            var queueRepo = new QueueRepository(db);

            // تحقق أولاً: هل هذا المريض مسجَّل مسبقاً بنفس الاسم ونفس رقم الهاتف؟
            // (يُتخطى هذا الفحص إن لم يُدخَل رقم هاتف، لأن التطابق يفقد معناه بلا رقم مرجعي)
            var existing = string.IsNullOrWhiteSpace(phone) ? null : patientRepo.FindDuplicate(fullName, phone);
            if (existing != null)
            {
                var confirm = MessageBox.Show(
                    LocalizationManager.T("AddPatient_DuplicateFoundFormat", existing.FullName, existing.PhoneNumber),
                    LocalizationManager.T("AddPatient_DuplicateTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm == MessageBoxResult.Yes)
                {
                    var (success, message, _) = queueRepo.AddToQueue(existing.PatientID, _currentUser.UserID);
                    if (!success)
                    {
                        ErrorText.Text = message;
                        return;
                    }

                    DialogResult = true;
                    Close();
                    return;
                }
                // اختار "لا" => نفترض أنه فعلاً شخص مختلف ونكمل تسجيله كمريض جديد
            }

            var patient = new Patient
            {
                FullName = fullName,
                Age = age,
                Gender = gender,
                PhoneNumber = phone,
                Address = string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim(),
                BasicMedicalNotes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
                RegisteredByUserID = _currentUser.UserID
            };

            var newPatientId = patientRepo.Add(patient);
            queueRepo.AddToQueue(newPatientId, _currentUser.UserID);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("AddPatient_SaveErrorFormat", ex.Message);
        }
    }
}
