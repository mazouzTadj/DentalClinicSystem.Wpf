namespace DentalClinic.Data.Models;

// صف واحد من جدول Permissions - تعريف "قابلية" وليس منح فعلي لأحد (المنح الفعلي في UserPermissions).
// هذا هو النظام "المرن" المطلوب لصلاحيات المرمم: إضافة صلاحية جديدة = صف جديد في القاعدة،
// بدون أي تعديل كود. راجع PermissionRepository وPermissionKeys (المفاتيح الأساسية المزروعة تلقائيًا).
public class PermissionDefinition
{
    public string PermissionKey { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
}

// أسماء مفاتيح الصلاحيات الأساسية الخاصة بالترميم، مزروعة تلقائيًا في جدول Permissions عبر
// SchemaInitializer.EnsureProstheticPermissionsSeeded. تُستخدم كثوابت لتفادي أخطاء الطباعة عند
// الفحص من الكود (PermissionRepository.HasKey وغيرها) - لكن الجدول نفسه هو مصدر الحقيقة، ويمكن
// للطبيب لاحقًا (عبر أداة إدارية) إضافة مفاتيح أخرى غير هذه القائمة دون أي تعديل برمجي.
//
// ⚠️ كل صلاحية هنا ذرّية (تحكم عملية واحدة فقط) عمدًا - لا توجد صلاحية "مُجمِّعة" مثل
// ManageCases القديمة، حتى يستطيع الطبيب منح "تغيير المرحلة" لمرمم دون أن يمنحه ضمنيًا
// حق حذف الحالة أو نقلها لمرمم آخر.
public static class ProstheticPermissionKeys
{
    // ===== الحالة (Case) =====
    public const string ViewAllCases = "Prosthetics.ViewAllCases";   // رؤية كل الحالات وليس المسندة له فقط
    public const string CreateCase = "Prosthetics.CreateCase";
    public const string EditCase = "Prosthetics.EditCase";           // نوع العمل / Upper-Lower / السعر المتفق عليه / الملاحظات
    public const string DeleteCase = "Prosthetics.DeleteCase";
    public const string TransferCase = "Prosthetics.TransferCase";   // نقل الحالة لمرمم آخر
    public const string ViewStage = "Prosthetics.ViewStage";
    public const string EditStage = "Prosthetics.EditStage";
    public const string ManageLookups = "Prosthetics.ManageLookups"; // إدارة قوائم أنواع العمل/المراحل نفسها

    // ===== الجلسات (Sessions) =====
    public const string AddSession = "Prosthetics.AddSession";
    public const string EditSession = "Prosthetics.EditSession";
    public const string DeleteSession = "Prosthetics.DeleteSession";

    // ===== بيانات المريض المرتبطة (سريرية - للاطلاع فقط ما لم يُذكر خلاف ذلك) =====
    public const string ViewPatient = "Prosthetics.ViewPatient";
    public const string EditPatientInfo = "Prosthetics.EditPatientInfo";
    public const string ViewTreatment = "Prosthetics.ViewTreatment";
    public const string ViewDiagnosis = "Prosthetics.ViewDiagnosis";
    public const string ViewMedications = "Prosthetics.ViewMedications";

    // ===== المالية (Finance) =====
    public const string ViewFinance = "Prosthetics.ViewFinance";     // الوصول للوحة المالية العامة (Dashboard)
    public const string ViewPayment = "Prosthetics.ViewPayment";     // رؤية دفعات حالة واحدة داخل ملفها
    public const string AddPayment = "Prosthetics.AddPayment";
    public const string EditPayment = "Prosthetics.EditPayment";
    public const string DeletePayment = "Prosthetics.DeletePayment";

    // مصاريف المرمم (ProstheticExpenses، Patch 12) - ذرّيتان مثل AddPayment/DeletePayment أعلاه
    // بالضبط. رؤية القائمة والإجمالي (بما فيها الدخل الصافي) تبقى تحت ViewFinance نفسها بلا صلاحية
    // جديدة (نفس ما تخضع له بطاقات القيمة/المدفوعات/المتبقي أصلاً) - هاتان الصلاحيتان فقط لعمليتي
    // الإضافة والحذف.
    public const string AddExpense = "Prosthetics.AddExpense";
    public const string EditExpense = "Prosthetics.EditExpense";
    public const string DeleteExpense = "Prosthetics.DeleteExpense";
}
