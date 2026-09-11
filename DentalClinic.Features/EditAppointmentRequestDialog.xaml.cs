using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class EditAppointmentRequestDialog : Window
{
    private readonly int _visitId;
    private readonly int _requestedByUserId;
    private readonly QueueRepository _queueRepo;
    private readonly AppointmentChangeRequestRepository _requestRepo;
    private readonly DatabaseHelper _db;

    private readonly List<TreatmentPresetItem> _treatments = new();
    private List<SessionChipItem> _selectedTreatments = new();

    public EditAppointmentRequestDialog(int visitId, int requestedByUserId, QueueRepository queueRepo,
        AppointmentChangeRequestRepository requestRepo, DatabaseHelper db)
    {
        InitializeComponent();
        _visitId = visitId;
        _requestedByUserId = requestedByUserId;
        _queueRepo = queueRepo;
        _requestRepo = requestRepo;
        _db = db;

        DpAppointmentDate.DisplayDateStart = DateTime.Today.AddDays(1);

        LoadTreatments();

        // نعبّئ الحقول بالقيم الحالية للموعد (قبل أي تعديل)
        var current = _queueRepo.GetScheduledVisit(_visitId);
        if (current != null)
        {
            DpAppointmentDate.SelectedDate = current.Value.ScheduledDate.Date;

            if (!string.IsNullOrWhiteSpace(current.Value.PlannedTreatment))
            {
                var names = current.Value.PlannedTreatment.Split("; ", StringSplitOptions.RemoveEmptyEntries);
                _selectedTreatments = names.Select(n =>
                {
                    var match = _treatments.FirstOrDefault(t => t.TreatmentName == n);
                    return new SessionChipItem { Id = match?.TreatmentID ?? 0, Name = n };
                }).ToList();
            }
        }
        else
        {
            DpAppointmentDate.SelectedDate = DateTime.Today.AddDays(1);
        }

        RefreshTreatmentChips();
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

    private void LoadTreatments()
    {
        try
        {
            const string sql = "SELECT TreatmentID, TreatmentName, Price FROM TreatmentPresets WHERE IsActive = 1 ORDER BY TreatmentName ASC";
            var table = _db.ExecuteQuery(sql);

            _treatments.Clear();
            foreach (DataRow row in table.Rows)
            {
                _treatments.Add(new TreatmentPresetItem
                {
                    TreatmentID = (int)row["TreatmentID"],
                    TreatmentName = row["TreatmentName"].ToString()!,
                    Price = (decimal)row["Price"]
                });
            }
        }
        catch
        {
            // القائمة اختيارية
        }
    }

    private void PickTreatmentsButton_Click(object sender, RoutedEventArgs e)
    {
        var items = _treatments.Select(t => (t.TreatmentID, t.TreatmentName, (decimal?)t.Price, (string?)null));
        var preSelectedIds = _selectedTreatments.Select(t => t.Id);

        var picker = new ItemPickerWindow(LocalizationManager.T("PF_PickerTreatmentsTitle"), items, preSelectedIds, showTotal: false) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            _selectedTreatments = picker.SelectedItems
                .Select(i => new SessionChipItem { Id = i.Id, Name = i.Name })
                .ToList();
            RefreshTreatmentChips();
        }
    }

    private void RemoveTreatmentChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SessionChipItem chip })
        {
            _selectedTreatments = _selectedTreatments.Where(t => t != chip).ToList();
            RefreshTreatmentChips();
        }
    }

    private void RefreshTreatmentChips()
    {
        SelectedTreatmentsItems.ItemsSource = null;
        SelectedTreatmentsItems.ItemsSource = _selectedTreatments;
        NoTreatmentsSelectedText.Visibility = _selectedTreatments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        if (!DpAppointmentDate.SelectedDate.HasValue)
        {
            ErrorText.Text = LocalizationManager.T("Sched_SelectDateFirst");
            return;
        }

        var plannedTreatment = _selectedTreatments.Count > 0 ? string.Join("; ", _selectedTreatments.Select(t => t.Name)) : null;

        var created = _requestRepo.CreateChangeRequest(_visitId, DpAppointmentDate.SelectedDate.Value, plannedTreatment, _requestedByUserId);

        if (!created)
        {
            ErrorText.Text = LocalizationManager.T("Sched_EditErrorAlreadyPending");
            return;
        }

        MessageBox.Show(LocalizationManager.T("Sched_EditSuccessMessage"), LocalizationManager.T("Sched_EditSuccessTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
