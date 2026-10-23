using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Ai;
using Explore.Application.DTOs.Ai.Validators;
using Explore.Application.Features.AiAssistant.Actors;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Application.Contracts.Operations;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Constants;

namespace Explore.Application.Features.AiAssistant.Handlers.Commands;

public sealed class SendAiMessageCommandHandler : ICommandHandler<SendAiMessageCommand, BaseCommandResponse<Guid>>
{
    private const string IdempotencyMethod = "AI_SEND";
    private const string JsonContentType = "application/json";
    private const string TextContentType = "text/plain";
    private const string UserDailyQuotaScope = "user_daily";
    private const string TenantDailyQuotaScope = "tenant_daily";
    private const string UserConcurrentQuotaScope = "user_concurrent";
    private const string StaleRunFailureCode = "stale_ai_run_released";
    private const string StaleRunFailureMessage = "AI run was released after it stopped reporting progress.";
    private readonly IAiConversationRepository _conversationRepository;
    private readonly IIdempotencyRepository _idempotencyRepository;
    private readonly IHierarchicalSettingsResolver _settingsResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAiModelCatalog _modelCatalog;
    private readonly IAiAssistantActorContextService _actorContextService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAiProviderCapabilityResolver _capabilityResolver;

    public SendAiMessageCommandHandler(
        IAiConversationRepository conversationRepository,
        IIdempotencyRepository idempotencyRepository,
        IHierarchicalSettingsResolver settingsResolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IAiModelCatalog modelCatalog,
        IAiAssistantActorContextService actorContextService,
        IUnitOfWork unitOfWork,
        IAiProviderCapabilityResolver capabilityResolver)
    {
        _conversationRepository = conversationRepository;
        _idempotencyRepository = idempotencyRepository;
        _settingsResolver = settingsResolver;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _modelCatalog = modelCatalog;
        _actorContextService = actorContextService;
        _unitOfWork = unitOfWork;
        _capabilityResolver = capabilityResolver;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        SendAiMessageCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new SendAiMessageRequestDtoValidator();
        var validationResult = await validator.ValidateAsync(request.Message, cancellationToken);

        if (!validationResult.IsValid)
        {
            return Failure(
                "AI message send failed.",
                validationResult.Errors.Select(error => error.ErrorMessage),
                "validation_failed");
        }

        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId is not { } userId)
        {
            return Failure("AI messages require an authenticated user.", ["User is not authenticated."], "unauthenticated");
        }

        if (!AiMessageImageAttachmentSerializer.TrySerializeValidated(
                request.Message.Images,
                out string? imageAttachmentsJson,
                out string? imageValidationError))
        {
            return Failure(
                "AI message send failed.",
                [imageValidationError ?? "AI message image validation failed."],
                "invalid_ai_image_attachment");
        }

        var tenantId = _tenantContext.TenantId;
        var content = request.Message.Content?.Trim() ?? string.Empty;
        var idempotencyKey = request.Message.IdempotencyKey.Trim();
        var interactionMode = AiAssistantInteractionModes.Normalize(request.Message.Mode);
        var requestTarget = $"ai/conversations/{request.ConversationId:N}/messages";
        var principalFingerprint = ComputePrincipalFingerprint(userId);

        var settings = await _settingsResolver.ResolveGroupAsync<AiAssistantSettingGroup>(
            new SettingContext(TenantId: tenantId), cancellationToken);
        var disabledReason = await AiAssistantAvailability.ResolveDisabledReasonAsync(
            settings, _capabilityResolver, cancellationToken);

        if (disabledReason is not null)
        {
            return Failure(
                "AI assistant is not available for this tenant.",
                [$"AI assistant is unavailable: {disabledReason}."],
                disabledReason);
        }

        var requestedModelId = request.Message.ModelId?.Trim();
        if (!string.IsNullOrWhiteSpace(requestedModelId) &&
            !AiAssistantAvailability.IsModelAllowed(settings, requestedModelId))
        {
            return Failure(
                "AI model is not allowed for this tenant.",
                [AiAssistantAvailability.ModelNotAllowedFailureMessage],
                AiAssistantAvailability.ModelNotAllowedFailureCode);
        }

