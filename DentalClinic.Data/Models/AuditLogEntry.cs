namespace DentalClinic.Data.Models;

public class AuditLogEntry
{
    public long AuditLogID { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int? EntityID { get; set; }
    public string? ActorName { get; set; }
    public string? ClientHost { get; set; }

    public string OccurredAtText => OccurredAt.ToString("yyyy-MM-dd HH:mm:ss");
    public string EntityText => EntityID.HasValue ? $"{EntityType} #{EntityID}" : EntityType;
    public string ActorText => string.IsNullOrWhiteSpace(ActorName) ? "System / unknown" : ActorName;
}
