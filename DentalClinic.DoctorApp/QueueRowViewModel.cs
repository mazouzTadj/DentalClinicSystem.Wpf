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
            OnPropertyChanged(nameof(CanCheckIn));
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
        PatientFullName = item.PatientFullName;
        CheckInTimeText = item.CheckInTime.ToString("hh:mm tt");
        _status = item.Status;
    }

    // يُستدعى عند كل تحديث تلقائي بدل إنشاء عنصر جديد - يحدّث الحالة فقط إن تغيّرت
    public void UpdateFrom(VisitQueueItem item)
    {
        Status = item.Status;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
