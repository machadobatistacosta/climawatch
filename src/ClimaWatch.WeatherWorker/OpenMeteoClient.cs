using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ClimaWatch.WeatherWorker;

public sealed class OpenMeteoClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenMeteoClient> _logger;

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public OpenMeteoClient(HttpClient httpClient, ILogger<OpenMeteoClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<GeocodingResult?> GetCoordinatesAsync(string city, CancellationToken ct)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=pt&format=json";

        _logger.LogInformation("Consultando Open-Meteo Geocoding API para a cidade: '{City}'", city);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            await EnsureSuccessOrThrowTransientAsync(response);

            var result = await response.Content.ReadFromJsonAsync<GeocodingResponse>(JsonSerializerOptions, ct);
            if (result?.Results == null || result.Results.Count == 0)
            {
                _logger.LogWarning("Geocoding API não encontrou resultados para a cidade: '{City}'", city);
                return null;
            }

            return result.Results[0];
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Erro de comunicação com a Geocoding API da Open-Meteo para a cidade: '{City}'", city);
            throw;
        }
    }

    public async Task<(ForecastResult Result, string RawJson)> GetCurrentWeatherAsync(double latitude, double longitude, CancellationToken ct)
    {
        var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,apparent_temperature,precipitation,weather_code,wind_speed_10m&timezone=UTC";

        _logger.LogInformation("Consultando Open-Meteo Forecast API para Lat: {Latitude}, Lon: {Longitude}", latitude, longitude);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            await EnsureSuccessOrThrowTransientAsync(response);

            var rawJson = await response.Content.ReadAsStringAsync(ct);
            var forecast = JsonSerializer.Deserialize<ForecastResponse>(rawJson, JsonSerializerOptions);

            if (forecast?.Current == null)
            {
                throw new InvalidOperationException("Resposta da Forecast API não contem o bloco 'current'.");
            }

            // Converter a string de tempo retornada (ex: "2026-06-17T15:00") para DateTimeOffset
            // A API com &timezone=UTC retorna o horário em UTC, mas sem o offset Z no final.
            DateTimeOffset observedAtUtc;
            if (DateTimeOffset.TryParse(forecast.Current.Time + "Z", out var parsedTime))
            {
                observedAtUtc = parsedTime;
            }
            else
            {
                observedAtUtc = DateTimeOffset.UtcNow;
            }

            var result = new ForecastResult(
                forecast.Current.Temperature2m,
                forecast.Current.ApparentTemperature,
                forecast.Current.Precipitation,
                forecast.Current.WindSpeed10m,
                forecast.Current.WeatherCode,
                observedAtUtc
            );

            return (result, rawJson);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Erro de comunicação com a Forecast API da Open-Meteo para Lat: {Latitude}, Lon: {Longitude}", latitude, longitude);
            throw;
        }
    }

    private static async Task EnsureSuccessOrThrowTransientAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Tratar 429 ou 5xx como erros transientes de rede/serviço
        if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
        {
            throw new HttpRequestException($"Erro transiente do Open-Meteo. Status Code: {response.StatusCode}", null, response.StatusCode);
        }

        // Para outros códigos 4xx, lançar erro que não induz retry transiente ou tratar explicitamente
        var content = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"Erro não-transiente ao chamar Open-Meteo. Status: {response.StatusCode}, Detalhes: {content}");
    }
}

public record GeocodingResponse(
    [property: JsonPropertyName("results")] List<GeocodingResult>? Results
);

public record GeocodingResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("country_code")] string? CountryCode,
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("timezone")] string? Timezone
);

public record ForecastResponse(
    [property: JsonPropertyName("current")] CurrentWeather? Current
);

public record CurrentWeather(
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("temperature_2m")] double Temperature2m,
    [property: JsonPropertyName("apparent_temperature")] double ApparentTemperature,
    [property: JsonPropertyName("precipitation")] double Precipitation,
    [property: JsonPropertyName("weather_code")] int WeatherCode,
    [property: JsonPropertyName("wind_speed_10m")] double WindSpeed10m
);

public record ForecastResult(
    double TemperatureC,
    double ApparentTemperatureC,
    double PrecipitationMm,
    double WindSpeedKmh,
    int WeatherCode,
    DateTimeOffset ObservedAtUtc
);
