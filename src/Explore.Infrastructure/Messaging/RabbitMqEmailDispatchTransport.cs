// ABOUTME: RabbitMQ.Client adapter for optional EmailDispatch pointer publishing and topology checks.
// ABOUTME: Uses mandatory publishes plus publisher confirms while PostgreSQL remains the delivery source of truth.

using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Explore.Infrastructure.Messaging;

public sealed class RabbitMqEmailDispatchTransport : IEmailDispatchTransport, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<EmailDispatchRabbitMqSettings> _settings;
    private readonly BusinessMetrics _metrics;
    private readonly ILogger<RabbitMqEmailDispatchTransport> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;

    public RabbitMqEmailDispatchTransport(
        IConfiguration configuration,
        IOptionsMonitor<EmailDispatchRabbitMqSettings> settings,
        BusinessMetrics metrics,
        ILogger<RabbitMqEmailDispatchTransport> logger)
    {
        _configuration = configuration;
        _settings = settings;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task DeclareTopologyAsync(CancellationToken cancellationToken = default)
    {
        var options = _settings.CurrentValue;
        if (!options.Enabled)
        {
            return;
        }

        IConnection connection = await GetConnectionAsync(options, cancellationToken);
        IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        try
        {
            await channel.ExchangeDeclareAsync(
                options.ExchangeName,
                ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                arguments: null,
                noWait: false,
                cancellationToken: cancellationToken);

            await channel.ExchangeDeclareAsync(
                options.DeadLetterExchangeName,
                ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                arguments: null,
                noWait: false,
                cancellationToken: cancellationToken);

            Dictionary<string, object?> dispatchQueueArguments = new(StringComparer.Ordinal)
            {
                ["x-dead-letter-exchange"] = options.DeadLetterExchangeName,
                ["x-dead-letter-routing-key"] = options.DeadLetterRoutingKey
            };

            await channel.QueueDeclareAsync(
                options.DispatchQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: dispatchQueueArguments,
                noWait: false,
                cancellationToken: cancellationToken);

            await channel.QueueDeclareAsync(
                options.DeadLetterQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                noWait: false,
                cancellationToken: cancellationToken);

            await channel.QueueDeclareAsync(
                options.ParkingQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                noWait: false,
                cancellationToken: cancellationToken);

            await channel.QueueBindAsync(
                options.DispatchQueueName,
                options.ExchangeName,
                options.DispatchRoutingKey,
                arguments: null,
                noWait: false,
                cancellationToken: cancellationToken);

            await channel.QueueBindAsync(
                options.DeadLetterQueueName,
                options.DeadLetterExchangeName,
                options.DeadLetterRoutingKey,
                arguments: null,
                noWait: false,
                cancellationToken: cancellationToken);

            await channel.QueueBindAsync(
                options.ParkingQueueName,
                options.DeadLetterExchangeName,
                options.ParkingRoutingKey,
                arguments: null,
                noWait: false,
                cancellationToken: cancellationToken);
        }
        finally
        {
            await CloseAndDisposeChannelAsync(channel, CancellationToken.None);
        }
    }

    public async Task<EmailDispatchPublishResult> PublishDispatchPointerAsync(
        EmailDispatchPointer pointer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pointer);

        var options = _settings.CurrentValue;
        if (!options.Enabled)
        {
            _metrics.RecordEmailDispatchRabbitMqPublish(EmailDispatchPublishOutcome.Disabled);
            return EmailDispatchPublishResult.Disabled();
        }

        await DeclareTopologyAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.PublishTimeoutSeconds));

        IConnection connection = await GetConnectionAsync(options, timeout.Token);
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true,
            outstandingPublisherConfirmationsRateLimiter: null,
            consumerDispatchConcurrency: null);
        IChannel channel = await connection.CreateChannelAsync(channelOptions, timeout.Token);
        BasicReturnEventArgs? returned = null;

        Task OnBasicReturnAsync(object sender, BasicReturnEventArgs args)
        {
            returned = args;
            _logger.LogWarning(
                "RabbitMQ returned EmailDispatch pointer publish {PublishEventId} for tenant {TenantId} with reply code {ReplyCode} on routing key {RoutingKey}",
                pointer.PublishEventId,
                pointer.TenantId,
                args.ReplyCode,
                args.RoutingKey);
            return Task.CompletedTask;
        }

        channel.BasicReturnAsync += OnBasicReturnAsync;

        try
        {
            ulong sequenceNumber = await channel.GetNextPublishSequenceNumberAsync(timeout.Token);
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(pointer, JsonOptions);
            var properties = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Persistent = true,
                MessageId = pointer.PublishEventId.ToString(),
                CorrelationId = pointer.PublishEventId.ToString(),
                Type = nameof(EmailDispatchPointer),
                AppId = "explore-api",
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>
                {
                    ["tenant_id"] = pointer.TenantId.ToString(),
                    ["publish_event_id"] = pointer.PublishEventId.ToString(),
                    ["source_type"] = pointer.SourceType
                }
            };

            await channel.BasicPublishAsync(
                options.ExchangeName,
                options.DispatchRoutingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: timeout.Token);

            _metrics.RecordEmailDispatchRabbitMqPublish(EmailDispatchPublishOutcome.Confirmed);
            _logger.LogInformation(
                "RabbitMQ confirmed EmailDispatch pointer publish {PublishEventId} for tenant {TenantId} with sequence {PublishSequenceNumber}",
                pointer.PublishEventId,
                pointer.TenantId,
                sequenceNumber);

            return EmailDispatchPublishResult.Confirmed(sequenceNumber);
        }
        catch (PublishReturnException ex)
        {
            _metrics.RecordEmailDispatchRabbitMqPublish(EmailDispatchPublishOutcome.Returned, EmailDispatchPublishFailure.MandatoryReturn.ToCode());
            return new EmailDispatchPublishResult(
                Outcome: EmailDispatchPublishOutcome.Returned,
                PublishSequenceNumber: ex.PublishSequenceNumber,
                ReplyCode: ex.ReplyCode,
                ReplyText: ex.ReplyText,
                FailureCategory: EmailDispatchPublishFailure.MandatoryReturn);
        }
        catch (PublishException ex)
        {
            var outcome = ex.IsReturn ? EmailDispatchPublishOutcome.Returned : EmailDispatchPublishOutcome.Nacked;
            var failureCategory = ex.IsReturn ? EmailDispatchPublishFailure.MandatoryReturn : EmailDispatchPublishFailure.PublisherNack;
            _metrics.RecordEmailDispatchRabbitMqPublish(outcome, failureCategory.ToCode());
            return new EmailDispatchPublishResult(outcome, ex.PublishSequenceNumber, FailureCategory: failureCategory);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _metrics.RecordEmailDispatchRabbitMqPublish(EmailDispatchPublishOutcome.Failed, EmailDispatchPublishFailure.PublishTimeout.ToCode());
            return new EmailDispatchPublishResult(EmailDispatchPublishOutcome.Failed, FailureCategory: EmailDispatchPublishFailure.PublishTimeout);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _metrics.RecordEmailDispatchRabbitMqPublish(EmailDispatchPublishOutcome.Failed, EmailDispatchPublishFailure.BrokerPublishFailed.ToCode());
            _logger.LogWarning(
                ex,
                "RabbitMQ EmailDispatch pointer publish failed for {PublishEventId} and tenant {TenantId}",
                pointer.PublishEventId,
                pointer.TenantId);
            return new EmailDispatchPublishResult(EmailDispatchPublishOutcome.Failed, FailureCategory: EmailDispatchPublishFailure.BrokerPublishFailed);
        }
        finally
        {
            channel.BasicReturnAsync -= OnBasicReturnAsync;
            if (returned is not null)
            {
                _logger.LogDebug(
                    "RabbitMQ return metadata captured for EmailDispatch pointer publish {PublishEventId}: {ReplyCode}/{ReplyText}",
                    pointer.PublishEventId,
                    returned.ReplyCode,
                    returned.ReplyText);
            }

            await CloseAndDisposeChannelAsync(channel, CancellationToken.None);
        }
    }

    public async Task<EmailDispatchTransportHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var options = _settings.CurrentValue;
        Dictionary<string, object> data = new(StringComparer.Ordinal)
        {
            ["enabled"] = options.Enabled,
            ["connectionStringName"] = options.ConnectionStringName,
            ["exchange"] = options.ExchangeName,
            ["dispatchQueue"] = options.DispatchQueueName,
            ["deadLetterQueue"] = options.DeadLetterQueueName,
            ["parkingQueue"] = options.ParkingQueueName
        };

        if (!options.Enabled)
        {
            return new EmailDispatchTransportHealth(
                Enabled: false,
                Healthy: true,
                Description: "RabbitMQ Dispatch Mode is disabled; Basic Dispatch Mode remains independent.",
                Data: data);
        }

        try
        {
            await DeclareTopologyAsync(cancellationToken);
            return new EmailDispatchTransportHealth(
                Enabled: true,
                Healthy: true,
                Description: "RabbitMQ Dispatch Mode topology is reachable and declared.",
                Data: data);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RabbitMQ Dispatch Mode health check failed for exchange {ExchangeName}", options.ExchangeName);
            return new EmailDispatchTransportHealth(
                Enabled: true,
                Healthy: false,
                Description: "RabbitMQ Dispatch Mode is enabled but the broker topology is not healthy.",
                Data: data);
        }
    }

    public async ValueTask DisposeAsync()
    {
        IConnection? connection = _connection;
        _connection = null;

        if (connection is null)
        {
            _connectionLock.Dispose();
            return;
        }

        try
        {
            if (connection.IsOpen)
            {
                await connection.CloseAsync(200, "EmailDispatch RabbitMQ transport disposed", TimeSpan.FromSeconds(5), abort: false, CancellationToken.None);
            }
        }
        finally
        {
            await connection.DisposeAsync();
            _connectionLock.Dispose();
        }
    }

    private async Task<IConnection> GetConnectionAsync(EmailDispatchRabbitMqSettings options, CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true } existing)
        {
            return existing;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true } current)
            {
                return current;
            }

            _connection = await RabbitMqEmailDispatchConnectionFactory.CreateConnectionAsync(
                _configuration,
                options,
                options.ClientProvidedName,
                cancellationToken);
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private static async Task CloseAndDisposeChannelAsync(IChannel channel, CancellationToken cancellationToken)
    {
        try
        {
            if (channel.IsOpen)
            {
                await channel.CloseAsync(200, "OK", abort: false, cancellationToken);
            }
        }
        finally
        {
            await channel.DisposeAsync();
        }
    }
}
