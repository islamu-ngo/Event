using Explore.Application.DTOs.ExternalApiKey;

namespace Explore.Application.Responses;

/// <summary>
/// Separates native failure metadata from the success-only credential disclosure payload.
/// </summary>
public sealed record CreateExternalApiKeyCommandResponse : BaseCommandResponse<Guid>
{
    /// <summary>Enforces payload presence and aggregate identity for successful native outcomes.</summary>
    private CreateExternalApiKeyCommandResponse(
        BaseCommandResponse<Guid> state, ExternalApiKeyIssuanceDto? issue) : base(state, true)
    {
        if (state.IsSuccess != (issue is not null) || issue is not null && state.Id != issue.Id)
            throw new ArgumentException("The native outcome and issuance payload must agree.");
        Issue = issue;
    }

    /// <summary>Contains a validated success payload, or null for every native failure outcome.</summary>
    public ExternalApiKeyIssuanceDto? Issue { get; }

    /// <summary>Builds a native success with the one-time disclosure payload.</summary>
    public static CreateExternalApiKeyCommandResponse Issued(
        Guid id,
        string? message,
        string? apiKey,
        string? keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        return new(BaseCommandResponse.Success(id, message),
            ExternalApiKeyIssuanceDto.Issued(id, keyId, apiKey));
    }

    /// <summary>Builds metadata recovery without inventing a new credential or operation.</summary>
    public static CreateExternalApiKeyCommandResponse PreviouslyIssued(Guid id, string keyId) =>
        new(BaseCommandResponse.Success(id), ExternalApiKeyIssuanceDto.PreviouslyIssued(id, keyId));

    /// <summary>Preserves native failure metadata while excluding all credential payload.</summary>
    public static CreateExternalApiKeyCommandResponse Failure(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null);

    /// <summary>Prevents diagnostic rendering from traversing the secret-bearing payload.</summary>
    public override string ToString() => nameof(CreateExternalApiKeyCommandResponse);
}
