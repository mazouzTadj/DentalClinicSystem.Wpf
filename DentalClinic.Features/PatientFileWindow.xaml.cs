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
    private readonly int? _requestedSessionId;
    private int? _editingSessionId;
    private byte[]? _editingSessionRowVersion;
    private readonly UserAccount _currentUser;

    private readonly PatientRepository _patientRepo;
    private readonly SessionRepository _sessionRepo;
    private readonly QueueRepository _queueRepo;
    private readonly MedicationPresetRepository _medicationRepo;
    private readonly DiagnosisPresetRepository _diagnosisRepo;
    private readonly CertificatePresetRepository _certificateRepo;
    private readonly DatabaseHelper _db;

    public ObservableCollection<SessionHistoryRowViewModel> History { get; } = new();
    private readonly List<TreatmentPresetItem> _treatments = new();
    private readonly List<MedicationPreset> _medications = new();
    private readonly List<DiagnosisPreset> _diagnoses = new();
    private readonly List<CertificatePreset> _certificates = new();

    private List<SessionChipItem> _selectedTreatments = new();
    private List<SessionChipItem> _selectedMedications = new();
    private List<SessionChipItem> _selectedDiagnoses = new();
    private List<SessionChipItem> _selectedCertificates = new();

    private Patient? _currentPatient;

    // نفس منطق قيود الرؤية في PatientSearchWindow بالضبط - طبقة حماية إضافية هنا حتى لو وصل الطلب
    // من مسار بحث آخر (مثلاً AdvancedSearchWindow) ما راعى القيد بنفسه
    private List<int>? _allowedDoctorUserIds;
    private bool _includeUnassigned = true;

    public PatientFileWindow(int patientId, int? visitId, UserAccount currentUser, int? sessionId = null)
    {
        _patientId = patientId;
        _visitId = visitId;
        _requestedSessionId = sessionId;
        _currentUser = currentUser;

        InitializeComponent();
        HistoryGrid.ItemsSource = History;

        // على الشاشات الصغيرة، الارتفاع الثابت (870) يتجاوز مساحة الشاشة المرئية، فيُركَّز
        // النافذة بحيث يظهر جزء منها فوق حافة الشاشة العلوية - بما فيه شريط العنوان وزر
        // الإغلاق، فيصبح المستخدم عالقاً بلا طريقة لإغلاقها. نحدّ الارتفاع هنا ليطابق الحد
        // الأقصى المتاح فعلياً (بهامش بسيط)، والمحتوى أصلاً داخل ScrollViewer فيتولى أي فائض.
        var maxAvailableHeight = SystemParameters.WorkArea.Height - 20;
        if (Height > maxAvailableHeight) Height = maxAvailableHeight;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        _db = new DatabaseHelper(connectionString);
        _patientRepo = new PatientRepository(_db);
        _sessionRepo = new SessionRepository(_db);
        _queueRepo = new QueueRepository(_db);
        _medicationRepo = new MedicationPresetRepository(_db);
        _diagnosisRepo = new DiagnosisPresetRepository(_db);
        _certificateRepo = new CertificatePresetRepository(_db);

        ComputeVisibilityFilter();

        // زر حالات الترميم: للطبيب فقط (راجع البند 5 في متطلبات نظام المرمم - الطبيب الرئيسي
        // هو من يفتح ملف المريض وينشئ حالة الترميم، وليس الممرضة)
        ProstheticCasesButton.Visibility = _currentUser.Role == UserRole.Doctor ? Visibility.Visible : Visibility.Collapsed;

        if (_visitId == null)
        {
            SaveSessionButton.Content = LocalizationManager.T("PF_SaveSessionButtonReviewMode");
        }

        Loaded += (s, e) =>
        {
            LoadPatientInfo();
            LoadTreatments();
            LoadMedications();
            LoadDiagnoses();
            LoadCertificates();
            LoadHistory();
            RefreshTreatmentChips();
            RefreshMedicationChips();
            RefreshDiagnosisChips();
            RefreshCertificateChips();
        };
    }

    // يحسب من يقدر هذا المستخدم يشوف مرضاهم - نفس القاعدة بالضبط المطبَّقة في قائمة الانتظار
    // وشاشة البحث: طبيب رئيسي = بلا تقييد، طبيب ثانوي = مرضاه فقط + الغير مُسنَدين،
    // ممرضة = فقط الأطباء اللي مُنحت صلاحية عليهم + الغير مُسنَدين دائماً
    private void ComputeVisibilityFilter()
    {
        try
        {
            var userRepo = new UserRepository(_db);

            if (_currentUser.Role == UserRole.Doctor)
            {
                var commissionService = new DoctorCommissionService(_db);
                var primaryDoctorId = commissionService.GetPrimaryDoctorUserId();
                var isPrimaryDoctor = primaryDoctorId.HasValue && primaryDoctorId.Value == _currentUser.UserID;

                if (isPrimaryDoctor)
                {
                    _allowedDoctorUserIds = null;
                    _includeUnassigned = true;
                }
                else
                {
                    _allowedDoctorUserIds = new List<int> { _currentUser.UserID };
                    _includeUnassigned = true;
                }
            }
            else
            {
                var accessRepo = new NurseDoctorAccessRepository(_db);
                _allowedDoctorUserIds = accessRepo.GetAllowedDoctorIds(_currentUser.UserID);
                _includeUnassigned = true;
            }
        }
        catch
        {
            // فشل نادر بحساب القيد - نطبّق أشد تقييد ممكن احتياطاً بدل ترك النافذة بلا أي تقييد بالخطأ
            _allowedDoctorUserIds = new List<int>();
            _includeUnassigned = true;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ProstheticCasesButton_Click(object sender, RoutedEventArgs e)
    {
        // فحص دفاعي إضافي (نفس نمط ComputeVisibilityFilter وباقي النافذة): لا يكفي إخفاء الزر
        // في الواجهة وحده - راجع بند الأمان في متطلبات نظام المرمم (التحقق في الواجهة والكود الخلفي)
        if (_currentUser.Role != UserRole.Doctor) return;

        var window = new ProstheticCaseWindow(_patientId, _currentUser) { Owner = this };
        window.ShowDialog();
    }

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

    private void LoadDiagnoses()
    {
        try
        {
            _diagnoses.Clear();
            _diagnoses.AddRange(_diagnosisRepo.GetActivePresets());
        }
        catch
        {
            // القائمة السريعة اختيارية - عدم توفرها لا يمنع بقية العمل
        }
    }

    private void LoadCertificates()
    {
        try
        {
            _certificates.Clear();
            _certificates.AddRange(_certificateRepo.GetActivePresets());
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

    private void PickDiagnosesButton_Click(object sender, RoutedEventArgs e)
    {
        var items = _diagnoses.Select(d => (d.DiagnosisID, d.DiagnosisName, (decimal?)null, (string?)null));
        var preSelectedIds = _selectedDiagnoses.Select(d => d.Id);

        var picker = new ItemPickerWindow(LocalizationManager.T("PF_PickerDiagnosesTitle"), items, preSelectedIds, showTotal: false) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            _selectedDiagnoses = picker.SelectedItems
                .Select(i => new SessionChipItem { Id = i.Id, Name = i.Name })
                .ToList();
            RefreshDiagnosisChips();
        }
    }

    // 📄 فتح نافذة الاختيار المتعدد للشهادات الطبية/العطل - لا سعر لها، فقط تُضاف لقائمة شهادات الجلسة والوصفة الطبية
    private void PickCertificatesButton_Click(object sender, RoutedEventArgs e)
    {
        var items = _certificates.Select(c =>
        {
            var subText = !string.IsNullOrWhiteSpace(c.DefaultText) && c.DefaultText.Length > 40
                ? c.DefaultText[..40] + "…"
                : c.DefaultText;
            return (c.CertificateID, c.CertificateName, (decimal?)null, subText);
        });
        var preSelectedIds = _selectedCertificates.Select(c => c.Id);

        var picker = new ItemPickerWindow(LocalizationManager.T("PF_PickerCertificatesTitle"), items, preSelectedIds, showTotal: false) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            _selectedCertificates = picker.SelectedItems
                .Select(i => new SessionChipItem { Id = i.Id, Name = i.Name })
                .ToList();
            RefreshCertificateChips();
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

    private void RemoveDiagnosisChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SessionChipItem chip })
        {
            _selectedDiagnoses = _selectedDiagnoses.Where(d => d != chip).ToList();
            RefreshDiagnosisChips();
        }
    }

    private void RemoveCertificateChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SessionChipItem chip })
        {
            _selectedCertificates = _selectedCertificates.Where(c => c != chip).ToList();
            RefreshCertificateChips();
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

    private void RefreshDiagnosisChips()
    {
        SelectedDiagnosesItems.ItemsSource = null;
        SelectedDiagnosesItems.ItemsSource = _selectedDiagnoses;
        NoDiagnosesSelectedText.Visibility = _selectedDiagnoses.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshCertificateChips()
    {
        SelectedCertificatesItems.ItemsSource = null;
        SelectedCertificatesItems.ItemsSource = _selectedCertificates;
        NoCertificatesSelectedText.Visibility = _selectedCertificates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
        var certificateNames = _selectedCertificates.Select(c => c.Name).ToList();
        var window = new PrescriptionWindow(_currentPatient.FullName, medicationNames, _currentPatient.Age, certificateNames) { Owner = this };
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

        var dialog = new ScheduleAppointmentDialog(_patientId, _currentUser.UserID, _queueRepo, _db)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }

    private void LoadPatientInfo()
    {
        var patient = _patientRepo.GetById(_patientId, _allowedDoctorUserIds, _includeUnassigned);
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

        var targetSession = _requestedSessionId.HasValue
            ? sessions.FirstOrDefault(s => s.SessionID == _requestedSessionId.Value)
            : _sessionRepo.GetByPatientOnDate(_patientId, DateTime.Now.Date);

        if (targetSession != null)
        {
            _editingSessionId = targetSession.SessionID;
            _editingSessionRowVersion = targetSession.RowVersion;
            ChiefComplaintBox.Text = targetSession.ChiefComplaint ?? string.Empty;
            TotalPriceBox.Text = targetSession.TotalPrice > 0 ? targetSession.TotalPrice.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

            _selectedTreatments = SplitToChips(targetSession.TreatmentPerformed,
                name => _treatments.FirstOrDefault(t => t.TreatmentName == name) is { } matchedTreatment
                    ? (matchedTreatment.TreatmentID, (decimal?)matchedTreatment.Price)
                    : (0, null));
            _selectedMedications = SplitToChips(targetSession.Medication,
                name => _medications.FirstOrDefault(med => med.MedicationName == name) is { } matchedMedication
                    ? (matchedMedication.MedicationID, (decimal?)null)
                    : (0, null));
            _selectedDiagnoses = SplitToChips(targetSession.Diagnosis,
                name => _diagnoses.FirstOrDefault(diag => diag.DiagnosisName == name) is { } matchedDiagnosis
                    ? (matchedDiagnosis.DiagnosisID, (decimal?)null)
                    : (0, null));
            _selectedCertificates = SplitToChips(targetSession.Certificate,
                name => _certificates.FirstOrDefault(cert => cert.CertificateName == name) is { } matchedCertificate
                    ? (matchedCertificate.CertificateID, (decimal?)null)
                    : (0, null));

            var toothNumbers = _sessionRepo.GetToothNumbersForSession(targetSession.SessionID);
            Odontogram.SetSelectedTeeth(toothNumbers);

            PrefillNoticeText.Text = _requestedSessionId.HasValue
                ? LocalizationManager.T("PF_EditingSessionNotice")
                : LocalizationManager.T("PF_TodaySessionNotice");
            PrefillNoticeText.Visibility = Visibility.Visible;

            SaveSessionButton.Content = LocalizationManager.T("PF_UpdateSessionButton");
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

    private void EditSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SessionHistoryRowViewModel row }) return;
        var window = new PatientFileWindow(_patientId, null, _currentUser, row.SessionID) { Owner = this };
        if (window.ShowDialog() == true) LoadHistory();
    }

    private void DeleteSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SessionHistoryRowViewModel row }) return;
        var session = _sessionRepo.GetById(row.SessionID);
        if (session == null) return;

        var warning = session.PaidAmount > 0 || _sessionRepo.HasPayments(session.SessionID)
            ? LocalizationManager.T("PF_DeleteSessionPaidConfirm")
            : LocalizationManager.T("PF_DeleteSessionConfirm");

        var confirm = MessageBox.Show(warning, LocalizationManager.T("PF_DeleteSessionTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _sessionRepo.Delete(row.SessionID);
            if (_editingSessionId == row.SessionID) _editingSessionId = null;
            LoadHistory();
            RefreshTreatmentChips();
            RefreshMedicationChips();
            RefreshDiagnosisChips();
            RefreshCertificateChips();
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("PF_DeleteSessionErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                ErrorText.Text = LocalizationManager.T("PF_InvalidTotalPrice");
                return;
            }
        }

        try
        {
            // الطبيب يحفظ تفاصيل العلاج والتكلفة، بينما يبقى المبلغ المدفوع (PaidAmount) صفر ليتولى موظف الاستقبال/الممرضة قبضه
            var targetSession = _editingSessionId.HasValue
                ? _sessionRepo.GetById(_editingSessionId.Value)
                : _sessionRepo.GetByPatientOnDate(_patientId, DateTime.Now.Date);

            var session = new MedicalSession
            {
                SessionID = targetSession?.SessionID ?? 0,
                VisitID = targetSession?.VisitID ?? _visitId,
                PatientID = _patientId,
                DoctorID = targetSession?.DoctorID ?? _currentUser.UserID,
                ChiefComplaint = string.IsNullOrWhiteSpace(ChiefComplaintBox.Text) ? null : ChiefComplaintBox.Text.Trim(),
                Diagnosis = _selectedDiagnoses.Count > 0 ? string.Join("; ", _selectedDiagnoses.Select(d => d.Name)) : null,
                TreatmentPerformed = _selectedTreatments.Count > 0 ? string.Join("; ", _selectedTreatments.Select(t => t.Name)) : null,
                Medication = _selectedMedications.Count > 0 ? string.Join("; ", _selectedMedications.Select(m => m.Name)) : null,
                Certificate = _selectedCertificates.Count > 0 ? string.Join("; ", _selectedCertificates.Select(c => c.Name)) : null,
                TotalPrice = totalPrice,
                PaidAmount = targetSession?.PaidAmount ?? 0,
                // نحتفظ بإصدار السجل وقت فتحه؛ قراءة إصدار جديد هنا ستخفي تعارض التعديل.
                RowVersion = targetSession?.SessionID == _editingSessionId && _editingSessionRowVersion != null
                    ? _editingSessionRowVersion
                    : targetSession?.RowVersion ?? Array.Empty<byte>()
            };

            var isUpdate = targetSession != null;
            if (isUpdate)
            {
                _sessionRepo.Update(session);
                _sessionRepo.ReplaceToothRecords(session.SessionID, Odontogram.SelectedTeeth);
            }
            else
            {
                var newSessionId = _sessionRepo.Add(session);
                foreach (var toothNumber in Odontogram.SelectedTeeth)
                {
                    _sessionRepo.AddToothRecord(newSessionId, toothNumber, "Treated", null);
                }
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
        catch (ConcurrencyConflictException)
        {
            ErrorText.Text = LocalizationManager.T("PF_ConcurrencyConflict");
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("PF_SaveErrorFormat", ex.Message);
        }
    }
}
