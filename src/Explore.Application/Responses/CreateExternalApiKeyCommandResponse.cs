namespace Explore.Application.Responses;

public sealed record CreateExternalApiKeyCommandResponse : BaseCommandResponse<Guid>
{
    private CreateExternalApiKeyCommandResponse(
        BaseCommandResponse<Guid> state,
        string? apiKey,
        string? keyId) : base(state, true)
    {
        ApiKey = apiKey;
        KeyId = keyId;
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
        string? keyId)
        : this(BaseCommandResponse.Restore(id, isSuccess, message, errors, failureCode, quotaExceeded), apiKey, keyId)
    {
    }

    public string? ApiKey { get; }
    public string? KeyId { get; }

    public static CreateExternalApiKeyCommandResponse Success(
        Guid id,
        string? message,
        string? apiKey,
        string? keyId) =>
        new(BaseCommandResponse.Success(id, message), apiKey, keyId);

    public static CreateExternalApiKeyCommandResponse Failure(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null, null);
}
