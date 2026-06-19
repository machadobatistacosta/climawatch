namespace ClimaWatch.Contracts;

public sealed record AlertDetected(
    Guid EventId,
    Guid CorrelationId,
    Guid WeatherAlertId,
    Guid WeatherCheckId,
    Guid WeatherSnapshotId,
    string AlertType,
    string Severity,
    string Message,
    DateTimeOffset DetectedAtUtc);
