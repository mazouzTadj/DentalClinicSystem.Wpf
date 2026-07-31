using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DentalClinic.UI.Localization;

namespace DentalClinic.UI.Controls;

// عنصر تفاعلي لاختيار سن واحد أو أكثر بترقيم FDI (32 سناً)
// التحديد المتعدد: CheckBox بدل RadioButton، كل سن يُحدَّد/يُلغى تحديده بشكل مستقل عن البقية
public partial class OdontogramControl : UserControl
{
    private CheckBox[] _allTeeth = Array.Empty<CheckBox>();

    // مجموعة الأسنان المحدَّدة حالياً - تحافظ على ترتيب الاختيار (وليس ترتيب الترقيم)
    private readonly List<string> _selectedTeeth = new();

    // يُطلَق عند تغيّر التحديد (إضافة أو إزالة سن) - يحمل القائمة الكاملة المحدَّثة
    public event EventHandler<IReadOnlyList<string>>? SelectionChanged;

    public IReadOnlyList<string> SelectedTeeth => _selectedTeeth.AsReadOnly();

    // للتوافق مع الكود القديم الذي كان يتعامل مع سن واحد فقط: يعيد أول سن محدَّد أو null
    public string? SelectedTooth => _selectedTeeth.Count > 0 ? _selectedTeeth[0] : null;

    public OdontogramControl()
    {
        InitializeComponent();

        _allTeeth = new[]
        {
            Tooth18, Tooth17, Tooth16, Tooth15, Tooth14, Tooth13, Tooth12, Tooth11,
            Tooth21, Tooth22, Tooth23, Tooth24, Tooth25, Tooth26, Tooth27, Tooth28,
            Tooth48, Tooth47, Tooth46, Tooth45, Tooth44, Tooth43, Tooth42, Tooth41,
            Tooth31, Tooth32, Tooth33, Tooth34, Tooth35, Tooth36, Tooth37, Tooth38
        };

        UpperArchLabel.Text = LocalizationManager.T("Odonto_UpperArch");
        LowerArchLabel.Text = LocalizationManager.T("Odonto_LowerArch");
        SelectedToothPrefixLabel.Text = LocalizationManager.T("Odonto_SelectedPrefix");
        UpdateSelectedLabel();
    }

    private void Tooth_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string toothNumber } && !_selectedTeeth.Contains(toothNumber))
        {
            _selectedTeeth.Add(toothNumber);
            UpdateSelectedLabel();
            SelectionChanged?.Invoke(this, SelectedTeeth);
        }
    }

    private void Tooth_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string toothNumber })
        {
            _selectedTeeth.Remove(toothNumber);
            UpdateSelectedLabel();
            SelectionChanged?.Invoke(this, SelectedTeeth);
        }
    }

    private void UpdateSelectedLabel()
    {
        SelectedToothLabel.Text = _selectedTeeth.Count == 0
            ? LocalizationManager.T("Odonto_None")
            : string.Join(", ", _selectedTeeth.OrderBy(t => t));
    }

    // إعادة ضبط التحديد بالكامل - تُستدعى عند فتح ملف مريض جديد
    public void ClearSelection()
    {
        foreach (var rb in _allTeeth)
        {
            rb.IsChecked = false; // يُطلق Tooth_Unchecked تلقائياً لكل سن كان محدَّداً فيُفرِغ _selectedTeeth تدريجياً
        }
        _selectedTeeth.Clear();
        UpdateSelectedLabel();
    }

    // تحديد سن واحد برمجياً (يُبقي على أي تحديد سابق - للتوافق مع الاستخدام القديم لسن واحد)
    public void SetSelectedTooth(string toothNumber)
    {
        var match = Array.Find(_allTeeth, t => (string?)t.Tag == toothNumber);
        if (match != null)
        {
            match.IsChecked = true; // يُطلق Tooth_Checked تلقائياً فيضيف السن إلى القائمة ويحدّث التسمية
        }
    }

    // تحديد عدة أسنان دفعة واحدة برمجياً (تُستخدم عند التعبئة التلقائية من بيانات آخر زيارة)
    public void SetSelectedTeeth(IEnumerable<string> toothNumbers)
    {
        foreach (var toothNumber in toothNumbers)
        {
            SetSelectedTooth(toothNumber);
        }
    }
}
