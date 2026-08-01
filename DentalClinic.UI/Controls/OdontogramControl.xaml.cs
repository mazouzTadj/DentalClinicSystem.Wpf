using System.Windows;
using System.Windows.Controls;

namespace DentalClinic.UI.Controls;

// عنصر تفاعلي لاختيار سن واحد بترقيم FDI (32 سناً، تحديد واحد في كل مرة)
public partial class OdontogramControl : UserControl
{
    private RadioButton[] _allTeeth = Array.Empty<RadioButton>();

    // يُطلَق عند تغيّر السن المحدَّد - يحمل رقم السن أو null عند عدم وجود تحديد
    public event EventHandler<string?>? SelectionChanged;

    public string? SelectedTooth { get; private set; }

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
        if (sender is RadioButton rb && rb.Tag is string toothNumber)
        {
            SelectedTooth = toothNumber;
            SelectedToothLabel.Text = toothNumber;
            SelectionChanged?.Invoke(this, SelectedTooth);
        }
    }

    // إعادة ضبط التحديد بالكامل - تُستدعى عند فتح ملف مريض جديد
    public void ClearSelection()
    {
        SelectedTooth = null;
        SelectedToothLabel.Text = "لا شيء";
        foreach (var rb in _allTeeth)
        {
            rb.IsChecked = false;
        }
    }

    // تحديد سن برمجياً مسبقاً (يُستخدم عند التعبئة التلقائية من بيانات آخر زيارة)
    public void SetSelectedTooth(string toothNumber)
    {
        var match = Array.Find(_allTeeth, t => (string?)t.Tag == toothNumber);
        if (match != null)
        {
            match.IsChecked = true; // يُطلق Tooth_Checked تلقائياً فيحدّث SelectedTooth والتسمية
        }
    }
}
