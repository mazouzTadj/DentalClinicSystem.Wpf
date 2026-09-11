using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class ScheduleAppointmentDialog : Window
{
    private readonly int _patientId;
    private readonly int _currentUserId;
    private readonly QueueRepository _repository;
    private readonly DatabaseHelper _db;

    private readonly List<TreatmentPresetItem> _treatments = new();
    private List<SessionChipItem> _selectedTreatments = new();

    private class SameDayPatientRow
    {
        public string Name { get; set; } = string.Empty;
        public string Treatment { get; set; } = string.Empty;
    }

    public ScheduleAppointmentDialog(int patientId, int currentUserId, QueueRepository repository, DatabaseHelper db)
    {
        InitializeComponent();
        _patientId = patientId;
        _currentUserId = currentUserId;
        _repository = repository;
        _db = db;

        // تحديد أن الموعد لا يمكن أن يكون في الماضي (يبدأ من اليوم)
        DpAppointmentDate.DisplayDateStart = DateTime.Today;
        DpAppointmentDate.SelectedDate = DateTime.Today;

        LoadTreatments();
        RefreshTreatmentChips();
        RefreshSameDayPatients();
    }

    // خاصية سحب النافذة من الشريط العلوي
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    // زر الإغلاق X في الأعلى
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
            // القائمة اختيارية - عدم توفرها لا يمنع اختيار تاريخ الموعد بلا علاج مخطَّط
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

    // يُحدَّث فوراً عند تغيير التاريخ - يعرض كل من له موعد بنفس اليوم فعلاً + علاجه المخطَّط، حتى يقدّر
    // الطبيب حِمل ذلك اليوم قبل ما يوافق على حجز إضافي قد يتجاوز وقت العمل المتاح
    private void DpAppointmentDate_SelectedDateChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => RefreshSameDayPatients();

    private void RefreshSameDayPatients()
    {
        if (!DpAppointmentDate.SelectedDate.HasValue)
        {
            SameDayPatientsList.ItemsSource = null;
            NoSameDayPatientsText.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var patients = _repository.GetScheduledPatientsForDate(DpAppointmentDate.SelectedDate.Value)
                .Select(p => new SameDayPatientRow
                {
                    Name = p.PatientFullName,
                    Treatment = string.IsNullOrWhiteSpace(p.PlannedTreatment)
                        ? LocalizationManager.T("Sched_NoPlannedTreatment")
                        : p.PlannedTreatment
                })
                .ToList();

            SameDayPatientsList.ItemsSource = patients;
            NoSameDayPatientsText.Visibility = patients.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            SameDayPatientsList.ItemsSource = null;
            NoSameDayPatientsText.Visibility = Visibility.Visible;
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (!DpAppointmentDate.SelectedDate.HasValue)
        {
            MessageBox.Show(LocalizationManager.T("Sched_SelectDateFirst"), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var plannedTreatment = _selectedTreatments.Count > 0 ? string.Join("; ", _selectedTreatments.Select(t => t.Name)) : null;

        bool success = _repository.ScheduleAppointment(_patientId, DpAppointmentDate.SelectedDate.Value, plannedTreatment, _currentUserId);

        if (success)
        {
            MessageBox.Show(LocalizationManager.T("Sched_SuccessMessage"), LocalizationManager.T("Sched_SuccessTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show(LocalizationManager.T("Sched_ErrorMessage"), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
