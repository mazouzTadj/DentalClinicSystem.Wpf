using System.Collections.Generic;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public class UserRowViewModel
{
    public UserAccount User { get; }

    public int UserID => User.UserID;
    public string FullName => User.FullName;
    public string Username => User.Username;
    public string RoleText
    {
        get
        {
            var baseText = LocalizationManager.T(User.Role switch
            {
                UserRole.Doctor => "UserMgmt_RoleDoctor",
                UserRole.Nurse => "UserMgmt_RoleNurse",
                UserRole.Prosthetist => "UserMgmt_RoleProsthetist",
                _ => "UserMgmt_RoleDoctor"
            });
            // "★" لمن هو الطبيب الرئيسي حالياً - أبسط تعديل ممكن لإظهار الحالة في القائمة الموجودة
            // أصلاً بدل إضافة عمود جديد كامل في XAML لهذا الغرض وحده
            return User.Role == UserRole.Doctor && User.IsMainDoctor
                ? baseText + " ★ " + LocalizationManager.T("UserMgmt_MainDoctorBadge")
                : baseText;
        }
    }
    public string AdminText => User.IsSuperAdmin ? LocalizationManager.T("UserMgmt_AdminYes") : "-";
    // ملاحظة: لا تُستخدَم هذه القيمة لتحديد لون شارة الحالة في XAML (استُخدم User.IsActive مباشرة لذلك
    // حتى يبقى صحيحاً بغض النظر عن اللغة) - هذه فقط للعرض النصي.
    public string StatusText => User.IsActive ? LocalizationManager.T("UserMgmt_StatusActive") : LocalizationManager.T("UserMgmt_StatusInactive");
    public string LastLoginText => User.LastLoginAt.HasValue
        ? User.LastLoginAt.Value.ToString("yyyy-MM-dd HH:mm")
        : LocalizationManager.T("UserMgmt_Never");

    // ملخص نصي مختصر لكل الصلاحيات النشطة - مفيد لعرضه كعمود إضافي في شبكة المستخدمين
    public string PermissionsSummary
    {
        get
        {
            var parts = new List<string>();
            if (User.HasPermission(UserPermission.ManageUsers)) parts.Add(LocalizationManager.T("Perm_Users"));
            if (User.HasPermission(UserPermission.AccessFinance)) parts.Add(LocalizationManager.T("Perm_Finance"));
            if (User.HasPermission(UserPermission.OpenPatientFile)) parts.Add(LocalizationManager.T("Perm_PatientFiles"));
            if (User.HasPermission(UserPermission.AccessBackup)) parts.Add(LocalizationManager.T("Perm_Backup"));
            if (User.HasPermission(UserPermission.ManageTreatments)) parts.Add(LocalizationManager.T("Perm_Treatments"));
            if (User.HasPermission(UserPermission.CollectPayments)) parts.Add(LocalizationManager.T("Perm_Payments"));
            if (User.HasPermission(UserPermission.RegisterPatients)) parts.Add(LocalizationManager.T("Perm_RegisterPatients"));
            if (User.HasPermission(UserPermission.EditPatients)) parts.Add(LocalizationManager.T("Perm_EditPatients"));
            return parts.Count == 0 ? "-" : string.Join(", ", parts);
        }
    }

    public UserRowViewModel(UserAccount user)
    {
        User = user;
    }
}
