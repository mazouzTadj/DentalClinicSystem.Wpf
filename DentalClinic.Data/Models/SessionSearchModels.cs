namespace DentalClinic.Data.Models;

// معايير البحث المتقدم في الجلسات الطبية - كل الحقول اختيارية، يُطبَّق فقط ما يُملأ منها
public class SessionSearchCriteria
{
    public string? PatientNameOrPhone { get; set; }
    public string? DiagnosisContains { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public bool OnlyWithOutstandingBalance { get; set; }
    public string? ToothNumber { get; set; }
}

// صف نتيجة بحث متقدم - يجمع بيانات المريض مع الجلسة في سطر واحد جاهز للعرض
public class SessionSearchResult
{
    public int SessionID { get; set; }
    public int PatientID { get; set; }
    public string PatientFullName { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;
    public DateTime SessionDateTime { get; set; }
    public string? Diagnosis { get; set; }
    public string? TreatmentPerformed { get; set; }
    public decimal TotalPrice { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount => TotalPrice - PaidAmount;
}
