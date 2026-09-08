using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure;

public sealed class PrivacyErasureCredentialCleanupService(
    IPrivacyErasureStateRepository stateRepository,
    IPrivacyErasureProviderWorkRepository providerWorkRepository,
    IOptions<PrivacyErasureOptions> options) : IPrivacyErasureCredentialCleanupService
{
    private readonly PrivacyErasureOptions _options = options.Value;

    public async Task<PrivacyErasureCredentialCleanupResult> CleanupAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        if (utcNow == default || utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", nameof(utcNow));
        }

        int receiptHashesEligible = await stateRepository.ClearExpiredReceiptHashesAsync(
            utcNow,
            _options.RetentionCleanupBatchSize,
            _options.RetentionCleanupDryRun,
            cancellationToken);
        int providerLocatorsEligible = await providerWorkRepository.ExpireLocatorsAsync(
            utcNow,
            _options.RetentionCleanupBatchSize,
            _options.RetentionCleanupDryRun,
            cancellationToken);

        return new PrivacyErasureCredentialCleanupResult(
            receiptHashesEligible,
            _options.RetentionCleanupDryRun ? 0 : receiptHashesEligible,
            providerLocatorsEligible,
            _options.RetentionCleanupDryRun ? 0 : providerLocatorsEligible,
            _options.RetentionCleanupDryRun);
    }
}
