namespace DentalClinic.Data.DataAccess;

// يُرمى عند محاولة حذف نوع عمل أو مرحلة نهائياً وهو لا يزال مستخدَماً في حالة ترميم واحدة أو
// أكثر (قيد FK_ProstheticCases_WorkType / FK_ProstheticCases_Stage) - بدل ترك SQL Server يرمي
// خطأ FK خام غير مفهوم للمستخدم، تُمسَك هذه في الواجهة وتُعرَض برسالة عربية واضحة تقترح التعطيل
// كبديل بما أن السجل التاريخي يعتمد عليه.
public class ProstheticLookupInUseException : Exception
{
    public int UsageCount { get; }

    public ProstheticLookupInUseException(int usageCount)
        : base($"This item is still used by {usageCount} case(s) and cannot be permanently deleted.")
    {
        UsageCount = usageCount;
    }
}
