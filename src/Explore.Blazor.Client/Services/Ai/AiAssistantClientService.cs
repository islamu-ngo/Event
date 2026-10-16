using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Ai;

namespace Explore.Blazor.Client.Services.Ai;

public sealed class AiAssistantClientService(
    IAiAssistantClient apiClient,
    ILogger<AiAssistantClientService> logger) : IAiAssistantClientService
{
    private const int MaximumFallbackResponseCharacters = 64 * 1024;
    private const int MaximumProblemCodeCharacters = 128;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };

    public async Task<HalResourceOfAiAssistantBootstrapDto?> GetBootstrapAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await apiClient.GetAiAssistantBootstrapAsync(cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            logger.LogWarning("AI bootstrap failed. FailureCategory={FailureCategory} StatusCode={StatusCode}",
                "bootstrap_failed", ex.StatusCode);
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
            logger.LogWarning("AI conversation list failed. FailureCategory={FailureCategory} StatusCode={StatusCode}",
                "conversation_list_failed", ex.StatusCode);
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
            logger.LogWarning("AI conversation read failed. FailureCategory={FailureCategory} StatusCode={StatusCode}",
                "conversation_read_failed", ex.StatusCode);
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
            logger.LogWarning("AI conversation creation failed. FailureCategory={FailureCategory} StatusCode={StatusCode}",
                "conversation_create_failed", ex.StatusCode);
            return AiAssistantCommandResult.Failure(
                FailureCodeFor(ex),
                ex.Result?.Detail ?? ex.Result?.Title ?? "The AI assistant conversation could not be created.");
        }
        catch (ApiException ex)
        {
            logger.LogWarning("AI conversation creation failed. FailureCategory={FailureCategory} StatusCode={StatusCode}",
                "conversation_create_failed", ex.StatusCode);
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
            logger.LogWarning("AI message send failed. FailureCategory={FailureCategory} StatusCode={StatusCode}",
                "message_send_failed", ex.StatusCode);
            return FailureFromProblem(ex, "The AI assistant message could not be sent.");
        }
        catch (ApiException ex)
        {
            if (TryMapLegacySuccess(ex, out var legacyResult))
            {
                return legacyResult;
            }

            logger.LogWarning("AI message send failed. FailureCategory={FailureCategory} StatusCode={StatusCode}",
                "message_send_failed", ex.StatusCode);
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
            logger.LogWarning("AI run read denied. FailureCategory={FailureCategory} StatusCode={StatusCode}",
                "run_read_denied", ex.StatusCode);
            return AiRunStatusResult.Unauthorized();
        }
        catch (ApiException ex)
        {
            logger.LogWarning("AI run read failed. FailureCategory={FailureCategory} StatusCode={StatusCode}",
                "run_read_failed", ex.StatusCode);
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
            logger.LogWarning("AI run cancellation failed. FailureCategory={FailureCategory} StatusCode={StatusCode}",
                "run_cancel_failed", ex.StatusCode);
            return FailureFromProblem(ex, "The AI assistant run could not be cancelled.");
        }
        catch (ApiException ex)
        {
            logger.LogWarning("AI run cancellation failed. FailureCategory={FailureCategory} StatusCode={StatusCode}",
                "run_cancel_failed", ex.StatusCode);
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
            logger.LogWarning("AI reference search failed. FailureCategory={FailureCategory} StatusCode={StatusCode}",
                "reference_search_failed", ex.StatusCode);
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
                "AI confirmation failed. FailureCategory={FailureCategory} StatusCode={StatusCode}",
                "confirmation_failed", ex.StatusCode);

            return FailureFromProblem(ex, "The AI proposed action could not be confirmed.");
        }
        catch (ApiException ex)
        {
            logger.LogWarning(
                "AI confirmation failed. FailureCategory={FailureCategory} StatusCode={StatusCode}",
                "confirmation_failed", ex.StatusCode);

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
                "AI rejection failed. FailureCategory={FailureCategory} StatusCode={StatusCode}",
                "rejection_failed", ex.StatusCode);

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

        if (ex.StatusCode != 200 || string.IsNullOrWhiteSpace(ex.Response)
            || ex.Response.Length > MaximumFallbackResponseCharacters)
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
            string code when !string.IsNullOrWhiteSpace(code)
                && code.Length <= MaximumProblemCodeCharacters => code.Trim(),
            JsonElement { ValueKind: JsonValueKind.String } json
                when json.GetString() is { Length: <= MaximumProblemCodeCharacters } code => code.Trim(),
            _ => null
        };
    }

    private static string? TryGetProblemCode(string? response)
    {
        if (string.IsNullOrWhiteSpace(response) || response.Length > MaximumFallbackResponseCharacters)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(response, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            JsonElement? code = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name != "code")
                    continue;
                if (code is not null)
                    return null;
                code = property.Value;
            }
            if (code is { ValueKind: JsonValueKind.String } value
                && value.GetString() is { Length: <= MaximumProblemCodeCharacters } text)
                return text.Trim();
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
