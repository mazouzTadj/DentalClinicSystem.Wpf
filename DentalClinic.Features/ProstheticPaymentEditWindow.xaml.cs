using System;
using System.Configuration;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

// نافذة إضافة/تعديل دفعة ترميم واحدة - تُفتَح من تبويب "المدفوعات" في ProstheticCaseEditWindow.
// تحذير تجاوز السعر المتفق عليه (Overpayment) يُطبَّق في الإضافة والتعديل معاً (راجع
// ProstheticPaymentRepository.GetProjectedOverpayment - في وضع التعديل تُستبعَد قيمة الدفعة
// القديمة نفسها من الحساب عبر excludePaymentId قبل إضافة القيمة الجديدة، حتى لا تُحتسَب مرتين).
public partial class ProstheticPaymentEditWindow : Window
{
    private readonly int _caseId;
    private readonly UserAccount _currentUser;
    private readonly ProstheticPayment? _existingPayment; // null = إضافة
    private readonly ProstheticPaymentRepository _paymentRepo;

    private bool IsEditMode => _existingPayment != null;

    public ProstheticPaymentEditWindow(int caseId, UserAccount currentUser)
    {
        _caseId = caseId;
        _existingPayment = null;
        _currentUser = currentUser;
        InitializeComponent();

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _paymentRepo = new ProstheticPaymentRepository(db);

        TitleText.Text = LocalizationManager.T("ProsthPayment_AddTitle");
        PaymentDateBox.SelectedDate = DateTime.Now.Date;
    }

    public ProstheticPaymentEditWindow(ProstheticPayment existingPayment, UserAccount currentUser)
    {
        _caseId = existingPayment.CaseID;
        _existingPayment = existingPayment;
        _currentUser = currentUser;
        InitializeComponent();

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _paymentRepo = new ProstheticPaymentRepository(db);

        TitleText.Text = LocalizationManager.T("ProsthPayment_EditTitle");
        AmountBox.Text = existingPayment.Amount.ToString(CultureInfo.InvariantCulture);
        PaymentDateBox.SelectedDate = existingPayment.PaymentDate.Date;
        NotesBox.Text = existingPayment.Notes ?? string.Empty;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        if (!decimal.TryParse(AmountBox.Text.Trim(), out var amount) || amount <= 0)
        {
            ErrorText.Text = LocalizationManager.T("ProsthPayment_InvalidAmount");
            return;
        }

        if (PaymentDateBox.SelectedDate == null)
        {
            ErrorText.Text = LocalizationManager.T("ProsthSession_DateRequired");
            return;
        }

        var notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();

        // فحص تجاوز السعر المتفق عليه - في الإضافة والتعديل معاً الآن (كانت مقتصرة على الإضافة
        // فقط سابقاً، مما يجعل تعديل دفعة موجودة لأعلى لا يُظهر أي تحذير رغم تجاوز فعلي للسعر).
        // في وضع التعديل: نستبعد قيمة الدفعة الحالية القديمة من "المدفوع حالياً" عبر
        // excludePaymentId قبل إضافة القيمة الجديدة، وإلا تُحتسَب الدفعة نفسها مرتين.
        var excludePaymentId = IsEditMode ? _existingPayment!.ProstheticPaymentID : (int?)null;
        var overpayment = _paymentRepo.GetProjectedOverpayment(_caseId, amount, excludePaymentId);
        if (overpayment > 0)
        {
            var confirm = MessageBox.Show(
                LocalizationManager.T("ProsthPayment_OverpaymentWarningFormat",
                    overpayment.ToString("N2", CultureInfo.InvariantCulture)),
                LocalizationManager.T("Common_Notice"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;
        }

        try
        {
            if (!IsEditMode)
            {
                var newPayment = new ProstheticPayment
                {
                    CaseID = _caseId,
                    Amount = amount,
                    PaymentDate = PaymentDateBox.SelectedDate.Value.Date + DateTime.Now.TimeOfDay,
                    ReceivedByUserID = _currentUser.UserID,
                    Notes = notes
                };
                _paymentRepo.AddPayment(newPayment, _currentUser);
            }
            else
            {
                var originalTimeOfDay = _existingPayment!.PaymentDate.TimeOfDay;
                var updated = new ProstheticPayment
                {
                    ProstheticPaymentID = _existingPayment.ProstheticPaymentID,
                    Amount = amount,
                    PaymentDate = PaymentDateBox.SelectedDate.Value.Date + originalTimeOfDay,
                    Notes = notes
                };
                _paymentRepo.UpdatePayment(updated, _currentUser);
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("ProsthCase_SaveErrorFormat", ex.Message);
        }
    }
}
