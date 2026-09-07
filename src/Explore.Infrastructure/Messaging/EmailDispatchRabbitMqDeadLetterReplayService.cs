// ABOUTME: Hosted RabbitMQ dead-letter replay worker for EmailDispatch pointer messages.
// ABOUTME: Replays only database-validated pointers and parks unsafe DLQ payloads for operator review.

using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Explore.Infrastructure.Messaging;

public sealed class EmailDispatchRabbitMqDeadLetterReplayService(
    IConfiguration configuration,
    IOptionsMonitor<EmailDispatchRabbitMqSettings> settings,
    IEmailDispatchTransport transport,
    IServiceProvider serviceProvider,
    BusinessMetrics metrics,
    ILogger<EmailDispatchRabbitMqDeadLetterReplayService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = settings.CurrentValue;
        if (!options.Enabled || !options.DeadLetterReplayEnabled)
        {
            logger.LogInformation("RabbitMQ EmailDispatch dead-letter replay consumer disabled");
            return;
        }

        IConnection? connection = null;
        IChannel? channel = null;
        string? consumerTag = null;

        try
        {
            logger.LogInformation(
                "Starting RabbitMQ EmailDispatch dead-letter replay consumer {ConsumerId} on queue {DeadLetterQueueName} with prefetch {PrefetchCount}",
                options.DeadLetterReplayConsumerId,
                options.DeadLetterQueueName,
                options.DeadLetterReplayPrefetchCount);

            await transport.DeclareTopologyAsync(stoppingToken);
            connection = await RabbitMqEmailDispatchConnectionFactory.CreateConnectionAsync(
                configuration,
                options,
                $"{options.ClientProvidedName}:{options.DeadLetterReplayConsumerId}",
                stoppingToken);

            var channelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true,
                outstandingPublisherConfirmationsRateLimiter: null,
                consumerDispatchConcurrency: null);
            channel = await connection.CreateChannelAsync(channelOptions, stoppingToken);

            await channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: options.DeadLetterReplayPrefetchCount,
                global: false,
                cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, args) => HandleDeliveryAsync(channel, options, args, stoppingToken);

            consumerTag = await channel.BasicConsumeAsync(
                options.DeadLetterQueueName,
                autoAck: false,
                consumerTag: options.DeadLetterReplayConsumerId,
                noLocal: false,
                exclusive: false,
                arguments: null,
                consumer: consumer,
                cancellationToken: stoppingToken);

            logger.LogInformation(
                "RabbitMQ EmailDispatch dead-letter replay consumer {ConsumerId} is consuming with tag {ConsumerTag}",
                options.DeadLetterReplayConsumerId,
                consumerTag);

            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("RabbitMQ EmailDispatch dead-letter replay consumer stopping");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RabbitMQ EmailDispatch dead-letter replay consumer stopped unexpectedly");
            throw;
        }
        finally
        {
            if (channel is not null)
            {
                await CancelConsumerAsync(channel, consumerTag);
                await CloseAndDisposeChannelAsync(channel);
            }

            if (connection is not null)
            {
                await CloseAndDisposeConnectionAsync(connection);
            }
        }
    }

    private async Task HandleDeliveryAsync(
        IChannel channel,
        EmailDispatchRabbitMqSettings options,
        BasicDeliverEventArgs args,
        CancellationToken stoppingToken)
    {
        byte[] body = args.Body.ToArray();
        var parseResult = EmailDispatchRabbitMqConsumerDecision.ParsePointer(body);
        if (!parseResult.IsValid)
        {
            await ParkAndAckAsync(channel, options, args, body, "invalid_payload", parseResult.FailureCategory, stoppingToken);
            return;
        }

        EmailDispatchPointer pointer = parseResult.Pointer!;
        try
        {
            await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IEmailDispatchOutboxRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var dispatch = await repository.GetByTenantAndPublishEventId(
                pointer.TenantId,
                pointer.PublishEventId,
                stoppingToken);
            EmailDispatchRabbitMqDeadLetterReplaySettlement decision =
                EmailDispatchRabbitMqDeadLetterReplayDecision.Decide(pointer, dispatch);

            if (decision.Action == EmailDispatchRabbitMqDeadLetterReplayAction.Replay)
            {
                if (decision.RequiresDurableReplayReset)
                {
                    bool replayed = await unitOfWork.ExecuteInTransactionAsync(
                        ct => repository.TryReplayForOperator(
                            pointer.TenantId,
                            dispatch!.Id,
                            changedBy: null,
                            replayAt: DateTime.UtcNow,
                            cancellationToken: ct),
                        stoppingToken);
                    if (!replayed)
                    {
                        await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true, stoppingToken);
                        metrics.RecordEmailDispatchRabbitMqConsume(EmailDispatchConsumeOutcome.Nacked, "replay_state_changed");
                        logger.LogWarning(
                            "Nacked RabbitMQ EmailDispatch dead-letter pointer {PublishEventId} for tenant {TenantId} because durable replay state changed before reset",
                            pointer.PublishEventId,
                            pointer.TenantId);
                        return;
                    }
                }

                await PublishReplayAsync(channel, options, pointer, stoppingToken);
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, stoppingToken);
                metrics.RecordEmailDispatchRabbitMqConsume(EmailDispatchConsumeOutcome.Replayed, "none");
                logger.LogInformation(
                    "Replayed RabbitMQ EmailDispatch dead-letter pointer {PublishEventId} for tenant {TenantId}",
                    pointer.PublishEventId,
                    pointer.TenantId);
                return;
            }

            await ParkAndAckAsync(
                channel,
                options,
                args,
                body,
                decision.FailureCategory,
                decision.FailureCategory,
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "RabbitMQ EmailDispatch dead-letter delivery {DeliveryTag} stopped during shutdown before settlement",
                args.DeliveryTag);
        }
        catch (Exception ex)
        {
            metrics.RecordEmailDispatchRabbitMqConsume(EmailDispatchConsumeOutcome.Nacked, "dlq_replay_exception");
            logger.LogWarning(
                ex,
                "Nacking RabbitMQ EmailDispatch dead-letter pointer {PublishEventId} for tenant {TenantId} after replay exception",
                pointer.PublishEventId,
                pointer.TenantId);
            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
        }
    }

    private async Task ParkAndAckAsync(
        IChannel channel,
        EmailDispatchRabbitMqSettings options,
        BasicDeliverEventArgs args,
        byte[] body,
        string replayReason,
        string failureCategory,
        CancellationToken cancellationToken)
    {
        await PublishParkingAsync(channel, options, args, body, replayReason, cancellationToken);
        await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
        metrics.RecordEmailDispatchRabbitMqConsume(EmailDispatchConsumeOutcome.Parked, failureCategory);
        logger.LogWarning(
            "Parked RabbitMQ EmailDispatch dead-letter delivery {DeliveryTag} with reason {ReplayReason}",
            args.DeliveryTag,
            replayReason);
    }

    private static async Task PublishReplayAsync(
        IChannel channel,
        EmailDispatchRabbitMqSettings options,
        EmailDispatchPointer pointer,
        CancellationToken cancellationToken)
    {
        byte[] replayBody = JsonSerializer.SerializeToUtf8Bytes(pointer, JsonOptions);
        var properties = CreatePointerProperties(pointer, replayReason: null, originalRoutingKey: null);

        await channel.BasicPublishAsync(
            options.ExchangeName,
            options.DispatchRoutingKey,
            mandatory: true,
            basicProperties: properties,
            body: replayBody,
            cancellationToken: cancellationToken);
    }

    private static async Task PublishParkingAsync(
        IChannel channel,
        EmailDispatchRabbitMqSettings options,
        BasicDeliverEventArgs args,
        byte[] body,
        string replayReason,
        CancellationToken cancellationToken)
    {
        var properties = CreateParkingProperties(args, replayReason);

        await channel.BasicPublishAsync(
            options.DeadLetterExchangeName,
            options.ParkingRoutingKey,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    private static BasicProperties CreatePointerProperties(
        EmailDispatchPointer pointer,
        string? replayReason,
        string? originalRoutingKey)
    {
        Dictionary<string, object?> headers = new(StringComparer.Ordinal)
        {
            ["tenant_id"] = pointer.TenantId.ToString(),
            ["publish_event_id"] = pointer.PublishEventId.ToString(),
            ["source_type"] = pointer.SourceType
        };

        if (!string.IsNullOrWhiteSpace(replayReason))
        {
            headers["x-email-dispatch-replay-reason"] = replayReason;
            headers["x-email-dispatch-replay-at"] = DateTimeOffset.UtcNow.ToString("O");
        }

        if (!string.IsNullOrWhiteSpace(originalRoutingKey))
        {
            headers["x-email-dispatch-original-routing-key"] = originalRoutingKey;
        }

        return new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Persistent = true,
            MessageId = pointer.PublishEventId.ToString(),
            CorrelationId = pointer.PublishEventId.ToString(),
            Type = nameof(EmailDispatchPointer),
            AppId = "explore-api",
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Headers = headers
        };
    }

    private static BasicProperties CreateParkingProperties(BasicDeliverEventArgs args, string replayReason)
    {
        Dictionary<string, object?> headers = args.BasicProperties.Headers is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(args.BasicProperties.Headers, StringComparer.Ordinal);

        headers["x-email-dispatch-replay-reason"] = replayReason;
        headers["x-email-dispatch-replay-at"] = DateTimeOffset.UtcNow.ToString("O");
        headers["x-email-dispatch-original-routing-key"] = args.RoutingKey;

        return new BasicProperties
        {
            ContentType = string.IsNullOrWhiteSpace(args.BasicProperties.ContentType)
                ? "application/json"
                : args.BasicProperties.ContentType,
            DeliveryMode = DeliveryModes.Persistent,
            Persistent = true,
            MessageId = args.BasicProperties.MessageId,
            CorrelationId = args.BasicProperties.CorrelationId,
            Type = args.BasicProperties.Type,
            AppId = "explore-api",
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Headers = headers
        };
    }

    private static async Task CancelConsumerAsync(IChannel channel, string? consumerTag)
    {
        if (!channel.IsOpen || string.IsNullOrWhiteSpace(consumerTag))
        {
            return;
        }

        try
        {
            await channel.BasicCancelAsync(consumerTag, noWait: false, CancellationToken.None);
        }
        catch (Exception) when (!channel.IsOpen)
        {
            // Channel shutdown already completed; cancellation failure should not mask host shutdown.
        }
        catch (Exception)
        {
            // Shutdown continues through channel close/dispose; cancellation failure should not mask host shutdown.
        }
    }

    private static async Task CloseAndDisposeChannelAsync(IChannel channel)
    {
        try
        {
            if (channel.IsOpen)
            {
                await channel.CloseAsync(200, "EmailDispatch RabbitMQ DLQ replay consumer stopped", abort: false, CancellationToken.None);
            }
        }
        finally
        {
            await channel.DisposeAsync();
        }
    }

    private static async Task CloseAndDisposeConnectionAsync(IConnection connection)
    {
        try
        {
            if (connection.IsOpen)
            {
                await connection.CloseAsync(200, "EmailDispatch RabbitMQ DLQ replay consumer stopped", TimeSpan.FromSeconds(5), abort: false, CancellationToken.None);
            }
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }
}
