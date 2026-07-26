using System;
using System.Configuration;
using System.Data;
using System.Windows;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;

namespace DentalClinic.NurseApp;

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

            PatientNameText.Text = $"Patient: {patientName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error initializing database: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show("Session details not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            var row = table.Rows[0];
            _totalPrice = row["TotalPrice"] != DBNull.Value ? Convert.ToDecimal(row["TotalPrice"]) : 0m;
            _alreadyPaid = row["PaidAmount"] != DBNull.Value ? Convert.ToDecimal(row["PaidAmount"]) : 0m;
            _remainingBalance = _totalPrice - _alreadyPaid;

            TotalPriceText.Text = $"{_totalPrice:N0} DA";
            AlreadyPaidText.Text = $"{_alreadyPaid:N0} DA";
            RemainingText.Text = $"{_remainingBalance:N0} DA";

            PaymentAmountBox.Text = _remainingBalance > 0 ? _remainingBalance.ToString("0.##") : "0";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load payment details: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ConfirmPaymentButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        if (_sessionRepo == null || _paymentRepo == null)
        {
            ErrorText.Text = "Database connection is not available.";
            return;
        }

        if (!decimal.TryParse(PaymentAmountBox.Text.Trim(), out var amount) || amount <= 0)
        {
            ErrorText.Text = "Please enter a valid payment amount greater than 0.";
            return;
        }

        if (amount > _remainingBalance)
        {
            ErrorText.Text = $"Entered amount exceeds the remaining balance ({_remainingBalance:N0} DA).";
            return;
        }

        try
        {
            var newTotalPaid = _alreadyPaid + amount;

            // 1. Update the paid amount in MedicalSessions
            _sessionRepo.UpdateSessionPayment(_sessionId, newTotalPaid, null, null, null, null);

            // 2. Record the payment entry in Payments table
            _paymentRepo.AddPayment(_sessionId, amount, _currentUser.UserID);

            MessageBox.Show("Payment recorded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = "Error saving payment: " + ex.Message;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}