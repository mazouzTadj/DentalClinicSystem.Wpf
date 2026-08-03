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

// عنصر "شريحة" واحد معروض في منطقة العلاجات/الأدوية المختارة لهذه الجلسة
public class SessionChipItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public string PriceText => Price.HasValue && Price.Value > 0 ? Price.Value.ToString("N2", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
}

public partial class PatientFileWindow : Window
{
    private readonly int _patientId;
    private readonly int? _visitId;
    private readonly UserAccount _currentUser;

    private readonly PatientRepository _patientRepo;
    private readonly SessionRepository _sessionRepo;
    private readonly QueueRepository _queueRepo;
    private readonly MedicationPresetRepository _medicationRepo;
    private readonly DatabaseHelper _db;

    public ObservableCollection<SessionHistoryRowViewModel> History { get; } = new();
    private readonly List<TreatmentPresetItem> _treatments = new();
    private readonly List<MedicationPreset> _medications = new();

    private List<SessionChipItem> _selectedTreatments = new();
    private List<SessionChipItem> _selectedMedications = new();

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
        _medicationRepo = new MedicationPresetRepository(_db);

        if (_visitId == null)
        {
            SaveSessionButton.Content = LocalizationManager.T("PF_SaveSessionButtonReviewMode");
        }

        Loaded += (s, e) =>
        {
            LoadPatientInfo();
            LoadTreatments();
            LoadMedications();
            LoadHistory();
            RefreshTreatmentChips();
            RefreshMedicationChips();
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
        }
        catch
        {
            // تجنب أي توقف في حال حدث خطأ بسيط في الاستعلام
        }
    }

    private void LoadMedications()
    {
        try
        {
            _medications.Clear();
            _medications.AddRange(_medicationRepo.GetActivePresets());
        }
        catch
        {
            // القائمة السريعة اختيارية - عدم توفرها لا يمنع بقية العمل
        }
    }

    // 🦷 فتح نافذة الاختيار المتعدد للعلاجات - يُجمَع سعر كل ما يُختار تلقائياً في خانة التكلفة الإجمالية
    private void PickTreatmentsButton_Click(object sender, RoutedEventArgs e)
    {
        var items = _treatments.Select(t => (t.TreatmentID, t.TreatmentName, (decimal?)t.Price, (string?)null));
        var preSelectedIds = _selectedTreatments.Select(t => t.Id);

        var picker = new ItemPickerWindow(LocalizationManager.T("PF_PickerTreatmentsTitle"), items, preSelectedIds, showTotal: true) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            _selectedTreatments = picker.SelectedItems
                .Select(i => new SessionChipItem { Id = i.Id, Name = i.Name, Price = i.Price })
                .ToList();
            RefreshTreatmentChips();
            RecomputeTotalPriceFromTreatments();
        }
    }

    // 💊 فتح نافذة الاختيار المتعدد للأدوية - لا سعر لها، فقط تُضاف لقائمة أدوية الجلسة والوصفة الطبية
    private void PickMedicationsButton_Click(object sender, RoutedEventArgs e)
    {
        var items = _medications.Select(m =>
        {
            var subParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(m.DefaultDosage)) subParts.Add(m.DefaultDosage!);
            if (!string.IsNullOrWhiteSpace(m.DefaultDuration)) subParts.Add(m.DefaultDuration!);
            string? subText = subParts.Count > 0 ? string.Join(" • ", subParts) : null;
            return (m.MedicationID, m.MedicationName, (decimal?)null, subText);
        });
        var preSelectedIds = _selectedMedications.Select(m => m.Id);

        var picker = new ItemPickerWindow(LocalizationManager.T("PF_PickerMedicationsTitle"), items, preSelectedIds, showTotal: false) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            _selectedMedications = picker.SelectedItems
                .Select(i => new SessionChipItem { Id = i.Id, Name = i.Name })
                .ToList();
            RefreshMedicationChips();
        }
    }

    private void RemoveTreatmentChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SessionChipItem chip })
        {
            _selectedTreatments = _selectedTreatments.Where(t => t != chip).ToList();
            RefreshTreatmentChips();
            RecomputeTotalPriceFromTreatments();
        }
    }

    private void RemoveMedicationChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SessionChipItem chip })
        {
            _selectedMedications = _selectedMedications.Where(m => m != chip).ToList();
            RefreshMedicationChips();
        }
    }

    private void RefreshTreatmentChips()
    {
        SelectedTreatmentsItems.ItemsSource = null;
        SelectedTreatmentsItems.ItemsSource = _selectedTreatments;
        NoTreatmentsSelectedText.Visibility = _selectedTreatments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshMedicationChips()
    {
        SelectedMedicationsItems.ItemsSource = null;
        SelectedMedicationsItems.ItemsSource = _selectedMedications;
        NoMedicationsSelectedText.Visibility = _selectedMedications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // 💰 مجموع أسعار العلاجات المختارة يُعبَّأ تلقائياً في خانة التكلفة الإجمالية - تبقى قابلة للتعديل يدوياً بعدها
    private void RecomputeTotalPriceFromTreatments()
    {
        var sum = _selectedTreatments.Sum(t => t.Price ?? 0);
        TotalPriceBox.Text = sum > 0 ? sum.ToString("0.##") : string.Empty;
    }

    private void PrescriptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPatient == null)
        {
            MessageBox.Show(LocalizationManager.T("PF_PatientDataNotLoaded"), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var medicationNames = _selectedMedications.Select(m => m.Name).ToList();
        var window = new PrescriptionWindow(_currentPatient.FullName, medicationNames, _currentPatient.Age) { Owner = this };
        window.ShowDialog();
    }

    private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPatient == null)
        {
            MessageBox.Show(LocalizationManager.T("PF_PatientDataNotLoaded"), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var sessions = _sessionRepo.GetByPatient(_patientId);
            var pdfBytes = PatientFilePdfExporter.Generate(_currentPatient, sessions);

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"{_currentPatient.FullName.Replace(' ', '_')}_MedicalRecord_{DateTime.Now:yyyy-MM-dd}.pdf",
                Filter = LocalizationManager.T("PF_PdfFilter"),
                DefaultExt = ".pdf"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            System.IO.File.WriteAllBytes(dialog.FileName, pdfBytes);

            var openIt = MessageBox.Show(
                LocalizationManager.T("PF_ExportSuccessMessage"),
                LocalizationManager.T("PF_ExportCompleteTitle"),
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
            MessageBox.Show(LocalizationManager.T("PF_ExportFailedFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ScheduleAppointmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPatient == null)
        {
            MessageBox.Show(LocalizationManager.T("PF_PatientDataNotLoaded"), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show(LocalizationManager.T("PF_PatientNotFound"), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        PatientHeaderText.Text = LocalizationManager.T("PF_HeaderFormat", patient.FullName);
        _currentPatient = patient;

        var info = LocalizationManager.T("PF_InfoLineFormat", patient.Age?.ToString() ?? "-", patient.Gender ?? "-", patient.PhoneNumber);
        if (!string.IsNullOrWhiteSpace(patient.BasicMedicalNotes))
        {
            info += LocalizationManager.T("PF_BasicNotesFormat", patient.BasicMedicalNotes);
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
            TotalPriceBox.Text = lastSession.TotalPrice > 0 ? lastSession.TotalPrice.ToString("0.##") : string.Empty;

            // إعادة بناء شرائح العلاجات/الأدوية المختارة من نص آخر جلسة (مفصولة بـ "; ") - مطابقة بالاسم
            // مع القوائم النشطة الحالية للحصول على السعر/المعرِّف إن وُجدت، وإلا تُعرض كنص فقط بلا سعر
            _selectedTreatments = SplitToChips(lastSession.TreatmentPerformed,
                name => _treatments.FirstOrDefault(t => t.TreatmentName == name) is { } matchedTreatment
                    ? (matchedTreatment.TreatmentID, (decimal?)matchedTreatment.Price)
                    : (0, null));
            _selectedMedications = SplitToChips(lastSession.Medication,
                name => _medications.FirstOrDefault(med => med.MedicationName == name) is { } matchedMedication
                    ? (matchedMedication.MedicationID, (decimal?)null)
                    : (0, null));

            var lastToothNumbers = _sessionRepo.GetToothNumbersForSession(lastSession.SessionID);
            if (lastToothNumbers.Count > 0)
            {
                Odontogram.SetSelectedTooth(lastToothNumbers[0]);
            }

            PrefillNoticeText.Text = LocalizationManager.T("PF_PrefillNotice");
            PrefillNoticeText.Visibility = Visibility.Visible;
        }
    }

    // مساعد صغير: يحوّل نص "أ; ب; ج" المخزَّن في الجلسة إلى قائمة شرائح، مع محاولة مطابقة كل اسم
    // بالقائمة النشطة الحالية (عبر resolver) للحصول على السعر/المعرِّف الصحيحين إن كان لا يزال موجوداً
    private static List<SessionChipItem> SplitToChips(string? raw, Func<string, (int Id, decimal? Price)> resolver)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<SessionChipItem>();

        return raw.Split("; ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name =>
            {
                var (id, price) = resolver(name);
                return new SessionChipItem { Id = id, Name = name, Price = price };
            })
            .ToList();
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
                ErrorText.Text = LocalizationManager.T("PF_InvalidTotalPrice");
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
                TreatmentPerformed = _selectedTreatments.Count > 0 ? string.Join("; ", _selectedTreatments.Select(t => t.Name)) : null,
                Medication = _selectedMedications.Count > 0 ? string.Join("; ", _selectedMedications.Select(m => m.Name)) : null,
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
                ? LocalizationManager.T("PF_SavedMessageVisit")
                : LocalizationManager.T("PF_SavedMessageNoVisit");

            MessageBox.Show(savedMessage, LocalizationManager.T("PF_SavedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("PF_SaveErrorFormat", ex.Message);
        }
    }
}