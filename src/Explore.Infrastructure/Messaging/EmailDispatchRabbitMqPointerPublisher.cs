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
    private const string PublisherExceptionFailureCategory = "pointer_publish_exception";

    public async Task<EmailDispatchRabbitMqPointerPublisherResult> PublishDuePointersAsync(CancellationToken cancellationToken)
    {
        var options = settings.CurrentValue;
        if (!options.Enabled)
        {
            return new EmailDispatchRabbitMqPointerPublisherResult(0, 0, 0, 0);
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
                    NormalizeFailureCategory(result.FailureCategory, result.Outcome),
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
                    PublisherExceptionFailureCategory,
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

        return new EmailDispatchRabbitMqPointerPublisherResult(rows.Count, confirmed, failed, skipped);
    }

    private static string NormalizeFailureCategory(string? failureCategory, EmailDispatchPublishOutcome outcome)
    {
        var value = string.IsNullOrWhiteSpace(failureCategory)
            ? outcome.ToString().ToLowerInvariant()
            : failureCategory.Trim();
        return value.Length <= 100 ? value : value[..100];
    }
}

public sealed record EmailDispatchRabbitMqPointerPublisherResult(
    int EligibleCount,
    int ConfirmedCount,
    int FailedCount,
    int SkippedCount);
