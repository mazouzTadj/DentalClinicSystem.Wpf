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

namespace DentalClinic.DoctorApp;

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
            SaveSessionButton.Content = "Save Session";
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
            MessageBox.Show("Patient data is not loaded yet", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new PrescriptionWindow(_currentPatient.FullName, MedicationBox.Text) { Owner = this };
        window.ShowDialog();
    }

    private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPatient == null)
        {
            MessageBox.Show("Patient data is not loaded yet", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
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
                "PDF saved successfully. Do you want to open it now?",
                "Export Complete",
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
            MessageBox.Show("Failed to export PDF: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ScheduleAppointmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPatient == null)
        {
            MessageBox.Show("Patient data is not loaded yet", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ScheduleAppointmentDialog(_patientId, _currentUser.UserID, _queueRepo)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }

    private void LoadPatientInfo()
    {
        var patient = _patientRepo.GetById(_patientId);
        if (patient == null)
        {
            MessageBox.Show("Could not find this patient's data", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        PatientHeaderText.Text = $"Patient File: {patient.FullName}";
        _currentPatient = patient;

        var info = $"Age: {(patient.Age?.ToString() ?? "-")}   |   Gender: {patient.Gender ?? "-"}   |   Phone: {patient.PhoneNumber}";
        if (!string.IsNullOrWhiteSpace(patient.BasicMedicalNotes))
        {
            info += $"\nBasic notes from reception: {patient.BasicMedicalNotes}";
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
                Odontogram.SetSelectedTooth(lastToothNumbers[0]);
            }

            PrefillNoticeText.Text = "Pre-filled from the last visit — please review and update before saving.";
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
                ErrorText.Text = "Invalid total price format";
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

            if (!string.IsNullOrWhiteSpace(Odontogram.SelectedTooth))
            {
                _sessionRepo.AddToothRecord(newSessionId, Odontogram.SelectedTooth, "Treated", null);
            }

            if (_visitId.HasValue)
            {
                _queueRepo.UpdateStatus(_visitId.Value, VisitStatus.Completed, _currentUser.UserID);
            }

            var savedMessage = _visitId.HasValue
                ? "Medical treatment completed and saved successfully. Patient can now proceed to payment at reception."
                : "Session saved successfully";

            MessageBox.Show(savedMessage, "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = "An error occurred while saving: " + ex.Message;
        }
    }
}