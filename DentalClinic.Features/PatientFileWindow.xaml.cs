using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public class TreatmentPresetItem
{
    public int TreatmentID { get; set; }
    public string TreatmentName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public override string ToString() => TreatmentName;
}

public partial class PatientFileWindow : Window
{
    private readonly int _patientId;
    private readonly int? _visitId;
    private readonly UserAccount _currentUser;

    private readonly PatientRepository _patientRepo;
    private readonly SessionRepository _sessionRepo;
    private readonly QueueRepository _queueRepo;
    private readonly DatabaseHelper _db;

    public ObservableCollection<SessionHistoryRowViewModel> History { get; } = new();
    private readonly List<TreatmentPresetItem> _treatments = new();

    private Patient? _currentPatient;

    public PatientFileWindow(int patientId, int? visitId, UserAccount currentUser)
    {
        _patientId = patientId;
        _visitId = visitId;
        _currentUser = currentUser;

        InitializeComponent();
        HistoryGrid.ItemsSource = History;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        _db = new DatabaseHelper(connectionString);
        _patientRepo = new PatientRepository(_db);
        _sessionRepo = new SessionRepository(_db);
        _queueRepo = new QueueRepository(_db);

        if (_visitId == null)
        {
            SaveSessionButton.Content = LocalizationManager.T("File_SaveButtonSimple");
        }

        Loaded += (s, e) =>
        {
            LoadPatientInfo();
            LoadTreatments();
            LoadHistory();
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void LoadTreatments()
    {
        try
        {
            const string createTableSql = @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TreatmentPresets')
                BEGIN
                    CREATE TABLE TreatmentPresets (
                        TreatmentID INT IDENTITY(1,1) PRIMARY KEY,
                        TreatmentName NVARCHAR(200) NOT NULL,
                        Price DECIMAL(18,2) NOT NULL DEFAULT 0,
                        IsActive BIT NOT NULL DEFAULT 1
                    );
                END";
            _db.ExecuteNonQuery(createTableSql);

            const string sql = "SELECT TreatmentID, TreatmentName, Price FROM TreatmentPresets WHERE IsActive = 1 ORDER BY TreatmentName ASC";
            var table = _db.ExecuteQuery(sql);

            _treatments.Clear();
            foreach (DataRow row in table.Rows)
            {
                _treatments.Add(new TreatmentPresetItem
                {
                    TreatmentID = (int)row["TreatmentID"],
                    TreatmentName = row["TreatmentName"].ToString()!,
                    Price = Convert.ToDecimal(row["Price"])
                });
            }
            CmbTreatment.ItemsSource = _treatments;
        }
        catch
        {
            // تجنب أي توقف في حال حدث خطأ بسيط في الاستعلام
        }
    }

    private void CmbTreatment_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbTreatment.SelectedItem is TreatmentPresetItem selectedTreatment)
        {
            TotalPriceBox.Text = selectedTreatment.Price > 0 ? selectedTreatment.Price.ToString("0.##") : string.Empty;
        }
    }

