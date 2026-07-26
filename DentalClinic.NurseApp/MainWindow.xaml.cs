using System.Collections.ObjectModel;
using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;

namespace DentalClinic.NurseApp;

public partial class MainWindow : Window
{
    private readonly UserAccount _currentUser;
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
        var db = new DatabaseHelper(connectionString);
        _queueRepo = new QueueRepository(db);

        // تحديث قائمة الانتظار تلقائياً كل 4 ثوانٍ (نفس آلية Timer التي استخدمناها في WinForms)
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

            // دمج ذكي بدل Clear() + إعادة الإضافة: يحافظ على أي تحديد حالي في الجدول أثناء التحديث التلقائي
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

        // إيقاف المؤقت التلقائي فوراً لضمان عدم استهلاك أي موارد أو استعلامات في الخلفية أثناء تبديل الشاشات
        _refreshTimer.Stop();

        // هذه النافذة مسجَّلة كـ Application.MainWindow تحت ShutdownMode.OnMainWindowClose؛ لو أُغلقت الآن
        // مباشرة سيُنهي ذلك التطبيق بالكامل بالخطأ. نمنع الإغلاق التلقائي مؤقتاً حتى نُثبّت نافذة رئيسية بديلة.
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // نُخفي هذه النافذة فوراً بدل تركها ظاهرة خلف شاشة الدخول أثناء إدخال بيانات المستخدم الجديد
        Hide();

        var loginWindow = new LoginWindow();
        var loginResult = loginWindow.ShowDialog();

        if (loginResult == true && loginWindow.LoggedInUser != null)
        {
            var newMainWindow = new MainWindow(loginWindow.LoggedInUser);
            Application.Current.MainWindow = newMainWindow;
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            newMainWindow.Show();

            Close(); // إغلاق آمن الآن بعد وجود نافذة رئيسية بديلة تحمل راية التطبيق
        }
        else
        {
            // لم يسجّل أحد دخولاً جديداً: أنهِ التطبيق بالكامل بدل ترك نافذة يتيمة أو عملية معلَّقة في الخلفية
            Application.Current.Shutdown();
        }
    }
}
