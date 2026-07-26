using System.ComponentModel;
using DentalClinic.Data.Models;

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
