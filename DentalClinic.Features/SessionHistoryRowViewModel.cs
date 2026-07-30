using System.Globalization;
using DentalClinic.Data.Models;

namespace DentalClinic.Features;

public class SessionHistoryRowViewModel
{
    public string DateText { get; }
    public string Diagnosis { get; }
    public string Treatment { get; }
    public string TotalText { get; }
    public string PaidText { get; }
    public string RemainingText { get; }

    public SessionHistoryRowViewModel(MedicalSession s)
    {
        DateText = s.SessionDateTime.ToString("yyyy-MM-dd HH:mm");
        Diagnosis = string.IsNullOrWhiteSpace(s.Diagnosis) ? "-" : s.Diagnosis;
        Treatment = string.IsNullOrWhiteSpace(s.TreatmentPerformed) ? "-" : s.TreatmentPerformed;
        TotalText = s.TotalPrice.ToString("N2", CultureInfo.InvariantCulture);
        PaidText = s.PaidAmount.ToString("N2", CultureInfo.InvariantCulture);
        RemainingText = s.RemainingAmount.ToString("N2", CultureInfo.InvariantCulture);
    }
}
