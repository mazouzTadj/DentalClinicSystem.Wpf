using System.Data;
using Microsoft.Data.SqlClient;

namespace DentalClinic.Data.DataAccess;

/// <summary>
/// Versioned database migration runner.
/// Version 1 is a compatibility baseline for databases created by the pre-migration
/// SchemaInitializer. Future schema changes must be added as numbered migrations.
/// </summary>
public static class DatabaseMigrationRunner
{
    public const int CurrentVersion = 2;
    private const int BaselineVersion = 1;
    private const int MigrationLockTimeoutMs = 30000;
    private const string MigrationLockName = "DentalClinicSystem.DatabaseMigration";

    public static void EnsureCurrent(DatabaseHelper db)
    {
        using var conn = db.GetConnection();
        conn.Open();

        AcquireMigrationLock(conn);
        try
        {
            EnsureHistoryTable(conn);

            var currentVersion = GetCurrentVersion(conn);

            // Existing databases created before the versioned migration system have no
            // history. The legacy idempotent upgrades are run once, then recorded as v1.
            if (currentVersion == 0)
            {
                SchemaInitializer.EnsureSchemaUpgrades(db);
                RecordMigration(conn, BaselineVersion, "Legacy schema baseline", "Pre-versioned SchemaInitializer upgrades");
                currentVersion = BaselineVersion;
            }

            if (currentVersion > CurrentVersion)
            {
                throw new InvalidOperationException(
                    $"The database schema version ({currentVersion}) is newer than this application supports ({CurrentVersion}). Please update the application.");
            }

            foreach (var migration in GetMigrations())
            {
                if (migration.Version <= currentVersion) continue;
                ApplyMigration(conn, migration);
                currentVersion = migration.Version;
            }
        }
        finally
        {
            ReleaseMigrationLock(conn);
        }
    }

    private static IReadOnlyList<DatabaseMigration> GetMigrations() =>
        new[]
        {
            new DatabaseMigration(
                2,
                "Add prosthetist appointment permissions",
                "Adds ViewAppointments and ManageAppointments permission definitions introduced by the prosthetist appointments feature.",
                ApplyMigration002AddProsthetistAppointmentPermissions)
        };

    private static void ApplyMigration002AddProsthetistAppointmentPermissions(SqlConnection conn, SqlTransaction tx)
    {
        // Historical migration: keep the permission keys literal so this migration remains
        // stable even if the application constants are renamed in a future release.
        const string sql = @"
IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE PermissionKey = N'Prosthetics.ViewAppointments')
BEGIN
    INSERT INTO dbo.Permissions (PermissionKey, Category, DisplayName, Description, SortOrder)
    VALUES (N'Prosthetics.ViewAppointments', N'Prosthetics', N'View patient appointments (today & upcoming)', NULL, 240);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE PermissionKey = N'Prosthetics.ManageAppointments')
BEGIN
    INSERT INTO dbo.Permissions (PermissionKey, Category, DisplayName, Description, SortOrder)
    VALUES (N'Prosthetics.ManageAppointments', N'Prosthetics', N'Schedule / edit / delete patient appointments', NULL, 250);
END;";

        using var cmd = CreateCommand(conn, sql, tx);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureHistoryTable(SqlConnection conn)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.DatabaseMigrations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DatabaseMigrations
    (
        MigrationID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DatabaseMigrations PRIMARY KEY,
        Version INT NOT NULL CONSTRAINT UQ_DatabaseMigrations_Version UNIQUE,
        MigrationName NVARCHAR(200) NOT NULL,
        Description NVARCHAR(1000) NULL,
        AppliedAt DATETIME2(0) NOT NULL CONSTRAINT DF_DatabaseMigrations_AppliedAt DEFAULT (SYSDATETIME()),
        AppliedByMachine NVARCHAR(128) NULL,
        AppliedByUser NVARCHAR(128) NULL
    );
