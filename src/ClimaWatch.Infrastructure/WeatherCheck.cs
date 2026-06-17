using System;

namespace ClimaWatch.Infrastructure;

public sealed class WeatherCheck
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid CorrelationId { get; set; }
    public string City { get; set; } = string.Empty;
    public string Status { get; set; } = WeatherCheckStatuses.Queued;
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
}
