using System;
using System.Configuration;
using System.Data;
using System.Windows;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class CollectPaymentWindow : Window
{
    private readonly int _sessionId;
    private readonly UserAccount _currentUser;
    private DatabaseHelper? _db;
    private SessionRepository? _sessionRepo;
    private PaymentRepository? _paymentRepo;

    private decimal _totalPrice;
    private decimal _alreadyPaid;
    private decimal _remainingBalance;

    public CollectPaymentWindow(int sessionId, string patientName, UserAccount currentUser)
    {
        _sessionId = sessionId;
        _currentUser = currentUser;

        InitializeComponent();

        try
        {
            var connSetting = ConfigurationManager.ConnectionStrings["DentalClinicDB"];
            if (connSetting != null && !string.IsNullOrEmpty(connSetting.ConnectionString))
            {
                _db = new DatabaseHelper(connSetting.ConnectionString);
                _sessionRepo = new SessionRepository(_db);
                _paymentRepo = new PaymentRepository(_db);
            }

            PatientNameText.Text = LocalizationManager.T("Payment_PatientFormat", patientName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Payment_DbInitErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        Loaded += (s, e) => LoadSessionData();
    }

    private void LoadSessionData()
    {
        if (_db == null) return;

        try
        {
            const string sql = "SELECT TotalPrice, PaidAmount FROM MedicalSessions WHERE SessionID = @SessionID";
            var table = _db.ExecuteQuery(sql, new Microsoft.Data.SqlClient.SqlParameter("@SessionID", _sessionId));

            if (table.Rows.Count == 0)
            {
                MessageBox.Show(LocalizationManager.T("Payment_SessionNotFound"), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            var row = table.Rows[0];
            _totalPrice = row["TotalPrice"] != DBNull.Value ? Convert.ToDecimal(row["TotalPrice"]) : 0m;
            _alreadyPaid = row["PaidAmount"] != DBNull.Value ? Convert.ToDecimal(row["PaidAmount"]) : 0m;
            _remainingBalance = _totalPrice - _alreadyPaid;

            TotalPriceText.Text = LocalizationManager.T("Payment_AmountFormat", _totalPrice);
            AlreadyPaidText.Text = LocalizationManager.T("Payment_AmountFormat", _alreadyPaid);
            RemainingText.Text = LocalizationManager.T("Payment_AmountFormat", _remainingBalance);

            PaymentAmountBox.Text = _remainingBalance > 0 ? _remainingBalance.ToString("0.##") : "0";
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Payment_LoadFailedFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ConfirmPaymentButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        if (_sessionRepo == null || _paymentRepo == null)
        {
            ErrorText.Text = LocalizationManager.T("Payment_DbNotAvailable");
            return;
        }

        if (!decimal.TryParse(PaymentAmountBox.Text.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            ErrorText.Text = LocalizationManager.T("Payment_InvalidAmount");
            return;
        }

        if (amount > _remainingBalance)
        {
            ErrorText.Text = LocalizationManager.T("Payment_ExceedsBalanceFormat", _remainingBalance);
            return;
        }

        try
        {
            var newTotalPaid = _alreadyPaid + amount;
            var notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();

            // 1. Update the paid amount in MedicalSessions
            _sessionRepo.UpdateSessionPayment(_sessionId, newTotalPaid, null, null, null, null);

            // 2. Record the payment entry in Payments table (كانت الملاحظات تُهمَل بالكامل سابقاً - أصبحت تُحفظ الآن)
            _paymentRepo.AddPayment(_sessionId, amount, _currentUser.UserID, notes);

            MessageBox.Show(LocalizationManager.T("Payment_SuccessMessage"), LocalizationManager.T("Payment_SuccessTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("Payment_SaveErrorFormat", ex.Message);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}