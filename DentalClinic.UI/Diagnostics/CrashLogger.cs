using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace DentalClinic.UI.Diagnostics;

/// <summary>
/// Local application error logger. It intentionally does not log connection strings,
/// passwords, license signatures or other known secret values.
/// </summary>
public static class CrashLogger
{
    private static readonly object SyncRoot = new();

    private static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DentalClinicSystem",
        "Logs");

    public static string CurrentLogFilePath => Path.Combine(
        LogDirectory,
        $"{SanitizeFileName(GetApplicationName())}-{DateTime.Now:yyyy-MM-dd}.log");

    public static void LogUnhandledException(string source, Exception exception)
    {
        try
        {
            var applicationName = GetApplicationName();
            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
            var text = Sanitize(exception.ToString());

            var entry = new StringBuilder()
                .AppendLine(new string('=', 80))
                .AppendLine($"Date (local): {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz}")
                .AppendLine($"Application: {applicationName}")
                .AppendLine($"Version: {version}")
                .AppendLine($"Source: {source}")
                .AppendLine($"Machine: {Environment.MachineName}")
                .AppendLine($"Windows User: {Environment.UserName}")
                .AppendLine("Exception:")
                .AppendLine(text)
                .AppendLine();

            Write(entry.ToString());
        }
        catch
        {
            // Error logging must never become a second application failure.
        }
    }

    public static void Log(string source, string message, Exception? exception = null)
    {
        try
        {
            var entry = new StringBuilder()
                .AppendLine(new string('-', 80))
                .AppendLine($"Date (local): {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz}")
                .AppendLine($"Application: {GetApplicationName()}")
                .AppendLine($"Source: {source}")
                .AppendLine($"Message: {Sanitize(message)}");

            if (exception != null)
            {
                entry.AppendLine("Exception:")
                     .AppendLine(Sanitize(exception.ToString()));
            }

            entry.AppendLine();
            Write(entry.ToString());
        }
        catch
        {
            // Error logging must never become a second application failure.
        }
    }

    private static void Write(string entry)
    {
        lock (SyncRoot)
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(CurrentLogFilePath, entry, Encoding.UTF8);
        }
    }

    private static string GetApplicationName()
    {
        return Assembly.GetEntryAssembly()?.GetName().Name ?? "DentalClinicSystem";
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value;
    }

    private static string Sanitize(string value)
    {
        // Defense in depth: exceptions should not normally contain secrets, but if a
        // provider includes a connection string or password in an exception message,
        // redact common secret-bearing fields before writing the log.
        value = Regex.Replace(value, @"(?i)(Password|Pwd)\s*=\s*[^;\r\n]*", "$1=<redacted>");
        value = Regex.Replace(value, @"(?i)(User ID|UID)\s*=\s*[^;\r\n]*", "$1=<redacted>");
        value = Regex.Replace(value, @"(?i)(LicenseSignature)\s*[:=]\s*[^;\r\n]*", "$1=<redacted>");
        value = Regex.Replace(value, @"(?i)(PrivateKey|Private Key)\s*[:=]\s*[^\r\n]*", "$1=<redacted>");
        return value;
    }
}
