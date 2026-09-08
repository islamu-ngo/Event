namespace Explore.Application.DTOs.ExternalApiKey;

public sealed record CreateExternalApiKeyDto
{
    private IReadOnlyList<string> _scopes = Array.AsReadOnly(Array.Empty<string>());

    public required string Name { get; init; }
    public string? Description { get; init; }
    public int ExternalApiKeyOwnerTypeId { get; init; } = 1;
    public Guid? OrganizationId { get; init; }
    public Guid? GroupId { get; init; }
    public IReadOnlyList<string> Scopes
    {
        get => _scopes;
        init => _scopes = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
    public DateTime? ExpiresAt { get; init; }
    public int? CreditPeriodId { get; init; }
    public int? CreditLimit { get; init; }
    public int? MaxRolloverCredits { get; init; }
}
