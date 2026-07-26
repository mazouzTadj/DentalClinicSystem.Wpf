using System.Collections.ObjectModel;
using System.Configuration;
using System.Windows;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;

namespace DentalClinic.DoctorApp;

public partial class AdvancedSearchWindow : Window
{
    private readonly SessionRepository _sessionRepo;
    private readonly UserAccount _currentUser;
    public ObservableCollection<AdvancedSearchRowViewModel> Results { get; } = new();

    public AdvancedSearchWindow(UserAccount currentUser)
    {
        _currentUser = currentUser;
        InitializeComponent();
        ResultsGrid.ItemsSource = Results;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _sessionRepo = new SessionRepository(db);

        ResultCountText.Text = "Enter any combination of criteria above and click Search";
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
            var matches = _sessionRepo.AdvancedSearch(criteria);

            Results.Clear();
            foreach (var r in matches)
            {
                Results.Add(new AdvancedSearchRowViewModel(r));
            }

            ResultCountText.Text = $"{matches.Count} result(s) found";
        }
        catch (Exception ex)
        {
            ResultCountText.Text = "Search error: " + ex.Message;
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
        ResultCountText.Text = "Enter any combination of criteria above and click Search";
    }

    private void ResultsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => OpenSelectedPatientFile();

    private void OpenPatientFileButton_Click(object sender, RoutedEventArgs e) => OpenSelectedPatientFile();

    private void OpenSelectedPatientFile()
    {
        if (ResultsGrid.SelectedItem is not AdvancedSearchRowViewModel selected)
        {
            MessageBox.Show("Please select a result from the list first", "Notice",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // visitId = null: نفتح الملف للمراجعة، وليس من زيارة نشطة اليوم (لا يوجد زيارة لإنهائها هنا)
        var window = new PatientFileWindow(selected.PatientID, null, _currentUser) { Owner = this };
        window.ShowDialog();
    }
}
