using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure;

public sealed class EmailDispatchRetentionCleanupService(
    IEmailDispatchOutboxRepository repository,
    IUnitOfWork unitOfWork,
    IOptions<EmailDispatchRetentionSettings> settings,
    ILogger<EmailDispatchRetentionCleanupService> logger) : IEmailDispatchRetentionCleanupService
{
    private readonly EmailDispatchRetentionSettings _settings = settings.Value;

    public async Task<EmailDispatchRetentionCleanupResult> CleanupAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var cutoffUtc = utcNow.AddDays(-_settings.RetentionDays);
        var tenantIds = await repository.GetRetentionTenantIds(
            cutoffUtc,
            _settings.MaxTenantsPerPass,
            cancellationToken);
        var eligibleCount = 0;
        var redactedCount = 0;
        var succeededTenantCount = 0;
        var failedTenantCount = 0;

        foreach (var tenantId in tenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var tenantEligibleCount = await repository.CountRetentionRedactionEligible(
                    tenantId,
                    cutoffUtc,
                    _settings.BatchSize,
                    cancellationToken);
                eligibleCount += tenantEligibleCount;

                if (!_settings.DryRun && tenantEligibleCount > 0)
                {
                    redactedCount += await unitOfWork.ExecuteInTransactionAsync(
                        ct => repository.RedactRetentionEligible(
                            tenantId,
                            cutoffUtc,
                            utcNow,
                            _settings.BatchSize,
                            ct),
                        cancellationToken);
                }

                succeededTenantCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failedTenantCount++;
                logger.LogWarning("Email dispatch retention cleanup failed for one tenant: {ExceptionType}", exception.GetType().Name);
            }
        }

        logger.LogInformation(
            "Email dispatch retention processed {TenantCount} tenants in {Mode} mode. Eligible={EligibleCount}, Redacted={RedactedCount}, FailedTenants={FailedTenantCount}, Cutoff={CutoffUtc}.",
            tenantIds.Count,
            _settings.DryRun ? "dry_run" : "redact",
            eligibleCount,
            redactedCount,
            failedTenantCount,
            cutoffUtc);
        return new EmailDispatchRetentionCleanupResult(
            cutoffUtc,
            tenantIds.Count,
            succeededTenantCount,
            failedTenantCount,
            eligibleCount,
            redactedCount,
            _settings.DryRun);
    }
}
