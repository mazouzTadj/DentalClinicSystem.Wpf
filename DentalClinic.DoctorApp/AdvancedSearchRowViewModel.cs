using System.Globalization;
using DentalClinic.Data.Models;

namespace DentalClinic.DoctorApp;

public class AdvancedSearchRowViewModel
{
    public int PatientID { get; }
    public string PatientFullName { get; }
    public string PatientPhone { get; }
    public string DateText { get; }
    public string Diagnosis { get; }
    public string Treatment { get; }
    public string TotalText { get; }
    public string PaidText { get; }
    public string RemainingText { get; }

    public AdvancedSearchRowViewModel(SessionSearchResult r)
    {
        PatientID = r.PatientID;
        PatientFullName = r.PatientFullName;
        PatientPhone = r.PatientPhone;
        DateText = r.SessionDateTime.ToString("yyyy-MM-dd HH:mm");
        Diagnosis = string.IsNullOrWhiteSpace(r.Diagnosis) ? "-" : r.Diagnosis;
        Treatment = string.IsNullOrWhiteSpace(r.TreatmentPerformed) ? "-" : r.TreatmentPerformed;
        TotalText = r.TotalPrice.ToString("N2", CultureInfo.InvariantCulture);
        PaidText = r.PaidAmount.ToString("N2", CultureInfo.InvariantCulture);
        RemainingText = r.RemainingAmount.ToString("N2", CultureInfo.InvariantCulture);
    }
}
