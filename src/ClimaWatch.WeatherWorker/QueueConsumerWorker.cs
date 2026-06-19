using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ClimaWatch.Contracts;
using ClimaWatch.Infrastructure;

namespace ClimaWatch.WeatherWorker;

public sealed class QueueConsumerWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<QueueConsumerWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private IConnection? _connection;

    // Canal exclusivo para consumo de weather-checks
    private IChannel? _channel;

    // Canal separado para publicação de alertas, protegido por semáforo
    private IChannel? _publishChannel;
    private readonly SemaphoreSlim _publishSemaphore = new(1, 1);

    public QueueConsumerWorker(IConfiguration configuration, ILogger<QueueConsumerWorker> logger, IServiceScopeFactory scopeFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hostName = _configuration["RabbitMq:HostName"] ?? "localhost";
        var portStr = _configuration["RabbitMq:Port"] ?? "5672";
        var userName = _configuration["RabbitMq:UserName"] ?? "guest";
        var password = _configuration["RabbitMq:Password"] ?? "guest";

        if (!int.TryParse(portStr, out int port))
        {
            port = 5672;
        }

        var connectionFactory = new ConnectionFactory
        {
            HostName = hostName,
            Port = port,
            UserName = userName,
            Password = password,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            ClientProvidedName = "climawatch-weather-worker"
        };

        // Retry loop para conexão inicial
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Tentando se conectar ao RabbitMQ em {HostName}:{Port}...", hostName, port);
                _connection = await connectionFactory.CreateConnectionAsync(stoppingToken);
                _logger.LogInformation("Conexão com o RabbitMQ estabelecida.");
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Falha ao conectar ao RabbitMQ. Nova tentativa em 5 segundos...");
                try
                {
                    await Task.Delay(5000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        if (stoppingToken.IsCancellationRequested || _connection is null)
        {
            return;
        }

        try
        {
            // Canal de consumo
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            // Canal de publicação (separado)
            _publishChannel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            _logger.LogInformation("Declarando topologia RabbitMQ (Worker)...");

            await _channel.ExchangeDeclareAsync(
                exchange: MessagingTopology.ExchangeName,
                type: "topic",
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken);

            // Declarar DLX (Dead Letter Exchange)
            await _channel.ExchangeDeclareAsync(
                exchange: MessagingTopology.DeadLetterExchangeName,
                type: "direct",
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken);

            // Declarar DLQ (Dead Letter Queue)
            await _channel.QueueDeclareAsync(
                queue: MessagingTopology.DeadLetterQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken);

            // Vincular DLQ ao DLX
            await _channel.QueueBindAsync(
                queue: MessagingTopology.DeadLetterQueueName,
                exchange: MessagingTopology.DeadLetterExchangeName,
                routingKey: MessagingTopology.DeadLetterRoutingKey,
                arguments: null,
                cancellationToken: stoppingToken);

            var weatherChecksArgs = new Dictionary<string, object?>
            {
                { "x-dead-letter-exchange", MessagingTopology.DeadLetterExchangeName },
                { "x-dead-letter-routing-key", MessagingTopology.DeadLetterRoutingKey }
            };

            // Fila de weather-checks (consumo)
            await _channel.QueueDeclareAsync(
                queue: MessagingTopology.WeatherChecksQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: weatherChecksArgs,
                cancellationToken: stoppingToken);

            await _channel.QueueBindAsync(
                queue: MessagingTopology.WeatherChecksQueueName,
                exchange: MessagingTopology.ExchangeName,
                routingKey: MessagingTopology.WeatherCheckRequestedRoutingKey,
                arguments: null,
                cancellationToken: stoppingToken);

            var alertsArgs = new Dictionary<string, object?>
            {
                { "x-dead-letter-exchange", MessagingTopology.DeadLetterExchangeName },
                { "x-dead-letter-routing-key", MessagingTopology.DeadLetterRoutingKey }
            };

            // Fila de alertas (declarada idempotentemente para que o NotificationWorker encontre)
            await _channel.QueueDeclareAsync(
                queue: MessagingTopology.AlertsQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: alertsArgs,
                cancellationToken: stoppingToken);

            await _channel.QueueBindAsync(
                queue: MessagingTopology.AlertsQueueName,
                exchange: MessagingTopology.ExchangeName,
                routingKey: MessagingTopology.AlertDetectedRoutingKey,
                arguments: null,
                cancellationToken: stoppingToken);

            await _channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: 1,
                global: false,
                cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += async (sender, ea) =>
            {
                var body = ea.Body.ToArray();
                var rawJson = Encoding.UTF8.GetString(body);

                WeatherCheckRequested? message = null;
                bool isJsonValid = false;

                try
                {
                    message = JsonSerializer.Deserialize<WeatherCheckRequested>(rawJson);
                    if (message is not null && message.EventId != Guid.Empty && !string.IsNullOrWhiteSpace(message.City))
                    {
                        isJsonValid = true;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Mensagem inválida recebida (JSON malformado). Payload: {Payload}", rawJson);
                }

                if (!isJsonValid || message is null)
                {
                    _logger.LogWarning("JSON inválido ou contrato inconsistente. Enviando NACK (requeue: false). Payload: {Payload}", rawJson);
                    try
                    {
                        await _channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erro ao enviar NACK para a mensagem inválida.");
                    }
                    return;
                }

                if (message.WeatherCheckId == Guid.Empty)
                {
                    _logger.LogWarning("Evento recebido com WeatherCheckId vazio. EventId: {EventId}. Enviando ACK.", message.EventId);
                    try { await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false); }
                    catch (Exception ex) { _logger.LogError(ex, "Erro ao enviar ACK para {EventId}", message.EventId); }
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ClimaWatchDbContext>();

                WeatherCheck? weatherCheck = null;
                try
                {
                    weatherCheck = await dbContext.WeatherChecks.FindAsync(new object[] { message.WeatherCheckId }, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao acessar o banco para buscar WeatherCheck {WeatherCheckId}. Enviando NACK (requeue: true).", message.WeatherCheckId);
                    await HandleTransientErrorAsync(ea.DeliveryTag, stoppingToken);
                    return;
                }

                if (weatherCheck is null)
                {
                    _logger.LogWarning("WeatherCheck {WeatherCheckId} não encontrado. Enviando ACK.", message.WeatherCheckId);
                    try { await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false); }
                    catch (Exception ex) { _logger.LogError(ex, "Erro ao enviar ACK para {EventId}", message.EventId); }
                    return;
                }

                if (weatherCheck.Status == WeatherCheckStatuses.Processed)
                {
                    _logger.LogInformation("WeatherCheck {WeatherCheckId} já está processed. Enviando ACK sem reprocessar.", message.WeatherCheckId);
                    try { await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false); }
                    catch (Exception ex) { _logger.LogError(ex, "Erro ao enviar ACK para {EventId}", message.EventId); }
                    return;
                }

                if (weatherCheck.Status == WeatherCheckStatuses.Failed)
                {
                    _logger.LogWarning("WeatherCheck {WeatherCheckId} está em estado Failed. Enviando ACK.", message.WeatherCheckId);
                    try { await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false); }
                    catch (Exception ex) { _logger.LogError(ex, "Erro ao enviar ACK para {EventId}", message.EventId); }
                    return;
                }

                if (weatherCheck.Status == WeatherCheckStatuses.Queued)
                {
                    try
                    {
                        var openMeteoClient = scope.ServiceProvider.GetRequiredService<OpenMeteoClient>();

                        // 1. Geocoding
                        var coordinates = await openMeteoClient.GetCoordinatesAsync(weatherCheck.City, stoppingToken);
                        if (coordinates is null)
                        {
                            _logger.LogWarning("Cidade '{City}' não encontrada. Marcando WeatherCheck {WeatherCheckId} como failed.", weatherCheck.City, weatherCheck.Id);
                            weatherCheck.Status = WeatherCheckStatuses.Failed;
                            weatherCheck.ErrorMessage = "Location not found.";
                            await dbContext.SaveChangesAsync(stoppingToken);
                            await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
                            return;
                        }

                        // 2. Forecast
                        var (weather, weatherJson) = await openMeteoClient.GetCurrentWeatherAsync(coordinates.Latitude, coordinates.Longitude, stoppingToken);

                        // 3. Salvar snapshot
                        var snapshot = new WeatherSnapshot
                        {
                            Id = Guid.NewGuid(),
                            WeatherCheckId = weatherCheck.Id,
                            LocationName = coordinates.Name,
                            CountryCode = coordinates.CountryCode ?? string.Empty,
                            Latitude = coordinates.Latitude,
                            Longitude = coordinates.Longitude,
                            Timezone = coordinates.Timezone ?? string.Empty,
                            TemperatureC = weather.TemperatureC,
                            ApparentTemperatureC = weather.ApparentTemperatureC,
                            PrecipitationMm = weather.PrecipitationMm,
                            WindSpeedKmh = weather.WindSpeedKmh,
                            WeatherCode = weather.WeatherCode,
                            ObservedAtUtc = weather.ObservedAtUtc,
                            RawPayloadJson = weatherJson
                        };

                        dbContext.WeatherSnapshots.Add(snapshot);

                        weatherCheck.Status = WeatherCheckStatuses.Processed;
                        weatherCheck.ProcessedAtUtc = DateTimeOffset.UtcNow;

                        await dbContext.SaveChangesAsync(stoppingToken);

                        _logger.LogInformation(
                            "Snapshot salvo. WeatherCheckId: {WeatherCheckId}, Cidade: {City}, Temp: {Temp}C, Chuva: {Rain}mm, Vento: {Wind}km/h",
                            weatherCheck.Id, weatherCheck.City, weather.TemperatureC, weather.PrecipitationMm, weather.WindSpeedKmh);

                        // 4. Avaliar regras de alerta e publicar
                        var alerts = EvaluateAlertRules(weather, weatherCheck, snapshot);
                        foreach (var alert in alerts)
                        {
                            dbContext.WeatherAlerts.Add(alert);
                            await dbContext.SaveChangesAsync(stoppingToken);

                            _logger.LogInformation(
                                "Alerta detectado: {AlertType} ({Severity}) para WeatherCheckId {WeatherCheckId}.",
                                alert.AlertType, alert.Severity, weatherCheck.Id);

                            var alertEvent = new AlertDetected(
                                EventId: alert.EventId,
                                CorrelationId: alert.CorrelationId,
                                WeatherAlertId: alert.Id,
                                WeatherCheckId: alert.WeatherCheckId,
                                WeatherSnapshotId: alert.WeatherSnapshotId,
                                AlertType: alert.AlertType,
                                Severity: alert.Severity,
                                Message: alert.Message,
                                DetectedAtUtc: alert.DetectedAtUtc);

                            await PublishAlertAsync(alertEvent, stoppingToken);
                        }

                        if (alerts.Count == 0)
                        {
                            _logger.LogInformation("Nenhuma regra de alerta atingida para WeatherCheckId {WeatherCheckId}.", weatherCheck.Id);
                        }

                        // 5. ACK somente após tudo salvo e publicado
                        await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogError(ex, "Erro de rede ao consultar Open-Meteo para '{City}'. Enviando NACK (requeue: true).", weatherCheck.City);
                        await HandleTransientErrorAsync(ea.DeliveryTag, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erro ao processar WeatherCheck {WeatherCheckId}. Enviando NACK (requeue: true).", weatherCheck.Id);
                        await HandleTransientErrorAsync(ea.DeliveryTag, stoppingToken);
                    }
                    return;
                }

                _logger.LogWarning("Status inesperado '{Status}' para WeatherCheck {WeatherCheckId}. Enviando ACK.", weatherCheck.Status, message.WeatherCheckId);
                try { await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false); }
                catch (Exception ex) { _logger.LogError(ex, "Erro ao enviar ACK para {EventId}", message.EventId); }
            };

            await _channel.BasicConsumeAsync(
                queue: MessagingTopology.WeatherChecksQueueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken);

            _logger.LogInformation("Worker iniciado e consumindo da fila {QueueName}...", MessagingTopology.WeatherChecksQueueName);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Worker sendo cancelado...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Erro crítico no worker de consumo.");
        }
    }

    // ─── Regras de Alerta ───────────────────────────────────────────────────

    private static List<WeatherAlert> EvaluateAlertRules(
        ForecastResult weather,
        WeatherCheck weatherCheck,
        WeatherSnapshot snapshot)
    {
        var alerts = new List<WeatherAlert>();
        var now = DateTimeOffset.UtcNow;

        // Regra 1: Vento forte (critical)
        if (weather.WindSpeedKmh >= 40)
        {
            alerts.Add(new WeatherAlert
            {
                Id = Guid.NewGuid(),
                EventId = Guid.NewGuid(),
                CorrelationId = weatherCheck.CorrelationId,
                WeatherCheckId = weatherCheck.Id,
                WeatherSnapshotId = snapshot.Id,
                AlertType = "strong_wind",
                Severity = "critical",
                Message = $"Vento forte detectado em {snapshot.LocationName}: {weather.WindSpeedKmh} km/h.",
                DetectedAtUtc = now
            });
        }

        // Regra 2: Temperatura alta (warning)
        if (weather.TemperatureC >= 30)
        {
            alerts.Add(new WeatherAlert
            {
                Id = Guid.NewGuid(),
                EventId = Guid.NewGuid(),
                CorrelationId = weatherCheck.CorrelationId,
                WeatherCheckId = weatherCheck.Id,
                WeatherSnapshotId = snapshot.Id,
                AlertType = "high_temperature",
                Severity = "warning",
                Message = $"Temperatura alta detectada em {snapshot.LocationName}: {weather.TemperatureC}°C.",
                DetectedAtUtc = now
            });
        }

        // Regra 3: Chuva (info)
        if (weather.PrecipitationMm > 0)
        {
            alerts.Add(new WeatherAlert
            {
                Id = Guid.NewGuid(),
                EventId = Guid.NewGuid(),
                CorrelationId = weatherCheck.CorrelationId,
                WeatherCheckId = weatherCheck.Id,
                WeatherSnapshotId = snapshot.Id,
                AlertType = "precipitation",
                Severity = "info",
                Message = $"Precipitação detectada em {snapshot.LocationName}: {weather.PrecipitationMm} mm.",
                DetectedAtUtc = now
            });
        }

        return alerts;
    }

    // ─── Publicação de Alertas (canal separado + SemaphoreSlim) ─────────────

    private async Task PublishAlertAsync(AlertDetected alertEvent, CancellationToken ct)
    {
        if (_publishChannel is null) return;

        await _publishSemaphore.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(alertEvent);
            var body = Encoding.UTF8.GetBytes(json);

            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = alertEvent.EventId.ToString(),
                CorrelationId = alertEvent.CorrelationId.ToString(),
                Type = nameof(AlertDetected)
            };

            await _publishChannel.BasicPublishAsync(
                exchange: MessagingTopology.ExchangeName,
                routingKey: MessagingTopology.AlertDetectedRoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: ct);

            _logger.LogInformation(
                "Evento AlertDetected publicado. AlertType: {AlertType}, WeatherAlertId: {WeatherAlertId}.",
                alertEvent.AlertType, alertEvent.WeatherAlertId);
        }
        finally
        {
            _publishSemaphore.Release();
        }
    }

    // ─── StopAsync ──────────────────────────────────────────────────────────

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Finalizando o worker...");

        if (_publishChannel is not null)
        {
            try { await _publishChannel.CloseAsync(cancellationToken); _publishChannel.Dispose(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Erro ao fechar o canal de publicação."); }
        }

        if (_channel is not null)
        {
            try { await _channel.CloseAsync(cancellationToken); _channel.Dispose(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Erro ao fechar o canal do RabbitMQ no encerramento."); }
        }

        if (_connection is not null)
        {
            try { await _connection.CloseAsync(cancellationToken); _connection.Dispose(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Erro ao fechar a conexão do RabbitMQ no encerramento."); }
        }

        _publishSemaphore.Dispose();
        await base.StopAsync(cancellationToken);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private async Task HandleTransientErrorAsync(ulong deliveryTag, CancellationToken ct)
    {
        if (_channel is null) return;
        try { await Task.Delay(2000, ct); }
        catch (OperationCanceledException) { return; }

        try
        {
            await _channel.BasicNackAsync(deliveryTag: deliveryTag, multiple: false, requeue: true);
        }
        catch (Exception nackEx)
        {
            _logger.LogError(nackEx, "Erro ao enviar NACK com requeue para deliveryTag {DeliveryTag}", deliveryTag);
        }
    }
}
