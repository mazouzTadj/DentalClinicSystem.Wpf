namespace DentalClinic.Data.Models;

public class Payment
{
    public int PaymentID { get; set; }
    public int SessionID { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public int ReceivedByUserID { get; set; }
    public string? Notes { get; set; }
}
