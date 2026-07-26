using System.Globalization;
using DentalClinic.Data.Models;

namespace DentalClinic.DoctorApp;

public class OutstandingBalanceRowViewModel
{
    public string PatientFullName { get; }
    public string PhoneNumber { get; }
    public string LastVisitText { get; }
    public string TotalOwedText { get; }

    public OutstandingBalanceRowViewModel(OutstandingBalanceRow r)
    {
        PatientFullName = r.PatientFullName;
        PhoneNumber = r.PhoneNumber;
        LastVisitText = r.LastVisit.ToString("yyyy-MM-dd");
        TotalOwedText = r.TotalOwed.ToString("N2", CultureInfo.InvariantCulture);
    }
}
