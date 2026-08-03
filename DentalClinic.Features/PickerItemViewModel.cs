using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;

namespace DentalClinic.Features;

// صف واحد قابل للاختيار (Checkbox) ضمن نافذة الاختيار المتعدد العامة (ItemPickerWindow) -
// تُستخدم لكل من قوائم العلاجات (مع سعر) والأدوية (بدون سعر) بنفس المكوّن
public class PickerItemViewModel : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public string? SubText { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string PriceText => Price.HasValue ? Price.Value.ToString("N2", CultureInfo.InvariantCulture) : string.Empty;
    public Visibility PriceVisibility => Price.HasValue ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SubTextVisibility => string.IsNullOrWhiteSpace(SubText) ? Visibility.Collapsed : Visibility.Visible;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// نتيجة عنصر مُختار عند تأكيد الاختيار - نسخة غير قابلة للتعديل (لا تحمل حالة IsSelected)
public record PickerResultItem(int Id, string Name, decimal? Price);
