namespace Explore.Application.DTOs.PrivacyErasure;

public sealed record PrivacyErasureStartDto(
    string Status,
    string? Receipt,
    DateTime ReceiptExpiresAtUtc);
