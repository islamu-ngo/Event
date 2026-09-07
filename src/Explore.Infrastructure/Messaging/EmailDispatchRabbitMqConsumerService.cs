using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Explore.Infrastructure.Messaging;

public sealed class EmailDispatchRabbitMqConsumerService(
    IConfiguration configuration,
    IOptionsMonitor<EmailDispatchRabbitMqSettings> settings,
    IEmailDispatchTransport transport,
    IServiceProvider serviceProvider,
    BusinessMetrics metrics,
    ILogger<EmailDispatchRabbitMqConsumerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = settings.CurrentValue;
        if (!options.Enabled)
        {
            logger.LogInformation("RabbitMQ EmailDispatch consumer disabled");
            return;
        }

        IConnection? connection = null;
        IChannel? channel = null;
        string? consumerTag = null;

        try
        {
            logger.LogInformation(
                "Starting RabbitMQ EmailDispatch consumer {ConsumerId} on queue {DispatchQueueName} with prefetch {PrefetchCount}",
                options.ConsumerId,
                options.DispatchQueueName,
                options.PrefetchCount);

            await transport.DeclareTopologyAsync(stoppingToken);
            connection = await RabbitMqEmailDispatchConnectionFactory.CreateConnectionAsync(
                configuration,
                options,
                $"{options.ClientProvidedName}:{options.ConsumerId}",
                stoppingToken);
            channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

            await channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: options.PrefetchCount,
                global: false,
                cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, args) => HandleDeliveryAsync(channel, options, args, stoppingToken);

            consumerTag = await channel.BasicConsumeAsync(
                options.DispatchQueueName,
                autoAck: false,
                consumerTag: options.ConsumerId,
                noLocal: false,
                exclusive: false,
                arguments: null,
                consumer: consumer,
                cancellationToken: stoppingToken);

            logger.LogInformation(
                "RabbitMQ EmailDispatch consumer {ConsumerId} is consuming with tag {ConsumerTag}",
                options.ConsumerId,
                consumerTag);

            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("RabbitMQ EmailDispatch consumer stopping");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RabbitMQ EmailDispatch consumer stopped unexpectedly");
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
            metrics.RecordEmailDispatchRabbitMqConsume("rejected", parseResult.FailureCategory);
            logger.LogWarning(
                "Rejecting malformed RabbitMQ EmailDispatch pointer delivery {DeliveryTag} with category {FailureCategory}",
                args.DeliveryTag,
                parseResult.FailureCategory);
            await ApplySettlementAsync(
                channel,
                args.DeliveryTag,
                EmailDispatchRabbitMqSettlement.Reject(parseResult.FailureCategory),
                stoppingToken);
            return;
        }

        EmailDispatchPointer pointer = parseResult.Pointer!;
        try
        {
            await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();
            var drainService = scope.ServiceProvider.GetRequiredService<IEmailDispatchDrainService>();
            EmailDispatchSingleDrainResult result = await drainService.ProcessSingleAsync(
                pointer.TenantId,
                pointer.PublishEventId,
                options.ConsumerId,
                stoppingToken);

            EmailDispatchRabbitMqSettlement settlement = EmailDispatchRabbitMqConsumerDecision.DecideForDrainResult(result);
            await ApplySettlementAsync(channel, args.DeliveryTag, settlement, stoppingToken);
            logger.LogDebug(
                "Settled RabbitMQ EmailDispatch pointer {PublishEventId} for tenant {TenantId} with broker action {BrokerAction} after drain outcome {DrainOutcome}",
                pointer.PublishEventId,
                pointer.TenantId,
                settlement.Action,
                result.Outcome);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "RabbitMQ EmailDispatch delivery {DeliveryTag} stopped during shutdown before settlement",
                args.DeliveryTag);
        }
        catch (Exception ex)
        {
            metrics.RecordEmailDispatchRabbitMqConsume("nacked", "consumer_exception");
            logger.LogWarning(
                ex,
                "Nacking RabbitMQ EmailDispatch pointer {PublishEventId} for tenant {TenantId} after consumer exception",
                pointer.PublishEventId,
                pointer.TenantId);
            await ApplySettlementAsync(
                channel,
                args.DeliveryTag,
                EmailDispatchRabbitMqConsumerDecision.DecideForUnexpectedFailure(),
                CancellationToken.None);
        }
    }

    private static ValueTask ApplySettlementAsync(
        IChannel channel,
        ulong deliveryTag,
        EmailDispatchRabbitMqSettlement settlement,
        CancellationToken cancellationToken) =>
        settlement.Action switch
        {
            EmailDispatchRabbitMqSettlementAction.Ack => channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken),
            EmailDispatchRabbitMqSettlementAction.Reject => channel.BasicRejectAsync(deliveryTag, requeue: settlement.Requeue, cancellationToken),
            EmailDispatchRabbitMqSettlementAction.Nack => channel.BasicNackAsync(deliveryTag, multiple: false, requeue: settlement.Requeue, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown RabbitMQ settlement action {settlement.Action}.")
        };

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
                await channel.CloseAsync(200, "EmailDispatch RabbitMQ consumer stopped", abort: false, CancellationToken.None);
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
                await connection.CloseAsync(200, "EmailDispatch RabbitMQ consumer stopped", TimeSpan.FromSeconds(5), abort: false, CancellationToken.None);
            }
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }
}