END";

        using var cmd = CreateCommand(conn, sql);
        cmd.ExecuteNonQuery();
    }

    private static int GetCurrentVersion(SqlConnection conn)
    {
        const string sql = "SELECT ISNULL(MAX(Version), 0) FROM dbo.DatabaseMigrations;";
        using var cmd = CreateCommand(conn, sql);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void RecordMigration(SqlConnection conn, int version, string name, string? description)
    {
        const string sql = @"
INSERT INTO dbo.DatabaseMigrations
    (Version, MigrationName, Description, AppliedByMachine, AppliedByUser)
VALUES
    (@Version, @Name, @Description, @Machine, @User);";

        using var cmd = CreateCommand(conn, sql);
        cmd.Parameters.AddWithValue("@Version", version);
        cmd.Parameters.AddWithValue("@Name", name);
        cmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Machine", Environment.MachineName);
        cmd.Parameters.AddWithValue("@User", Environment.UserName);
        cmd.ExecuteNonQuery();
    }

    private static void ApplyMigration(SqlConnection conn, DatabaseMigration migration)
    {
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            // Double-check inside the transaction so a migration can never be recorded twice.
            const string existsSql = "SELECT COUNT(1) FROM dbo.DatabaseMigrations WHERE Version = @Version;";
            using (var exists = CreateCommand(conn, existsSql, tx))
            {
                exists.Parameters.AddWithValue("@Version", migration.Version);
                if (Convert.ToInt32(exists.ExecuteScalar()) > 0)
                {
                    tx.Commit();
                    return;
                }
            }

            migration.Apply(conn, tx);

            const string insertSql = @"
INSERT INTO dbo.DatabaseMigrations
    (Version, MigrationName, Description, AppliedByMachine, AppliedByUser)
VALUES
    (@Version, @Name, @Description, @Machine, @User);";
            using var insert = CreateCommand(conn, insertSql, tx);
            insert.Parameters.AddWithValue("@Version", migration.Version);
            insert.Parameters.AddWithValue("@Name", migration.Name);
            insert.Parameters.AddWithValue("@Description", (object?)migration.Description ?? DBNull.Value);
            insert.Parameters.AddWithValue("@Machine", Environment.MachineName);
            insert.Parameters.AddWithValue("@User", Environment.UserName);
            insert.ExecuteNonQuery();

            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { /* preserve original migration exception */ }
            throw;
        }
    }

    private static void AcquireMigrationLock(SqlConnection conn)
    {
        const string sql = @"
DECLARE @Result INT;
EXEC @Result = sys.sp_getapplock
    @Resource = @Resource,
    @LockMode = N'Exclusive',
    @LockOwner = N'Session',
    @LockTimeout = @Timeout;
SELECT @Result;";

        using var cmd = CreateCommand(conn, sql);
        cmd.Parameters.AddWithValue("@Resource", MigrationLockName);
        cmd.Parameters.AddWithValue("@Timeout", MigrationLockTimeoutMs);
        var result = Convert.ToInt32(cmd.ExecuteScalar());
        if (result < 0)
            throw new InvalidOperationException("Another computer is currently updating the clinic database. Please try again in a moment.");
    }

    private static void ReleaseMigrationLock(SqlConnection conn)
    {
        try
        {
            const string sql = "EXEC sys.sp_releaseapplock @Resource = @Resource, @LockOwner = N'Session';";
            using var cmd = CreateCommand(conn, sql);
            cmd.Parameters.AddWithValue("@Resource", MigrationLockName);
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // The SQL connection closing will release a session-owned application lock.
        }
    }

    private static SqlCommand CreateCommand(SqlConnection conn, string sql, SqlTransaction? tx = null)
    {
        return new SqlCommand(sql, conn, tx)
        {
            CommandTimeout = DatabaseHelper.CommandTimeoutSeconds
        };
    }

    private sealed record DatabaseMigration(int Version, string Name, string? Description, Action<SqlConnection, SqlTransaction> Apply);
}
