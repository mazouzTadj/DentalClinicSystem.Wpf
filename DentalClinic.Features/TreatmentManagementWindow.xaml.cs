using System;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using Microsoft.Data.SqlClient;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public class TreatmentGridRowModel
{
    public int TreatmentID { get; set; }
    public string TreatmentName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string PriceText => Price.ToString("0.##");
}

public partial class TreatmentManagementWindow : Window
{
    private readonly DatabaseHelper _db;
    private readonly MedicationPresetRepository _medicationRepo;
    private readonly DiagnosisPresetRepository _diagnosisRepo;
    private readonly CertificatePresetRepository _certificateRepo;
    public ObservableCollection<TreatmentGridRowModel> TreatmentsList { get; } = new();
    public ObservableCollection<MedicationPreset> MedicationsList { get; } = new();
    public ObservableCollection<DiagnosisPreset> DiagnosesList { get; } = new();
    public ObservableCollection<CertificatePreset> CertificatesList { get; } = new();

    private int? _editingTreatmentId = null;
    private int? _editingMedicationId = null;
    private int? _editingDiagnosisId = null;
    private int? _editingCertificateId = null;

    public TreatmentManagementWindow()
    {
        InitializeComponent();

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        _db = new DatabaseHelper(connectionString);
        _medicationRepo = new MedicationPresetRepository(_db);
        _diagnosisRepo = new DiagnosisPresetRepository(_db);
        _certificateRepo = new CertificatePresetRepository(_db);

        TreatmentsGrid.ItemsSource = TreatmentsList;
        MedicationsGrid.ItemsSource = MedicationsList;
        DiagnosesGrid.ItemsSource = DiagnosesList;
        CertificatesGrid.ItemsSource = CertificatesList;

        Loaded += (s, e) =>
        {
            EnsureTableExists();
            LoadTreatments();
            LoadMedications();
            LoadDiagnoses();
            LoadCertificates();
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // ===================== تبديل بين لوحات "العلاجات" و"الأدوية" و"التشخيصات" - نفس نمط تبويبات لوحة الفاينانس =====================
    private void TabTreatments_Click(object sender, RoutedEventArgs e)
    {
        TreatmentsPanelGrid.Visibility = Visibility.Visible;
        MedicationsPanelGrid.Visibility = Visibility.Collapsed;
        DiagnosesPanelGrid.Visibility = Visibility.Collapsed;
        CertificatesPanelGrid.Visibility = Visibility.Collapsed;
        TabTreatmentsButton.Style = (Style)FindResource("PrimaryButtonStyle");
        TabMedicationsButton.Style = (Style)FindResource("SecondaryButtonStyle");
        TabDiagnosesButton.Style = (Style)FindResource("SecondaryButtonStyle");
        TabCertificatesButton.Style = (Style)FindResource("SecondaryButtonStyle");
    }

    private void TabMedications_Click(object sender, RoutedEventArgs e)
    {
        MedicationsPanelGrid.Visibility = Visibility.Visible;
        TreatmentsPanelGrid.Visibility = Visibility.Collapsed;
        DiagnosesPanelGrid.Visibility = Visibility.Collapsed;
        CertificatesPanelGrid.Visibility = Visibility.Collapsed;
        TabMedicationsButton.Style = (Style)FindResource("PrimaryButtonStyle");
        TabTreatmentsButton.Style = (Style)FindResource("SecondaryButtonStyle");
        TabDiagnosesButton.Style = (Style)FindResource("SecondaryButtonStyle");
        TabCertificatesButton.Style = (Style)FindResource("SecondaryButtonStyle");
    }

    private void TabDiagnoses_Click(object sender, RoutedEventArgs e)
    {
        DiagnosesPanelGrid.Visibility = Visibility.Visible;
        TreatmentsPanelGrid.Visibility = Visibility.Collapsed;
        MedicationsPanelGrid.Visibility = Visibility.Collapsed;
        CertificatesPanelGrid.Visibility = Visibility.Collapsed;
        TabDiagnosesButton.Style = (Style)FindResource("PrimaryButtonStyle");
        TabTreatmentsButton.Style = (Style)FindResource("SecondaryButtonStyle");
        TabMedicationsButton.Style = (Style)FindResource("SecondaryButtonStyle");
        TabCertificatesButton.Style = (Style)FindResource("SecondaryButtonStyle");
    }

    private void TabCertificates_Click(object sender, RoutedEventArgs e)
    {
        CertificatesPanelGrid.Visibility = Visibility.Visible;
        TreatmentsPanelGrid.Visibility = Visibility.Collapsed;
        MedicationsPanelGrid.Visibility = Visibility.Collapsed;
        DiagnosesPanelGrid.Visibility = Visibility.Collapsed;
        TabCertificatesButton.Style = (Style)FindResource("PrimaryButtonStyle");
        TabTreatmentsButton.Style = (Style)FindResource("SecondaryButtonStyle");
        TabMedicationsButton.Style = (Style)FindResource("SecondaryButtonStyle");
        TabDiagnosesButton.Style = (Style)FindResource("SecondaryButtonStyle");
    }

    private void EnsureTableExists()
    {
        try
        {
            const string sql = @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TreatmentPresets')
                BEGIN
                    CREATE TABLE TreatmentPresets (
                        TreatmentID INT IDENTITY(1,1) PRIMARY KEY,
                        TreatmentName NVARCHAR(200) NOT NULL,
                        Price DECIMAL(18,2) NOT NULL DEFAULT 0,
                        IsActive BIT NOT NULL DEFAULT 1
                    );
                END";
            _db.ExecuteNonQuery(sql);
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Treat_InitErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadTreatments()
    {
        try
        {
            const string sql = "SELECT TreatmentID, TreatmentName, Price FROM TreatmentPresets WHERE IsActive = 1 ORDER BY TreatmentName ASC";
            var table = _db.ExecuteQuery(sql);

            TreatmentsList.Clear();
            foreach (DataRow row in table.Rows)
            {
                TreatmentsList.Add(new TreatmentGridRowModel
                {
                    TreatmentID = (int)row["TreatmentID"],
                    TreatmentName = row["TreatmentName"].ToString()!,
                    Price = Convert.ToDecimal(row["Price"])
                });
            }
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("Treat_LoadErrorFormat", ex.Message);
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var name = TxtTreatmentName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = LocalizationManager.T("Treat_NameRequired");
            return;
        }

        decimal price = 0;
        if (!string.IsNullOrWhiteSpace(TxtPrice.Text) && (!decimal.TryParse(TxtPrice.Text.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out price) || price < 0))
        {
            ErrorText.Text = LocalizationManager.T("Treat_InvalidPrice");
            return;
        }

        try
        {
            if (_editingTreatmentId.HasValue)
            {
                // تعديل علاج حالي
                const string updateSql = "UPDATE TreatmentPresets SET TreatmentName = @Name, Price = @Price WHERE TreatmentID = @ID";
                _db.ExecuteNonQuery(updateSql,
                    new SqlParameter("@Name", name),
                    new SqlParameter("@Price", price),
                    new SqlParameter("@ID", _editingTreatmentId.Value));
            }
            else
            {
                // إضافة علاج جديد
                const string insertSql = "INSERT INTO TreatmentPresets (TreatmentName, Price, IsActive) VALUES (@Name, @Price, 1)";
                _db.ExecuteNonQuery(insertSql,
                    new SqlParameter("@Name", name),
                    new SqlParameter("@Price", price));
            }

            ResetForm();
            LoadTreatments();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("Treat_SaveErrorFormat", ex.Message);
        }
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TreatmentGridRowModel item)
        {
            _editingTreatmentId = item.TreatmentID;
            TxtTreatmentName.Text = item.TreatmentName;
            TxtPrice.Text = item.Price.ToString("0.##");

            BtnSave.Content = LocalizationManager.T("Treat_UpdateButton");
            BtnCancelEdit.Visibility = Visibility.Visible;
        }
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TreatmentGridRowModel item)
        {
            var result = MessageBox.Show(LocalizationManager.T("Treat_ConfirmDeleteFormat", item.TreatmentName), LocalizationManager.T("Treat_ConfirmDeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    const string sql = "UPDATE TreatmentPresets SET IsActive = 0 WHERE TreatmentID = @ID";
                    _db.ExecuteNonQuery(sql, new SqlParameter("@ID", item.TreatmentID));

                    if (_editingTreatmentId == item.TreatmentID) ResetForm();

                    LoadTreatments();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(LocalizationManager.T("Treat_DeleteErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void BtnCancelEdit_Click(object sender, RoutedEventArgs e)
    {
        ResetForm();
    }

    private void ResetForm()
    {
        _editingTreatmentId = null;
        TxtTreatmentName.Text = string.Empty;
        TxtPrice.Text = string.Empty;
        BtnSave.Content = LocalizationManager.T("Treat_AddButton");
        BtnCancelEdit.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
    }

    // ===================== لوحة الأدوية =====================

    private void LoadMedications()
    {
        try
        {
            MedicationsList.Clear();
            foreach (var med in _medicationRepo.GetActivePresets())
            {
                MedicationsList.Add(med);
            }
        }
        catch (Exception ex)
        {
            MedErrorText.Text = LocalizationManager.T("Treat_LoadErrorFormat", ex.Message);
        }
    }

    private void BtnSaveMed_Click(object sender, RoutedEventArgs e)
    {
        MedErrorText.Text = string.Empty;

        var name = TxtMedicationName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MedErrorText.Text = LocalizationManager.T("Med_NameRequired");
            return;
        }

        var dosage = string.IsNullOrWhiteSpace(TxtMedicationDosage.Text) ? null : TxtMedicationDosage.Text.Trim();
        var duration = string.IsNullOrWhiteSpace(TxtMedicationDuration.Text) ? null : TxtMedicationDuration.Text.Trim();

        try
        {
            if (_editingMedicationId.HasValue)
            {
                _medicationRepo.UpdatePreset(_editingMedicationId.Value, name, dosage, duration);
            }
            else
            {
                _medicationRepo.AddPreset(name, dosage, duration);
            }

            ResetMedForm();
            LoadMedications();
        }
        catch (Exception ex)
        {
            MedErrorText.Text = LocalizationManager.T("Treat_SaveErrorFormat", ex.Message);
        }
    }

    private void BtnEditMed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MedicationPreset item)
        {
            _editingMedicationId = item.MedicationID;
            TxtMedicationName.Text = item.MedicationName;
            TxtMedicationDosage.Text = item.DefaultDosage ?? string.Empty;
            TxtMedicationDuration.Text = item.DefaultDuration ?? string.Empty;

            BtnSaveMed.Content = LocalizationManager.T("Treat_UpdateButton");
            BtnCancelEditMed.Visibility = Visibility.Visible;
        }
    }

    private void BtnDeleteMed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MedicationPreset item)
        {
            var result = MessageBox.Show(LocalizationManager.T("Treat_ConfirmDeleteFormat", item.MedicationName), LocalizationManager.T("Treat_ConfirmDeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _medicationRepo.DeactivatePreset(item.MedicationID);

                    if (_editingMedicationId == item.MedicationID) ResetMedForm();

                    LoadMedications();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(LocalizationManager.T("Treat_DeleteErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void BtnCancelEditMed_Click(object sender, RoutedEventArgs e)
    {
        ResetMedForm();
    }

    private void ResetMedForm()
    {
        _editingMedicationId = null;
        TxtMedicationName.Text = string.Empty;
        TxtMedicationDosage.Text = string.Empty;
        TxtMedicationDuration.Text = string.Empty;
        BtnSaveMed.Content = LocalizationManager.T("Med_AddButton");
        BtnCancelEditMed.Visibility = Visibility.Collapsed;
        MedErrorText.Text = string.Empty;
    }

    // ===================== لوحة التشخيصات =====================

    private void LoadDiagnoses()
    {
        try
        {
            DiagnosesList.Clear();
            foreach (var diag in _diagnosisRepo.GetActivePresets())
            {
                DiagnosesList.Add(diag);
            }
        }
        catch (Exception ex)
        {
            DiagErrorText.Text = LocalizationManager.T("Treat_LoadErrorFormat", ex.Message);
        }
    }

    private void BtnSaveDiag_Click(object sender, RoutedEventArgs e)
    {
        DiagErrorText.Text = string.Empty;

        var name = TxtDiagnosisName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            DiagErrorText.Text = LocalizationManager.T("Diag_NameRequired");
            return;
        }

        try
        {
            if (_editingDiagnosisId.HasValue)
            {
                _diagnosisRepo.UpdatePreset(_editingDiagnosisId.Value, name);
            }
            else
            {
                _diagnosisRepo.AddPreset(name);
            }

            ResetDiagForm();
            LoadDiagnoses();
        }
        catch (Exception ex)
        {
            DiagErrorText.Text = LocalizationManager.T("Treat_SaveErrorFormat", ex.Message);
        }
    }

    private void BtnEditDiag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DiagnosisPreset item)
        {
            _editingDiagnosisId = item.DiagnosisID;
            TxtDiagnosisName.Text = item.DiagnosisName;

            BtnSaveDiag.Content = LocalizationManager.T("Treat_UpdateButton");
            BtnCancelEditDiag.Visibility = Visibility.Visible;
        }
    }

    private void BtnDeleteDiag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DiagnosisPreset item)
        {
            var result = MessageBox.Show(LocalizationManager.T("Treat_ConfirmDeleteFormat", item.DiagnosisName), LocalizationManager.T("Treat_ConfirmDeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _diagnosisRepo.DeactivatePreset(item.DiagnosisID);

                    if (_editingDiagnosisId == item.DiagnosisID) ResetDiagForm();

                    LoadDiagnoses();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(LocalizationManager.T("Treat_DeleteErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void BtnCancelEditDiag_Click(object sender, RoutedEventArgs e)
    {
        ResetDiagForm();
    }

    private void ResetDiagForm()
    {
        _editingDiagnosisId = null;
        TxtDiagnosisName.Text = string.Empty;
        BtnSaveDiag.Content = LocalizationManager.T("Diag_AddButton");
        BtnCancelEditDiag.Visibility = Visibility.Collapsed;
        DiagErrorText.Text = string.Empty;
    }

    // ===================== لوحة الشهادات الطبية / العطل المرضية =====================

    private void LoadCertificates()
    {
        try
        {
            CertificatesList.Clear();
            foreach (var cert in _certificateRepo.GetActivePresets())
            {
                CertificatesList.Add(cert);
            }
        }
        catch (Exception ex)
        {
            CertErrorText.Text = LocalizationManager.T("Treat_LoadErrorFormat", ex.Message);
        }
    }

    private void BtnSaveCert_Click(object sender, RoutedEventArgs e)
    {
        CertErrorText.Text = string.Empty;

        var name = TxtCertificateName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            CertErrorText.Text = LocalizationManager.T("Cert_NameRequired");
            return;
        }

        var text = string.IsNullOrWhiteSpace(TxtCertificateText.Text) ? null : TxtCertificateText.Text.Trim();

        try
        {
            if (_editingCertificateId.HasValue)
            {
                _certificateRepo.UpdatePreset(_editingCertificateId.Value, name, text);
            }
            else
            {
                _certificateRepo.AddPreset(name, text);
            }

            ResetCertForm();
            LoadCertificates();
        }
        catch (Exception ex)
        {
            CertErrorText.Text = LocalizationManager.T("Treat_SaveErrorFormat", ex.Message);
        }
    }

    private void BtnEditCert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CertificatePreset item)
        {
            _editingCertificateId = item.CertificateID;
            TxtCertificateName.Text = item.CertificateName;
            TxtCertificateText.Text = item.DefaultText ?? string.Empty;

            BtnSaveCert.Content = LocalizationManager.T("Treat_UpdateButton");
            BtnCancelEditCert.Visibility = Visibility.Visible;
        }
    }

    private void BtnDeleteCert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CertificatePreset item)
        {
            var result = MessageBox.Show(LocalizationManager.T("Treat_ConfirmDeleteFormat", item.CertificateName), LocalizationManager.T("Treat_ConfirmDeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _certificateRepo.DeactivatePreset(item.CertificateID);

                    if (_editingCertificateId == item.CertificateID) ResetCertForm();

                    LoadCertificates();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(LocalizationManager.T("Treat_DeleteErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void BtnCancelEditCert_Click(object sender, RoutedEventArgs e)
    {
        ResetCertForm();
    }

    private void ResetCertForm()
    {
        _editingCertificateId = null;
        TxtCertificateName.Text = string.Empty;
        TxtCertificateText.Text = string.Empty;
        BtnSaveCert.Content = LocalizationManager.T("Cert_AddButton");
        BtnCancelEditCert.Visibility = Visibility.Collapsed;
        CertErrorText.Text = string.Empty;
    }
}