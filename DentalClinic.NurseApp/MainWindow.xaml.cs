using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;

namespace DentalClinic.NurseApp;

public partial class MainWindow : Window
{
    private readonly UserAccount _currentUser;
    private readonly DatabaseHelper _db;
    private readonly QueueRepository _queueRepo;
    private readonly DispatcherTimer _refreshTimer;

    public ObservableCollection<QueueRowViewModel> QueueRows { get; } = new();

    public MainWindow(UserAccount currentUser)
    {
        _currentUser = currentUser;
        InitializeComponent();
        Title = $"Reception App - Dental Clinic | Welcome {_currentUser.FullName}";

        QueueGrid.ItemsSource = QueueRows;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        _db = new DatabaseHelper(connectionString);
        _queueRepo = new QueueRepository(_db);

        // تحديث قائمة الانتظار تلقائياً كل 4 ثوانٍ
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _refreshTimer.Tick += (s, e) => LoadQueue();

        Loaded += (s, e) =>
        {
            LoadQueue();
            _refreshTimer.Start();
        };
        Closed += (s, e) => _refreshTimer.Stop();
    }

    private void LoadQueue()
    {
        try
        {
            var queue = _queueRepo.GetTodayQueue();

            var freshIds = new HashSet<int>(queue.Select(q => q.VisitID));

            for (int i = QueueRows.Count - 1; i >= 0; i--)
            {
                if (!freshIds.Contains(QueueRows[i].VisitID))
                {
                    QueueRows.RemoveAt(i);
                }
            }

            foreach (var item in queue)
            {
                var existingRow = QueueRows.FirstOrDefault(r => r.VisitID == item.VisitID);
                if (existingRow != null)
                {
                    existingRow.UpdateFrom(item);
                }
                else
                {
                    QueueRows.Add(new QueueRowViewModel(item));
                }
            }

            CountText.Text = $"Patients today: {queue.Count}";
        }
        catch (Exception ex)
        {
            CountText.Text = "Could not load the queue: " + ex.Message;
        }
    }

    private void AddPatientButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new AddPatientWindow(_currentUser) { Owner = this };
        if (window.ShowDialog() == true)
        {
            LoadQueue();
        }
    }

    private void SearchPatientButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new PatientSearchWindow(_currentUser) { Owner = this };
        if (window.ShowDialog() == true)
        {
            LoadQueue();
        }
    }

    // 💳 حدث تسجيل الدفعات للممرضة (سواء من زر الشريط العلوي أو من زر Pay في الجدول)
    private void CollectPaymentButton_Click(object sender, RoutedEventArgs e)
    {
        QueueRowViewModel? selectedRow = null;

        if (sender is Button { Tag: QueueRowViewModel taggedRow })
        {
            selectedRow = taggedRow;
        }
        else if (QueueGrid.SelectedItem is QueueRowViewModel rowFromGrid)
        {
            selectedRow = rowFromGrid;
        }

        if (selectedRow == null)
        {
            MessageBox.Show("Please select a patient from the queue first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            // استعلام جلب آخر جلسة غير مدفوعة بالكامل لهذا المريض
            const string sql = @"
                SELECT TOP 1 SessionID 
                FROM MedicalSessions 
                WHERE PatientID = @PatientID AND TotalPrice > PaidAmount 
                ORDER BY SessionDateTime DESC";

            var table = _db.ExecuteQuery(sql, new Microsoft.Data.SqlClient.SqlParameter("@PatientID", selectedRow.PatientID));

            if (table.Rows.Count > 0)
            {
                int sessionId = Convert.ToInt32(table.Rows[0]["SessionID"]);
                var paymentWindow = new CollectPaymentWindow(sessionId, selectedRow.PatientFullName, _currentUser)
                {
                    Owner = this
                };

                if (paymentWindow.ShowDialog() == true)
                {
                    LoadQueue(); // إعادة تحميل القائمة لتحديث المبالغ والحالات
                }
            }
            else
            {
                MessageBox.Show($"No outstanding balance or unpaid session found for '{selectedRow.PatientFullName}'.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error checking payment status: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadQueue();

    private void CancelVisitButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: QueueRowViewModel row })
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"Are you sure you want to cancel this visit for \"{row.PatientFullName}\"?",
            "Confirm Cancellation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var success = _queueRepo.CancelVisit(row.VisitID, _currentUser.UserID);
            if (success)
            {
                LoadQueue();
            }
            else
            {
                MessageBox.Show("Could not cancel the visit", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Are you sure you want to log out?",
            "Confirm Logout",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _refreshTimer.Stop();

        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Hide();

        var loginWindow = new LoginWindow();
        var loginResult = loginWindow.ShowDialog();

        if (loginResult == true && loginWindow.LoggedInUser != null)
        {
            var newMainWindow = new MainWindow(loginWindow.LoggedInUser);
            Application.Current.MainWindow = newMainWindow;
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            newMainWindow.Show();

            Close();
        }
        else
        {
            Application.Current.Shutdown();
        }
    }
}