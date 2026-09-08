using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Ai;

namespace Explore.Blazor.Client.Services.Ai;

public sealed class AiAssistantClientService(
    IAiAssistantClient apiClient,
    ILogger<AiAssistantClientService> logger) : IAiAssistantClientService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<HalResourceOfAiAssistantBootstrapDto?> GetBootstrapAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await apiClient.GetAiAssistantBootstrapAsync(cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to load AI assistant bootstrap.");
            return null;
        }
    }

    public async Task<HalCollectionResourceOfAiConversationSummaryDto?> GetConversationCollectionAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await apiClient.GetAiConversationsAsync(limit, cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to load AI assistant conversations.");
            return null;
        }
    }

    public async Task<IReadOnlyList<HalResourceOfAiConversationSummaryDto>> GetConversationsAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var response = await GetConversationCollectionAsync(limit, cancellationToken);
        return response?._embedded?.Items?.ToList() ?? [];
    }

    public async Task<HalResourceOfAiConversationDto?> GetConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await apiClient.GetAiConversationAsync(conversationId, cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to load AI assistant conversation {ConversationId}.", conversationId);
            return null;
        }
    }

    public async Task<AiAssistantCommandResult> CreateConversationAsync(
        CreateAiConversationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await apiClient.CreateAiConversationAsync(request, cancellationToken: cancellationToken);
            return AiAssistantCommandResult.FromResponse(response);
        }
        catch (ApiException<ProblemDetails> ex)
        {
            logger.LogWarning(ex, "Failed to create AI assistant conversation.");
            return AiAssistantCommandResult.Failure(
                FailureCodeFor(ex),
                ex.Result?.Detail ?? ex.Result?.Title ?? "The AI assistant conversation could not be created.");
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to create AI assistant conversation.");
            return AiAssistantCommandResult.Failure(
                FailureCodeFor(ex),
                "The AI assistant conversation could not be created.");
        }
    }

    public async Task<AiAssistantCommandResult> SendMessageAsync(
        Guid conversationId,
        string content,
        string? modelId = null,
        string? idempotencyKey = null,
        string? mode = null,
        Guid? actorId = null,
        IReadOnlyList<AiMessageImageInputDto>? images = null,
        IReadOnlyList<AiSelectedReferenceDto>? references = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new SendAiMessageRequestDto
            {
                Content = content,
                IdempotencyKey = idempotencyKey,
                ActorId = actorId,
                ModelId = modelId,
                Mode = mode,
                Images = images?.ToList() ?? [],
                References = references?.ToList() ?? []
            };

            var response = await apiClient.SendAiMessageAsync(
                conversationId,
                request,
                idempotencyKey,
                cancellationToken: cancellationToken);

            return AiAssistantCommandResult.FromResponse(response);
        }
        catch (ApiException<ProblemDetails> ex)
        {
            logger.LogWarning(ex, "Failed to send AI assistant message for conversation {ConversationId}.", conversationId);
            return FailureFromProblem(ex, "The AI assistant message could not be sent.");
        }
        catch (ApiException ex)
        {
            if (TryMapLegacySuccess(ex, out var legacyResult))
            {
                return legacyResult;
            }

            logger.LogWarning(ex, "Failed to send AI assistant message for conversation {ConversationId}.", conversationId);
            return AiAssistantCommandResult.Failure(FailureCodeFor(ex), "The AI assistant message could not be sent.");
        }
    }

    public async Task<AiRunStatusResult> GetRunStatusAsync(
        Guid conversationId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await apiClient.GetAiRunStatusAsync(conversationId, runId, cancellationToken: cancellationToken);
            return AiRunStatusResult.Ok(result);
        }
        catch (ApiException ex) when (ex.StatusCode == 401)
        {
            logger.LogWarning(ex, "Unauthorized while loading AI run {RunId} for conversation {ConversationId}.", runId, conversationId);
            return AiRunStatusResult.Unauthorized();
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to load AI run {RunId} for conversation {ConversationId}.", runId, conversationId);
            return AiRunStatusResult.NotFound();
        }
    }

    public async Task<AiAssistantCommandResult> CancelRunAsync(
        Guid conversationId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await apiClient.CancelAiRunAsync(
                conversationId,
                runId,
                cancellationToken: cancellationToken);

            return AiAssistantCommandResult.FromResponse(response);
        }
        catch (ApiException<ProblemDetails> ex)
        {
            logger.LogWarning(ex, "Failed to cancel AI run {RunId} for conversation {ConversationId}.", runId, conversationId);
            return FailureFromProblem(ex, "The AI assistant run could not be cancelled.");
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to cancel AI run {RunId} for conversation {ConversationId}.", runId, conversationId);
            return AiAssistantCommandResult.Failure("api_error", "The AI assistant run could not be cancelled.");
        }
    }

    public async Task<IReadOnlyList<HalResourceOfAiReferenceSearchResultDto>> SearchReferencesAsync(
        string searchTerm,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await apiClient.SearchAiReferencesAsync(searchTerm, limit, cancellationToken: cancellationToken);
            return response?._embedded?.Items?.ToList() ?? [];
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to search AI assistant references.");
            return [];
        }
    }

    public async Task<AiAssistantCommandResult> ConfirmProposedActionAsync(
        Guid conversationId,
        Guid proposedActionId,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await apiClient.ConfirmAiProposedActionAsync(
                conversationId,
                proposedActionId,
                idempotencyKey,
                cancellationToken: cancellationToken);

            return AiAssistantCommandResult.FromResponse(response);
        }
        catch (ApiException<ProblemDetails> ex)
        {
            logger.LogWarning(
                ex,
                "Failed to confirm AI proposed action {ProposedActionId} for conversation {ConversationId}.",
                proposedActionId,
                conversationId);

            return FailureFromProblem(ex, "The AI proposed action could not be confirmed.");
        }
        catch (ApiException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to confirm AI proposed action {ProposedActionId} for conversation {ConversationId}.",
                proposedActionId,
                conversationId);

            return AiAssistantCommandResult.Failure("api_error", "The AI proposed action could not be confirmed.");
        }
    }

    public async Task<AiAssistantCommandResult> RejectProposedActionAsync(
        Guid conversationId,
        Guid proposedActionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await apiClient.RejectAiProposedActionAsync(
                conversationId,
                proposedActionId,
                cancellationToken: cancellationToken);

            return AiAssistantCommandResult.FromResponse(response);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to reject AI proposed action {ProposedActionId} for conversation {ConversationId}.",
                proposedActionId,
                conversationId);

            return AiAssistantCommandResult.Failure("api_error", "The AI proposed action could not be rejected.");
        }
    }

    private static AiAssistantCommandResult FailureFromProblem(
        ApiException<ProblemDetails> ex,
        string fallbackMessage)
    {
        var message = FirstNonEmpty(ex.Result?.Detail, ex.Result?.Title, fallbackMessage);
        var failureCode = FirstNonEmpty(TryGetProblemCode(ex.Result), TryGetProblemCode(ex.Response), FailureCodeFor(ex));
        return AiAssistantCommandResult.Failure(failureCode, message);
    }

    private static bool TryMapLegacySuccess(ApiException ex, out AiAssistantCommandResult result)
    {
        result = AiAssistantCommandResult.Failure("api_error", "The AI assistant message could not be sent.");

        if (ex.StatusCode != 200 || string.IsNullOrWhiteSpace(ex.Response))
        {
            return false;
        }

        try
        {
            var response = JsonSerializer.Deserialize<BaseCommandResponseOfGuid>(ex.Response, JsonOptions);
            if (response?.Success != true)
            {
                return false;
            }

            result = AiAssistantCommandResult.FromResponse(response);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? TryGetProblemCode(ProblemDetails? problemDetails)
    {
        if (problemDetails?.AdditionalProperties.TryGetValue("code", out var value) != true)
        {
            return null;
        }

        return value switch
        {
            string code when !string.IsNullOrWhiteSpace(code) => code.Trim(),
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString()?.Trim(),
            _ => null
        };
    }

    private static string? TryGetProblemCode(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(response);
            if (document.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String)
            {
                return code.GetString()?.Trim();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string FailureCodeFor(ApiException ex) => ex.StatusCode switch
    {
        401 => "unauthorized",
        403 => "forbidden",
        409 => "conflict",
        _ => "api_error"
    };
}
