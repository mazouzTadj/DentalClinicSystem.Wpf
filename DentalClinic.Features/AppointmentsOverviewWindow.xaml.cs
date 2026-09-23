using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

// شاشة "مواعيد اليوم والقادمة" المشتركة بين تطبيق الطبيب وتطبيق المرمم (الزر الجديد في كلتا
// النافذتين الرئيسيتين). كل تطبيق يمرّر دالة تحميل (loadAppointments) بمنطق التصفية الخاص به
// (مرضى الطبيب المُسنَدون / مرضى حالات المرمم المرئية له)، وهذه النافذة لا تعرف شيئاً عن ذلك
// التمييز - فقط تعرض ما يصلها مُجمَّعاً حسب التاريخ (اليوم/غداً/تاريخ)، مع تعديل/حذف اختياريين
// إن كان المستخدم مخوَّلاً (canManage)، عبر نفس EditAppointmentDialog / QueueRepository.DeleteFutureAppointment
// المستخدَمين في تطبيق الطبيب أصلاً.
public partial class AppointmentsOverviewWindow : Window
{
    private readonly UserAccount _currentUser;
    private readonly QueueRepository _queueRepo;
    private readonly AppointmentChangeRequestRepository _requestRepo;
    private readonly DatabaseHelper _db;
    private readonly Func<List<AppointmentListItem>> _loadAppointments;
    private readonly bool _canManage;

    public class RowViewModel
    {
        public AppointmentListItem Appointment { get; }
        public int VisitID => Appointment.VisitID;
        public string PatientFullName => Appointment.PatientFullName;
        public string TimeText => Appointment.ScheduledDate.ToString("HH:mm");
        public string TreatmentText => string.IsNullOrWhiteSpace(Appointment.PlannedTreatment)
            ? LocalizationManager.T("Sched_NoPlannedTreatment")
            : Appointment.PlannedTreatment!;
        public Visibility CanManageVisibility { get; }

        public RowViewModel(AppointmentListItem appointment, bool canManage)
        {
            Appointment = appointment;
            CanManageVisibility = canManage ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public class GroupViewModel
    {
        public string HeaderText { get; }
        public List<RowViewModel> Items { get; }

        public GroupViewModel(string headerText, List<RowViewModel> items)
        {
            HeaderText = headerText;
            Items = items;
        }
    }

    public AppointmentsOverviewWindow(
        UserAccount currentUser,
        QueueRepository queueRepo,
        AppointmentChangeRequestRepository requestRepo,
        DatabaseHelper db,
        Func<List<AppointmentListItem>> loadAppointments,
        bool canManage)
    {
        InitializeComponent();
        _currentUser = currentUser;
        _queueRepo = queueRepo;
        _requestRepo = requestRepo;
        _db = db;
        _loadAppointments = loadAppointments;
        _canManage = canManage;

        RefreshList();
    }

    private void RefreshList()
    {
        List<AppointmentListItem> appointments;
        try
        {
            appointments = _loadAppointments();
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Main_ErrorPrefixFormat", ex.Message),
                LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            appointments = new List<AppointmentListItem>();
        }

        var today = DateTime.Today;
        var groups = appointments
            .OrderBy(a => a.ScheduledDate)
            .GroupBy(a => a.ScheduledDate.Date)
            .Select(g => new GroupViewModel(
                FormatGroupHeader(g.Key, today),
                g.Select(a => new RowViewModel(a, _canManage)).ToList()))
            .ToList();

        GroupsList.ItemsSource = groups;
        NoAppointmentsText.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string FormatGroupHeader(DateTime date, DateTime today)
    {
        if (date == today) return LocalizationManager.T("ApptOverview_Today");
        if (date == today.AddDays(1)) return LocalizationManager.T("ApptOverview_Tomorrow");
        return date.ToString("dd/MM/yyyy");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshList();

    private void EditAppointmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_canManage || sender is not Button { Tag: RowViewModel row }) return;

        if (_requestRepo.HasPendingRequest(row.VisitID))
        {
            MessageBox.Show(LocalizationManager.T("Sched_DirectBlockedPending"), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new EditAppointmentDialog(row.VisitID, _currentUser, _queueRepo, _db) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            RefreshList();
        }
    }

    private void DeleteAppointmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_canManage || sender is not Button { Tag: RowViewModel row }) return;

        if (_requestRepo.HasPendingRequest(row.VisitID))
        {
            MessageBox.Show(LocalizationManager.T("Sched_DirectBlockedPending"), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            LocalizationManager.T("Sched_ConfirmDeleteFormat", row.PatientFullName, row.Appointment.ScheduledDate.ToString("dd/MM/yyyy")),
            LocalizationManager.T("Sched_DeleteAppointmentTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            if (_queueRepo.DeleteFutureAppointment(row.VisitID, _currentUser))
            {
                RefreshList();
            }
            else
            {
                MessageBox.Show(LocalizationManager.T("Sched_DirectAppointmentNotFound"), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Main_ErrorPrefixFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
