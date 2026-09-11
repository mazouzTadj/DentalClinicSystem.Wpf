using DentalClinic.Data.Models;

namespace DentalClinic.Features;

// يغلّف IncomeRow (طبقة البيانات) لإضافة نص مبلغ منسَّق برمز العملة - نفس نمط
// OutstandingBalanceRowViewModel/DoctorStatRowViewModel المستخدَم أصلاً بهذه الشاشة
public class IncomeRowViewModel
{
    public bool IsSelected { get; set; }
    public int PaymentID { get; }
    public string PatientFullName { get; }
    public decimal Amount { get; }
    public string AmountText { get; }
    public string DateText { get; }

    public IncomeRowViewModel(IncomeRow r)
    {
        PaymentID = r.PaymentID;
        PatientFullName = r.PatientFullName;
        Amount = r.Amount;
        AmountText = MoneyFormatter.Format(r.Amount);
        DateText = r.DateText;
    }
}
