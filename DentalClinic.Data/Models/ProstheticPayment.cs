namespace DentalClinic.Data.Models;

// دفعة مالية ضمن حالة ترميم واحدة - جدول مستقل تمامًا عن Payments الخاصة بالعيادة.
// ⚠️ لا يجب أن يظهر أي صف من هذا الجدول في أي استعلام لـ FinancialRepository (Finance العيادة)،
// والعكس صحيح - هذا هو أساس "العزل المالي الكامل" المطلوب في المتطلبات (بند 23-27).
public class ProstheticPayment
{
    public int ProstheticPaymentID { get; set; }
    public int CaseID { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public int ReceivedByUserID { get; set; }
    public string? ReceivedByUserName { get; set; } // JOIN فقط
    public string? Notes { get; set; }
}

// ملخص مالي مُجمَّع لفترة زمنية (اليوم/الشهر/السنة) - يعادل بطاقات Finance العيادة لكن لبيانات
// الترميم فقط، محسوب دائمًا من ProstheticPayments مباشرة (وليس من TotalAgreedPrice) حتى يبقى
// متوافقًا مع نفس منطق "حذف الدفعة لا يُنشئ دَينًا وهميًا" المُصلَح سابقًا في Finance العيادة.
public class ProstheticFinanceSummary
{
    public decimal TodayRevenue { get; set; }
    public decimal MonthRevenue { get; set; }
    public decimal YearRevenue { get; set; }

    // ⚠️ كلاهما محسوب لكل حالة على حدة ثم يُجمَع (وليس فرق مجموعين خام)، حتى لا يُلغي فائض حالة
    // نقصان حالة أخرى في المجموع النهائي - راجع ProstheticPaymentRepository.GetFinanceSummary
    public decimal TotalOutstandingBalance { get; set; }
    public decimal TotalCreditBalance { get; set; }
}
