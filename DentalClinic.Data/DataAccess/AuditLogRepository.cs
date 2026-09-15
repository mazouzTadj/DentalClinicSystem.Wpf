using Microsoft.Data.SqlClient;
using System.Data;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

public class AuditLogRepository
{
    private readonly DatabaseHelper _db;

    public AuditLogRepository(DatabaseHelper db) => _db = db;

    public List<AuditLogEntry> GetRecent(int limit = 500)
    {
        const string sql = @"
            SELECT TOP (@Limit) a.AuditLogID, a.OccurredAt, a.Action, a.EntityType, a.EntityID,
                   u.FullName AS ActorName, a.ClientHost
            FROM dbo.AuditLog a
            LEFT JOIN dbo.Users u ON u.UserID = a.ActorUserID
            ORDER BY a.AuditLogID DESC";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@Limit", limit));
        var result = new List<AuditLogEntry>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(new AuditLogEntry
            {
                AuditLogID = Convert.ToInt64(row["AuditLogID"]),
                OccurredAt = Convert.ToDateTime(row["OccurredAt"]),
                Action = row["Action"].ToString() ?? string.Empty,
                EntityType = row["EntityType"].ToString() ?? string.Empty,
                EntityID = row["EntityID"] == DBNull.Value ? null : Convert.ToInt32(row["EntityID"]),
                ActorName = row["ActorName"] == DBNull.Value ? null : row["ActorName"].ToString(),
                ClientHost = row["ClientHost"] == DBNull.Value ? null : row["ClientHost"].ToString()
            });
        }
        return result;
    }
}
