namespace DentalClinic.Data.Models;

// "نوع العمل" (تاج، جسر، طقم كامل، طقم جزئي ...) - قائمة قابلة للتوسع بالكامل من واجهة الطبيب،
// وليست Enum ثابتة في الكود (تنفيذًا لطلب صريح: لا تجعلها Hardcoded إذا أمكن جعلها قابلة للتوسع)
public class ProstheticWorkType
{
    public int WorkTypeID { get; set; }
    public string WorkTypeName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

// "مرحلة العلاج" (انطباع، تصميم، تجربة، تسليم ...) - قابلة للتوسع والتعديل مستقبلًا من قِبل الطبيب،
// منفصلة تمامًا عن "حالة الملف" (CaseStatus) في ProstheticCase - راجع التمييز بينهما هناك.
public class ProstheticStage
{
    public int StageID { get; set; }
    public string StageName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
