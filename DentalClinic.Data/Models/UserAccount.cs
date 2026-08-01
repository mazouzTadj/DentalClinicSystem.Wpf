namespace DentalClinic.Data.Models;

public enum UserRole
{
    Doctor = 1,
    Nurse = 2
}

// نظام صلاحيات دقيق (Bitmask) مستقل تماماً عن الدور الأساسي (Doctor/Nurse).
// كل صلاحية = بت واحد، ويمكن دمج أي عدد منها في عمود واحد PermissionsMask في قاعدة البيانات.
// لإضافة صلاحية جديدة مستقبلاً: أضف سطراً جديداً بالقيمة التالية في التسلسل (1 << 6, 1 << 7, ...)
// ولا تُعِد استخدام قيمة صلاحية محذوفة سابقاً كي لا تتضارب مع بيانات قديمة.
[Flags]
public enum UserPermission
{
    None             = 0,
    ManageUsers      = 1 << 0, // فتح شاشة إدارة المستخدمين وتعديل صلاحيات الآخرين
    AccessFinance    = 1 << 1, // الدخول إلى لوحة الفاينانس / الأرباح
    OpenPatientFile  = 1 << 2, // فتح الملف الطبي الكامل للمريض
    AccessBackup     = 1 << 3, // النسخ الاحتياطي والاستعادة
    ManageTreatments = 1 << 4, // إدارة قائمة العلاجات والأسعار
    CollectPayments  = 1 << 5, // تحصيل دفعات المرضى
    RegisterPatients = 1 << 6, // تسجيل مريض جديد (Register New Patient)
}

public class UserAccount
{
    public int UserID { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    // كل الصلاحيات الدقيقة للمستخدم، مخزَّنة كقناع بتّي واحد (يحل محل IsAdmin القديم)
    public UserPermission Permissions { get; set; } = UserPermission.None;

    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    // نسبة عمولة مخصَّصة لهذا الطبيب تحديداً (0-100). إن كانت فارغة (null) يُستخدم بدلاً منها
    // الإعداد العام الافتراضي (ClinicSettings: DoctorCommissionPercent). لا قيمة لها بالنسبة للطبيب الرئيسي.
    public decimal? CommissionPercent { get; set; }

    // دالة الفحص الموحَّدة - تُستخدم في كل مكان بدل تكرار العمليات البتّية يدوياً
    public bool HasPermission(UserPermission permission) =>
        (Permissions & permission) == permission;

    // "مدير عام" = من يملك صلاحية إدارة المستخدمين تحديداً
    public bool IsSuperAdmin => HasPermission(UserPermission.ManageUsers);
}
