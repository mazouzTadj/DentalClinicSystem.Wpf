using System.Data;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

public class ProstheticSessionRepository
{
    private readonly DatabaseHelper _db;

    public ProstheticSessionRepository(DatabaseHelper db)
    {
        _db = db;
    }

    public List<ProstheticSession> GetByCase(int caseId)
    {
        const string sql = @"
            SELECT s.ProstheticSessionID, s.CaseID, s.SessionDateTime, s.PerformedByUserID,
                   u.FullName AS PerformedByUserName, s.Description, s.CreatedAt
            FROM dbo.ProstheticSessions s
            LEFT JOIN dbo.Users u ON u.UserID = s.PerformedByUserID
            WHERE s.CaseID = @CaseID
            ORDER BY s.SessionDateTime DESC";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@CaseID", caseId));
        var result = new List<ProstheticSession>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(new ProstheticSession
            {
                ProstheticSessionID = (int)row["ProstheticSessionID"],
                CaseID = (int)row["CaseID"],
                SessionDateTime = (DateTime)row["SessionDateTime"],
                PerformedByUserID = (int)row["PerformedByUserID"],
                PerformedByUserName = row["PerformedByUserName"] as string,
                Description = row["Description"] as string,
                CreatedAt = (DateTime)row["CreatedAt"]
            });
        }
        return result;
    }

    // Patch 14: كل دالة كتابة هنا تتحقق بذاتها من الصلاحية الدقيقة المطابقة (دفاع في العمق - لا
    // تعتمد فقط على أن الواجهة تحقَّقت مسبقًا). AddSession/EditSession/DeleteSession ثلاث صلاحيات
    // منفصلة أصلًا في النظام (وليست EditCase العامة) - كل دالة تستخدم صلاحيتها الدقيقة فقط.
    public int Add(ProstheticSession session, UserAccount actingUser)
    {
        ProstheticPermissionGuard.Ensure(actingUser, ProstheticPermissionKeys.AddSession);

        const string sql = @"
            INSERT INTO dbo.ProstheticSessions (CaseID, SessionDateTime, PerformedByUserID, Description, CreatedAt)
            VALUES (@CaseID, @SessionDateTime, @PerformedByUserID, @Description, GETDATE())";

        return _db.ExecuteInsertAndGetId(sql,
            new SqlParameter("@CaseID", session.CaseID),
            new SqlParameter("@SessionDateTime", session.SessionDateTime),
            new SqlParameter("@PerformedByUserID", session.PerformedByUserID),
            new SqlParameter("@Description", (object?)session.Description ?? DBNull.Value));
    }

    public void Update(ProstheticSession session, UserAccount actingUser)
    {
        ProstheticPermissionGuard.Ensure(actingUser, ProstheticPermissionKeys.EditSession);

        const string sql = @"
            UPDATE dbo.ProstheticSessions
            SET SessionDateTime = @SessionDateTime, Description = @Description
            WHERE ProstheticSessionID = @ID";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@SessionDateTime", session.SessionDateTime),
            new SqlParameter("@Description", (object?)session.Description ?? DBNull.Value),
            new SqlParameter("@ID", session.ProstheticSessionID));
    }

    public void Delete(int prostheticSessionId, UserAccount actingUser)
    {
        ProstheticPermissionGuard.Ensure(actingUser, ProstheticPermissionKeys.DeleteSession);

        _db.ExecuteNonQuery(
            "DELETE FROM dbo.ProstheticSessions WHERE ProstheticSessionID = @ID",
            new SqlParameter("@ID", prostheticSessionId));
    }
}
