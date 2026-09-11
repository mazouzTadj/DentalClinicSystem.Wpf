using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DentalClinic.UI.Localization;

namespace DentalClinic.UI.Controls;

// عنصر تفاعلي لاختيار أسنان متعددة بترقيم FDI (32 سناً، تحديد متعدد - كل سن يُبدَّل
// بشكل مستقل عن الآخرين، بلا حصر بسن واحد كما كان سابقاً)
public partial class OdontogramControl : UserControl
{
    private ToggleButton[] _allTeeth = Array.Empty<ToggleButton>();
    private readonly HashSet<string> _selectedTeeth = new();

    // يُطلَق عند تغيّر التحديد (إضافة أو إزالة سن) - يحمل القائمة الكاملة للأسنان المحدَّدة حالياً
    public event EventHandler<IReadOnlyCollection<string>>? SelectionChanged;

    public IReadOnlyCollection<string> SelectedTeeth => _selectedTeeth;

    // أُبقيت للتوافق مع أي كود قديم يتوقع سناً واحداً فقط: أول سن محدَّد، أو null إن لم يوجد تحديد
    public string? SelectedTooth => _selectedTeeth.Count > 0 ? _selectedTeeth.OrderBy(t => t).First() : null;

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
    }

    private void Tooth_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.Tag is string toothNumber)
        {
            _selectedTeeth.Add(toothNumber);
            UpdateLabel();
            SelectionChanged?.Invoke(this, _selectedTeeth);
        }
    }

    private void Tooth_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.Tag is string toothNumber)
        {
            _selectedTeeth.Remove(toothNumber);
            UpdateLabel();
            SelectionChanged?.Invoke(this, _selectedTeeth);
        }
    }

    private void UpdateLabel()
    {
        SelectedToothLabel.Text = _selectedTeeth.Count > 0
            ? string.Join(", ", _selectedTeeth.OrderBy(t => t))
            : LocalizationManager.T("Odont_None");
    }

    // إعادة ضبط التحديد بالكامل - تُستدعى عند فتح ملف مريض جديد
    public void ClearSelection()
    {
        _selectedTeeth.Clear();
        SelectedToothLabel.Text = LocalizationManager.T("Odont_None");
        foreach (var tb in _allTeeth)
        {
            tb.IsChecked = false;
        }
    }

    // تحديد عدة أسنان برمجياً مسبقاً (يُستخدم عند التعبئة التلقائية من بيانات آخر زيارة)
    public void SetSelectedTeeth(IEnumerable<string> teethNumbers)
    {
        foreach (var toothNumber in teethNumbers)
        {
            var match = Array.Find(_allTeeth, t => (string?)t.Tag == toothNumber);
            if (match != null)
            {
                match.IsChecked = true; // يُطلق Tooth_Checked تلقائياً فيحدّث المجموعة والتسمية
            }
        }
    }

    // أُبقيت للتوافق: تحديد سن واحد فقط
    public void SetSelectedTooth(string toothNumber) => SetSelectedTeeth(new[] { toothNumber });
}
