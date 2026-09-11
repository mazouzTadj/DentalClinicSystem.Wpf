using System;
using System.Data;
using System.Windows;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class EditAppointmentDialog : Window
{
    private readonly int _visitId;
    private readonly UserAccount _currentUser;
    private readonly QueueRepository _repository;
    private readonly DatabaseHelper _db;
    private readonly List<TreatmentPresetItem> _treatments = new();
    private List<SessionChipItem> _selectedTreatments = new();

    public EditAppointmentDialog(int visitId, UserAccount currentUser, QueueRepository repository, DatabaseHelper db)
    {
        InitializeComponent();
        _visitId = visitId;
        _currentUser = currentUser;
        _repository = repository;
        _db = db;
        DpAppointmentDate.DisplayDateStart = DateTime.Today.AddDays(1);

        var appointment = _repository.GetScheduledVisit(visitId);
        if (appointment == null)
        {
            ErrorText.Text = LocalizationManager.T("Sched_DirectAppointmentNotFound");
            return;
        }

        DpAppointmentDate.SelectedDate = appointment.Value.ScheduledDate.Date;

        LoadTreatments();
        if (!string.IsNullOrWhiteSpace(appointment.Value.PlannedTreatment))
        {
            var names = appointment.Value.PlannedTreatment.Split("; ", StringSplitOptions.RemoveEmptyEntries);
            _selectedTreatments = names.Select(n =>
            {
                var match = _treatments.FirstOrDefault(t => string.Equals(t.TreatmentName, n, StringComparison.OrdinalIgnoreCase));
                return new SessionChipItem { Id = match?.TreatmentID ?? 0, Name = n };
            }).ToList();
        }
        RefreshTreatmentChips();
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
        catch { }
    }

    private void PickTreatmentsButton_Click(object sender, RoutedEventArgs e)
    {
        var items = _treatments.Select(t => (t.TreatmentID, t.TreatmentName, (decimal?)t.Price, (string?)null));
        var preSelectedIds = _selectedTreatments.Where(t => t.Id > 0).Select(t => t.Id);
        var picker = new ItemPickerWindow(LocalizationManager.T("PF_PickerTreatmentsTitle"), items, preSelectedIds, showTotal: false) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            _selectedTreatments = picker.SelectedItems.Select(i => new SessionChipItem { Id = i.Id, Name = i.Name }).ToList();
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (!DpAppointmentDate.SelectedDate.HasValue || DpAppointmentDate.SelectedDate.Value.Date <= DateTime.Today)
        {
            ErrorText.Text = LocalizationManager.T("Sched_SelectFutureDate");
            return;
        }

        try
        {
            var plannedTreatment = _selectedTreatments.Count > 0
                ? string.Join("; ", _selectedTreatments.Select(t => t.Name))
                : null;
            if (!_repository.UpdateFutureAppointment(_visitId, DpAppointmentDate.SelectedDate.Value, plannedTreatment, _currentUser))
            {
                ErrorText.Text = LocalizationManager.T("Sched_DirectAppointmentNotFound");
                return;
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }
}
