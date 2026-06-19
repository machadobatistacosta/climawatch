namespace ClimaWatch.Contracts;

public static class MessagingTopology
{
    public const string ExchangeName = "climawatch.events";

    public const string WeatherChecksQueueName = "climawatch.weather-checks";
    public const string WeatherCheckRequestedRoutingKey = "weather.check.requested";

    public const string AlertsQueueName = "climawatch.alerts";
    public const string AlertDetectedRoutingKey = "alert.detected";
}
