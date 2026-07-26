using DentalClinic.Data.Models;

namespace DentalClinic.NurseApp;

public class PatientSearchRowViewModel
{
    public int PatientID { get; }
    public string FullName { get; }
    public string AgeText { get; }
    public string PhoneNumber { get; }

    public PatientSearchRowViewModel(Patient patient)
    {
        PatientID = patient.PatientID;
        FullName = patient.FullName;
        AgeText = patient.Age?.ToString() ?? "-";
        PhoneNumber = patient.PhoneNumber;
    }
}
