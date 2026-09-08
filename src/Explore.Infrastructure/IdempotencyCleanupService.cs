using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Application.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure;

public sealed class IdempotencyCleanupService(
    IIdempotencyRepository repository,
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
        var mode = _settings.DryRun ? "dry_run" : "delete";

        try
        {
            var eligibleCount = await repository.CountExpiredAsync(
                expiresBeforeUtc,
                _settings.BatchSize,
                cancellationToken);

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

            var deletedCount = eligibleCount == 0
                ? 0
                : await repository.DeleteExpiredAsync(expiresBeforeUtc, _settings.BatchSize, cancellationToken);

            metrics.RecordIdempotencyCleanupRun(mode, "succeeded");
            metrics.RecordIdempotencyCleanupRows(deletedCount, mode, "deleted");
            logger.LogInformation(
                "Idempotency cleanup deleted {DeletedCount} expired rows before {ExpiresBeforeUtc} from {TableFamily}.",
                deletedCount,
                expiresBeforeUtc,
                TableFamily);

            return new IdempotencyCleanupResult(expiresBeforeUtc, eligibleCount, deletedCount, DryRun: false);
        }
        catch (Exception)
        {
            metrics.RecordIdempotencyCleanupRun(mode, "failed");
            throw;
        }
    }
}
