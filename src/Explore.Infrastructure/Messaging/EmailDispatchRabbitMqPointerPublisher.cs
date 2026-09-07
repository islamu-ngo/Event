// ABOUTME: Publishes pointer-only EmailDispatchOutbox rows into optional RabbitMQ Dispatch Mode.
// ABOUTME: Records producer attempt metadata while PostgreSQL remains the source of delivery truth.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Messaging;

public sealed class EmailDispatchRabbitMqPointerPublisher(
    IEmailDispatchOutboxRepository repository,
    IEmailDispatchTransport transport,
    IOptionsMonitor<EmailDispatchRabbitMqSettings> settings,
    ILogger<EmailDispatchRabbitMqPointerPublisher> logger)
{
    public async Task<EmailDispatchRabbitMqPointerPublisherResult> PublishDuePointersAsync(CancellationToken cancellationToken)
    {
        var options = settings.CurrentValue;
        if (!options.Enabled)
        {
            return new EmailDispatchRabbitMqPointerPublisherResult(
                EligibleCount: 0,
                ConfirmedCount: 0,
                FailedCount: 0,
                SkippedCount: 0);
        }

        var now = DateTime.UtcNow;
        var retryAttemptsBefore = now.AddSeconds(-options.PublisherRetryDelaySeconds);
        IReadOnlyList<EmailDispatchOutbox> rows = await repository.GetRabbitMqPublishBatch(
            options.PublisherBatchSize,
            now,
            retryAttemptsBefore,
            cancellationToken);

        var confirmed = 0;
        var failed = 0;
        var skipped = 0;

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pointer = EmailDispatchPointer.FromOutbox(row);
            var attemptedAt = DateTime.UtcNow;
            try
            {
                EmailDispatchPublishResult result = await transport.PublishDispatchPointerAsync(pointer, cancellationToken);
                if (result.Outcome == EmailDispatchPublishOutcome.Confirmed)
                {
                    await repository.MarkRabbitMqPublishSucceeded(row.Id, attemptedAt, cancellationToken);
                    confirmed++;
                    continue;
                }

                if (result.Outcome == EmailDispatchPublishOutcome.Disabled)
                {
                    skipped++;
                    continue;
                }

                await repository.MarkRabbitMqPublishFailed(
                    row.Id,
                    GetFailureCode(result),
                    attemptedAt,
                    cancellationToken);
                failed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await repository.MarkRabbitMqPublishFailed(
                    row.Id,
                    EmailDispatchPublishFailure.PointerPublishException.ToCode(),
                    attemptedAt,
                    CancellationToken.None);
                failed++;
                logger.LogWarning(
                    ex,
                    "RabbitMQ EmailDispatch pointer producer failed for outbox row {OutboxId}, publish event {PublishEventId}, tenant {TenantId}",
                    row.Id,
                    row.PublishEventId,
                    row.TenantId);
            }
        }

        return new EmailDispatchRabbitMqPointerPublisherResult(
            EligibleCount: rows.Count,
            ConfirmedCount: confirmed,
            FailedCount: failed,
            SkippedCount: skipped);
    }

    private static string GetFailureCode(EmailDispatchPublishResult result) =>
        result.FailureCategory is { } failure
            ? failure.ToCode()
            : result.Outcome switch
            {
                EmailDispatchPublishOutcome.Returned => "returned",
                EmailDispatchPublishOutcome.Nacked => "nacked",
                EmailDispatchPublishOutcome.Failed => "failed",
                _ => "unknown"
            };
}

public sealed record EmailDispatchRabbitMqPointerPublisherResult(
    int EligibleCount,
    int ConfirmedCount,
    int FailedCount,
    int SkippedCount);
