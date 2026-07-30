using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;

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

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phone))
        {
            ErrorText.Text = "Full name and phone number are required";
            return;
        }

        int? age = null;
        if (!string.IsNullOrWhiteSpace(AgeBox.Text))
        {
            if (!int.TryParse(AgeBox.Text.Trim(), out int parsedAge) || parsedAge < 0 || parsedAge > 130)
            {
                ErrorText.Text = "Invalid age";
                return;
            }
            age = parsedAge;
        }

        string? gender = (GenderBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var patientRepo = new PatientRepository(db);
            var queueRepo = new QueueRepository(db);

            // تحقق أولاً: هل هذا المريض مسجَّل مسبقاً بنفس الاسم ونفس رقم الهاتف؟
            var existing = patientRepo.FindDuplicate(fullName, phone);
            if (existing != null)
            {
                var confirm = MessageBox.Show(
                    $"A patient is already registered with the same name and phone number:\n{existing.FullName} - {existing.PhoneNumber}\n\n" +
                    "Do you want to add them to the queue instead of creating a new record?",
                    "Possible Duplicate Patient",
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
            ErrorText.Text = "An error occurred while saving: " + ex.Message;
        }
    }
}
