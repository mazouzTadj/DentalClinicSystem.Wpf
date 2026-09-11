using System.ComponentModel;
using System.Globalization;
using DentalClinic.Data.Models;

namespace DentalClinic.Features;

public class OutstandingBalanceRowViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
    public int PatientID { get; }
    public string PatientFullName { get; }
    public string PhoneNumber { get; }
    public string LastVisitText { get; }
    public string TotalOwedText { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public OutstandingBalanceRowViewModel(OutstandingBalanceRow r)
    {
        PatientID = r.PatientID;
        PatientFullName = r.PatientFullName;
        PhoneNumber = r.PhoneNumber;
        LastVisitText = r.LastVisit.ToString("yyyy-MM-dd");
        TotalOwedText = MoneyFormatter.Format(r.TotalOwed);
    }
}