    private void PrescriptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPatient == null)
        {
            MessageBox.Show(LocalizationManager.T("File_PatientDataNotLoaded"), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new PrescriptionWindow(_currentPatient.FullName, MedicationBox.Text) { Owner = this };
        window.ShowDialog();
    }

    private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPatient == null)
        {
            MessageBox.Show(LocalizationManager.T("File_PatientDataNotLoaded"), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var sessions = _sessionRepo.GetByPatient(_patientId);
            var pdfBytes = PatientFilePdfExporter.Generate(_currentPatient, sessions);

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"{_currentPatient.FullName.Replace(' ', '_')}_MedicalRecord_{DateTime.Now:yyyy-MM-dd}.pdf",
                Filter = "PDF Files (*.pdf)|*.pdf",
                DefaultExt = ".pdf"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            System.IO.File.WriteAllBytes(dialog.FileName, pdfBytes);

            var openIt = MessageBox.Show(
                LocalizationManager.T("File_PdfSavedMessage"),
                LocalizationManager.T("Rx_ExportCompleteTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (openIt == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName)
                {
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("File_ExportPdfErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ScheduleAppointmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPatient == null)
        {
            MessageBox.Show(LocalizationManager.T("File_PatientDataNotLoaded"), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ScheduleAppointmentDialog(_patientId, _currentUser.UserID, _queueRepo)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }

    // القيمة المخزَّنة في قاعدة البيانات ثابتة دائماً (Male/Female بالإنجليزية، كما هو معمول به في AddPatientWindow)
    // بينما هذه الدالة تترجمها فقط للعرض حسب لغة الواجهة الحالية. أي قيمة قديمة/حرة أخرى تُعرَض كما هي.
    private static string LocalizeGender(string? rawGender) => rawGender switch
    {
        "Male" => LocalizationManager.T("AddPatient_GenderMale"),
        "Female" => LocalizationManager.T("AddPatient_GenderFemale"),
        null or "" => "-",
        _ => rawGender
    };

    private void LoadPatientInfo()
    {
        var patient = _patientRepo.GetById(_patientId);
        if (patient == null)
        {
            MessageBox.Show(LocalizationManager.T("File_PatientNotFound"), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        PatientHeaderText.Text = LocalizationManager.T("File_HeaderFormat", patient.FullName);
        _currentPatient = patient;

        var info = LocalizationManager.T("File_InfoLineFormat", patient.Age?.ToString() ?? "-", LocalizeGender(patient.Gender), patient.PhoneNumber);
        if (!string.IsNullOrWhiteSpace(patient.BasicMedicalNotes))
        {
            info += LocalizationManager.T("File_BasicNotesFormat", Environment.NewLine, patient.BasicMedicalNotes);
        }
        PatientInfoText.Text = info;
    }

    private void LoadHistory()
    {
        var sessions = _sessionRepo.GetByPatient(_patientId);
        History.Clear();
        foreach (var s in sessions)
        {
            History.Add(new SessionHistoryRowViewModel(s));
        }

        var lastSession = sessions.FirstOrDefault();
        if (lastSession != null)
        {
            ChiefComplaintBox.Text = lastSession.ChiefComplaint ?? string.Empty;
            DiagnosisBox.Text = lastSession.Diagnosis ?? string.Empty;
            CmbTreatment.Text = lastSession.TreatmentPerformed ?? string.Empty;
            MedicationBox.Text = lastSession.Medication ?? string.Empty;
            TotalPriceBox.Text = lastSession.TotalPrice > 0 ? lastSession.TotalPrice.ToString("0.##") : string.Empty;

            var lastToothNumbers = _sessionRepo.GetToothNumbersForSession(lastSession.SessionID);
            if (lastToothNumbers.Count > 0)
            {
                // إصلاح: كانت تُعبَّأ فقط بأول سن مسجَّل في الزيارة السابقة، الآن تُعبَّأ كل الأسنان المسجَّلة
                Odontogram.SetSelectedTeeth(lastToothNumbers);
            }

            PrefillNoticeText.Text = LocalizationManager.T("File_PrefillNotice");
            PrefillNoticeText.Visibility = Visibility.Visible;
        }
    }

    private void SaveSessionButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        // 🟢 إدخال التكلفة الإجمالية اختياري (تعتبر 0 إن تركت فارغة)
        decimal totalPrice = 0;
        if (!string.IsNullOrWhiteSpace(TotalPriceBox.Text))
        {
            if (!decimal.TryParse(TotalPriceBox.Text.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out totalPrice) || totalPrice < 0)
            {
                ErrorText.Text = LocalizationManager.T("File_InvalidTotalPrice");
                return;
            }
        }

        try
        {
            // الطبيب يحفظ تفاصيل العلاج والتكلفة، بينما يبقى المبلغ المدفوع (PaidAmount) صفر ليتولى موظف الاستقبال/الممرضة قبضه
            var session = new MedicalSession
            {
                VisitID = _visitId,
                PatientID = _patientId,
                DoctorID = _currentUser.UserID,
                ChiefComplaint = string.IsNullOrWhiteSpace(ChiefComplaintBox.Text) ? null : ChiefComplaintBox.Text.Trim(),
                Diagnosis = string.IsNullOrWhiteSpace(DiagnosisBox.Text) ? null : DiagnosisBox.Text.Trim(),
                TreatmentPerformed = string.IsNullOrWhiteSpace(CmbTreatment.Text) ? null : CmbTreatment.Text.Trim(),
                Medication = string.IsNullOrWhiteSpace(MedicationBox.Text) ? null : MedicationBox.Text.Trim(),
                TotalPrice = totalPrice,
                PaidAmount = 0
            };

            var newSessionId = _sessionRepo.Add(session);

            // كل الأسنان المحدَّدة (وليس أول واحد فقط) تُسجَّل كسجل منفصل مرتبط بنفس الجلسة
            foreach (var toothNumber in Odontogram.SelectedTeeth)
            {
                _sessionRepo.AddToothRecord(newSessionId, toothNumber, "Treated", null);
            }

            if (_visitId.HasValue)
            {
                _queueRepo.UpdateStatus(_visitId.Value, VisitStatus.Completed, _currentUser.UserID);
            }

            var savedMessage = _visitId.HasValue
                ? LocalizationManager.T("File_CompletedSavedMessage")
                : LocalizationManager.T("File_SessionSavedMessage");

            MessageBox.Show(savedMessage, LocalizationManager.T("File_SavedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("File_SaveErrorFormat", ex.Message);
        }
    }
}