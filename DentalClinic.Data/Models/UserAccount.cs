namespace DentalClinic.Data.Models;

// ⚠️ Doctor=1 وNurse=2 فقط يجب أن يطابقا RoleID الفعلي في جدول Roles مباشرة (راجع تعليق Seed
// في DentalClinicSchema.sql). لا تُعِد ترتيبهما ولا تُدرِج دورًا جديدًا بينهما.
// Prosthetist=3 هنا هي قيمة Enum داخلية للبرنامج فقط - لا تُكتب ولا تُقرأ مباشرة من/إلى عمود
// RoleID في القاعدة؛ UserRepository.RoleToId/IdToRole يبحثان عن RoleID الفعلي للمرمم بالاسم
// (RoleName = 'Prosthetist') لأن قواعد قديمة/مخصَّصة قد لا يكون رقمه فيها 3 بالضبط.
public enum UserRole
{
    Doctor = 1,
    Nurse = 2,
    Prosthetist = 3
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
    DeletePatients   = 1 << 7, // حذف مريض نهائياً من قاعدة البيانات (عملية لا رجعة فيها)
    EditPatients     = 1 << 8, // تعديل البيانات الأساسية لمريض مسجَّل مسبقاً (تصحيح خطأ إدخال، مثلاً)
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

    // "نبضة" دورية يُحدِّثها التطبيق كل فترة قصيرة طالما التطبيق مفتوح والمستخدم مسجَّل دخوله بها -
    // أساس ميزة "متصل الآن" (النقطة الخضراء). لا تُستخدم مباشرة من الواجهة؛ استخدم IsOnline أدناه.
    public DateTime? LastHeartbeatAt { get; set; }

    // محسوبة من طرف قاعدة البيانات وقت الجلب فقط (وليست عمود مخزَّن) - راجع UserRepository.
    // "متصل الآن" = آخر نبضة خلال آخر 90 ثانية تقريباً. تُصحَّح نفسها تلقائياً حتى لو انطفأ
    // التطبيق فجأة بلا تسجيل خروج سليم (لا تعتمد على حدث "خروج" صريح إطلاقاً).
    public bool IsOnline { get; set; }

    // نسبة عمولة مخصَّصة لهذا الطبيب تحديداً (0-100). إن كانت فارغة (null) يُستخدم بدلاً منها
    // الإعداد العام الافتراضي (ClinicSettings: DoctorCommissionPercent). لا قيمة لها بالنسبة للطبيب الرئيسي.
    public decimal? CommissionPercent { get; set; }

    // ⚠️ "الطبيب الرئيسي" هنا مفهوم أمني/إداري جديد كلياً - منفصل تماماً عن UserRepository.
    // GetPrimaryDoctorUserId() (ذلك يُستخدم فقط لحساب العمولات، ويُعرَّف بأصغر UserID - أي "أول
    // طبيب سُجِّل بالنظام" بغض النظر عن صلاحياته الفعلية اليوم). لا تخلط بينهما.
    // يُحدَّد هذا العلم صراحة عبر عمود Users.IsMainDoctor (لا يعتمد على ترتيب الإنشاء ولا على
    // اسم المستخدم) - يتحكم حصرياً بمن يستطيع الدخول إلى DentalClinic.ProsthetistApp بحساب طبيب
    // (بصلاحية كاملة هناك) ورؤية زر فتحه من DoctorApp. راجع UserRepository.EnsureMainDoctorColumnExists.
    public bool IsMainDoctor { get; set; }

    // دالة الفحص الموحَّدة - تُستخدم في كل مكان بدل تكرار العمليات البتّية يدوياً
    public bool HasPermission(UserPermission permission) =>
        (Permissions & permission) == permission;

    // "مدير عام" = من يملك صلاحية إدارة المستخدمين تحديداً
    public bool IsSuperAdmin => HasPermission(UserPermission.ManageUsers);

    // ===================== صلاحيات المرمم الدقيقة (نظام منفصل تمامًا عن UserPermission Bitmask) =====================
    // نظام Key/Value مرن مبني على جدولي Permissions/UserPermissions (كانا موجودين في المخطط بدون استخدام
    // فعلي قبل إضافة تطبيق المرمم) - يسمح للطبيب الرئيسي بإضافة/تعديل صلاحيات دقيقة كبيانات محضة،
    // دون أي حاجة لتعديل كود C# أو Enum عند إضافة صلاحية جديدة مستقبلاً (بعكس UserPermission أعلاه).
    // فارغة افتراضيًا؛ تُملأ فقط عبر PermissionRepository.GetGrantedKeys عندما يكون ذلك مطلوبًا فعليًا
    // (تطبيق المرمم أساسًا)، حتى لا تُثقِل تسجيل الدخول العادي لتطبيقي الطبيب/الممرضة باستعلام إضافي غير لازم لهما.
    public HashSet<string> ProstheticPermissionKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasProstheticPermission(string permissionKey) =>
        IsSuperAdmin || ProstheticPermissionKeys.Contains(permissionKey);
}
