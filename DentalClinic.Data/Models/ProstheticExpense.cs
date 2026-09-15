namespace DentalClinic.Data.Models;

// مصروف خاص بعمل مرمم واحد (تكاليف مخبر/مواد/شحن/مستحقات المرمم...) - Patch 12.
// جدول مستقل تماماً عن ClinicExpenses (مصاريف العيادة العامة التي تُدار من FinancialRepository
// في تطبيق الطبيب) - بنفس منطق الفصل المالي المعتمد أصلاً في كل ملفات نظام الترميم (راجع تعليق
// أعلى ProstheticStatisticsSummary/ProstheticStatisticsRepository). كل مصروف يخص مرمماً واحداً
// بالتحديد (ProsthetistUserID)، وهو أساس حساب "الدخل الصافي" لكل مرمم = مدفوعاته الواردة
// (ProstheticPayments) ناقص مصاريفه هنا، ضمن نفس نطاق الفترة/الفلتر المُختار في شاشات الإحصائيات.
public class ProstheticExpense
{
    public int ExpenseID { get; set; }
    public int ProsthetistUserID { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General / Other";
    public DateTime ExpenseDate { get; set; }
    public int CreatedByUserID { get; set; }
    public DateTime CreatedAt { get; set; }
}

// صف عرض واحد في جدول قائمة المصاريف (ProstheticExpensesWindow) - اسم المرمم جاهز للعرض هنا
// (وليس UserID خام) حتى لا تحتاج الواجهة لأي Join/Lookup إضافي عند العرض.
public class ProstheticExpenseRow
{
    public int ExpenseID { get; set; }
    public int ProsthetistUserID { get; set; }
    public string ProsthetistName { get; set; } = "-";
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTime ExpenseDate { get; set; }

    public string DateText => ExpenseDate.ToString("yyyy-MM-dd");
}
