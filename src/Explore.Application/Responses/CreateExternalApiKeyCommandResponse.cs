namespace Explore.Application.Responses;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ExternalApiKeyDisclosureStatus>))]
public enum ExternalApiKeyDisclosureStatus
{
    Issued,
    PreviouslyIssued
}

public sealed record CreateExternalApiKeyCommandResponse : BaseCommandResponse<Guid>
{
    private CreateExternalApiKeyCommandResponse(
        BaseCommandResponse<Guid> state,
        string? apiKey,
        string? keyId,
        ExternalApiKeyDisclosureStatus? disclosureStatus) : base(state, true)
    {
        if (state.IsSuccess)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
            if (disclosureStatus == ExternalApiKeyDisclosureStatus.Issued)
                ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
            else if (disclosureStatus != ExternalApiKeyDisclosureStatus.PreviouslyIssued || apiKey is not null)
                throw new ArgumentException("Successful issuance requires a valid disclosure outcome.");
        }
        else if (apiKey is not null || keyId is not null || disclosureStatus is not null)
        {
            throw new ArgumentException("Failed issuance cannot contain credential or disclosure state.");
        }

        ApiKey = apiKey;
        KeyId = keyId;
        DisclosureStatus = disclosureStatus;
    }

    [System.Text.Json.Serialization.JsonConstructor]
    internal CreateExternalApiKeyCommandResponse(
        Guid id,
        bool isSuccess,
        string? message,
        IReadOnlyList<string>? errors,
        string? failureCode,
        QuotaExceededDetails? quotaExceeded,
        string? apiKey,
        string? keyId,
        ExternalApiKeyDisclosureStatus? disclosureStatus)
        : this(BaseCommandResponse.Restore(id, isSuccess, message, errors, failureCode, quotaExceeded), apiKey, keyId, disclosureStatus)
    {
    }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public string? ApiKey { get; }
    public string? KeyId { get; }
    public ExternalApiKeyDisclosureStatus? DisclosureStatus { get; }

    public static CreateExternalApiKeyCommandResponse Issued(
        Guid id,
        string? message,
        string? apiKey,
        string? keyId) =>
        new(BaseCommandResponse.Success(id, message), apiKey, keyId, ExternalApiKeyDisclosureStatus.Issued);

    public static CreateExternalApiKeyCommandResponse PreviouslyIssued(Guid id, string keyId) =>
        new(BaseCommandResponse.Success(id), null, keyId, ExternalApiKeyDisclosureStatus.PreviouslyIssued);

    public static CreateExternalApiKeyCommandResponse Failure(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null, null, null);
}
