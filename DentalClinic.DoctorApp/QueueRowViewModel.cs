using System.ComponentModel;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.DoctorApp;

// عنصر عرض قابل للتحديث في مكانه (INotifyPropertyChanged) بدل استبداله بالكامل عند كل تحديث تلقائي
// هذا يحافظ على تحديد المستخدم الحالي في الجدول أثناء التحديث كل 4 ثوانٍ
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

    private DateTime? _scheduledDate;
    public DateTime? ScheduledDate
    {
        get => _scheduledDate;
        private set
        {
            if (_scheduledDate == value) return;
            _scheduledDate = value;
            OnPropertyChanged(nameof(ScheduledDate));
            OnPropertyChanged(nameof(HasNextAppointment));
            OnPropertyChanged(nameof(NextAppointmentText));
            OnPropertyChanged(nameof(CanEditAppointment));
            OnPropertyChanged(nameof(CanDeleteAppointment));
        }
    }

    public int? FutureVisitID { get; private set; }
    public bool HasNextAppointment => FutureVisitID.HasValue && ScheduledDate.HasValue;
    public bool CanEditAppointment => HasNextAppointment;
    public bool CanDeleteAppointment => HasNextAppointment;
    public string NextAppointmentText => ScheduledDate.HasValue ? ScheduledDate.Value.ToString("dd/MM/yyyy") : "-";

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
            OnPropertyChanged(nameof(CanEditAppointment));
            OnPropertyChanged(nameof(CanDeleteAppointment));
        }
    }

    // يمكن إلغاء الزيارة فقط إن كانت لا تزال في الانتظار أو قيد المعالجة أو موعداً محجوزاً لم يبدأ بعد
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

    public QueueRowViewModel(VisitQueueItem item)
    {
        VisitID = item.VisitID;
        PatientID = item.PatientID;
        _patientFullName = item.PatientFullName;
        CheckInTimeText = item.CheckInTime.ToString("hh:mm tt");
        _status = item.Status;
        _scheduledDate = item.ScheduledDate;
        FutureVisitID = item.FutureVisitID;
    }

    // يُستدعى عند كل تحديث تلقائي بدل إنشاء عنصر جديد - يحدّث الحالة والاسم فقط إن تغيّرا
    public void UpdateFrom(VisitQueueItem item)
    {
        PatientFullName = item.PatientFullName;
        Status = item.Status;
        ScheduledDate = item.ScheduledDate;
        FutureVisitID = item.FutureVisitID;
        OnPropertyChanged(nameof(HasNextAppointment));
        OnPropertyChanged(nameof(CanEditAppointment));
        OnPropertyChanged(nameof(CanDeleteAppointment));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
