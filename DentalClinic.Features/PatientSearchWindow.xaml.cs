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
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class PatientSearchWindow : Window
{
    private readonly UserAccount _currentUser;
    public ObservableCollection<PatientSearchRowViewModel> Results { get; } = new();

    public PatientSearchWindow(UserAccount currentUser)
    {
        _currentUser = currentUser;
        InitializeComponent();
        ResultsGrid.ItemsSource = Results;

        // عمود فتح الملف الطبي بأكمله يُخفى لمن لا يملك صلاحية OpenPatientFile
        OpenFileColumn.Visibility = _currentUser.HasPermission(UserPermission.OpenPatientFile)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OpenPatientFileButton_Click(object sender, RoutedEventArgs e)
    {
        // فحص دفاعي إضافي: حتى لو ظهر الزر بطريقة غير متوقعة، لن يُفتح الملف إلا لمن يملك الصلاحية فعلاً
        if (!_currentUser.HasPermission(UserPermission.OpenPatientFile))
        {
            MessageBox.Show(LocalizationManager.T("Main_NoPermissionOpenFile"), LocalizationManager.T("Common_AccessDenied"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (sender is not Button { Tag: PatientSearchRowViewModel selectedRow }) return;

        // نفس شاشة الملف الطبي المستخدَمة في تطبيق الطبيب (PatientFileWindow) - الآن كلاهما في نفس المشروع المشترك DentalClinic.Features
        var window = new PatientFileWindow(selectedRow.PatientID, null, _currentUser)
        {
            Owner = this
        };
        window.ShowDialog();
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

        // نقرأ Tag (قيمة ثابتة: Any/Male/Female، All/Unpaid/Paid) بدل Content (النص المترجَم المعروض)
        // حتى تعمل المقارنة بشكل صحيح بغض النظر عن لغة الواجهة الحالية
        string? genderTag = (GenderFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        string? gender = genderTag == "Male" ? "Male" : genderTag == "Female" ? "Female" : null;

        string? paymentFilter = (PaymentFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        // السماح بالبحث إذا تم إدخال نص، أو اختيار جنس، أو فلتر حالة دفع معين
        if (string.IsNullOrWhiteSpace(term) && gender == null && (paymentFilter == null || paymentFilter == "All"))
        {
            StatusText.Text = LocalizationManager.T("Search_EnterTermOrFilter");
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
                if (paymentFilter == "Unpaid" && !owesMoney)
                    continue;
                if (paymentFilter == "Paid" && owesMoney)
                    continue;

                Results.Add(new PatientSearchRowViewModel(p, owesMoney));
            }

            if (Results.Count == 0)
            {
                StatusText.Text = LocalizationManager.T("Search_NoMatchFound");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationManager.T("Search_ErrorWhileSearchingFormat", ex.Message);
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
                MessageBox.Show(LocalizationManager.T("Main_NoUnpaidSessionFormat", selectedRow.FullName), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Main_ErrorCheckingPaymentFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddToQueueButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = string.Empty;

        if (ResultsGrid.SelectedItem is not PatientSearchRowViewModel selected)
        {
            StatusText.Foreground = (Brush)FindResource("ErrorBrush");
            StatusText.Text = LocalizationManager.T("Main_SelectPatientFirst");
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
            StatusText.Text = LocalizationManager.T("Main_ErrorPrefixFormat", ex.Message);
        }
    }
}