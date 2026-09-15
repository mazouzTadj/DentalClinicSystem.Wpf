using System;
using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class AddExpenseWindow : Window
{
    private readonly FinancialRepository _financialRepo;
    private readonly ExpenseRow? _existingExpense; // null = إضافة، غير null = تعديل

    private bool IsEditMode => _existingExpense != null;

    public AddExpenseWindow()
    {
        _existingExpense = null;
        InitializeComponent();

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _financialRepo = new FinancialRepository(db);

        ExpenseDatePicker.SelectedDate = DateTime.Now;
    }

    // وضع التعديل: نفس نافذة الإضافة، مملوءة مسبقاً بقيم المصروف الحالي - نفس نمط
    // ProstheticPaymentEditWindow (مُنشِئ ثانٍ يأخذ الكائن الموجود بدل إعادة بناء نافذة منفصلة).
    public AddExpenseWindow(ExpenseRow existingExpense)
    {
        _existingExpense = existingExpense;
        InitializeComponent();

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _financialRepo = new FinancialRepository(db);

        Title = LocalizationManager.T("Expense_EditTitle");
        HeaderText.Text = LocalizationManager.T("Expense_EditHeader");

        AmountBox.Text = existingExpense.Amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        DescriptionBox.Text = existingExpense.Description;
        ExpenseDatePicker.SelectedDate = existingExpense.ExpenseDate.Date;

        foreach (ComboBoxItem item in CategoryComboBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), existingExpense.Category, StringComparison.Ordinal))
            {
                CategoryComboBox.SelectedItem = item;
                break;
            }
        }
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
            if (IsEditMode)
                _financialRepo.UpdateExpense(_existingExpense!.ExpenseID, amount, DescriptionBox.Text.Trim(), category, selectedDate);
            else
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