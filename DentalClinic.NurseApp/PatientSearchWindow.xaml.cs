using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data;
using System.Linq;
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

        string? paymentFilter = (PaymentFilterBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

        // السماح بالبحث إذا تم إدخال نص، أو اختيار جنس، أو فلتر حالة دفع معين
        if (string.IsNullOrWhiteSpace(term) && gender == null && (paymentFilter == null || paymentFilter == "All Payment Statuses"))
        {
            StatusText.Text = "Enter a name/phone, or choose a filter";
            return;
        }

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var patientRepo = new PatientRepository(db);
            var paymentRepo = new PaymentRepository(db);

            // نمرر null للعمر الأدنى والأقصى
            var matches = patientRepo.Search(term, gender, null, null);

            // جلب قائمة المعرفات للمرضى الذين يملكون ديوناً غير مسددة (عبر PaymentRepository الموحَّد الآن)
            var unpaidPatientIds = paymentRepo.GetUnpaidPatientIds(matches.Select(p => p.PatientID));

            Results.Clear();
            foreach (var p in matches)
            {
                bool owesMoney = unpaidPatientIds.Contains(p.PatientID);

                // 💳 تصفية النتائج بناءً على الخيار المحدد في فلتر الدفع
                if (paymentFilter == "Has Unpaid Balance" && !owesMoney)
                    continue;
                if (paymentFilter == "Fully Paid" && owesMoney)
                    continue;

                Results.Add(new PatientSearchRowViewModel(p, owesMoney));
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

    private void CollectPaymentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PatientSearchRowViewModel selectedRow }) return;

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var paymentRepo = new PaymentRepository(db);

            var sessionId = paymentRepo.GetLatestUnpaidSessionId(selectedRow.PatientID);

            if (sessionId.HasValue)
            {
                var paymentWindow = new CollectPaymentWindow(sessionId.Value, selectedRow.FullName, _currentUser)
                {
                    Owner = this
                };

                if (paymentWindow.ShowDialog() == true)
                {
                    var unpaidIds = paymentRepo.GetUnpaidPatientIds(new[] { selectedRow.PatientID });
                    selectedRow.HasUnpaidBalance = unpaidIds.Contains(selectedRow.PatientID);
                }
            }
            else
            {
                MessageBox.Show($"No outstanding balance or unpaid session found for '{selectedRow.FullName}'.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error checking payment status: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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