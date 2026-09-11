using System.Data;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

// طبقة وصول لجدولي Permissions (تعريف الصلاحيات المتاحة) وUserPermissions (المنح الفعلي لكل مستخدم).
// كلا الجدولين كانا موجودين في المخطط الأصلي بدون أي استخدام برمجي فعلي - هذا الملف يُفعِّلهما.
// هذا هو النظام المطلوب لصلاحيات المرمم تحديدًا: "قابلة للتغيير دون الحاجة إلى تعديل الكود".
// لا علاقة له إطلاقًا بـ UserPermission (Bitmask) المستخدَم لصلاحيات الطبيب/الممرضة - ذلك النظام
// يبقى كما هو دون أي تعديل.
public class PermissionRepository
{
    private readonly DatabaseHelper _db;

    public PermissionRepository(DatabaseHelper db)
    {
        _db = db;
    }

    // كل تعريفات الصلاحيات المتاحة في النظام (مرتبة حسب Category ثم SortOrder) - تُستخدم لبناء
    // شاشة "تحديد صلاحيات المرمم" ديناميكيًا بالكامل من القاعدة، بدون أي قائمة مكتوبة في XAML
    public List<PermissionDefinition> GetAllDefinitions()
    {
        const string sql = @"
            SELECT PermissionKey, Category, DisplayName, Description, SortOrder
            FROM dbo.Permissions
            ORDER BY Category, SortOrder, DisplayName";

        var table = _db.ExecuteQuery(sql);
        var result = new List<PermissionDefinition>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(new PermissionDefinition
            {
                PermissionKey = row["PermissionKey"].ToString()!,
                Category = row["Category"].ToString()!,
                DisplayName = row["DisplayName"].ToString()!,
                Description = row["Description"] as string,
                SortOrder = (int)row["SortOrder"]
            });
        }
        return result;
    }

    // فقط تعريفات صلاحيات فئة معيَّنة (مثلًا "Prosthetics") - تُستخدم لعزل شاشة صلاحيات المرمم
    // عن أي فئات أخرى قد تُضاف مستقبلًا لأدوار أخرى
    public List<PermissionDefinition> GetDefinitionsByCategory(string category)
    {
        const string sql = @"
            SELECT PermissionKey, Category, DisplayName, Description, SortOrder
            FROM dbo.Permissions
            WHERE Category = @Category
            ORDER BY SortOrder, DisplayName";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@Category", category));
        var result = new List<PermissionDefinition>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(new PermissionDefinition
            {
                PermissionKey = row["PermissionKey"].ToString()!,
                Category = row["Category"].ToString()!,
                DisplayName = row["DisplayName"].ToString()!,
                Description = row["Description"] as string,
                SortOrder = (int)row["SortOrder"]
            });
        }
        return result;
    }

    // مجموعة مفاتيح الصلاحيات الممنوحة فعليًا (IsGranted = 1) لمستخدم معيَّن - هذا ما يُملأ في
    // UserAccount.ProstheticPermissionKeys بعد تسجيل الدخول لتطبيق المرمم تحديدًا
    public HashSet<string> GetGrantedKeys(int userId)
    {
        const string sql = @"
            SELECT PermissionKey FROM dbo.UserPermissions
            WHERE UserID = @UserID AND IsGranted = 1";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@UserID", userId));
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow row in table.Rows)
            result.Add(row["PermissionKey"].ToString()!);
        return result;
    }

    // يستبدل كامل مجموعة صلاحيات مستخدم دفعة واحدة (تُستدعى من شاشة "تحديد صلاحيات المرمم" عند الحفظ)
    // - Delete-then-Insert ضمن معاملة واحدة (Transaction) لضمان عدم بقاء حالة متضاربة عند فشل جزئي.
    // Patch 14: دفاع في العمق - نفس صلاحية ManageUsers التي تحكم أصلاً فتح شاشة إدارة المستخدمين
    // بالكامل (راجع DoctorApp/NurseApp.ManageUsersButton_Click)، وليست صلاحية جديدة. سابقاً كانت
    // هذه الدالة بلا أي فحص خاص بها، فاستدعاؤها مباشرة من كود آخر (متجاوزاً UserEditWindow تماماً)
    // كان يُغيِّر صلاحيات أي مستخدم بلا أي اعتراض.
    public void ReplaceUserPermissions(int userId, IEnumerable<string> grantedKeys, UserAccount actingUser)
    {
        if (!actingUser.HasPermission(UserPermission.ManageUsers))
        {
            throw new UnauthorizedAccessException("You don't have permission to manage user permissions.");
        }

        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var del = new SqlCommand("DELETE FROM dbo.UserPermissions WHERE UserID = @UserID", conn, tx) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds })
            {
                del.Parameters.AddWithValue("@UserID", userId);
                del.ExecuteNonQuery();
            }

            foreach (var key in grantedKeys.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                using var ins = new SqlCommand(
                    "INSERT INTO dbo.UserPermissions (UserID, PermissionKey, IsGranted) VALUES (@UserID, @Key, 1)",
                    conn, tx) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds };
                ins.Parameters.AddWithValue("@UserID", userId);
                ins.Parameters.AddWithValue("@Key", key);
                ins.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // منح/سحب صلاحية واحدة فقط (بدون استبدال باقي الصلاحيات) - أسرع عند تبديل مفتاح واحد من واجهة تفاعلية
    // Patch 14: لا يوجد أي مستدعٍ فعلي لهذه الدالة في المشروع حاليًا (بحث شامل لم يجد Call Site) -
    // أضفت نفس فحص ManageUsers تحسبًا لاستخدامها مستقبلًا دون أن تبقى غير محمية، بلا أي تغيير آخر.
    public void SetPermission(int userId, string permissionKey, bool isGranted, UserAccount actingUser)
    {
        if (!actingUser.HasPermission(UserPermission.ManageUsers))
        {
            throw new UnauthorizedAccessException("You don't have permission to manage user permissions.");
        }

        const string sql = @"
            IF EXISTS (SELECT 1 FROM dbo.UserPermissions WHERE UserID = @UserID AND PermissionKey = @Key)
                UPDATE dbo.UserPermissions SET IsGranted = @IsGranted WHERE UserID = @UserID AND PermissionKey = @Key
            ELSE
                INSERT INTO dbo.UserPermissions (UserID, PermissionKey, IsGranted) VALUES (@UserID, @Key, @IsGranted)";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@UserID", userId),
            new SqlParameter("@Key", permissionKey),
            new SqlParameter("@IsGranted", isGranted));
    }
}
