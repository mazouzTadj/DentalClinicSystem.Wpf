using System.Collections.ObjectModel;
using System.Configuration;
using System.Windows;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;

namespace DentalClinic.DoctorApp;

public partial class PatientFileWindow : Window
{
    private readonly int _patientId;
    private readonly int? _visitId;
    private readonly UserAccount _currentUser;

    private readonly PatientRepository _patientRepo;
    private readonly SessionRepository _sessionRepo;
    private readonly QueueRepository _queueRepo;
    private readonly PaymentRepository _paymentRepo;

    public ObservableCollection<SessionHistoryRowViewModel> History { get; } = new();

    private Patient? _currentPatient;

    private int? _outstandingSessionId;
    private decimal? _outstandingSessionTotalPrice;
    private decimal _outstandingSessionPaidAmount;

    public PatientFileWindow(int patientId, int? visitId, UserAccount currentUser)
    {
        _patientId = patientId;
        _visitId = visitId;
        _currentUser = currentUser;

        InitializeComponent();
        HistoryGrid.ItemsSource = History;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _patientRepo = new PatientRepository(db);
        _sessionRepo = new SessionRepository(db);
        _queueRepo = new QueueRepository(db);
        _paymentRepo = new PaymentRepository(db);

        if (_visitId == null)
        {
            SaveSessionButton.Content = "Save Session";
        }

        Loaded += (s, e) =>
        {
            LoadPatientInfo();
            LoadHistory();
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

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

    // 📅 الحدث المسؤول عن فتح نافذة حجز موعد مستقبلي للمريض
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
            TreatmentBox.Text = lastSession.TreatmentPerformed ?? string.Empty;
            MedicationBox.Text = lastSession.Medication ?? string.Empty;
            TotalPriceBox.Text = lastSession.TotalPrice > 0 ? lastSession.TotalPrice.ToString("0.##") : string.Empty;
            PaidAmountBox.Text = string.Empty;

            var lastToothNumbers = _sessionRepo.GetToothNumbersForSession(lastSession.SessionID);
            if (lastToothNumbers.Count > 0)
            {
                Odontogram.SetSelectedTooth(lastToothNumbers[0]);
            }

            if (lastSession.RemainingAmount > 0)
            {
                _outstandingSessionId = lastSession.SessionID;
                _outstandingSessionTotalPrice = lastSession.TotalPrice;
                _outstandingSessionPaidAmount = lastSession.PaidAmount;

                PrefillNoticeText.Text =
                    $"Outstanding balance: {lastSession.RemainingAmount:0.##} from the last visit. " +
                    "Keep the Total Price as-is to record a new payment toward this balance, " +
                    "or increase it only if this is a new treatment.";
            }
            else
            {
                _outstandingSessionId = null;
                _outstandingSessionTotalPrice = null;

                PrefillNoticeText.Text = "Pre-filled from the last visit — please review and update before saving.";
            }

            PrefillNoticeText.Visibility = Visibility.Visible;
        }
        else
        {
            _outstandingSessionId = null;
            _outstandingSessionTotalPrice = null;
        }
    }

    private void SaveSessionButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        if (!decimal.TryParse(TotalPriceBox.Text.Trim(), out decimal totalPrice) || totalPrice < 0)
        {
            ErrorText.Text = "Invalid total price";
            return;
        }

        decimal paidAmount = 0;
        if (!string.IsNullOrWhiteSpace(PaidAmountBox.Text) &&
            (!decimal.TryParse(PaidAmountBox.Text.Trim(), out paidAmount) || paidAmount < 0))
        {
            ErrorText.Text = "Invalid paid amount";
            return;
        }

        if (paidAmount > totalPrice)
        {
            ErrorText.Text = "Paid amount exceeds the total price";
            return;
        }

        try
        {
            bool isPaymentOnExistingBalance =
                _outstandingSessionId.HasValue &&
                _outstandingSessionTotalPrice.HasValue &&
                totalPrice == _outstandingSessionTotalPrice.Value;

            if (isPaymentOnExistingBalance && paidAmount > 0)
            {
                var newTotalPaid = _outstandingSessionPaidAmount + paidAmount;

                _sessionRepo.UpdateSessionPayment(
                    _outstandingSessionId!.Value,
                    newTotalPaid,
                    string.IsNullOrWhiteSpace(ChiefComplaintBox.Text) ? null : ChiefComplaintBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DiagnosisBox.Text) ? null : DiagnosisBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(TreatmentBox.Text) ? null : TreatmentBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(MedicationBox.Text) ? null : MedicationBox.Text.Trim());

                _paymentRepo.AddPayment(_outstandingSessionId.Value, paidAmount, _currentUser.UserID);

                if (!string.IsNullOrWhiteSpace(Odontogram.SelectedTooth))
                {
                    _sessionRepo.AddToothRecord(_outstandingSessionId.Value, Odontogram.SelectedTooth, "Treated", null);
                }
            }
            else
            {
                var session = new MedicalSession
                {
                    VisitID = _visitId,
                    PatientID = _patientId,
                    DoctorID = _currentUser.UserID,
                    ChiefComplaint = string.IsNullOrWhiteSpace(ChiefComplaintBox.Text) ? null : ChiefComplaintBox.Text.Trim(),
                    Diagnosis = string.IsNullOrWhiteSpace(DiagnosisBox.Text) ? null : DiagnosisBox.Text.Trim(),
                    TreatmentPerformed = string.IsNullOrWhiteSpace(TreatmentBox.Text) ? null : TreatmentBox.Text.Trim(),
                    Medication = string.IsNullOrWhiteSpace(MedicationBox.Text) ? null : MedicationBox.Text.Trim(),
                    TotalPrice = totalPrice,
                    PaidAmount = paidAmount
                };

                var newSessionId = _sessionRepo.Add(session);

                if (paidAmount > 0)
                {
                    _paymentRepo.AddPayment(newSessionId, paidAmount, _currentUser.UserID);
                }

                if (!string.IsNullOrWhiteSpace(Odontogram.SelectedTooth))
                {
                    _sessionRepo.AddToothRecord(newSessionId, Odontogram.SelectedTooth, "Treated", null);
                }
            }

            if (_visitId.HasValue)
            {
                _queueRepo.UpdateStatus(_visitId.Value, VisitStatus.Completed, _currentUser.UserID);
            }

            var savedMessage = _visitId.HasValue
                ? "Session saved and visit completed successfully"
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