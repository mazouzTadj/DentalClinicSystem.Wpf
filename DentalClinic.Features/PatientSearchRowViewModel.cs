using System.ComponentModel;
using DentalClinic.Data.Models;

namespace DentalClinic.Features;

public class PatientSearchRowViewModel : INotifyPropertyChanged
{
    public int PatientID { get; }
    public string FullName { get; }
    public string AgeText { get; }
    public string PhoneNumber { get; }

    // 💳 خاصية معرفة وجود ديون غير مسددة
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

    // تُستخدم لإظهار شارة "Paid ✓" عندما لا يكون على المريض ديون
    public bool IsPaid => !HasUnpaidBalance;

    public PatientSearchRowViewModel(Patient patient, bool hasUnpaidBalance = false)
    {
        PatientID = patient.PatientID;
        FullName = patient.FullName;
        AgeText = patient.Age?.ToString() ?? "-";
        PhoneNumber = patient.PhoneNumber;
        _hasUnpaidBalance = hasUnpaidBalance;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}