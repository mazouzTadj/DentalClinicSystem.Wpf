using System.Threading;

namespace DentalClinic.Data.DataAccess;

// هوية المستخدم الجاري تُمرّر إلى كل اتصال SQL جديد كي تسجلها Triggers قاعدة البيانات.
public static class AuditContext
{
    private static readonly AsyncLocal<int?> CurrentUser = new();

    public static int? CurrentUserId => CurrentUser.Value;

    public static void SetCurrentUser(int userId) => CurrentUser.Value = userId;
}
