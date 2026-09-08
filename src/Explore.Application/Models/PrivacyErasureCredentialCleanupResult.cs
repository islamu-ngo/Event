namespace Explore.Application.Models;

public sealed record PrivacyErasureCredentialCleanupResult(
    int ReceiptHashesEligible,
    int ReceiptHashesCleared,
    int ProviderLocatorsEligible,
    int ProviderLocatorsCleared,
    bool DryRun);
