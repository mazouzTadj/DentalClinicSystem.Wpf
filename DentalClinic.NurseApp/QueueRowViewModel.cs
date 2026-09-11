using System;
using System.ComponentModel;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.NurseApp;

public class QueueRowViewModel : INotifyPropertyChanged
{
    public int VisitID { get; }
    public int PatientID { get; }
    public string CheckInTimeText { get; }

    private string _patientFullName;
    // قابلة للتحديث في مكانها (وليست get فقط كما كانت) حتى يظهر الاسم الجديد فوراً في القائمة
    // بعد تعديل بيانات المريض (شاشة تعديل المريض)، دون انتظار إعادة بناء الصف بالكامل
    public string PatientFullName
    {
        get => _patientFullName;
        private set
        {
            if (_patientFullName == value) return;
            _patientFullName = value;
            OnPropertyChanged(nameof(PatientFullName));
        }
    }

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
            OnPropertyChanged(nameof(CanCheckIn));
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
            OnPropertyChanged(nameof(HasNextAppointment));
        }
    }

    private int? _futureVisitId;
    // معرّف صف الموعد المستقبلي نفسه - يُستخدم عند الضغط على زر "تعديل" لتحديد الموعد المطلوب تعديله
    public int? FutureVisitID
    {
        get => _futureVisitId;
        private set
        {
            if (_futureVisitId == value) return;
            _futureVisitId = value;
            OnPropertyChanged(nameof(FutureVisitID));
        }
    }

    public bool HasNextAppointment => ScheduledDate.HasValue;

    private bool _isAppointmentPending;
    // فيه طلب تعديل معلَّق بانتظار رد الطبيب حالياً؟ (تُحدَّث من MainWindow بعد كل تحميل للقائمة)
    public bool IsAppointmentPending
    {
        get => _isAppointmentPending;
        set
        {
            if (_isAppointmentPending == value) return;
            _isAppointmentPending = value;
            OnPropertyChanged(nameof(IsAppointmentPending));
            OnPropertyChanged(nameof(IsAppointmentEditable));
        }
    }

    // يُمنع تعديل موعد له طلب معلَّق أصلاً - تفادي تضارب طلبين بنفس الوقت
    public bool IsAppointmentEditable => HasNextAppointment && !IsAppointmentPending;

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

    // هل توجد وصفة طبية لليوم؟ تُستخدم لتعطيل زر الطباعة عندما لا توجد وصفة.
    private bool _hasPrescription;
    public bool HasPrescription
    {
        get => _hasPrescription;
        set
        {
            if (_hasPrescription == value) return;
            _hasPrescription = value;
            OnPropertyChanged(nameof(HasPrescription));
        }
    }

    public bool CanCancel => Status == VisitStatus.Waiting || Status == VisitStatus.InTreatment || Status == VisitStatus.Scheduled;

    // إصلاح: يظهر زر "Check-In" فقط عندما تكون الزيارة موعداً محجوزاً وصل يومه فعلياً
    public bool CanCheckIn => Status == VisitStatus.Scheduled;

    public string StatusText => Status switch
    {
        VisitStatus.Waiting => LocalizationManager.T("Main_StatusWaiting"),
        VisitStatus.InTreatment => LocalizationManager.T("Main_StatusInTreatment"),
        VisitStatus.Completed => LocalizationManager.T("Main_StatusCompleted"),
        VisitStatus.Cancelled => LocalizationManager.T("Main_StatusCancelled"),
        VisitStatus.Scheduled => LocalizationManager.T("Main_StatusScheduled"),
        _ => Status.ToString()
    };

    // بلا وقت - حُذف حقل الوقت من نظام حجز المواعيد بالكامل بناءً على طلب العميل
    public string NextAppointmentText => ScheduledDate.HasValue
        ? ScheduledDate.Value.ToString("dd/MM/yyyy")
        : "-";

    public QueueRowViewModel(VisitQueueItem item)
    {
        VisitID = item.VisitID;
        PatientID = item.PatientID;
        _patientFullName = item.PatientFullName;
        CheckInTimeText = item.CheckInTime.ToString("hh:mm tt");
        _status = item.Status;
        _scheduledDate = item.ScheduledDate;
        _futureVisitId = item.FutureVisitID;
    }

    public void UpdateFrom(VisitQueueItem item)
    {
        PatientFullName = item.PatientFullName;
        Status = item.Status;
        ScheduledDate = item.ScheduledDate;
        FutureVisitID = item.FutureVisitID;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}