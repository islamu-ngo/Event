namespace Explore.Application.DTOs.ExternalApiKey;

public sealed record ExternalApiKeyUsageReportDto
{
    public Guid ApiKeyId { get; init; }
    public required string ApiKeyName { get; init; }
    public Guid? TenantId { get; init; }
    public int ExternalApiKeyOwnerTypeId { get; init; }
    public required string ExternalApiKeyOwnerTypeCode { get; init; }
    public required string ExternalApiKeyOwnerTypeName { get; init; }
    public Guid OwnerId { get; init; }
    public long TotalRequestCount { get; init; }
    public int TotalCreditsUsed { get; init; }
    public int CreditLimit { get; init; }
}
