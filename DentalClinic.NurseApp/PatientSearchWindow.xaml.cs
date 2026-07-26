using System.Collections.ObjectModel;
using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;

namespace DentalClinic.NurseApp;

public partial class PatientSearchWindow : Window
{
    private readonly UserAccount _currentUser;
    public ObservableCollection<PatientSearchRowViewModel> Results { get; } = new();

    public PatientSearchWindow(UserAccount currentUser)
    {
        _currentUser = currentUser;
        InitializeComponent();
        ResultsGrid.ItemsSource = Results;
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

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SearchButton_Click(sender, e);
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Foreground = (Brush)FindResource("ErrorBrush");
        StatusText.Text = string.Empty;

        var term = SearchBox.Text.Trim();

        string? gender = (GenderFilterBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (gender == "Any Gender") gender = null;

        int? minAge = null;
        if (!string.IsNullOrWhiteSpace(MinAgeBox.Text))
        {
            if (!int.TryParse(MinAgeBox.Text.Trim(), out int parsedMin) || parsedMin < 0)
            {
                StatusText.Text = "Invalid minimum age";
                return;
            }
            minAge = parsedMin;
        }

        int? maxAge = null;
        if (!string.IsNullOrWhiteSpace(MaxAgeBox.Text))
        {
            if (!int.TryParse(MaxAgeBox.Text.Trim(), out int parsedMax) || parsedMax < 0)
            {
                StatusText.Text = "Invalid maximum age";
                return;
            }
            maxAge = parsedMax;
        }

        if (string.IsNullOrWhiteSpace(term) && gender == null && minAge == null && maxAge == null)
        {
            StatusText.Text = "Enter a name/phone, or choose at least one filter";
            return;
        }

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var patientRepo = new PatientRepository(db);

            var matches = patientRepo.Search(term, gender, minAge, maxAge);
            Results.Clear();
            foreach (var p in matches)
            {
                Results.Add(new PatientSearchRowViewModel(p));
            }

            if (Results.Count == 0)
            {
                StatusText.Text = "No matching patient found";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error while searching: " + ex.Message;
        }
    }

    private void AddToQueueButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = string.Empty;

        if (ResultsGrid.SelectedItem is not PatientSearchRowViewModel selected)
        {
            StatusText.Foreground = (Brush)FindResource("ErrorBrush");
            StatusText.Text = "Please select a patient from the list first";
            return;
        }

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var queueRepo = new QueueRepository(db);

            var (success, message, _) = queueRepo.AddToQueue(selected.PatientID, _currentUser.UserID);
            StatusText.Foreground = success ? new SolidColorBrush(Color.FromRgb(0x22, 0xA0, 0x6B)) : (Brush)FindResource("ErrorBrush");
            StatusText.Text = message;

            if (success)
            {
                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            StatusText.Foreground = (Brush)FindResource("ErrorBrush");
            StatusText.Text = "Error: " + ex.Message;
        }
    }
}
