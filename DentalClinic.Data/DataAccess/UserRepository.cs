using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;
using DentalClinic.Data.Helpers;

namespace DentalClinic.Data.DataAccess;

public class UserRepository
{
    private readonly DatabaseHelper _db;

    public UserRepository(DatabaseHelper db)
    {
        _db = db;
    }

    // البحث عن مستخدم عبر اسم المستخدم (بدون فلترة حسب الدور)
    public UserAccount? FindByUsername(string username)
    {
        const string sql = @"
            SELECT UserID, FullName, Username, PasswordHash, RoleID, PhoneNumber, IsActive, CreatedAt, LastLoginAt
            FROM Users
            WHERE Username = @Username AND IsActive = 1";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@Username", username));
        if (table.Rows.Count == 0) return null;

        var row = table.Rows[0];
        return new UserAccount
        {
            UserID = (int)row["UserID"],
            FullName = row["FullName"].ToString()!,
            Username = row["Username"].ToString()!,
            PasswordHash = row["PasswordHash"].ToString()!,
            Role = (int)row["RoleID"] == 1 ? UserRole.Doctor : UserRole.Nurse,
            PhoneNumber = row["PhoneNumber"] as string,
            IsActive = (bool)row["IsActive"],
            CreatedAt = (DateTime)row["CreatedAt"],
            LastLoginAt = row["LastLoginAt"] as DateTime?
        };
    }

    // تسجيل الدخول: يتحقق من اسم المستخدم، كلمة المرور، وأن الدور مطابق للتطبيق المُستخدَم
    // (مثال: حساب ممرضة لا يمكنه الدخول إلى تطبيق الطبيب والعكس صحيح)
    public UserAccount? Authenticate(string username, string password, UserRole requiredRole, out string errorMessage)
    {
        errorMessage = string.Empty;
        var user = FindByUsername(username);

        if (user == null)
        {
            errorMessage = "Username not found or account is not active";
            return null;
        }

        if (!PasswordHelper.Verify(password, user.PasswordHash))
        {
            errorMessage = "Incorrect password";
            return null;
        }

        if (user.Role != requiredRole)
        {
            errorMessage = "This account is not authorized to access this application";
            return null;
        }

        UpdateLastLogin(user.UserID);
        return user;
    }

    private void UpdateLastLogin(int userId)
    {
        const string sql = "UPDATE Users SET LastLoginAt = GETDATE() WHERE UserID = @UserID";
        _db.ExecuteNonQuery(sql, new SqlParameter("@UserID", userId));
    }
}
