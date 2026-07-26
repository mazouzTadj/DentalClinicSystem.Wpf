namespace DentalClinic.Data.Models;

public enum UserRole
{
    Doctor = 1,
    Nurse = 2
}

public class UserAccount
{
    public int UserID { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    // صلاحية إضافية مستقلة تماماً عن الدور الأساسي (Doctor/Nurse) - تفتح شاشة إدارة المستخدمين فقط
    public bool IsAdmin { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
