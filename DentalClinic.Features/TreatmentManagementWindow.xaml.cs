using System;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using Microsoft.Data.SqlClient;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public class TreatmentGridRowModel
{
    public int TreatmentID { get; set; }
    public string TreatmentName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string PriceText => Price.ToString("0.##");
}

public partial class TreatmentManagementWindow : Window
{
    private readonly DatabaseHelper _db;
    public ObservableCollection<TreatmentGridRowModel> TreatmentsList { get; } = new();

    private int? _editingTreatmentId = null;

    public TreatmentManagementWindow()
    {
        InitializeComponent();

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        _db = new DatabaseHelper(connectionString);

        TreatmentsGrid.ItemsSource = TreatmentsList;

        Loaded += (s, e) =>
        {
            EnsureTableExists();
            LoadTreatments();
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void EnsureTableExists()
    {
        try
        {
            const string sql = @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TreatmentPresets')
                BEGIN
                    CREATE TABLE TreatmentPresets (
                        TreatmentID INT IDENTITY(1,1) PRIMARY KEY,
                        TreatmentName NVARCHAR(200) NOT NULL,
                        Price DECIMAL(18,2) NOT NULL DEFAULT 0,
                        IsActive BIT NOT NULL DEFAULT 1
                    );
                END";
            _db.ExecuteNonQuery(sql);
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Treat_InitErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadTreatments()
    {
        try
        {
            const string sql = "SELECT TreatmentID, TreatmentName, Price FROM TreatmentPresets WHERE IsActive = 1 ORDER BY TreatmentName ASC";
            var table = _db.ExecuteQuery(sql);

            TreatmentsList.Clear();
            foreach (DataRow row in table.Rows)
            {
                TreatmentsList.Add(new TreatmentGridRowModel
                {
                    TreatmentID = (int)row["TreatmentID"],
                    TreatmentName = row["TreatmentName"].ToString()!,
                    Price = Convert.ToDecimal(row["Price"])
                });
            }
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("Treat_LoadErrorFormat", ex.Message);
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var name = TxtTreatmentName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = LocalizationManager.T("Treat_NameRequired");
            return;
        }

        decimal price = 0;
        if (!string.IsNullOrWhiteSpace(TxtPrice.Text) && (!decimal.TryParse(TxtPrice.Text.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out price) || price < 0))
        {
            ErrorText.Text = LocalizationManager.T("Treat_InvalidPrice");
            return;
        }

        try
        {
            if (_editingTreatmentId.HasValue)
            {
                // تعديل علاج حالي
                const string updateSql = "UPDATE TreatmentPresets SET TreatmentName = @Name, Price = @Price WHERE TreatmentID = @ID";
                _db.ExecuteNonQuery(updateSql,
                    new SqlParameter("@Name", name),
                    new SqlParameter("@Price", price),
                    new SqlParameter("@ID", _editingTreatmentId.Value));
            }
            else
            {
                // إضافة علاج جديد
                const string insertSql = "INSERT INTO TreatmentPresets (TreatmentName, Price, IsActive) VALUES (@Name, @Price, 1)";
                _db.ExecuteNonQuery(insertSql,
                    new SqlParameter("@Name", name),
                    new SqlParameter("@Price", price));
            }

            ResetForm();
            LoadTreatments();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("Treat_SaveErrorFormat", ex.Message);
        }
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TreatmentGridRowModel item)
        {
            _editingTreatmentId = item.TreatmentID;
            TxtTreatmentName.Text = item.TreatmentName;
            TxtPrice.Text = item.Price.ToString("0.##");

            BtnSave.Content = LocalizationManager.T("Treat_UpdateButton");
            BtnCancelEdit.Visibility = Visibility.Visible;
        }
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TreatmentGridRowModel item)
        {
            var result = MessageBox.Show(LocalizationManager.T("Treat_ConfirmDeleteFormat", item.TreatmentName), LocalizationManager.T("Treat_ConfirmDeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    const string sql = "UPDATE TreatmentPresets SET IsActive = 0 WHERE TreatmentID = @ID";
                    _db.ExecuteNonQuery(sql, new SqlParameter("@ID", item.TreatmentID));

                    if (_editingTreatmentId == item.TreatmentID) ResetForm();

                    LoadTreatments();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(LocalizationManager.T("Treat_DeleteErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void BtnCancelEdit_Click(object sender, RoutedEventArgs e)
    {
        ResetForm();
    }

    private void ResetForm()
    {
        _editingTreatmentId = null;
        TxtTreatmentName.Text = string.Empty;
        TxtPrice.Text = string.Empty;
        BtnSave.Content = LocalizationManager.T("Treat_AddButton");
        BtnCancelEdit.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
    }
}