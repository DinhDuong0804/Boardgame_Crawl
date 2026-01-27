using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BoardGameScraper.Api.Services;

/// <summary>
/// RabbitMQ Service for publishing and consuming messages
/// </summary>
public class RabbitMQService : IDisposable
{
    private readonly ILogger<RabbitMQService> _logger;
    private readonly IConfiguration _config;
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Exchange and Queue names
    public const string ExchangeName = "boardgame.exchange";
    public const string TranslationRequestQueue = "translation.requests";
    public const string TranslationCompletedQueue = "translation.completed";
    public const string ScraperCompletedQueue = "scraper.completed";

    // Routing keys
    public const string RoutingKeyTranslationRequest = "translation.request";
    public const string RoutingKeyTranslationCompleted = "translation.completed";
    public const string RoutingKeyGameScraped = "scraper.game.new";

    public RabbitMQService(ILogger<RabbitMQService> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    private async Task EnsureConnectionAsync()
    {
        if (_connection?.IsOpen == true && _channel?.IsOpen == true)
            return;

        await _lock.WaitAsync();
        try
        {
            if (_connection?.IsOpen == true && _channel?.IsOpen == true)
                return;

            var factory = new ConnectionFactory
            {
                HostName = _config["RabbitMQ:Host"] ?? "localhost",
                Port = _config.GetValue<int>("RabbitMQ:Port", 5672),
                UserName = _config["RabbitMQ:Username"] ?? "guest",
                Password = _config["RabbitMQ:Password"] ?? "guest",
                VirtualHost = _config["RabbitMQ:VirtualHost"] ?? "/"
            };

            _logger.LogInformation("Connecting to RabbitMQ at {Host}:{Port}", factory.HostName, factory.Port);

            _connection = await factory.CreateConnectionAsync();
            _channel = await _connection.CreateChannelAsync();

            // Declare exchange
            await _channel.ExchangeDeclareAsync(
                exchange: ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false
            );

            // Declare queues
            await DeclareQueueAsync(TranslationRequestQueue, RoutingKeyTranslationRequest);
            await DeclareQueueAsync(TranslationCompletedQueue, RoutingKeyTranslationCompleted);
            await DeclareQueueAsync(ScraperCompletedQueue, RoutingKeyGameScraped);

            _logger.LogInformation("RabbitMQ connection established successfully");
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task DeclareQueueAsync(string queueName, string routingKey)
    {
        if (_channel == null)
            return;

        await _channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        await _channel.QueueBindAsync(
            queue: queueName,
            exchange: ExchangeName,
            routingKey: routingKey
        );

        _logger.LogDebug("Queue {Queue} bound to {RoutingKey}", queueName, routingKey);
    }

    /// <summary>
    /// Publish a message to RabbitMQ
    /// </summary>
    public async Task PublishAsync<T>(string routingKey, T message, Guid? correlationId = null)
    {
        await EnsureConnectionAsync();

        if (_channel == null)
            throw new InvalidOperationException("RabbitMQ channel not available");

        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        var body = Encoding.UTF8.GetBytes(json);

        var properties = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = "application/json",
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = correlationId?.ToString() ?? Guid.NewGuid().ToString(),
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };

        await _channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body
        );

        _logger.LogInformation("Published message to {RoutingKey}: {MessageId}", routingKey, properties.MessageId);
    }

    /// <summary>
    /// Request translation for a game
    /// </summary>
    public async Task RequestTranslationAsync(TranslationRequest request)
    {
        await PublishAsync(RoutingKeyTranslationRequest, request, Guid.NewGuid());
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        _lock.Dispose();
    }
}

// Message DTOs
public class TranslationRequest
{
    public int GameId { get; set; }
    public int BggId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool TranslateInfo { get; set; } = true;
    public bool TranslateRulebooks { get; set; } = false;
    public List<RulebookToTranslate> Rulebooks { get; set; } = new();
}

public class RulebookToTranslate
{
    public int RulebookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? LocalFilePath { get; set; }
}

public class TranslationCompleted
{
    public int GameId { get; set; }
    public bool Success { get; set; }
    public string? NameVi { get; set; }
    public string? DescriptionVi { get; set; }
    public List<RulebookTranslationResult> Rulebooks { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class RulebookTranslationResult
{
    public int RulebookId { get; set; }
    public bool Success { get; set; }
    public string? ContentVi { get; set; }
    public string? MarkdownPath { get; set; }
    public string? ErrorMessage { get; set; }
}
