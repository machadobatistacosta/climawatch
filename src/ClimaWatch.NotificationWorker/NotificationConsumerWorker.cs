using System;
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

namespace ClimaWatch.NotificationWorker;

public sealed class NotificationConsumerWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationConsumerWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private IConnection? _connection;
    private IChannel? _channel;

    public NotificationConsumerWorker(
        IConfiguration configuration,
        ILogger<NotificationConsumerWorker> logger,
        IServiceScopeFactory scopeFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hostName = _configuration["RabbitMq:HostName"] ?? "localhost";
        var portStr  = _configuration["RabbitMq:Port"]     ?? "5672";
        var userName = _configuration["RabbitMq:UserName"] ?? "guest";
        var password = _configuration["RabbitMq:Password"] ?? "guest";

        if (!int.TryParse(portStr, out int port))
            port = 5672;

        var factory = new ConnectionFactory
        {
            HostName = hostName,
            Port = port,
            UserName = userName,
            Password = password,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            ClientProvidedName = "climawatch-notification-worker"
        };

        // Retry loop para conexão inicial
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Tentando se conectar ao RabbitMQ em {HostName}:{Port}...", hostName, port);
                _connection = await factory.CreateConnectionAsync(stoppingToken);
                _logger.LogInformation("Conexão com o RabbitMQ estabelecida.");
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Falha ao conectar ao RabbitMQ. Nova tentativa em 5 segundos...");
                try { await Task.Delay(5000, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }

        if (stoppingToken.IsCancellationRequested || _connection is null)
            return;

        try
        {
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            _logger.LogInformation("Declarando topologia RabbitMQ (NotificationWorker)...");

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

            var alertsArgs = new Dictionary<string, object?>
            {
                { "x-dead-letter-exchange", MessagingTopology.DeadLetterExchangeName },
                { "x-dead-letter-routing-key", MessagingTopology.DeadLetterRoutingKey }
            };

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
                var body   = ea.Body.ToArray();
                var rawJson = Encoding.UTF8.GetString(body);

                AlertDetected? alertEvent = null;
                try
                {
                    alertEvent = JsonSerializer.Deserialize<AlertDetected>(rawJson);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Mensagem inválida (JSON malformado). Enviando NACK (requeue: false). Payload: {Payload}", rawJson);
                    await _channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                    return;
                }

                if (alertEvent is null || alertEvent.WeatherAlertId == Guid.Empty)
                {
                    _logger.LogWarning("Contrato inválido ou WeatherAlertId vazio. Enviando NACK (requeue: false).");
                    await _channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ClimaWatchDbContext>();
                var telegramNotifier = scope.ServiceProvider.GetRequiredService<TelegramNotifier>();

                try
                {
                    // 1. Database notification (no changes)
                    const string dbChannel = "database";
                    var existingDb = await dbContext.Notifications
                        .FirstOrDefaultAsync(n =>
                            n.WeatherAlertId == alertEvent.WeatherAlertId &&
                            n.Channel == dbChannel,
                            stoppingToken);

                    if (existingDb is null)
                    {
                        var dbNotification = new Notification
                        {
                            Id             = Guid.NewGuid(),
                            WeatherAlertId = alertEvent.WeatherAlertId,
                            Channel        = dbChannel,
                            Status         = "created",
                            CreatedAtUtc   = DateTimeOffset.UtcNow
                        };

                        dbContext.Notifications.Add(dbNotification);
                        await dbContext.SaveChangesAsync(stoppingToken);

                        _logger.LogInformation(
                            "Notificação database salva. NotificationId: {NotificationId}, WeatherAlertId: {WeatherAlertId}, AlertType: {AlertType}.",
                            dbNotification.Id, alertEvent.WeatherAlertId, alertEvent.AlertType);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Notificação database já existe para WeatherAlertId {WeatherAlertId}. Pulando salvamento database.",
                            alertEvent.WeatherAlertId);
                    }

                    // 2. Telegram notification (conditional)
                    if (telegramNotifier.IsConfigured)
                    {
                        const string tgChannel = "telegram";
                        
                        var existingTg = await dbContext.Notifications
                            .FirstOrDefaultAsync(n =>
                                n.WeatherAlertId == alertEvent.WeatherAlertId &&
                                n.Channel == tgChannel,
                                stoppingToken);

                        if (existingTg is null)
                        {
                            _logger.LogInformation("Telegram configurado. Enviando alerta {WeatherAlertId}...", alertEvent.WeatherAlertId);
                            var success = await telegramNotifier.SendAlertAsync(alertEvent, stoppingToken);
                            
                            var status = success ? "sent" : "telegram_failed";

                            var tgNotification = new Notification
                            {
                                Id             = Guid.NewGuid(),
                                WeatherAlertId = alertEvent.WeatherAlertId,
                                Channel        = tgChannel,
                                Status         = status,
                                CreatedAtUtc   = DateTimeOffset.UtcNow
                            };

                            dbContext.Notifications.Add(tgNotification);
                            await dbContext.SaveChangesAsync(stoppingToken);

                            _logger.LogInformation(
                                "Notificação Telegram salva com status '{Status}'. NotificationId: {NotificationId}, WeatherAlertId: {WeatherAlertId}.",
                                status, tgNotification.Id, alertEvent.WeatherAlertId);
                        }
                        else
                        {
                            _logger.LogInformation(
                                "Notificação Telegram já existe para WeatherAlertId {WeatherAlertId} com status '{Status}'. Pulando envio.",
                                alertEvent.WeatherAlertId, existingTg.Status);
                        }
                    }

                    await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao processar alerta {WeatherAlertId}. Enviando NACK (requeue: true).", alertEvent.WeatherAlertId);
                    try { await Task.Delay(2000, stoppingToken); } catch (OperationCanceledException) { return; }
                    try { await _channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true); }
                    catch (Exception nackEx) { _logger.LogError(nackEx, "Erro ao enviar NACK."); }
                }
            };

            await _channel.BasicConsumeAsync(
                queue: MessagingTopology.AlertsQueueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken);

            _logger.LogInformation("NotificationWorker iniciado e consumindo da fila {QueueName}...", MessagingTopology.AlertsQueueName);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("NotificationWorker sendo cancelado...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Erro crítico no NotificationWorker.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Finalizando o NotificationWorker...");

        if (_channel is not null)
        {
            try { await _channel.CloseAsync(cancellationToken); _channel.Dispose(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Erro ao fechar o canal."); }
        }

        if (_connection is not null)
        {
            try { await _connection.CloseAsync(cancellationToken); _connection.Dispose(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Erro ao fechar a conexão."); }
        }

        await base.StopAsync(cancellationToken);
    }
}
