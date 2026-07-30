using DentalClinic.Data.Models;

namespace DentalClinic.Features;

public class UserRowViewModel
{
    public UserAccount User { get; }

    public int UserID => User.UserID;
    public string FullName => User.FullName;
    public string Username => User.Username;
    public string RoleText => User.Role == UserRole.Doctor ? "Doctor" : "Nurse";
    public string AdminText => User.IsSuperAdmin ? "Admin" : "-";
    public string StatusText => User.IsActive ? "Active" : "Inactive";
    public string LastLoginText => User.LastLoginAt.HasValue
        ? User.LastLoginAt.Value.ToString("yyyy-MM-dd HH:mm")
        : "Never";

    // ملخص نصي مختصر لكل الصلاحيات النشطة - مفيد لعرضه كعمود إضافي في شبكة المستخدمين
    public string PermissionsSummary
    {
        get
        {
            var parts = new List<string>();
            if (User.HasPermission(UserPermission.ManageUsers)) parts.Add("Users");
            if (User.HasPermission(UserPermission.AccessFinance)) parts.Add("Finance");
            if (User.HasPermission(UserPermission.OpenPatientFile)) parts.Add("Patient Files");
            if (User.HasPermission(UserPermission.AccessBackup)) parts.Add("Backup");
            if (User.HasPermission(UserPermission.ManageTreatments)) parts.Add("Treatments");
            if (User.HasPermission(UserPermission.CollectPayments)) parts.Add("Payments");
            if (User.HasPermission(UserPermission.RegisterPatients)) parts.Add("Register Patients");
            return parts.Count == 0 ? "-" : string.Join(", ", parts);
        }
    }

    public UserRowViewModel(UserAccount user)
    {
        User = user;
    }
}
