using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Windows;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.Features; // PatientFileWindow انتقلت إلى المشروع المشترك
using DentalClinic.UI.Localization;

namespace DentalClinic.DoctorApp;

public partial class AdvancedSearchWindow : Window
{
    private readonly SessionRepository _sessionRepo;
    private readonly UserAccount _currentUser;
    public ObservableCollection<AdvancedSearchRowViewModel> Results { get; } = new();

    // نفس منطق قيود الرؤية بالضبط المطبَّق في قائمة الانتظار وشاشة البحث - كانت هذي الشاشة تحديداً
    // ثغرة رؤية حقيقية (نتائج البحث بالتشخيص/السن تُظهر مرضى أطباء آخرين) قبل هذا الإصلاح
    private List<int>? _allowedDoctorUserIds;
    private bool _includeUnassigned = true;

    public AdvancedSearchWindow(UserAccount currentUser)
    {
        _currentUser = currentUser;
        InitializeComponent();
        ResultsGrid.ItemsSource = Results;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _sessionRepo = new SessionRepository(db);

        ComputeVisibilityFilter(db);

        ResultCountText.Text = LocalizationManager.T("AdvSearch_InitialHint");
        EmptyResultsText.Text = LocalizationManager.T("AdvSearch_InitialHint");
        EmptyResultsPanel.Visibility = Visibility.Visible;
    }

    // نفس تعريف "الطبيب الرئيسي" الموحَّد بكل النظام (DoctorCommissionService) - رئيسي = بلا تقييد،
    // ثانوي = مرضاه فقط + الغير مُسنَدين
    private void ComputeVisibilityFilter(DatabaseHelper db)
    {
        try
        {
            var commissionService = new DoctorCommissionService(db);
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
        catch
        {
            _allowedDoctorUserIds = new List<int>();
            _includeUnassigned = true;
        }
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        var criteria = new SessionSearchCriteria
        {
            PatientNameOrPhone = string.IsNullOrWhiteSpace(NameOrPhoneBox.Text) ? null : NameOrPhoneBox.Text.Trim(),
            DiagnosisContains = string.IsNullOrWhiteSpace(DiagnosisBox.Text) ? null : DiagnosisBox.Text.Trim(),
            ToothNumber = string.IsNullOrWhiteSpace(ToothBox.Text) ? null : ToothBox.Text.Trim(),
            FromDate = FromDatePicker.SelectedDate,
            ToDate = ToDatePicker.SelectedDate,
            OnlyWithOutstandingBalance = OutstandingOnlyCheck.IsChecked == true
        };

        try
        {
            var matches = _sessionRepo.AdvancedSearch(criteria, _allowedDoctorUserIds, _includeUnassigned);

            Results.Clear();
            foreach (var r in matches)
            {
                Results.Add(new AdvancedSearchRowViewModel(r));
            }

            ResultCountText.Text = LocalizationManager.T("AdvSearch_ResultsFoundFormat", matches.Count);
            EmptyResultsText.Text = LocalizationManager.T("AdvSearch_NoResultsFound");
            EmptyResultsPanel.Visibility = Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ResultCountText.Text = LocalizationManager.T("AdvSearch_SearchErrorFormat", ex.Message);
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        NameOrPhoneBox.Clear();
        DiagnosisBox.Clear();
        ToothBox.Clear();
        FromDatePicker.SelectedDate = null;
        ToDatePicker.SelectedDate = null;
        OutstandingOnlyCheck.IsChecked = false;
        Results.Clear();
        ResultCountText.Text = LocalizationManager.T("AdvSearch_InitialHint");
        EmptyResultsText.Text = LocalizationManager.T("AdvSearch_InitialHint");
        EmptyResultsPanel.Visibility = Visibility.Visible;
    }

    private void ResultsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => OpenSelectedPatientFile();

    private void OpenPatientFileButton_Click(object sender, RoutedEventArgs e) => OpenSelectedPatientFile();

    private void OpenSelectedPatientFile()
    {
        if (ResultsGrid.SelectedItem is not AdvancedSearchRowViewModel selected)
        {
            MessageBox.Show(LocalizationManager.T("AdvSearch_SelectResultFirst"), LocalizationManager.T("Common_Notice"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // visitId = null: نفتح الملف للمراجعة، وليس من زيارة نشطة اليوم (لا يوجد زيارة لإنهائها هنا)
        var window = new PatientFileWindow(selected.PatientID, null, _currentUser) { Owner = this };
        window.ShowDialog();
    }
}
