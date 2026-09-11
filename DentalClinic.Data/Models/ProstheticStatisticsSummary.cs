namespace DentalClinic.Data.Models;

// ملخص إحصائي واحد لمرمم (أو لكل المرممين) ضمن فترة زمنية محدَّدة - Patch 11.1 + 11.2.
// ⚠️ كل الحقول هنا مبنية حصراً من ProstheticCases + ProstheticSessions + ProstheticPayments
// + ProstheticStages + ProstheticCaseHistory (Patch 11.2، للمشتقات الزمنية فقط - راجع
// AverageCompletionDays/AverageDaysInStage) - لا صلة إطلاقاً بـ Payments أو MedicalSessions أو
// FinancialRepository (فاينانس العيادة العام)، فالفصل المالي المتَّفق عليه منذ Patch 1 محفوظ بالكامل.
public class ProstheticStatisticsSummary
{
    // ===== عدّادات الحالات - كلها ضمن نطاق الفترة المحدَّدة (CreatedAt) وفلتر المرمم =====
    public int TotalCases { get; set; }
    public int CompletedCases { get; set; }
    public int ActiveCases { get; set; }     // CaseStatus = Open
    public int CancelledCases { get; set; }

    // Upper / Lower / كلاهما - عدّاد لكل تركيبة على حدة (نفس الحالات أعلاه)
    public int UpperOnlyCases { get; set; }
    public int LowerOnlyCases { get; set; }
    public int BothArchCases { get; set; }

    // عدد الجلسات المسجَّلة فعلياً ضمن الفترة (SessionDateTime، وليس تاريخ إنشاء الحالة) لحالات
    // مطابقة لفلتر المرمم - راجع تعليق GetSummary لمزيد من التفصيل حول اختيار هذا المعيار الزمني
    public int SessionCount { get; set; }

    // ===== الحقول المالية - من ProstheticCases/ProstheticPayments حصراً =====
    public decimal TotalCaseValue { get; set; }      // مجموع TotalAgreedPrice لحالات الفترة
    public decimal TotalPayments { get; set; }        // مدفوعات وردت ضمن الفترة (PaymentDate)
    public decimal TotalOutstanding { get; set; }      // متبقٍّ حي (الآن) على حالات الفترة، وليس محسوباً وقت إنشائها

    // مصاريف المرمم ضمن نفس الفترة/الفلتر (ExpenseDate) - Patch 12، من dbo.ProstheticExpenses حصراً
    // (جدول مستقل تماماً عن ClinicExpenses العام - راجع تعليق أعلى الملف حول الفصل المالي. لا علاقة
    // لهذا الحقل بأي مصروف عيادة عام، فقط مصاريف مسجَّلة صراحة لهذا المرمم: مخبر/مواد/شحن/مستحقات...)
    public decimal TotalExpenses { get; set; }

    // الدخل الصافي = المدفوعات الواردة ناقص مصاريف المرمم لنفس الفترة/الفلتر - عرض مشتق فقط،
    // وليس عموداً مخزَّناً في أي جدول (نفس أسلوب AverageCaseValue/CompletionRatePercent أدناه)
    public decimal NetIncome => TotalPayments - TotalExpenses;

    public decimal AverageCaseValue => TotalCases == 0 ? 0 : TotalCaseValue / TotalCases;

    // نسبة إنجاز الحالات ضمن الفترة (Patch 11.2) - مشتقة من عدَّادين موجودين أصلاً في 11.1،
    // وليست استعلاماً جديداً؛ فقط عرض مختلف (شريط تقدُّم) لنفس البيانات
    public double CompletionRatePercent => TotalCases == 0 ? 0 : (double)CompletedCases / TotalCases * 100.0;

    // متوسط عدد أيام إنجاز الحالة (من الإنشاء إلى آخر StatusChanged→Completed في History) - Patch
    // 11.2. null إن لم توجد أي حالة مكتملة ذات سجل History متّسق ضمن الفترة (بدل عرض 0 مضلِّل)
    public double? AverageCompletionDays { get; set; }

    public List<ProstheticStageCount> CasesByStage { get; set; } = new();
}

// عدد الحالات في مرحلة واحدة (ضمن نفس فلتر الفترة/المرمم أعلاه) - للرسم البياني الشريطي البسيط
public class ProstheticStageCount
{
    public string StageName { get; set; } = "-";
    public int Count { get; set; }

    // متوسط عدد الأيام التي أمضتها الحالات "الحالية" في هذه المرحلة تحديداً (منذ آخر StageChanged
    // نحوها في History، أو منذ CreatedAt إن لم تتغيّر المرحلة إطلاقاً) - Patch 11.2، مؤشر تقريبي
    // على "الازدحام الزمني" لكل مرحلة، وليس فقط عدد الحالات فيها
    public double? AverageDaysInStage { get; set; }
}

// صف واحد في جدول "مقارنة المرممين" (Patch 11.2) - نفس مقاييس ProstheticStatisticsSummary
// الأساسية لكن لكل مرمم على حدة ضمن استعلام واحد مُجمَّع (وليس استدعاء GetSummary لكل مرمم على حدة)
public class ProstheticComparisonRow
{
    public int ProsthetistUserID { get; set; }
    public string ProsthetistName { get; set; } = "-";
    public int TotalCases { get; set; }
    public int CompletedCases { get; set; }
    public int ActiveCases { get; set; }
    public int SessionCount { get; set; }
    public decimal TotalCaseValue { get; set; }
    public decimal TotalPayments { get; set; }
    public decimal TotalOutstanding { get; set; }

    // Patch 12: نفس حقلي TotalExpenses/NetIncome في ProstheticStatisticsSummary أعلاه، لكن لكل
    // مرمم على حدة ضمن صف المقارنة الواحد
    public decimal TotalExpenses { get; set; }
    public decimal NetIncome => TotalPayments - TotalExpenses;
}

// صف واحد في جدول "الحالات المتأخرة" (Patch 11.2) - عمداً بلا أي بيانات مريض (لا اسم ولا هاتف)،
// تماماً كما في جدول قائمة الانتظار الأصلي (راجع القسم 7 من وثيقة التصميم: عدم إظهار اسم المريض
// في الجداول التجميعية قرار مقصود). يشمل المرحلة الحالية لكل حالة - عمود يُخفى في الواجهة لمن لا
// يملك Prosthetics.ViewStage/EditStage (نفس القيد المطبَّق على حقل المرحلة في كل مكان آخر منذ Patch 8)
public class ProstheticOverdueCaseRow
{
    public int CaseID { get; set; }
    public int CaseNumber { get; set; }
    public string ProsthetistName { get; set; } = "-";
    public string StageName { get; set; } = "-";
    public DateTime CreatedAt { get; set; }
    public int DaysOpen { get; set; }
}
