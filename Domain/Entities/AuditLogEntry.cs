namespace Domain.Entities;

public sealed record AuditLogEntry(DateTimeOffset Timestamp, string Actor, string Action, string Details);