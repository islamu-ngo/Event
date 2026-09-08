namespace Explore.Application.DTOs.PrivacyErasure;

public sealed record PrivacyErasureStatusDto(
    string Status,
    int ProviderWorkCount,
    int CompletedProviderWorkCount,
    DateTime ReceiptExpiresAtUtc,
    DateTime? LocalSettledAtUtc,
    DateTime? CompletedAtUtc);
