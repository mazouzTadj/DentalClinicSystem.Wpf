using System.Globalization;
using System.Windows;
using DentalClinic.Data.Models;

namespace DentalClinic.Features;

// عنصر عرض واحد ضمن لوحة "إحصائيات الأطباء" التي حلّت محل "أكثر العلاجات شيوعًا"
public class DoctorStatRowViewModel
{
    public string DoctorName { get; }
    public string PatientsAndSessionsText { get; }
    public string IncomeText { get; }
    public string CommissionText { get; }
    public double BarWidth { get; }

    public Visibility CrownVisibility { get; }
    public Visibility CommissionVisibility { get; }

    public DoctorStatRowViewModel(DoctorStatRow r, double maxIncome, double maxBarWidth)
    {
        DoctorName = r.DoctorName;
        PatientsAndSessionsText = $"{r.PatientCount} patients • {r.SessionCount} sessions";
        IncomeText = r.GrossIncome.ToString("N2", CultureInfo.InvariantCulture);

        CrownVisibility = r.IsPrimary ? Visibility.Visible : Visibility.Collapsed;
        CommissionVisibility = r.IsPrimary ? Visibility.Collapsed : Visibility.Visible;

        CommissionText = r.IsPrimary
            ? string.Empty
            : $"Commission {r.CommissionPercent:0.##}% → {r.DoctorShare.ToString("N2", CultureInfo.InvariantCulture)} (clinic keeps {r.ClinicShare.ToString("N2", CultureInfo.InvariantCulture)})";

        BarWidth = maxIncome > 0 ? Math.Max(6, (double)(r.GrossIncome / (decimal)maxIncome) * maxBarWidth) : 6;
    }
}
