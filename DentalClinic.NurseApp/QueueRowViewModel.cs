using System;
using System.ComponentModel;
using DentalClinic.Data.Models;

namespace DentalClinic.NurseApp;

// عنصر عرض قابل للتحديث في مكانه (INotifyPropertyChanged) بدل استبداله بالكامل عند كل تحديث تلقائي
// هذا يحافظ على أي تحديد للمستخدم في الجدول أثناء التحديث كل 4 ثوانٍ
public class QueueRowViewModel : INotifyPropertyChanged
{
    public int VisitID { get; }
    public string PatientFullName { get; }
    public string CheckInTimeText { get; }

    private VisitStatus _status;
    public VisitStatus Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CanCancel));
        }
    }

    // المتغير الجديد الخاص بالموعد القادم
    private DateTime? _scheduledDate;
    public DateTime? ScheduledDate
    {
        get => _scheduledDate;
        private set
        {
            if (_scheduledDate == value) return;
            _scheduledDate = value;
            OnPropertyChanged(nameof(ScheduledDate));
            OnPropertyChanged(nameof(NextAppointmentText));
        }
    }

    // يمكن إلغاء الزيارة فقط إن كانت لا تزال في الانتظار أو قيد المعالجة
    public bool CanCancel => Status == VisitStatus.Waiting || Status == VisitStatus.InTreatment;

    public string StatusText => Status switch
    {
        VisitStatus.Waiting => "Waiting",
        VisitStatus.InTreatment => "In Treatment",
        VisitStatus.Completed => "Completed",
        VisitStatus.Cancelled => "Cancelled",
        _ => Status.ToString()
    };

    // النص الذي سيظهر في الجدول أمام الممرضة
    public string NextAppointmentText => ScheduledDate.HasValue
        ? ScheduledDate.Value.ToString("dd/MM/yyyy hh:mm tt")
        : "-";

    public QueueRowViewModel(VisitQueueItem item)
    {
        VisitID = item.VisitID;
        PatientFullName = item.PatientFullName;
        CheckInTimeText = item.CheckInTime.ToString("hh:mm tt");
        _status = item.Status;
        _scheduledDate = item.ScheduledDate; // جلب الموعد عند التحميل الأول
    }

    public void UpdateFrom(VisitQueueItem item)
    {
        Status = item.Status;
        ScheduledDate = item.ScheduledDate; // تحديث الموعد تلقائياً إذا قام الطبيب بإضافته للتو
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}