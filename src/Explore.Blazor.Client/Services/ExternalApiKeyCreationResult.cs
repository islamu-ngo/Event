using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services;

/// <summary>
/// Keeps validated generated success data separate from locally owned failure guidance.
/// </summary>
public sealed record ExternalApiKeyCreationResult
{
    /// <summary>Restricts construction to validated acknowledgements or fixed local failure guidance.</summary>
    private ExternalApiKeyCreationResult(ExternalApiKeyIssuanceDto? issue, string? errorMessage)
    {
        Issue = issue;
        ErrorMessage = errorMessage;
    }

    /// <summary>Contains only a validated acknowledged issuance or metadata-recovery payload.</summary>
    public ExternalApiKeyIssuanceDto? Issue { get; }

    /// <summary>Contains fixed local guidance rather than a remote response or exception message.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Derives success from payload presence instead of a separately mutable Boolean.</summary>
    public bool IsSuccess => Issue is not null;

    /// <summary>Admits generated data only when its identity and disclosure relationship are valid.</summary>
    public static ExternalApiKeyCreationResult Acknowledged(ExternalApiKeyIssuanceDto issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        bool disclosureIsValid = issue.DisclosureStatus switch
        {
            ExternalApiKeyDisclosureStatus.Issued => !string.IsNullOrWhiteSpace(issue.ApiKey),
            ExternalApiKeyDisclosureStatus.PreviouslyIssued => issue.ApiKey is null,
            _ => false
        };
        if (issue.Id == Guid.Empty || string.IsNullOrWhiteSpace(issue.KeyId) || !disclosureIsValid)
            throw new ArgumentException("The response is not a valid acknowledged issuance payload.", nameof(issue));
        return new(issue, null);
    }

    /// <summary>Creates failure state from a bounded status class without accepting untrusted prose.</summary>
    public static ExternalApiKeyCreationResult Failure(int? statusCode = null) =>
        new(null, statusCode switch
        {
            400 => "The request was rejected. Cancel and correct the key policy.",
            401 => "Sign in again before retrying this operation.",
            403 => "Your current access does not permit this operation. Cancel or restore access before retrying.",
            404 => "The key or owner is unavailable. Cancel and review your keys before issuing a replacement.",
            409 => "This operation conflicts with an earlier request. Cancel and review your keys before starting an operation.",
            _ => "The API key request did not complete. Retry this operation to check its outcome."
        });

    /// <summary>Excludes credential material from handwritten result diagnostics.</summary>
    public override string ToString() => nameof(ExternalApiKeyCreationResult);
}
