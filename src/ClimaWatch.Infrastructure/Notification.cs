namespace ClimaWatch.Infrastructure;

public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid WeatherAlertId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }

    // Navigation
    public WeatherAlert? WeatherAlert { get; set; }
}
