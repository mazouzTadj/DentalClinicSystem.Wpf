using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

// نافذة "إضافة مصروف مرمم" - نظير AddExpenseWindow (فاينانس العيادة العام) لكن لجدول
// ProstheticExpenses المستقل تماماً (راجع تعليق ProstheticExpenseRepository حول الفصل المالي).
public partial class AddProstheticExpenseWindow : Window
{
    private readonly UserAccount _currentUser;
    private readonly ProstheticExpenseRepository _expenseRepo;
    private readonly ProstheticExpenseRow? _existingExpense; // null = إضافة، غير null = تعديل

    private bool IsEditMode => _existingExpense != null;

    // نفس معيار IsDoctorAccount/CanViewAllCases المعتمد في ProstheticStatisticsWindow بالحرف
    // (Role==Doctor && IsMainDoctor، وليس أي طبيب) - من يملكها يستطيع اختيار أي مرمم لهذا المصروف،
    // ومن لا يملكها يبقى مقفلاً على نفسه
    private bool IsDoctorAccount => _currentUser.Role == UserRole.Doctor && _currentUser.IsMainDoctor;
    private bool CanChooseProsthetist => IsDoctorAccount
        || _currentUser.HasProstheticPermission(ProstheticPermissionKeys.ViewAllCases);

    public AddProstheticExpenseWindow(UserAccount currentUser, DatabaseHelper db, int? preselectedProsthetistId = null)
    {
        _currentUser = currentUser;
        _existingExpense = null;
        InitializeComponent();

        _expenseRepo = new ProstheticExpenseRepository(db);
        var userRepo = new UserRepository(db);

        List<UserAccount> options;
        if (CanChooseProsthetist)
        {
            options = userRepo.GetAllProsthetists(activeOnly: false);
        }
        else
        {
            // مقفل على المرمم الحالي فقط - لا حاجة لاستعلام كامل عن كل المرممين
            options = new List<UserAccount> { _currentUser };
        }

        ProsthetistComboBox.ItemsSource = options;
        ProsthetistComboBox.IsEnabled = CanChooseProsthetist;

        var toSelect = preselectedProsthetistId.HasValue
            ? options.FirstOrDefault(p => p.UserID == preselectedProsthetistId.Value)
            : null;
        ProsthetistComboBox.SelectedItem = toSelect
            ?? options.FirstOrDefault(p => p.UserID == _currentUser.UserID)
            ?? options.FirstOrDefault();

        ExpenseDatePicker.SelectedDate = DateTime.Now;
    }

    // وضع التعديل: مملوءة مسبقاً بقيم المصروف الحالي. حقل المرمم يبقى مقفلاً دائماً هنا (حتى لمن
    // يملك CanChooseProsthetist) لأن نقل مصروف من مرمم لآخر ليس جزءاً من "تعديل" - فقط المبلغ/الوصف/
    // الفئة/التاريخ قابلة للتغيير، نفس منطق ProstheticPaymentEditWindow الذي لا يسمح بتغيير الحالة (Case).
    public AddProstheticExpenseWindow(ProstheticExpenseRow existingExpense, UserAccount currentUser, DatabaseHelper db)
    {
        _currentUser = currentUser;
        _existingExpense = existingExpense;
        InitializeComponent();

        _expenseRepo = new ProstheticExpenseRepository(db);

        Title = LocalizationManager.T("ProsthExpense_EditTitle");
        HeaderText.Text = LocalizationManager.T("ProsthExpense_EditHeader");

        ProsthetistComboBox.ItemsSource = new List<UserAccount>
        {
            new UserAccount { UserID = existingExpense.ProsthetistUserID, FullName = existingExpense.ProsthetistName }
        };
        ProsthetistComboBox.SelectedIndex = 0;
        ProsthetistComboBox.IsEnabled = false;

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
        ErrorText.Text = "";

        if (ProsthetistComboBox.SelectedItem is not UserAccount selectedProsthetist)
        {
            ErrorText.Text = LocalizationManager.T("ProsthExpense_ProsthetistRequired");
            return;
        }

        if (!decimal.TryParse(AmountBox.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal amount) || amount <= 0)
        {
            MessageBox.Show(LocalizationManager.T("Expense_InvalidAmount"), LocalizationManager.T("Expense_ValidationErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // الوصف اختياري بناءً على طلب المستخدم - يُحفَظ كسلسلة فارغة إن تُرك خالياً (العمود
        // Description في ProstheticExpenses مسموح فيه نص فارغ، وليس NULL فقط الممنوع)
        // نقرأ Tag (القيمة الإنجليزية الثابتة) بدل Content (النص المترجَم) - نفس أسلوب
        // AddExpenseWindow بالحرف، حتى تبقى فئات المصاريف المخزَّنة متسقة بغض النظر عن لغة الواجهة
        var selectedCategoryItem = CategoryComboBox.SelectedItem as ComboBoxItem;
        string category = selectedCategoryItem?.Tag?.ToString() ?? "General / Other";

        var selectedDate = ExpenseDatePicker.SelectedDate ?? DateTime.Now;

        try
        {
            if (IsEditMode)
            {
                var updated = new ProstheticExpense
                {
                    ExpenseID = _existingExpense!.ExpenseID,
                    ProsthetistUserID = _existingExpense.ProsthetistUserID,
                    Amount = amount,
                    Description = DescriptionBox.Text.Trim(),
                    Category = category,
                    ExpenseDate = selectedDate
                };
                _expenseRepo.UpdateExpense(updated, _currentUser);
            }
            else
            {
                var expense = new ProstheticExpense
                {
                    ProsthetistUserID = selectedProsthetist.UserID,
                    Amount = amount,
                    Description = DescriptionBox.Text.Trim(),
                    Category = category,
                    ExpenseDate = selectedDate
                };
                _expenseRepo.AddExpense(expense, _currentUser);
            }

            DialogResult = true;
            Close();
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(LocalizationManager.T("ProsthExpense_NoPermission"), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
