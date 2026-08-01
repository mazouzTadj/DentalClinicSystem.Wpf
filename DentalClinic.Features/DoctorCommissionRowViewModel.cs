using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using DentalClinic.Data.Models;

namespace DentalClinic.Features;

// صف واحد في شاشة إعدادات العمولات - يدعم اختيار الطبيب الرئيسي مباشرة (راديو) مع تحديث فوري
// لحالة حقل النسبة (تُعطَّل تلقائياً بمجرد اختيار الطبيب كرئيسي، حتى بدون حفظ/إعادة فتح الشاشة)
public class DoctorCommissionRowViewModel : INotifyPropertyChanged
{
    public int UserID { get; }
    public string DisplayName { get; }

    // نص فارغ = استخدام النسبة العامة الافتراضية لهذا الطبيب
    public string PercentText { get; set; }

    private bool _isPrimarySelected;
    public bool IsPrimarySelected
    {
        get => _isPrimarySelected;
        set
        {
            if (_isPrimarySelected == value) return;
            _isPrimarySelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(SubText));
        }
    }

    public bool IsEditable => !IsPrimarySelected;

    public string SubText => IsPrimarySelected
        ? "Primary doctor — always takes 100%, no commission applies"
        : "Leave empty to use the general default";

    public DoctorCommissionRowViewModel(UserAccount doctor, bool isPrimary)
    {
        UserID = doctor.UserID;
        DisplayName = doctor.FullName;
        _isPrimarySelected = isPrimary;

        PercentText = isPrimary
            ? string.Empty
            : doctor.CommissionPercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
