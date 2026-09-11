using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class PendingAppointmentRequestsWindow : Window
{
    private readonly UserAccount _currentUser;
    private readonly AppointmentChangeRequestRepository _requestRepo;

    // يُغلَّف كل طلب هنا لإضافة نصوص العرض الجاهزة + حقل مسودة سبب الرفض القابل للتعديل
    public class RequestRowViewModel : INotifyPropertyChanged
    {
        public AppointmentChangeRequest Request { get; }
        public string PatientFullName => Request.PatientFullName;
        public string RequestedByLine { get; }
        public string CurrentLine { get; }
        public string ProposedLine { get; }

        private string _rejectionReasonDraft = string.Empty;
        public string RejectionReasonDraft
        {
            get => _rejectionReasonDraft;
            set { _rejectionReasonDraft = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RejectionReasonDraft))); }
        }

        public RequestRowViewModel(AppointmentChangeRequest request, string requestedByName)
        {
            Request = request;
            RequestedByLine = LocalizationManager.T("Notif_RequestedByFormat", requestedByName, request.RequestedAt.ToString("dd/MM/yyyy hh:mm tt"));
            CurrentLine = LocalizationManager.T("Notif_CurrentAppointmentFormat", request.CurrentScheduledDate.ToString("dd/MM/yyyy"),
                string.IsNullOrWhiteSpace(request.CurrentPlannedTreatment) ? LocalizationManager.T("Sched_NoPlannedTreatment") : request.CurrentPlannedTreatment);
            ProposedLine = LocalizationManager.T("Notif_ProposedAppointmentFormat", request.NewScheduledDate.ToString("dd/MM/yyyy"),
                string.IsNullOrWhiteSpace(request.NewPlannedTreatment) ? LocalizationManager.T("Sched_NoPlannedTreatment") : request.NewPlannedTreatment);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public PendingAppointmentRequestsWindow(UserAccount currentUser, AppointmentChangeRequestRepository requestRepo, UserRepository userRepo, List<AppointmentChangeRequest> requests)
    {
        InitializeComponent();
        _currentUser = currentUser;
        _requestRepo = requestRepo;

        // اسم من قدَّم كل طلب - يُجلَب مرة واحدة هنا بدل استعلام منفصل لكل طلب
        var allUsers = userRepo.GetAllUsers();

        var rows = requests.Select(r =>
        {
            var requestedByName = allUsers.FirstOrDefault(u => u.UserID == r.RequestedByUserID)?.FullName ?? "-";
            return new RequestRowViewModel(r, requestedByName);
        }).ToList();

        RequestsList.ItemsSource = rows;
        NoRequestsText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ApproveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RequestRowViewModel row }) return;

        _requestRepo.ApproveRequest(row.Request.RequestID, _currentUser.UserID);
        MessageBox.Show(LocalizationManager.T("Notif_ApprovedMsg"), LocalizationManager.T("Notif_PendingRequestsTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        RemoveRowAndRefresh(row);
    }

    private void RejectButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RequestRowViewModel row }) return;

        _requestRepo.RejectRequest(row.Request.RequestID, _currentUser.UserID, row.RejectionReasonDraft);
        MessageBox.Show(LocalizationManager.T("Notif_RejectedMsg"), LocalizationManager.T("Notif_PendingRequestsTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        RemoveRowAndRefresh(row);
    }

    private void RemoveRowAndRefresh(RequestRowViewModel row)
    {
        if (RequestsList.ItemsSource is not List<RequestRowViewModel> current) return;

        var updated = current.Where(r => r != row).ToList();
        RequestsList.ItemsSource = updated;
        NoRequestsText.Visibility = updated.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (updated.Count == 0)
        {
            DialogResult = true;
            Close();
        }
    }
}
