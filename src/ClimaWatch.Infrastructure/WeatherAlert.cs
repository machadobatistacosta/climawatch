namespace ClimaWatch.Infrastructure;

public sealed class WeatherAlert
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid WeatherCheckId { get; set; }
    public Guid WeatherSnapshotId { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset DetectedAtUtc { get; set; }

    // Navigation
    public WeatherCheck? WeatherCheck { get; set; }
    public WeatherSnapshot? WeatherSnapshot { get; set; }
    public ICollection<Notification> Notifications { get; set; } = [];
}
