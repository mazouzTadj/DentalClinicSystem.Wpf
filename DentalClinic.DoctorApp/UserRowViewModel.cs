using DentalClinic.Data.Models;

namespace DentalClinic.DoctorApp;

public class UserRowViewModel
{
    public UserAccount User { get; }

    public int UserID => User.UserID;
    public string FullName => User.FullName;
    public string Username => User.Username;
    public string RoleText => User.Role == UserRole.Doctor ? "Doctor" : "Nurse";
    public string AdminText => User.IsAdmin ? "Admin" : "-";
    public string StatusText => User.IsActive ? "Active" : "Inactive";
    public string LastLoginText => User.LastLoginAt.HasValue
        ? User.LastLoginAt.Value.ToString("yyyy-MM-dd HH:mm")
        : "Never";

    public UserRowViewModel(UserAccount user)
    {
        User = user;
    }
}
