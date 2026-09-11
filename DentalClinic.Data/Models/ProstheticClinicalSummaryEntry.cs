using System;

namespace DentalClinic.Data.Models;

// صف واحد من "الملخص السريري" لمريض معيَّن - نسخة مُختزَلة جداً من MedicalSession (Patch 9)،
// تحتوي فقط الحقول الثلاثة المشمولة بصلاحيات Prosthetics.ViewTreatment/ViewDiagnosis/ViewMedications
// بالإضافة لتاريخ الجلسة واسم الطبيب (معلومات تعريف غير حساسة، لا صلاحية خاصة بها). لا تحتوي
// عمداً أي حقل مالي (TotalPrice/PaidAmount/WriteOffAmount/RemainingAmount) ولا الشكوى
// الرئيسية/الملاحظات (غير مُغطاة بأي صلاحية من الـ21 صلاحية الحالية، فتُستبعَد بدل عرضها بلا قيد).
public class ProstheticClinicalSummaryEntry
{
    public DateTime SessionDateTime { get; set; }
    public string DoctorName { get; set; } = "-";
    public string? Diagnosis { get; set; }
    public string? TreatmentPerformed { get; set; }
    public string? Medication { get; set; }
}
