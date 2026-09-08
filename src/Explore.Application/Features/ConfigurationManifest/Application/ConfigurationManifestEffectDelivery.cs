namespace Explore.Application.Features.ConfigurationManifest.Application;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;

public interface IConfigurationManifestEffectDeliveryStrategy
{
    Task CreatePendingAsync(
        Guid messageId,
        Guid operationId,
        DateTime occurredAtUtc);

    Task DrainPendingAsync(CancellationToken cancellationToken);

    Task DeliverAsync(
        Guid messageId,
        CancellationToken cancellationToken);
}

public sealed class ConfigurationManifestEffectDelivery(
    IConfigurationManifestEffectOutboxRepository outboxRepository,
    IConfigurationManifestEffectDispatcher dispatcher)
    : IConfigurationManifestEffectDeliveryStrategy
{
    private const int StartupDrainBatchSize = 1_000;

    public async Task CreatePendingAsync(
        Guid messageId,
        Guid operationId,
        DateTime occurredAtUtc) =>
        await outboxRepository.Create(ConfigurationManifestEffectOutbox.Create(
            messageId,
            operationId,
            occurredAtUtc));

    public async Task DrainPendingAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<OutboxMessage> pending =
            await outboxRepository.GetPendingManifestEffectsAsync(
                StartupDrainBatchSize,
                cancellationToken);
        foreach (OutboxMessage message in pending)
        {
            await DeliverAsync(message, cancellationToken);
        }
    }

    public async Task DeliverAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        OutboxMessage message = await outboxRepository.GetByIdAsync(
                messageId,
                cancellationToken)
            ?? throw new InvalidOperationException("Manifest effect outbox message was not found.");
        await DeliverAsync(message, cancellationToken);
    }

    private async Task DeliverAsync(
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        if (message.Status == OutboxMessageStatus.Completed)
        {
            return;
        }

        DateTime? leaseExpiresAt = await outboxRepository.TryClaimForProcessing(
            message.Id,
            DateTime.UtcNow,
            cancellationToken);
        if (!leaseExpiresAt.HasValue)
        {
            OutboxMessage? current = await outboxRepository.GetByIdAsync(
                message.Id,
                cancellationToken);
            if (current?.Status == OutboxMessageStatus.Completed)
            {
                return;
            }

            throw new InvalidOperationException("Manifest effect outbox message is already leased.");
        }

        try
        {
            await dispatcher.DispatchAsync(message.AggregateId, cancellationToken);
            bool completed = await outboxRepository.MarkAsCompleted(
                message.Id,
                leaseExpiresAt.Value,
                cancellationToken);
            if (!completed)
            {
                throw new InvalidOperationException("Manifest effect outbox completion lease was lost.");
            }
        }
        catch (Exception exception)
        {
            CancellationToken persistenceToken = cancellationToken.IsCancellationRequested
                ? CancellationToken.None
                : cancellationToken;
            await outboxRepository.MarkAsFailed(
                message.Id,
                leaseExpiresAt.Value,
                exception.GetType().Name,
                isRetryable: true,
                retryDelaySeconds: 0,
                DateTime.UtcNow,
                persistenceToken);
            throw;
        }
    }
}

public sealed class DeferredConfigurationManifestEffectDelivery(
    IConfigurationManifestEffectOutboxRepository outboxRepository)
    : IConfigurationManifestEffectDeliveryStrategy
{
    public async Task CreatePendingAsync(
        Guid messageId,
        Guid operationId,
        DateTime occurredAtUtc) =>
        await outboxRepository.Create(ConfigurationManifestEffectOutbox.Create(
            messageId,
            operationId,
            occurredAtUtc));

    public Task DrainPendingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task DeliverAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(messageId, Guid.Empty);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
