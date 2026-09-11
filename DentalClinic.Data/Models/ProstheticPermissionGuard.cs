namespace DentalClinic.Data.Models;

// Patch 14 — Security & Integrity Hardening.
// تحقق صلاحية موحَّد تستخدمه ثلاث Repositories مختلفة (ProstheticCaseRepository /
// ProstheticSessionRepository / ProstheticPaymentRepository) لعمليات الكتابة الحساسة، بدل تكرار
// نفس منطق "Role==Doctor || HasProstheticPermission(key)" ثلاث مرات بشكل مستقل (خطر تضارب أو نسيان
// تحديث نسخة منها لاحقًا). هذا هو الملف الجديد الوحيد المُضاف في Patch 14 - كل شيء آخر تعديل على
// ملفات موجودة، وهو مبرَّر لأن الحذف من ثلاث نسخ مكرَّرة لنفس الفحص الأمني أسوأ من ملف صغير مشترك.
//
// ⚠️ كل طبيب (وليس فقط الطبيب الرئيسي) يملك صلاحية كاملة على نظام الترميم بلا استثناء - هذا
// السلوك موجود أصلًا منذ التصميم الأول (راجع HasEditRight في ProstheticCaseEditWindow:
// "_currentUser.Role == UserRole.Doctor || _currentUser.HasProstheticPermission(permissionKey)")
// ولم يتغيَّر هنا؛ نُعيد استخدام نفس القاعدة بالضبط في طبقة البيانات، وليس قاعدة جديدة.
public static class ProstheticPermissionGuard
{
    public static void Ensure(UserAccount actingUser, string permissionKey)
    {
        if (actingUser.Role == UserRole.Doctor) return;
        if (actingUser.HasProstheticPermission(permissionKey)) return;

        throw new UnauthorizedAccessException(
            $"You don't have the required permission ({permissionKey}) to perform this action.");
    }
}
