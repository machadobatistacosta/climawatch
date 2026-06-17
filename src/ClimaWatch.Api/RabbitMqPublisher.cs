using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using ClimaWatch.Contracts;

namespace ClimaWatch.Api;

public sealed class RabbitMqPublisher : IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly ConnectionFactory _connectionFactory;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _disposed;

    public RabbitMqPublisher(IConfiguration configuration, ILogger<RabbitMqPublisher> logger)
    {
        _configuration = configuration;
        _logger = logger;

        var hostName = _configuration["RabbitMq:HostName"] ?? "localhost";
        var portStr = _configuration["RabbitMq:Port"] ?? "5672";
        var userName = _configuration["RabbitMq:UserName"] ?? "guest";
        var password = _configuration["RabbitMq:Password"] ?? "guest";

        if (!int.TryParse(portStr, out int port))
        {
            port = 5672;
        }

        _connectionFactory = new ConnectionFactory
        {
            HostName = hostName,
            Port = port,
            UserName = userName,
            Password = password,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            ClientProvidedName = "climawatch-api"
        };
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            return;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_channel is not null)
            {
                return;
            }

            _logger.LogInformation("Conectando ao RabbitMQ em {HostName}:{Port}...", _connectionFactory.HostName, _connectionFactory.Port);
            _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

            _logger.LogInformation("Declarando topologia RabbitMQ de forma idempotente...");
            
            await _channel.ExchangeDeclareAsync(
                exchange: MessagingTopology.ExchangeName,
                type: "topic",
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            await _channel.QueueDeclareAsync(
                queue: MessagingTopology.WeatherChecksQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            await _channel.QueueBindAsync(
                queue: MessagingTopology.WeatherChecksQueueName,
                exchange: MessagingTopology.ExchangeName,
                routingKey: MessagingTopology.WeatherCheckRequestedRoutingKey,
                arguments: null,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Conexão e topologia RabbitMQ estabelecidas.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao conectar ou declarar topologia no RabbitMQ.");
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task PublishAsync(WeatherCheckRequested message, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RabbitMqPublisher));
        }

        await EnsureConnectedAsync(cancellationToken);

        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);
        
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = message.EventId.ToString(),
            CorrelationId = message.CorrelationId.ToString(),
            Type = nameof(WeatherCheckRequested)
        };

        _logger.LogInformation("Publicando evento {EventId} para a cidade {City} (CorrelationId: {CorrelationId})...", 
            message.EventId, message.City, message.CorrelationId);

        await _channel!.BasicPublishAsync(
            exchange: MessagingTopology.ExchangeName,
            routingKey: MessagingTopology.WeatherCheckRequestedRoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_channel is not null)
        {
            try
            {
                await _channel.CloseAsync();
                _channel.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao fechar o canal do RabbitMQ durante dispose.");
            }
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.CloseAsync();
                _connection.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao fechar a conexão do RabbitMQ durante dispose.");
            }
        }

        _semaphore.Dispose();
    }
}
