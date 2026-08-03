using System;
using System.Windows;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features
{
    public partial class ScheduleAppointmentDialog : Window
    {
        private readonly int _patientId;
        private readonly int _currentUserId;
        private readonly QueueRepository _repository;

        public ScheduleAppointmentDialog(int patientId, int currentUserId, QueueRepository repository)
        {
            InitializeComponent();
            _patientId = patientId;
            _currentUserId = currentUserId;
            _repository = repository;

            // تحديد أن الموعد لا يمكن أن يكون في الماضي (يبدأ من الغد)
            DpAppointmentDate.DisplayDateStart = DateTime.Today.AddDays(1);
            DpAppointmentDate.SelectedDate = DateTime.Today.AddDays(1);

            PopulateTimeSlots();
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

        private void PopulateTimeSlots()
        {
            CmbAppointmentTime.Items.Clear();
            var startTime = new TimeSpan(9, 0, 0);  // 09:00 AM
            var endTime = new TimeSpan(18, 0, 0);   // 06:00 PM

            while (startTime <= endTime)
            {
                CmbAppointmentTime.Items.Add(DateTime.Today.Add(startTime).ToString("hh:mm tt"));
                startTime = startTime.Add(TimeSpan.FromMinutes(30));
            }

            if (CmbAppointmentTime.Items.Count > 0)
                CmbAppointmentTime.SelectedIndex = 0;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!DpAppointmentDate.SelectedDate.HasValue)
            {
                MessageBox.Show(LocalizationManager.T("Sched_SelectDateFirst"), LocalizationManager.T("Common_Notice"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedDate = DpAppointmentDate.SelectedDate.Value;
            var selectedTimeStr = CmbAppointmentTime.SelectedItem?.ToString() ?? "09:00 AM";
            var appointmentTime = DateTime.Parse(selectedTimeStr).TimeOfDay;

            DateTime fullScheduledDateTime = selectedDate.Date.Add(appointmentTime);

            bool success = _repository.ScheduleAppointment(_patientId, fullScheduledDateTime, _currentUserId);

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
}