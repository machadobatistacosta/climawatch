using System;

namespace ClimaWatch.Infrastructure;

public sealed class WeatherSnapshot
{
    public Guid Id { get; set; }
    public Guid WeatherCheckId { get; set; }
    public WeatherCheck? WeatherCheck { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Timezone { get; set; } = string.Empty;
    public double TemperatureC { get; set; }
    public double ApparentTemperatureC { get; set; }
    public double PrecipitationMm { get; set; }
    public double WindSpeedKmh { get; set; }
    public int WeatherCode { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
    public string RawPayloadJson { get; set; } = string.Empty;
}
