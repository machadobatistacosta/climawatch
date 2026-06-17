using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
    private IChannel? _channel;

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

        // Retry loop for initial connection
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
                    // Graceful shutdown requested during delay
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
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            _logger.LogInformation("Declarando topologia RabbitMQ (Worker)...");
            
            await _channel.ExchangeDeclareAsync(
                exchange: MessagingTopology.ExchangeName,
                type: "topic",
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken);

            await _channel.QueueDeclareAsync(
                queue: MessagingTopology.WeatherChecksQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken);

            await _channel.QueueBindAsync(
                queue: MessagingTopology.WeatherChecksQueueName,
                exchange: MessagingTopology.ExchangeName,
                routingKey: MessagingTopology.WeatherCheckRequestedRoutingKey,
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

                // 1. Se WeatherCheckId == Guid.Empty
                if (message.WeatherCheckId == Guid.Empty)
                {
                    _logger.LogWarning("Evento recebido com WeatherCheckId vazio ou ausente. EventId: {EventId}. Enviando ACK.", message.EventId);
                    try
                    {
                        await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erro ao enviar ACK para a mensagem {EventId}", message.EventId);
                    }
                    return;
                }

                // Processar banco de dados
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ClimaWatchDbContext>();

                WeatherCheck? weatherCheck = null;
                try
                {
                    weatherCheck = await dbContext.WeatherChecks.FindAsync(new object[] { message.WeatherCheckId }, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro temporário ao acessar o banco de dados para buscar WeatherCheck {WeatherCheckId}. Enviando NACK (requeue: true).", message.WeatherCheckId);
                    
                    try
                    {
                        await Task.Delay(2000, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    try
                    {
                        await _channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
                    }
                    catch (Exception nackEx)
                    {
                        _logger.LogError(nackEx, "Erro ao enviar NACK com requeue para {WeatherCheckId}", message.WeatherCheckId);
                    }
                    return;
                }

                // 2. Se o registro não existir
                if (weatherCheck is null)
                {
                    _logger.LogWarning("WeatherCheck {WeatherCheckId} não foi encontrado no banco de dados. Enviando ACK.", message.WeatherCheckId);
                    try
                    {
                        await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erro ao enviar ACK para a mensagem {EventId}", message.EventId);
                    }
                    return;
                }

                // 3. Se Status == WeatherCheckStatuses.Processed
                if (weatherCheck.Status == WeatherCheckStatuses.Processed)
                {
                    _logger.LogInformation("Mensagem duplicada recebida para WeatherCheck {WeatherCheckId} (já está processed). Enviando ACK sem reprocessar.", message.WeatherCheckId);
                    try
                    {
                        await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erro ao enviar ACK para a mensagem {EventId}", message.EventId);
                    }
                    return;
                }

                // 4. Se Status == WeatherCheckStatuses.Failed
                if (weatherCheck.Status == WeatherCheckStatuses.Failed)
                {
                    _logger.LogWarning("Mensagem recebida para WeatherCheck {WeatherCheckId} que está em estado Failed. Enviando ACK sem alterar o registro.", message.WeatherCheckId);
                    try
                    {
                        await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erro ao enviar ACK para a mensagem {EventId}", message.EventId);
                    }
                    return;
                }

                // 5. Somente se Status == WeatherCheckStatuses.Queued
                if (weatherCheck.Status == WeatherCheckStatuses.Queued)
                {
                    try
                    {
                        weatherCheck.Status = WeatherCheckStatuses.Processed;
                        weatherCheck.ProcessedAtUtc = DateTimeOffset.UtcNow;
                        
                        await dbContext.SaveChangesAsync(stoppingToken);
                        
                        _logger.LogInformation("Mensagem processada com sucesso. WeatherCheckId: {WeatherCheckId}, EventId: {EventId}, CorrelationId: {CorrelationId}, City: {City}, Status: processed",
                            weatherCheck.Id,
                            weatherCheck.EventId,
                            weatherCheck.CorrelationId,
                            weatherCheck.City);

                        await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erro ao atualizar status ou salvar alterações para WeatherCheck {WeatherCheckId}. Enviando NACK (requeue: true).", message.WeatherCheckId);
                        
                        try
                        {
                            await Task.Delay(2000, stoppingToken);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }

                        try
                        {
                            await _channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
                        }
                        catch (Exception nackEx)
                        {
                            _logger.LogError(nackEx, "Erro ao enviar NACK com requeue para {WeatherCheckId}", message.WeatherCheckId);
                        }
                    }
                    return;
                }

                // 6. Para status inesperado
                _logger.LogWarning("WeatherCheck {WeatherCheckId} possui status inesperado '{Status}'. Enviando ACK sem alterar o registro.", message.WeatherCheckId, weatherCheck.Status);
                try
                {
                    await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao enviar ACK para a mensagem {EventId}", message.EventId);
                }
            };

            await _channel.BasicConsumeAsync(
                queue: MessagingTopology.WeatherChecksQueueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken);

            _logger.LogInformation("Worker iniciado e consumindo da fila {QueueName}...", MessagingTopology.WeatherChecksQueueName);

            // Keep the service alive until cancellation is requested
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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Finalizando o worker...");

        if (_channel is not null)
        {
            try
            {
                await _channel.CloseAsync(cancellationToken);
                _channel.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao fechar o canal do RabbitMQ no encerramento.");
            }
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.CloseAsync(cancellationToken);
                _connection.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao fechar a conexão do RabbitMQ no encerramento.");
            }
        }

        await base.StopAsync(cancellationToken);
    }
}
