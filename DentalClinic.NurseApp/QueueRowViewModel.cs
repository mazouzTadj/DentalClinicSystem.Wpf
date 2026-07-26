using System;
using System.ComponentModel;
using DentalClinic.Data.Models;

namespace DentalClinic.NurseApp;

public class QueueRowViewModel : INotifyPropertyChanged
{
    public int VisitID { get; }
    public int PatientID { get; }
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

    // 💳 خاصية جديدة: هل يوجد على المريض مبالغ غير مدفوعة؟
    private bool _hasUnpaidBalance;
    public bool HasUnpaidBalance
    {
        get => _hasUnpaidBalance;
        set
        {
            if (_hasUnpaidBalance == value) return;
            _hasUnpaidBalance = value;
            OnPropertyChanged(nameof(HasUnpaidBalance));
            OnPropertyChanged(nameof(IsPaid));
        }
    }

    // تُستخدم لإظهار شارة "Paid ✓" الخضراء عندما لا يكون هناك ديون
    public bool IsPaid => !HasUnpaidBalance;

    public bool CanCancel => Status == VisitStatus.Waiting || Status == VisitStatus.InTreatment;

    public string StatusText => Status switch
    {
        VisitStatus.Waiting => "Waiting",
        VisitStatus.InTreatment => "In Treatment",
        VisitStatus.Completed => "Completed",
        VisitStatus.Cancelled => "Cancelled",
        _ => Status.ToString()
    };

    public string NextAppointmentText => ScheduledDate.HasValue
        ? ScheduledDate.Value.ToString("dd/MM/yyyy hh:mm tt")
        : "-";

    public QueueRowViewModel(VisitQueueItem item)
    {
        VisitID = item.VisitID;
        PatientID = item.PatientID;
        PatientFullName = item.PatientFullName;
        CheckInTimeText = item.CheckInTime.ToString("hh:mm tt");
        _status = item.Status;
        _scheduledDate = item.ScheduledDate;
    }

    public void UpdateFrom(VisitQueueItem item)
    {
        Status = item.Status;
        ScheduledDate = item.ScheduledDate;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}