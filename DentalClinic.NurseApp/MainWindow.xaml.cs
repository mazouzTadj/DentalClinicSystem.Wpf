using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.Features; // الشاشات المشتركة (PatientFileWindow, FinancialDashboardWindow, BackupWindow, ...)

namespace DentalClinic.NurseApp;

public partial class MainWindow : Window
{
    private readonly UserAccount _currentUser;
    private readonly DatabaseHelper _db;
    private readonly QueueRepository _queueRepo;
    private readonly PaymentRepository _paymentRepo;
    private readonly DispatcherTimer _refreshTimer;

    public ObservableCollection<QueueRowViewModel> QueueRows { get; } = new();

    public MainWindow(UserAccount currentUser)
    {
        _currentUser = currentUser;
        InitializeComponent();
        Title = $"Reception App - Dental Clinic | Welcome {_currentUser.FullName}";

        // كل زر يظهر فقط إذا كان المستخدم الحالي يملك الصلاحية المقابلة له - مستقلة تماماً عن الدور (Doctor/Nurse)
        OpenPatientFileButton.Visibility = ToVisibility(_currentUser.HasPermission(UserPermission.OpenPatientFile));
        RegisterPatientButton.Visibility = ToVisibility(_currentUser.HasPermission(UserPermission.RegisterPatients));
        CollectPaymentTopButton.Visibility = ToVisibility(_currentUser.HasPermission(UserPermission.CollectPayments));
        ManageTreatmentsButton.Visibility = ToVisibility(_currentUser.HasPermission(UserPermission.ManageTreatments));
        BackupButton.Visibility = ToVisibility(_currentUser.HasPermission(UserPermission.AccessBackup));
        ManageUsersButton.Visibility = ToVisibility(_currentUser.HasPermission(UserPermission.ManageUsers));
        FinancialDashboardButton.Visibility = ToVisibility(_currentUser.HasPermission(UserPermission.AccessFinance));

        QueueGrid.ItemsSource = QueueRows;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        _db = new DatabaseHelper(connectionString);
        _queueRepo = new QueueRepository(_db);
        _paymentRepo = new PaymentRepository(_db);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _refreshTimer.Tick += (s, e) => LoadQueue();

        Loaded += (s, e) =>
        {
            LoadQueue();
            _refreshTimer.Start();
        };
        Closed += (s, e) => _refreshTimer.Stop();
    }

    private static Visibility ToVisibility(bool hasPermission) =>
        hasPermission ? Visibility.Visible : Visibility.Collapsed;

    private void LoadQueue()
    {
        try
        {
            var queue = _queueRepo.GetTodayQueue();

            // جلب قائمة المرضى الذين لديهم ديون غير مسددة (الآن عبر PaymentRepository الموحَّد بدل استعلام محلي مكرر)
            var unpaidPatientIds = _paymentRepo.GetUnpaidPatientIds();

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
                bool owesMoney = unpaidPatientIds.Contains(item.PatientID);
                var existingRow = QueueRows.FirstOrDefault(r => r.VisitID == item.VisitID);

                if (existingRow != null)
                {
                    existingRow.UpdateFrom(item);
                    existingRow.HasUnpaidBalance = owesMoney; // تحديث حالة الدفع تلقائياً
                }
                else
                {
                    var newRow = new QueueRowViewModel(item)
                    {
                        HasUnpaidBalance = owesMoney
                    };
                    QueueRows.Add(newRow);
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
        // فحص دفاعي إضافي: حتى لو ظهر الزر بطريقة غير متوقعة، لن تُفتح الشاشة إلا لمن يملك الصلاحية فعلاً
        if (!_currentUser.HasPermission(UserPermission.RegisterPatients))
        {
            return;
        }

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

    // 🩺 فتح نافذة إدارة العلاجات والأسعار - نفس الشاشة المستخدَمة في تطبيق الطبيب
    private void ManageTreatmentsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentUser.HasPermission(UserPermission.ManageTreatments))
        {
            return;
        }

        var window = new TreatmentManagementWindow { Owner = this };
        window.ShowDialog();
    }

    private void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentUser.HasPermission(UserPermission.AccessBackup))
        {
            return;
        }

        var window = new BackupWindow { Owner = this };
        window.ShowDialog();
    }

    private void ManageUsersButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentUser.HasPermission(UserPermission.ManageUsers))
        {
            return;
        }

        var window = new UserManagementWindow(_currentUser) { Owner = this };
        window.ShowDialog();
    }

    private void FinancialDashboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentUser.HasPermission(UserPermission.AccessFinance))
        {
            return;
        }

        var window = new FinancialDashboardWindow { Owner = this };
        window.ShowDialog();
    }

    // فتح الملف الطبي الكامل للمريض المحدَّد في قائمة الانتظار (زر علوي أو نقر مزدوج على الصف)
    private void OpenPatientFileButton_Click(object sender, RoutedEventArgs e) => OpenSelectedPatientFile();

    private void QueueGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedPatientFile();

    private void OpenSelectedPatientFile()
    {
        // فحص دفاعي: حتى لو ظهر الزر أو استُخدم النقر المزدوج بطريقة غير متوقعة،
        // لن يُفتح الملف الطبي إلا لمن يملك صلاحية OpenPatientFile فعلاً.
        if (!_currentUser.HasPermission(UserPermission.OpenPatientFile))
        {
            MessageBox.Show("You don't have permission to open patient files.", "Access Denied",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (QueueGrid.SelectedItem is not QueueRowViewModel selected)
        {
            MessageBox.Show("Please select a patient from the list first", "Notice",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // نفس شاشة الملف الطبي المستخدَمة في المشروع المشترك (DentalClinic.Features.PatientFileWindow)
        var window = new PatientFileWindow(selected.PatientID, selected.VisitID, _currentUser)
        {
            Owner = this
        };
        window.ShowDialog();
        LoadQueue();
    }

    private void CollectPaymentButton_Click(object sender, RoutedEventArgs e)
    {
        // فحص دفاعي: يغطي زر الشريط العلوي وزر كل صف في القائمة معاً، حتى لو ظهر أحدهما بطريقة غير متوقعة
        if (!_currentUser.HasPermission(UserPermission.CollectPayments))
        {
            MessageBox.Show("You don't have permission to collect payments.", "Access Denied",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

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
            var sessionId = _paymentRepo.GetLatestUnpaidSessionId(selectedRow.PatientID);

            if (sessionId.HasValue)
            {
                var paymentWindow = new CollectPaymentWindow(sessionId.Value, selectedRow.PatientFullName, _currentUser)
                {
                    Owner = this
                };

                if (paymentWindow.ShowDialog() == true)
                {
                    LoadQueue(); // إعادة تحميل القائمة لتتغير حالة الزر فوراً إلى Paid
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

    // إصلاح: زر جديد لتسجيل حضور موعد محجوز (Scheduled) فعلياً عند وصول المريض في يوم موعده
    private void CheckInButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: QueueRowViewModel row })
        {
            return;
        }

        try
        {
            var success = _queueRepo.CheckInScheduledVisit(row.VisitID, _currentUser.UserID);
            if (success)
            {
                LoadQueue();
            }
            else
            {
                MessageBox.Show("Could not check in this appointment.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

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