        var modelId = string.IsNullOrWhiteSpace(requestedModelId)
            ? AiAssistantAvailability.ResolveModelId(settings)
            : requestedModelId;
        var provider = AiAssistantAvailability.NormalizeProvider(settings.Provider);
        if (provider == AiProviderDefaults.ProviderFake)
        {
            var availableModels = await _modelCatalog.ListAvailableModelsAsync(cancellationToken);

            if (!availableModels.Any(model => string.Equals(model.Id, modelId, StringComparison.Ordinal)))
            {
                return Failure(
                    "AI provider is not ready for this tenant model.",
                    ["Configured AI model is not available from the runtime provider."],
                    "provider_not_ready");
            }
        }

        var candidate = await _conversationRepository.GetByIdWithDetailsAsync(request.ConversationId, cancellationToken);

        if (candidate is null || candidate.UserId != userId)
        {
            return Failure("AI conversation was not found.", ["Conversation was not found."], "conversation_not_found");
        }

        var actorResolution = await _actorContextService.ResolveAuthorizedActorAsync(
            tenantId,
            userId,
            request.Message.ActorId ?? candidate.ActorId,
            cancellationToken);
        if (!actorResolution.Succeeded)
        {
            return Failure(
                "AI message acting actor is not authorized.",
                [actorResolution.FailureMessage ?? "AI acting actor is not authorized."],
                actorResolution.FailureCode ?? "actor_context_not_authorized");
        }

