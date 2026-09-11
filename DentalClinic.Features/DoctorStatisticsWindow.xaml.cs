using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Windows;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class DoctorStatisticsWindow : Window
{
    private readonly FinancialRepository _financialRepo;
    private readonly DoctorCommissionService _commissionService;
    private readonly List<UserAccount> _doctors;

    private sealed class DoctorFilterItem
    {
        public int? UserId { get; init; }
        public string FullName { get; init; } = string.Empty;
    }

    private sealed class DoctorDetailRow
    {
        public string DoctorName { get; init; } = string.Empty;
        public int PatientCount { get; init; }
        public int SessionCount { get; init; }
        public string GrossIncomeText { get; init; } = string.Empty;
        public string CommissionPercentText { get; init; } = string.Empty;
        public string DoctorShareText { get; init; } = string.Empty;
        public string ClinicShareText { get; init; } = string.Empty;
        public string IncomePerPatientText { get; init; } = string.Empty;
        public string IncomeShareText { get; init; } = string.Empty;
    }

    public DoctorStatisticsWindow(DatabaseHelper db)
    {
        InitializeComponent();
        _financialRepo = new FinancialRepository(db);
        _commissionService = new DoctorCommissionService(db);
        _doctors = _commissionService.GetAllDoctors();

        var filterItems = new List<DoctorFilterItem>
        {
            new() { UserId = null, FullName = LocalizationManager.T("Fin_AllDoctors") }
        };
        filterItems.AddRange(_doctors.Select(d => new DoctorFilterItem { UserId = d.UserID, FullName = d.FullName }));
        DoctorFilterCombo.ItemsSource = filterItems;
        DoctorFilterCombo.SelectedIndex = 0;
        DoctorFilterCombo.SelectedItem = filterItems[0];

        SetToday();
    }

    private void SetToday()
    {
        var today = DateTime.Today;
        FromDatePicker.SelectedDate = today;
        ToDatePicker.SelectedDate = today;
        LoadStatistics();
    }

    private void SetMonth()
    {
        var today = DateTime.Today;
        FromDatePicker.SelectedDate = new DateTime(today.Year, today.Month, 1);
        ToDatePicker.SelectedDate = today;
        LoadStatistics();
    }

    private void SetYear()
    {
        var today = DateTime.Today;
        FromDatePicker.SelectedDate = new DateTime(today.Year, 1, 1);
        ToDatePicker.SelectedDate = today;
        LoadStatistics();
    }

    private void TodayButton_Click(object sender, RoutedEventArgs e) => SetToday();
    private void MonthButton_Click(object sender, RoutedEventArgs e) => SetMonth();
    private void YearButton_Click(object sender, RoutedEventArgs e) => SetYear();

    private void ApplyFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (FromDatePicker.SelectedDate == null || ToDatePicker.SelectedDate == null)
        {
            MessageBox.Show(LocalizationManager.T("Fin_SelectBothDates"), LocalizationManager.T("Fin_DateRangeErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (FromDatePicker.SelectedDate.Value.Date > ToDatePicker.SelectedDate.Value.Date)
        {
            MessageBox.Show(LocalizationManager.T("Fin_StartAfterEnd"), LocalizationManager.T("Fin_DateRangeErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LoadStatistics();
    }

    private void LoadStatistics()
    {
        var from = FromDatePicker.SelectedDate?.Date ?? DateTime.Today;
        var to = (ToDatePicker.SelectedDate?.Date ?? DateTime.Today).AddDays(1);
        var selectedDoctorId = (DoctorFilterCombo.SelectedItem as DoctorFilterItem)?.UserId;

        var stats = _financialRepo.GetDetailedDoctorStatistics(from, to);
        var primaryId = _commissionService.GetPrimaryDoctorUserId();

        foreach (var stat in stats)
        {
            stat.IsPrimary = primaryId.HasValue && primaryId.Value == stat.DoctorUserId;
            stat.CommissionPercent = stat.IsPrimary ? 0m : _commissionService.GetEffectiveCommissionPercent(stat.DoctorUserId);
            stat.DoctorShare = stat.IsPrimary ? 0m : Math.Round(stat.GrossIncome * stat.CommissionPercent / 100m, 2);
        }

        if (selectedDoctorId.HasValue)
            stats = stats.Where(x => x.DoctorUserId == selectedDoctorId.Value).ToList();

        var totalIncome = stats.Sum(x => x.GrossIncome);
        foreach (var stat in stats)
            stat.IncomeSharePercent = totalIncome > 0 ? (double)(stat.GrossIncome / totalIncome * 100m) : 0d;

        var activitySummary = _financialRepo.GetDetailedDoctorSummary(from, to, selectedDoctorId);
        var summary = new DoctorDetailedSummary
        {
            DoctorCount = stats.Count,
            PatientCount = activitySummary.PatientCount,
            SessionCount = activitySummary.SessionCount,
            GrossIncome = totalIncome,
            DoctorShare = stats.Sum(x => x.DoctorShare)
        };

        DoctorCountText.Text = summary.DoctorCount.ToString("N0");
        PatientCountText.Text = summary.PatientCount.ToString("N0");
        SessionCountText.Text = summary.SessionCount.ToString("N0");
        IncomeText.Text = MoneyFormatter.Format(summary.GrossIncome);
        CommissionText.Text = MoneyFormatter.Format(summary.DoctorShare);
        ClinicShareText.Text = MoneyFormatter.Format(summary.ClinicShare);

        var selectedName = selectedDoctorId.HasValue
            ? _doctors.FirstOrDefault(d => d.UserID == selectedDoctorId.Value)?.FullName
            : null;
        PeriodText.Text = selectedName == null
            ? LocalizationManager.T("Fin_PeriodFormat", from.ToString("yyyy-MM-dd"), (to.AddDays(-1)).ToString("yyyy-MM-dd"))
            : LocalizationManager.T("Fin_PeriodDoctorFormat", selectedName, from.ToString("yyyy-MM-dd"), (to.AddDays(-1)).ToString("yyyy-MM-dd"));

        var topDoctor = stats.OrderByDescending(x => x.GrossIncome).FirstOrDefault();
        TopDoctorText.Text = topDoctor == null || topDoctor.GrossIncome <= 0
            ? LocalizationManager.T("Fin_NoDoctorIncome")
            : LocalizationManager.T("Fin_TopDoctorFormat", topDoctor.DoctorName, MoneyFormatter.Format(topDoctor.GrossIncome));

        DoctorDetailsGrid.ItemsSource = stats.Select(x => new DoctorDetailRow
        {
            DoctorName = x.IsPrimary ? $"👑 {x.DoctorName}" : x.DoctorName,
            PatientCount = x.PatientCount,
            SessionCount = x.SessionCount,
            GrossIncomeText = MoneyFormatter.Format(x.GrossIncome),
            CommissionPercentText = x.IsPrimary ? LocalizationManager.T("Fin_PrimaryDoctor") : $"{x.CommissionPercent:0.##}%",
            DoctorShareText = MoneyFormatter.Format(x.DoctorShare),
            ClinicShareText = MoneyFormatter.Format(x.ClinicShare),
            IncomePerPatientText = MoneyFormatter.Format(x.IncomePerPatient),
            IncomeShareText = $"{x.IncomeSharePercent:0.##}%"
        }).ToList();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
