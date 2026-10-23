using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Application.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure;

public sealed class IdempotencyCleanupService(
    IIdempotencyCleanupTenantDiscoveryRepository discovery,
    IIdempotencyCleanupExecutionScopeFactory scopeFactory,
    IOptions<IdempotencyCleanupSettings> settings,
    BusinessMetrics metrics,
    ILogger<IdempotencyCleanupService> logger) : IIdempotencyCleanupService
{
    private const string TableFamily = "idempotency_records";
    private readonly IdempotencyCleanupSettings _settings = settings.Value;

    public async Task<IdempotencyCleanupResult> CleanupExpiredAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var expiresBeforeUtc = utcNow.AddHours(-_settings.ExpirationGraceHours);
        var mode = _settings.DryRun ? "dry_run" : "scrub";

        try
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(_settings.BatchSize, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(_settings.BatchSize, IdempotencyCleanupLimits.MaximumRows);
            var tenantIds = await discovery.GetExpiredTenantIdsAsync(
                expiresBeforeUtc, _settings.BatchSize, cancellationToken);
            var remainingRows = _settings.BatchSize;
            var eligibleCount = 0;
            var scrubbedCount = 0;
            foreach (var tenantId in tenantIds)
            {
                if (remainingRows == 0)
                    break;

                await using var scope = await scopeFactory.CreateAsync(tenantId, cancellationToken);
                var tenantEligibleCount = await scope.CountExpiredAsync(
                    expiresBeforeUtc, remainingRows, cancellationToken);
                eligibleCount += tenantEligibleCount;
                remainingRows -= tenantEligibleCount;
                if (!_settings.DryRun && tenantEligibleCount != 0)
                    scrubbedCount += await scope.ScrubExpiredAsync(
                        expiresBeforeUtc, tenantEligibleCount, cancellationToken);
            }

            if (_settings.DryRun)
            {
                metrics.RecordIdempotencyCleanupRun(mode, "succeeded");
                metrics.RecordIdempotencyCleanupRows(eligibleCount, mode, "eligible");
                logger.LogInformation(
                    "Idempotency cleanup dry run found {EligibleCount} expired rows before {ExpiresBeforeUtc} in {TableFamily}.",
                    eligibleCount,
                    expiresBeforeUtc,
                    TableFamily);

                return new IdempotencyCleanupResult(expiresBeforeUtc, eligibleCount, 0, DryRun: true);
            }

            metrics.RecordIdempotencyCleanupRun(mode, "succeeded");
            metrics.RecordIdempotencyCleanupRows(scrubbedCount, mode, "scrubbed");
            logger.LogInformation(
                "Idempotency cleanup scrubbed {ScrubbedCount} expired replay payloads before {ExpiresBeforeUtc} in {TableFamily}; operation identities remain fenced.",
                scrubbedCount,
                expiresBeforeUtc,
                TableFamily);

            return new IdempotencyCleanupResult(expiresBeforeUtc, eligibleCount, scrubbedCount, DryRun: false);
        }
        catch (Exception)
        {
            metrics.RecordIdempotencyCleanupRun(mode, "failed");
            throw;
        }
    }
}
