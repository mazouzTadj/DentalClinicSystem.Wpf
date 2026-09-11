using System.Globalization;
using System.Windows;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class EditIncomeWindow : Window
{
    public decimal NewAmount { get; private set; }

    public EditIncomeWindow(string patientFullName, decimal currentAmount)
    {
        InitializeComponent();
        SubHeaderText.Text = LocalizationManager.T("Fin_EditIncomePromptFormat", patientFullName);
        AmountBox.Text = currentAmount.ToString("0.##", CultureInfo.InvariantCulture);
        AmountBox.SelectAll();
        Loaded += (s, e) => AmountBox.Focus();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(AmountBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0)
        {
            ErrorText.Text = LocalizationManager.T("Fin_InvalidAmount");
            return;
        }

        NewAmount = amount;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
