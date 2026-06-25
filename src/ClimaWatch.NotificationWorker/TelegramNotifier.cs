using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClimaWatch.Contracts;

namespace ClimaWatch.NotificationWorker;

public sealed class TelegramNotifier
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TelegramNotifier> _logger;
    private readonly string? _botToken;
    private readonly string? _chatId;

    public bool IsConfigured { get; }

    public TelegramNotifier(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<TelegramNotifier> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _botToken = configuration["Telegram:BotToken"] ?? configuration["Telegram__BotToken"];
        _chatId = configuration["Telegram:ChatId"] ?? configuration["Telegram__ChatId"];

        IsConfigured = !string.IsNullOrWhiteSpace(_botToken) && !string.IsNullOrWhiteSpace(_chatId);
    }

    public async Task<bool> SendAlertAsync(AlertDetected alert, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Tentativa de enviar alerta via Telegram, mas o serviço não está configurado.");
            return false;
        }

        try
        {
            var messageText = $"""
🚨 Alerta ClimaWatch

Tipo: {alert.AlertType}
Severidade: {alert.Severity}
Mensagem: {alert.Message}
Alert ID: {alert.WeatherAlertId}
Data/hora: {alert.DetectedAtUtc:yyyy-MM-dd HH:mm:ss} UTC
""";

            var payload = new
            {
                chat_id = _chatId,
                text = messageText
            };

            var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";
            
            _logger.LogInformation("Enviando alerta {WeatherAlertId} para o Telegram...", alert.WeatherAlertId);
            
            var response = await _httpClient.PostAsJsonAsync(url, payload, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Alerta {WeatherAlertId} enviado com sucesso para o Telegram.", alert.WeatherAlertId);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Falha ao enviar alerta para o Telegram. Status: {StatusCode}, Resposta: {Response}", response.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao enviar alerta para o Telegram.");
            return false;
        }
    }
}
