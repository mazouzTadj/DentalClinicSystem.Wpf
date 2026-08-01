using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using DentalClinic.Data.DataAccess;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class CommissionSettingsWindow : Window
{
    private readonly DoctorCommissionService _commissionService;

    public CommissionSettingsWindow(DoctorCommissionService commissionService)
    {
        InitializeComponent();
        _commissionService = commissionService;
        LoadData();
    }

    private void LoadData()
    {
        DefaultPercentBox.Text = _commissionService.GetDefaultCommissionPercent().ToString(CultureInfo.InvariantCulture);

        var primaryId = _commissionService.GetPrimaryDoctorUserId();
        var doctors = _commissionService.GetAllDoctors();

        DoctorsItems.ItemsSource = doctors
            .Select(d => new DoctorCommissionRowViewModel(d, primaryId.HasValue && primaryId.Value == d.UserID))
            .ToList();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(DefaultPercentBox.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var defaultPercent)
            || defaultPercent < 0 || defaultPercent > 100)
        {
            MessageBox.Show(LocalizationManager.T("Commission_InvalidPercent"), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var rows = (DoctorsItems.ItemsSource as System.Collections.Generic.List<DoctorCommissionRowViewModel>) ?? new();

        // نتحقق من صحة كل النسب المخصَّصة أولاً قبل حفظ أي شيء (كلها أو لا شيء)
        var parsedOverrides = new System.Collections.Generic.List<(int UserId, decimal? Percent)>();
        int? selectedPrimaryId = null;

        foreach (var row in rows)
        {
            if (row.IsPrimarySelected)
            {
                selectedPrimaryId = row.UserID;
                parsedOverrides.Add((row.UserID, null)); // الطبيب الرئيسي دائماً بلا نسبة عمولة خاصة به
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.PercentText))
            {
                parsedOverrides.Add((row.UserID, null)); // العودة لاستخدام النسبة العامة
                continue;
            }

            if (!decimal.TryParse(row.PercentText.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var percent)
                || percent < 0 || percent > 100)
            {
                MessageBox.Show(LocalizationManager.T("Commission_InvalidDoctorPercentFormat", row.DisplayName),
                    LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            parsedOverrides.Add((row.UserID, percent));
        }

        try
        {
            _commissionService.SetDefaultCommissionPercent(defaultPercent);
            _commissionService.SetPrimaryDoctor(selectedPrimaryId);
            foreach (var (userId, percent) in parsedOverrides)
            {
                _commissionService.SetDoctorCommissionPercent(userId, percent);
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationManager.T("Commission_SaveErrorFormat", ex.Message), LocalizationManager.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
