using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DentalClinic.Features;

public partial class ItemPickerWindow : Window
{
    private readonly List<PickerItemViewModel> _allItems;
    private readonly bool _showTotal;

    public List<PickerResultItem> SelectedItems { get; private set; } = new();

    // items: (Id, Name, Price اختياري - يظهر السعر ويُحسب الإجمالي إن كانت غير null, SubText اختياري)
    public ItemPickerWindow(string title, IEnumerable<(int Id, string Name, decimal? Price, string? SubText)> items,
        IEnumerable<int> preSelectedIds, bool showTotal)
    {
        InitializeComponent();
        TitleText.Text = title;
        _showTotal = showTotal;
        TotalPanel.Visibility = showTotal ? Visibility.Visible : Visibility.Collapsed;

        var preSelectedSet = new HashSet<int>(preSelectedIds);
        _allItems = items.Select(i => new PickerItemViewModel
        {
            Id = i.Id,
            Name = i.Name,
            Price = i.Price,
            SubText = i.SubText,
            IsSelected = preSelectedSet.Contains(i.Id)
        }).ToList();

        foreach (var item in _allItems)
        {
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PickerItemViewModel.IsSelected)) UpdateTotal();
            };
        }

        ItemsList.ItemsSource = _allItems;
        UpdateTotal();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        ItemsList.ItemsSource = string.IsNullOrEmpty(query)
            ? _allItems
            : _allItems.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void UpdateTotal()
    {
        if (!_showTotal) return;
        var total = _allItems.Where(i => i.IsSelected).Sum(i => i.Price ?? 0);
        TotalValueText.Text = total.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedItems = _allItems.Where(i => i.IsSelected)
            .Select(i => new PickerResultItem(i.Id, i.Name, i.Price))
            .ToList();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
