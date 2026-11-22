using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Messaging;

public sealed class EmailDispatchRabbitMqPointerPublisher(
    IEmailDispatchPointerPublicationStore store,
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
        IReadOnlyList<EmailDispatchPointerCandidate> rows = await store.GetPublishBatchAsync(
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

            var pointer = row.Pointer;
            var attemptedAt = DateTime.UtcNow;
            try
            {
                EmailDispatchPublishResult result = await transport.PublishDispatchPointerAsync(pointer, cancellationToken);
                if (result.Outcome == EmailDispatchPublishOutcome.Confirmed)
                {
                    await store.MarkPublishSucceededAsync(row, attemptedAt, cancellationToken);
                    confirmed++;
                    continue;
                }

                if (result.Outcome == EmailDispatchPublishOutcome.Disabled)
                {
                    skipped++;
                    continue;
                }

                await store.MarkPublishFailedAsync(
                    row,
                    GetFailureCode(result),
                    attemptedAt,
                    cancellationToken);
                failed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                await store.MarkPublishFailedAsync(
                    row,
                    EmailDispatchPublishFailure.PointerPublishException.ToCode(),
                    attemptedAt,
                    CancellationToken.None);
                failed++;
                logger.LogWarning(
                    "RabbitMQ EmailDispatch pointer producer failed");
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
