using System;
using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using DentalClinic.Data.DataAccess;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class AddExpenseWindow : Window
{
    private readonly FinancialRepository _financialRepo;

    public AddExpenseWindow()
    {
        InitializeComponent();

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _financialRepo = new FinancialRepository(db);

        ExpenseDatePicker.SelectedDate = DateTime.Now;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(AmountBox.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal amount) || amount <= 0)
        {
            MessageBox.Show(LocalizationManager.T("Expense_InvalidAmount"), LocalizationManager.T("Expense_ValidationErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(DescriptionBox.Text))
        {
            MessageBox.Show(LocalizationManager.T("Expense_DescriptionRequired"), LocalizationManager.T("Expense_ValidationErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 1. نقرأ Tag (القيمة الإنجليزية الثابتة) بدل Content (النص المترجَم) حتى تبقى فئات
        // المصاريف المخزَّنة متسقة دائماً بغض النظر عن لغة الواجهة عند الإضافة
        var selectedCategoryItem = CategoryComboBox.SelectedItem as ComboBoxItem;
        string category = selectedCategoryItem?.Tag?.ToString() ?? "General / Other";

        var selectedDate = ExpenseDatePicker.SelectedDate ?? DateTime.Now;

        try
        {
            // 2. Pass category to AddExpense
            _financialRepo.AddExpense(amount, DescriptionBox.Text.Trim(), category, selectedDate);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Expense_SaveErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}