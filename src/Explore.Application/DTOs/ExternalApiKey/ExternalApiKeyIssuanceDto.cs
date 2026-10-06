using System.Text.Json.Serialization;
using Explore.Application.Models;

namespace Explore.Application.DTOs.ExternalApiKey;

/// <summary>
/// Carries only a validated successful issuance or metadata recovery; failures use ProblemDetails.
/// </summary>
public sealed record ExternalApiKeyIssuanceDto
{
    /// <summary>
    /// Restores the same valid-state constraints used by the named issuance factories.
    /// </summary>
    [JsonConstructor]
    public ExternalApiKeyIssuanceDto(
        Guid id, string keyId, ExternalApiKeyDisclosureStatus disclosureStatus, string? apiKey)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("The issued key identity must be nonempty.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        if (disclosureStatus == ExternalApiKeyDisclosureStatus.Issued)
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        else if (disclosureStatus != ExternalApiKeyDisclosureStatus.PreviouslyIssued || apiKey is not null)
            throw new ArgumentException("A successful issuance requires a valid disclosure outcome.");

        Id = id;
        KeyId = keyId;
        DisclosureStatus = disclosureStatus;
        ApiKey = apiKey;
    }

    /// <summary>Identifies the same aggregate on the initial response and authorized replay.</summary>
    public Guid Id { get; }

    /// <summary>Provides the stable public credential identifier, not its secret segment.</summary>
    public string KeyId { get; }

    /// <summary>Requires an explicit acknowledged outcome; there is no null or failure variant.</summary>
    public ExternalApiKeyDisclosureStatus DisclosureStatus { get; }

    /// <summary>Reveals material only once and preserves explicit JSON null for metadata recovery.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ApiKey { get; }

    /// <summary>Constructs the only success outcome that may contain raw credential material.</summary>
    public static ExternalApiKeyIssuanceDto Issued(Guid id, string keyId, string apiKey) =>
        new(id, keyId, ExternalApiKeyDisclosureStatus.Issued, apiKey);

    /// <summary>Constructs an authorized recovery without retrieving or regenerating credential material.</summary>
    public static ExternalApiKeyIssuanceDto PreviouslyIssued(Guid id, string keyId) =>
        new(id, keyId, ExternalApiKeyDisclosureStatus.PreviouslyIssued, null);

    /// <summary>Prevents record diagnostics from including credential material.</summary>
    public override string ToString() => nameof(ExternalApiKeyIssuanceDto);
}
