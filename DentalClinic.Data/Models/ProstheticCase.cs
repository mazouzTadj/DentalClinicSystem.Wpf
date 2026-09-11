namespace DentalClinic.Data.Models;

// "حالة الملف" الإدارية العامة - مختلفة تمامًا عن "مرحلة العلاج" (ProstheticStage).
// مثال: حالة قد تكون CaseStatus=Open وStage=Impression في نفس الوقت، ثم يبقى CaseStatus=Open
// بينما تتغير Stage عدة مرات (Design ثم TryIn ثم Delivery)، وأخيرًا يصبح CaseStatus=Completed.
public static class ProstheticCaseStatus
{
    public const string Open = "Open";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

// حالة ترميم واحدة تخص مريضًا موجودًا أصلًا في النظام (لا تُنشئ Patient جديدًا أبدًا).
// المريض الواحد يمكن أن يملك عدة حالات في نفس الوقت (CaseNumber مستقل ومتسلسل، وليس PatientID).
public class ProstheticCase
{
    public int CaseID { get; set; }

    // رقم الحالة - متسلسل مستقل تمامًا (SQL SEQUENCE)، يبدأ من 1، لا يتكرر، لا يتغير أبدًا بعد الإنشاء.
    // ليس مشتقًا من PatientID ولا من CaseID (لتفادي أي التباس مستقبلي إن اختلف ترتيب الحذف/الأرشفة).
    public int CaseNumber { get; set; }

    public int PatientID { get; set; }

    // معلومات المريض الأصلي (تُملأ عبر JOIN عند القراءة فقط - لا تُخزَّن مكررة هنا في القاعدة)
    public string PatientFullName { get; set; } = string.Empty;
    public string PatientPhoneNumber { get; set; } = string.Empty;

    public int? WorkTypeID { get; set; }
    public string? WorkTypeName { get; set; } // JOIN فقط

    // لا افتراض بأن الحالة Upper أو Lower حصرًا - يمكن أن تكون كلاهما معًا، أو لا شيء منهما
    // (حالة بسنّ واحد مثلًا) موصوفة عبر ToothScope بدلًا من ذلك.
    public bool IncludesUpper { get; set; }
    public bool IncludesLower { get; set; }
    public string? ToothScope { get; set; }

    public int? AssignedProsthetistUserID { get; set; }
    public string? AssignedProsthetistName { get; set; } // JOIN فقط

    public int? StageID { get; set; }
    public string? StageName { get; set; } // JOIN فقط

    public string CaseStatus { get; set; } = ProstheticCaseStatus.Open;

    public decimal TotalAgreedPrice { get; set; }
    public decimal TotalPaid { get; set; } // محسوبة من ProstheticPayments عند القراءة

    // ⚠️ لا افتراض بأن الدفعات لن تتجاوز السعر المتفق عليه أبدًا (قد يُدفع مقدَّم كبير قبل تحديد
    // السعر النهائي، أو يُعدَّل السعر لاحقًا لأسفل). لذلك لا نسمح بقيمة سالبة مضلِّلة في
    // OutstandingBalance (تبدو وكأنها "دَين على العيادة")؛ بدلاً من ذلك نُظهر الفائض في
    // CreditBalance منفصلة، بحيث يبقى كل رقم واضح المعنى بذاته دون حساب ذهني إضافي في الواجهة.
    public decimal OutstandingBalance => Math.Max(0, TotalAgreedPrice - TotalPaid);
    public decimal CreditBalance => Math.Max(0, TotalPaid - TotalAgreedPrice);
    public bool IsOverpaid => TotalPaid > TotalAgreedPrice;

    public string? Notes { get; set; }

    public int CreatedByUserID { get; set; }
    public string? CreatedByUserName { get; set; } // JOIN فقط
    public DateTime CreatedAt { get; set; }
}

// سجل واحد في تاريخ/سجل تدقيق الحالة (Case History / Audit Log) - راجع البند 20 في المتطلبات
public class ProstheticCaseHistoryEntry
{
    public int HistoryID { get; set; }
    public int CaseID { get; set; }
    public int ChangedByUserID { get; set; }
    public string? ChangedByUserName { get; set; } // JOIN فقط
    public DateTime ChangedAt { get; set; }

    // مثال: "Created", "StageChanged", "ProsthetistReassigned", "StatusChanged", "InfoEdited"
    public string ActionType { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Notes { get; set; }
}
