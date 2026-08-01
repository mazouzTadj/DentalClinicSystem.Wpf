using System.Data;
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
        EnsureCommissionColumnExists();
    }

    // إضافة عمود نسبة العمولة المخصَّصة لكل طبيب - migration آمنة لقواعد البيانات القديمة
    // (نفس نمط EnsureClinicExpensesTableExists المستخدَم في FinancialRepository)
    private void EnsureCommissionColumnExists()
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'CommissionPercent' AND Object_ID = Object_ID(N'Users'))
            BEGIN
                ALTER TABLE Users ADD CommissionPercent DECIMAL(5,2) NULL;
            END";
        _db.ExecuteNonQuery(sql);
    }

    // البحث عن مستخدم عبر اسم المستخدم (حسابات نشطة فقط) - تُستخدم لتسجيل الدخول
    public UserAccount? FindByUsername(string username)
    {
        const string sql = @"
            SELECT UserID, FullName, Username, PasswordHash, RoleID, PhoneNumber, IsActive, PermissionsMask, CommissionPercent, CreatedAt, LastLoginAt
            FROM Users
            WHERE Username = @Username AND IsActive = 1";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@Username", username));
        return table.Rows.Count == 0 ? null : MapRow(table.Rows[0]);
    }

    // تسجيل الدخول: يتحقق من اسم المستخدم، كلمة المرور، وأن الدور مطابق للتطبيق المُستخدَم
    // (مثال: حساب ممرضة لا يمكنه الدخول إلى تطبيق الطبيب والعكس صحيح)
    // ملاحظة: الصلاحيات (Permissions) لا علاقة لها بهذا التحقق إطلاقاً - هي صلاحيات إضافية فوق الدور الأساسي فقط،
    // فحساب "جلولي زيد" (Permissions تتضمن ManageUsers, Role=Doctor) يسجّل دخوله لتطبيق الطبيب بشكل طبيعي تماماً كأي طبيب آخر.
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

    // ===================== إدارة المستخدمين (يتطلب صلاحية ManageUsers) =====================

    // كل المستخدمين بما فيهم غير النشطين - تُستخدم في شاشة إدارة المستخدمين فقط (بعكس FindByUsername)
    public List<UserAccount> GetAllUsers()
    {
        const string sql = @"
            SELECT UserID, FullName, Username, PasswordHash, RoleID, PhoneNumber, IsActive, PermissionsMask, CommissionPercent, CreatedAt, LastLoginAt
            FROM Users
            ORDER BY FullName";

        var table = _db.ExecuteQuery(sql);
        var result = new List<UserAccount>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(MapRow(row));
        }
        return result;
    }

    // إضافة مستخدم جديد (طبيب أو ممرضة، بأي مجموعة صلاحيات) - يرجع UserID الجديد
    public int AddUser(UserAccount user, string plainPassword)
    {
        const string sql = @"
            INSERT INTO Users (FullName, Username, PasswordHash, RoleID, PhoneNumber, PermissionsMask, IsActive)
            VALUES (@FullName, @Username, @PasswordHash, @RoleID, @PhoneNumber, @PermissionsMask, 1)";

        return _db.ExecuteInsertAndGetId(sql,
            new SqlParameter("@FullName", user.FullName),
            new SqlParameter("@Username", user.Username),
            new SqlParameter("@PasswordHash", PasswordHelper.Hash(plainPassword)),
            new SqlParameter("@RoleID", user.Role == UserRole.Doctor ? 1 : 2),
            new SqlParameter("@PhoneNumber", (object?)user.PhoneNumber ?? DBNull.Value),
            new SqlParameter("@PermissionsMask", (int)user.Permissions));
    }

    // تعديل بيانات مستخدم موجود (بدون تغيير كلمة المرور - لذلك دالة منفصلة UpdatePassword أدناه)
    public void UpdateUser(UserAccount user)
    {
        const string sql = @"
            UPDATE Users
            SET FullName = @FullName, Username = @Username, RoleID = @RoleID,
                PhoneNumber = @PhoneNumber, PermissionsMask = @PermissionsMask, IsActive = @IsActive
            WHERE UserID = @UserID";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@FullName", user.FullName),
            new SqlParameter("@Username", user.Username),
            new SqlParameter("@RoleID", user.Role == UserRole.Doctor ? 1 : 2),
            new SqlParameter("@PhoneNumber", (object?)user.PhoneNumber ?? DBNull.Value),
            new SqlParameter("@PermissionsMask", (int)user.Permissions),
            new SqlParameter("@IsActive", user.IsActive),
            new SqlParameter("@UserID", user.UserID));
    }

    // تُستدعى فقط عند إدخال كلمة مرور جديدة صراحة أثناء التعديل (اتركها فارغة في الواجهة = لا تغيير)
    public void UpdatePassword(int userId, string newPlainPassword)
    {
        const string sql = "UPDATE Users SET PasswordHash = @Hash WHERE UserID = @UserID";
        _db.ExecuteNonQuery(sql,
            new SqlParameter("@Hash", PasswordHelper.Hash(newPlainPassword)),
            new SqlParameter("@UserID", userId));
    }

    // تعطيل/إعادة تفعيل حساب (لا حذف فعلي أبداً، حفاظاً على سجل من أنشأ كل مريض/جلسة تاريخياً)
    public void SetActive(int userId, bool isActive)
    {
        const string sql = "UPDATE Users SET IsActive = @IsActive WHERE UserID = @UserID";
        _db.ExecuteNonQuery(sql,
            new SqlParameter("@IsActive", isActive),
            new SqlParameter("@UserID", userId));
    }

    // كم عدد المستخدمين النشطين الذين يملكون صلاحية ManageUsers حالياً؟
    // تُستخدم لمنع إزالة آخر مدير عام في النظام (وإلا يُقفل الجميع من شاشة الإدارة نهائياً)
    public int CountActiveSuperAdmins(int? excludeUserId = null)
    {
        var sql = "SELECT COUNT(*) FROM Users WHERE IsActive = 1 AND (PermissionsMask & @ManageUsersFlag) = @ManageUsersFlag";
        var parameters = new List<SqlParameter>
        {
            new SqlParameter("@ManageUsersFlag", (int)UserPermission.ManageUsers)
        };

        if (excludeUserId.HasValue)
        {
            sql += " AND UserID <> @ExcludeUserID";
            parameters.Add(new SqlParameter("@ExcludeUserID", excludeUserId.Value));
        }

        var result = _db.ExecuteScalar(sql, parameters.ToArray());
        return result == null ? 0 : Convert.ToInt32(result);
    }

    // هل اسم المستخدم هذا مستخدَم بالفعل من قِبل حساب آخر؟ (لإظهار رسالة واضحة بدل خطأ SQL خام)
    public bool UsernameExists(string username, int? excludeUserId = null)
    {
        var sql = "SELECT COUNT(*) FROM Users WHERE Username = @Username";
        var parameters = new List<SqlParameter> { new SqlParameter("@Username", username) };

        if (excludeUserId.HasValue)
        {
            sql += " AND UserID <> @ExcludeUserID";
            parameters.Add(new SqlParameter("@ExcludeUserID", excludeUserId.Value));
        }

        var result = _db.ExecuteScalar(sql, parameters.ToArray());
        return result != null && Convert.ToInt32(result) > 0;
    }

    private static UserAccount MapRow(DataRow row) => new UserAccount
    {
        UserID = (int)row["UserID"],
        FullName = row["FullName"].ToString()!,
        Username = row["Username"].ToString()!,
        PasswordHash = row["PasswordHash"].ToString()!,
        Role = (int)row["RoleID"] == 1 ? UserRole.Doctor : UserRole.Nurse,
        Permissions = (UserPermission)(int)row["PermissionsMask"],
        PhoneNumber = row["PhoneNumber"] as string,
        IsActive = (bool)row["IsActive"],
        CreatedAt = (DateTime)row["CreatedAt"],
        LastLoginAt = row["LastLoginAt"] as DateTime?,
        CommissionPercent = row.Table.Columns.Contains("CommissionPercent") && row["CommissionPercent"] != DBNull.Value
            ? Convert.ToDecimal(row["CommissionPercent"])
            : (decimal?)null
    };

    // ===================== نظام تقسيم إيرادات الأطباء (Doctor Commission) =====================

    // "الطبيب الرئيسي" = أول طبيب سُجِّل في النظام (أصغر UserID بين من دورهم Doctor)، بغض النظر عمّن يملك صلاحيات إدارية اليوم
    public int? GetPrimaryDoctorUserId()
    {
        const string sql = "SELECT TOP 1 UserID FROM Users WHERE RoleID = 1 ORDER BY UserID ASC";
        var result = _db.ExecuteScalar(sql);
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    // كل الأطباء النشطين - تُستخدم في شاشة إعدادات العمولات لعرض كل طبيب مع نسبته
    public List<UserAccount> GetAllDoctors()
    {
        const string sql = @"
            SELECT UserID, FullName, Username, PasswordHash, RoleID, PhoneNumber, IsActive, PermissionsMask, CommissionPercent, CreatedAt, LastLoginAt
            FROM Users
            WHERE RoleID = 1 AND IsActive = 1
            ORDER BY UserID ASC";

        var table = _db.ExecuteQuery(sql);
        var result = new List<UserAccount>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(MapRow(row));
        }
        return result;
    }

    // تعيين/إزالة نسبة عمولة مخصَّصة لطبيب معيَّن (null = العودة لاستخدام النسبة العامة الافتراضية)
    public void UpdateDoctorCommissionPercent(int userId, decimal? commissionPercent)
    {
        const string sql = "UPDATE Users SET CommissionPercent = @CommissionPercent WHERE UserID = @UserID";
        _db.ExecuteNonQuery(sql,
            new SqlParameter("@CommissionPercent", (object?)commissionPercent ?? DBNull.Value),
            new SqlParameter("@UserID", userId));
    }
}
