using System;
using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using DentalClinic.Data.DataAccess;

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
            MessageBox.Show("Please enter a valid amount.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(DescriptionBox.Text))
        {
            MessageBox.Show("Please enter a description.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 1. Get selected category from ComboBox
        var selectedCategoryItem = CategoryComboBox.SelectedItem as ComboBoxItem;
        string category = selectedCategoryItem?.Content?.ToString() ?? "General / Other";

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
            MessageBox.Show("Error saving expense: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}