        var selectedReferences = NormalizeReferences(request.Message.References, settings.SelectedReferenceLimit);
        var selectedReferencesJson = SerializeReferencesForHash(selectedReferences);
        var requestBodyHash = ComputeBodyHash(
            request.ConversationId,
            content,
            imageAttachmentsJson,
            selectedReferencesJson,
            modelId,
            interactionMode,
            actorResolution.ActorId);
        return await _unitOfWork.ExecuteSerializableAsync(async token =>
        {
            var conversation = await _conversationRepository.GetByIdForUpdateAsync(request.ConversationId, actorResolution.ActorId, token);
            if (conversation is null || conversation.UserId != userId || conversation.ActorId != candidate.ActorId)
                return Failure("AI conversation authority changed before message commit.", ["No message was queued."], "conversation_authority_changed");
            var replay = await TryReplayIdempotencyAsync(
                idempotencyKey, tenantId, userId, requestTarget, requestBodyHash, principalFingerprint, token);
            if (replay is not null)
                return replay;

            var utcNow = DateTime.UtcNow;
            await _conversationRepository.ReleaseStaleRunningConversationsForUserAsync(
                userId, ResolveStaleRunCutoffUtc(utcNow, settings), StaleRunFailureCode,
                StaleRunFailureMessage, utcNow, token);
            conversation = await _conversationRepository.GetByIdForUpdateAsync(request.ConversationId, actorResolution.ActorId, token);
            if (conversation is null || conversation.UserId != userId)
                throw new InvalidOperationException("AI message authority changed during stale-run recovery.");
            bool retryableBlock = conversation.Status == AiConversationStatus.Blocked
                && IsRetryableProviderBlock(conversation.BlockedReason);
            if (conversation.Status != AiConversationStatus.Active && !retryableBlock)
            {
                return Failure("AI conversation is not ready for a new message.",
                    [$"Conversation status is {conversation.Status}."], "conversation_not_active");
            }
            var todayUtc = utcNow.Date;
            var messageCount = await _conversationRepository.CountUserMessagesSinceAsync(userId, todayUtc, token);
            if (messageCount >= settings.DailyMessageLimit)
            {
                return BaseCommandResponse.Quota<Guid>("AI daily message limit exceeded.",
                    new QuotaExceededDetails(GovernanceSettingKeys.AiAssistant.DailyMessageLimit,
                        settings.DailyMessageLimit, messageCount, messageCount + 1, UserDailyQuotaScope, tenantId),
                    "Daily AI message quota exceeded.");
            }
            var tenantMessageCount = await _conversationRepository.CountTenantMessagesSinceAsync(todayUtc, token);
            if (tenantMessageCount >= settings.DailyTenantMessageLimit)
            {
                return BaseCommandResponse.Quota<Guid>("AI tenant daily message limit exceeded.",
                    new QuotaExceededDetails(GovernanceSettingKeys.AiAssistant.DailyTenantMessageLimit,
                        settings.DailyTenantMessageLimit, tenantMessageCount, tenantMessageCount + 1, TenantDailyQuotaScope, tenantId),
                    "Daily tenant AI message quota exceeded.");
            }
            var runningConversationCount = await _conversationRepository.CountRunningConversationsForUserAsync(userId, token);
            if (runningConversationCount >= settings.ConcurrentRunLimit)
            {
                return BaseCommandResponse.Quota<Guid>("AI concurrent run limit exceeded.",
                    new QuotaExceededDetails(GovernanceSettingKeys.AiAssistant.ConcurrentRunLimit,
                        settings.ConcurrentRunLimit, runningConversationCount, runningConversationCount + 1, UserConcurrentQuotaScope, tenantId),
                    "Concurrent AI run quota exceeded.");
            }
            var claim = await _idempotencyRepository.TryClaimAsync(CreateReceiptProposal(
                idempotencyKey, tenantId, userId, requestTarget, requestBodyHash, principalFingerprint, utcNow), token);
            using var receiptOwner = claim.Owner;
            if (receiptOwner is null)
                return await TryReplayIdempotencyAsync(idempotencyKey, tenantId, userId,
                    requestTarget, requestBodyHash, principalFingerprint, token)
                    ?? Failure("AI receipt acquisition changed.", ["No message was queued."], "idempotency_key_conflict");
            if (retryableBlock) conversation.Activate(utcNow);
            conversation.ActorId = actorResolution.ActorId;
            foreach (var selectedReference in selectedReferences)
            {
                if (!conversation.References.Any(existing => existing.Kind == selectedReference.Kind
                    && existing.ReferenceId == selectedReference.ReferenceId))
                {
                    conversation.AddReference(selectedReference.Kind, selectedReference.ReferenceId,
                        selectedReference.DisplayName, selectedReference.Summary, userId, utcNow);
                }
            }
            conversation.AddMessage(AiMessageRole.User, content, userId, utcNow, imageAttachmentsJson);
            var run = conversation.QueueRun(provider, modelId, utcNow);
            await _conversationRepository.Update(conversation);
            if (!await _idempotencyRepository.CompleteAsync(receiptOwner, 202,
                run.Id.ToString("D", CultureInfo.InvariantCulture), TextContentType, token))
                throw new InvalidOperationException("The original AI receipt could not be completed.");
            return BaseCommandResponse.Success(run.Id, "AI message queued.");
        }, cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>?> TryReplayIdempotencyAsync(
        string idempotencyKey,
        Guid tenantId,
        Guid userId,
        string requestTarget,
        string requestBodyHash,
        string principalFingerprint,
        CancellationToken cancellationToken)
    {
        var lookup = await _idempotencyRepository.LookupAsync(new(tenantId, idempotencyKey,
            userId.ToString("N", CultureInfo.InvariantCulture), IdempotencyMethod, requestTarget,
            JsonContentType, requestBodyHash, principalFingerprint), DateTime.UtcNow, cancellationToken);
        if (lookup.Outcome == IdempotencyLookupOutcome.RequestMismatch)
            return Failure("Idempotency key conflicts with a previous AI request.",
                ["Idempotency key was already used for a different request."], "idempotency_key_conflict");
        var existing = lookup.Record;

        if (existing is null)
        {
            return null;
        }

        if (existing.UserId != userId.ToString("N", CultureInfo.InvariantCulture) ||
            existing.RequestMethod != IdempotencyMethod ||
            existing.RequestTarget != requestTarget ||
            existing.RequestBodyHash != requestBodyHash ||
            existing.PrincipalFingerprint != principalFingerprint)
        {
            return Failure(
                "Idempotency key conflicts with a previous AI request.",
                ["Idempotency key was already used for a different request."],
                "idempotency_key_conflict");
        }

        if (existing.StatusCode == 202 && Guid.TryParse(existing.ResponseBody, out var runId))
        {
            return BaseCommandResponse.Success(runId, "AI message send already processed.");
        }

        return Failure(
            "Idempotency record cannot be replayed.",
            ["Stored AI idempotency result was invalid."],
            "idempotency_replay_failed");
    }

    private static IdempotencyRecord CreateReceiptProposal(
        string idempotencyKey,
        Guid tenantId,
        Guid userId,
        string requestTarget,
        string requestBodyHash,
        string principalFingerprint,
        DateTime utcNow) => new()
        {
            Id = Guid.CreateVersion7(),
            Key = idempotencyKey,
            TenantId = tenantId,
            UserId = userId.ToString("N", CultureInfo.InvariantCulture),
            RequestMethod = IdempotencyMethod,
            RequestTarget = requestTarget,
            RequestContentType = JsonContentType,
            RequestBodyHash = requestBodyHash,
            PrincipalFingerprint = principalFingerprint,
            StatusCode = IdempotencyRecord.InProgressStatusCode,
            CreatedAt = utcNow,
            ExpiresAt = utcNow.AddHours(24)
        };

    private static string ComputeBodyHash(
        Guid conversationId,
        string content,
        string? imageAttachmentsJson,
        string? selectedReferencesJson,
        string modelId,
        string mode,
        Guid? actorId)
    {
        var value = $"{conversationId:N}:{modelId}:{mode}:{actorId:N}:{content}:{imageAttachmentsJson}:{selectedReferencesJson}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static IReadOnlyList<(AiReferenceKind Kind, Guid ReferenceId, string DisplayName, string? Summary)> NormalizeReferences(
        IReadOnlyList<AiSelectedReferenceDto>? references,
        int selectedReferenceLimit)
    {
        if (references is null || references.Count == 0 || selectedReferenceLimit <= 0)
        {
            return [];
        }

        var selectedReferences = new List<(AiReferenceKind Kind, Guid ReferenceId, string DisplayName, string? Summary)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (AiSelectedReferenceDto reference in references)
        {
            if (!Enum.TryParse<AiReferenceKind>(reference.Kind?.Trim(), ignoreCase: true, out var kind)
                || reference.ReferenceId == Guid.Empty
                || string.IsNullOrWhiteSpace(reference.DisplayName))
            {
                continue;
            }

            string key = $"{kind}:{reference.ReferenceId:N}";
            if (!seen.Add(key))
            {
                continue;
            }

            selectedReferences.Add((
                kind,
                reference.ReferenceId,
                reference.DisplayName.Trim(),
                string.IsNullOrWhiteSpace(reference.Summary) ? null : reference.Summary.Trim()));

            if (selectedReferences.Count >= selectedReferenceLimit)
            {
                break;
            }
        }

        return selectedReferences;
    }

    private static string? SerializeReferencesForHash(
        IReadOnlyList<(AiReferenceKind Kind, Guid ReferenceId, string DisplayName, string? Summary)> references)
    {
        if (references.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(references.Select(reference => new
        {
            kind = reference.Kind.ToString(),
            referenceId = reference.ReferenceId,
            displayName = reference.DisplayName,
            summary = reference.Summary
        }), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static string ComputePrincipalFingerprint(Guid userId)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId.ToString("N", CultureInfo.InvariantCulture))))
            .ToLowerInvariant();

    private static DateTime ResolveStaleRunCutoffUtc(DateTime utcNow, AiAssistantSettingGroup settings)
    {
        var timeoutSeconds = AiAssistantAvailability.ResolveTimeoutSeconds(settings);
        return utcNow.AddSeconds(-(timeoutSeconds + 5));
    }

    private static bool IsRetryableProviderBlock(string? blockedReason)
    {
        if (string.IsNullOrWhiteSpace(blockedReason))
        {
            return false;
        }

        var reason = blockedReason.Trim();
        return reason.StartsWith("provider_", StringComparison.OrdinalIgnoreCase)
            || reason.StartsWith("http_", StringComparison.OrdinalIgnoreCase)
            || reason.Equals("invalid_response", StringComparison.OrdinalIgnoreCase)
            || reason.Equals("invalid_tool_arguments", StringComparison.OrdinalIgnoreCase)
            || reason.Equals("missing_tool_argument", StringComparison.OrdinalIgnoreCase)
            || reason.Equals("content_filtered", StringComparison.OrdinalIgnoreCase);
    }

    private static BaseCommandResponse<Guid> Failure(
        string message,
        IEnumerable<string> errors,
        string? failureCode = null,
        Guid id = default) => failureCode is null
            ? BaseCommandResponse.Validation<Guid>(errors, message, id)
            : BaseCommandResponse.Failure<Guid>(failureCode, message, errors, id);

}
