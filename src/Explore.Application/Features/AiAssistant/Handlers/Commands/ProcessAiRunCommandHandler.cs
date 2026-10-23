using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Ai;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.AiAssistant.Disclosure;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Ai;
using Explore.Domain.Enums;

namespace Explore.Application.Features.AiAssistant.Handlers.Commands;

public sealed class ProcessAiRunCommandHandler : ICommandHandler<ProcessAiRunCommand>
{
    private const string PrivacyErasureFencedFailureCode = "privacy_erasure_fenced";
    private const string PrivacyErasureFencedFailureMessage = "AI assistant processing is unavailable.";

    private readonly IAiConversationRepository _conversationRepository;
    private readonly IHierarchicalSettingsResolver _settingsResolver;
    private readonly IQueryHandler<GetEventDetailsRequest, EventDto?> _eventDetailsHandler;
    private readonly AiPromptContextBuilder _promptContextBuilder;
    private readonly AiProviderResponseResolver _providerResponseResolver;
    private readonly IAiContextGateway _contextGateway;
    private readonly IAiProviderTrustResolver _providerTrustResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAiProviderCapabilityResolver _capabilityResolver;

    public ProcessAiRunCommandHandler(
        IAiConversationRepository conversationRepository,
        IHierarchicalSettingsResolver settingsResolver,
        IAiChatProvider chatProvider,
        IQueryHandler<GetEventDetailsRequest, EventDto?> eventDetailsHandler,
        IAiContextGateway contextGateway,
        IAiProviderTrustResolver providerTrustResolver,
        IUnitOfWork unitOfWork,
        IAiProviderCapabilityResolver capabilityResolver)
    {
        _conversationRepository = conversationRepository;
        _settingsResolver = settingsResolver;
        _eventDetailsHandler = eventDetailsHandler;
        _contextGateway = contextGateway;
        _providerTrustResolver = providerTrustResolver;
        _unitOfWork = unitOfWork;
        _capabilityResolver = capabilityResolver;
        var toolRegistry = AiToolContractRegistry.CreateDefault();
        _promptContextBuilder = new AiPromptContextBuilder(new AiSystemPromptFactory(toolRegistry));
        _providerResponseResolver = new AiProviderResponseResolver(
            chatProvider,
            new AiStructuredActionParser(toolRegistry));
    }

    public async Task ExecuteAsync(ProcessAiRunCommand request, CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.ConversationId == Guid.Empty || request.RunId == Guid.Empty)
        {
            return;
        }

        var settings = await _settingsResolver.ResolveGroupAsync<AiAssistantSettingGroup>(
            new SettingContext(TenantId: request.TenantId),
            cancellationToken);
        var allowToolProposals = AiAssistantInteractionModes.AllowsToolProposals(request.Mode);

        var candidate = await _conversationRepository.GetByIdWithDetailsAsync(request.ConversationId, cancellationToken);
        if (!TryGetQueuedRun(candidate, request.TenantId, request.RunId, out _)) return;
        var disabledReason = await AiAssistantAvailability.ResolveDisabledReasonAsync(
            settings, _capabilityResolver, cancellationToken);
        var conversation = await _unitOfWork.ExecuteSerializableAsync(async token =>
        {
            var current = await _conversationRepository.GetByIdForUpdateAsync(request.ConversationId, candidate!.ActorId, token);
            if (!TryGetQueuedRun(current, request.TenantId, request.RunId, out var queuedRun)
                || current!.UserId != candidate.UserId || current.ActorId != candidate.ActorId) return null;
            if (await _conversationRepository.IsPrivacyFencedAsync(current, token))
            {
                await FailAndActivateAsync(current, queuedRun!, PrivacyErasureFencedFailureCode,
                    PrivacyErasureFencedFailureMessage, token);
                return null;
            }
            if (disabledReason is not null)
            {
                await FailAndActivateAsync(current, queuedRun!, disabledReason,
                    $"AI assistant is unavailable: {disabledReason}.", token);
                return null;
            }
            if (!AiAssistantAvailability.IsModelAllowed(settings, queuedRun!.ModelId))
            {
                await FailAndActivateAsync(current, queuedRun, AiAssistantAvailability.ModelNotAllowedFailureCode,
                    AiAssistantAvailability.ModelNotAllowedFailureMessage, token);
                return null;
            }
            queuedRun.Start(DateTime.UtcNow);
            await _conversationRepository.Update(current);
            return current;
        }, cancellationToken);
        if (conversation is null) return;
        conversation = await _conversationRepository.GetByIdWithDetailsAsync(request.ConversationId, cancellationToken);
        if (!TryGetInProgressRun(conversation, request.TenantId, request.RunId, out var run)) return;

        var selectedReferences = await BuildSelectedReferenceContextAsync(
            conversation!,
            settings,
            cancellationToken);
        var providerPayload = _promptContextBuilder.Build(
            conversation!,
            settings,
            run.ModelId,
            allowToolProposals,
            selectedReferences);

        AiProviderResponseResolution providerResolution;
        try
        {
            providerResolution = await _providerResponseResolver.ResolveAsync(
                providerPayload,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            await FailLatestRunAndActivateAsync(
                request,
                "provider_cancelled",
                "AI provider run was cancelled before completion.",
                CancellationToken.None);
            return;
        }

        await _unitOfWork.ExecuteSerializableAsync(async token =>
        {
            var latestConversation = await _conversationRepository.GetByIdForUpdateAsync(
                request.ConversationId, conversation.ActorId, token);
            if (!TryGetInProgressRun(latestConversation, request.TenantId, request.RunId, out var latestRun)
                || latestConversation.UserId != conversation.UserId || latestConversation.ActorId != conversation.ActorId
                || latestRun.StartedAt != run.StartedAt || latestRun.ModelId != run.ModelId)
                return false;
            if (await _conversationRepository.IsPrivacyFencedAsync(latestConversation, token))
            {
                await FailAndActivateAsync(latestConversation, latestRun, PrivacyErasureFencedFailureCode,
                    PrivacyErasureFencedFailureMessage, token);
                return false;
            }
            if (!providerResolution.Succeeded || providerResolution.Response is null || providerResolution.ParseResult is null)
            {
                await FailAndActivateAsync(latestConversation, latestRun,
                    providerResolution.FailureCode ?? "provider_failure",
                    providerResolution.FailureMessage ?? "AI provider failed.", token);
                return false;
            }
            var utcNow = DateTime.UtcNow;
            var assistantMessage = latestConversation.AddMessage(
                AiMessageRole.Assistant, BuildAssistantMessage(providerResolution.Response), null, utcNow);
            IReadOnlyList<AiParsedProposedAction> proposedActions = allowToolProposals
                ? providerResolution.ParseResult.Actions : Array.Empty<AiParsedProposedAction>();
            foreach (var proposedAction in proposedActions)
            {
                latestConversation.ProposeAction(proposedAction.Kind, proposedAction.PayloadJson,
                    assistantMessage.Id, latestConversation.UserId, utcNow);
            }
            latestConversation.CompleteRun(latestRun, utcNow);
            await _conversationRepository.Update(latestConversation);
            return true;
        }, cancellationToken);
    }

    private async Task FailLatestRunAndActivateAsync(
        ProcessAiRunCommand request,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.ExecuteSerializableAsync(async token =>
        {
            var conversation = await _conversationRepository.GetByIdForUpdateAsync(request.ConversationId, token);
            if (!TryGetActiveRun(conversation, request.TenantId, request.RunId, out var run))
                return false;
            await FailAndActivateAsync(conversation, run, failureCode, failureMessage, token);
            return true;
        }, cancellationToken);
    }

    private async Task FailAndActivateAsync(
        AiConversation conversation,
        AiRun run,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var failedAt = DateTime.UtcNow;
        conversation.FailRun(run, failureCode, failureMessage, failedAt);
        conversation.Activate(failedAt);
        await _conversationRepository.Update(conversation);
    }

    private static bool TryGetQueuedRun(
        [NotNullWhen(true)] AiConversation? conversation,
        Guid tenantId,
        Guid runId,
        [NotNullWhen(true)] out AiRun? run)
    {
        run = null;
        if (conversation is null || conversation.TenantId != tenantId)
        {
            return false;
        }

        run = conversation.Runs.FirstOrDefault(candidate => candidate.Id == runId);
        return run?.Status == AiRunStatus.Queued;
    }

    private static bool TryGetInProgressRun(
        [NotNullWhen(true)] AiConversation? conversation,
        Guid tenantId,
        Guid runId,
        [NotNullWhen(true)] out AiRun? run)
    {
        run = null;
        if (conversation is null || conversation.TenantId != tenantId)
        {
            return false;
        }

        run = conversation.Runs.FirstOrDefault(candidate => candidate.Id == runId);
        return run?.Status == AiRunStatus.InProgress;
    }

    private static bool TryGetActiveRun(
        [NotNullWhen(true)] AiConversation? conversation,
        Guid tenantId,
        Guid runId,
        [NotNullWhen(true)] out AiRun? run)
    {
        run = null;
        if (conversation is null || conversation.TenantId != tenantId)
        {
            return false;
        }

        run = conversation.Runs.FirstOrDefault(candidate => candidate.Id == runId);
        return run?.Status is AiRunStatus.Queued or AiRunStatus.InProgress;
    }

    private static string BuildAssistantMessage(AiChatResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.AssistantMessage))
        {
            return response.AssistantMessage;
        }

        return response.ProposedActions.Count > 0
            ? "I prepared a proposed action for your review."
            : "The AI provider returned an empty response.";
    }

    private async Task<IReadOnlyList<AiSelectedReferenceDto>> BuildSelectedReferenceContextAsync(
        AiConversation conversation,
        AiAssistantSettingGroup settings,
        CancellationToken cancellationToken)
    {
        var selectedReferences = conversation.References
            .OrderBy(reference => reference.CreatedAt)
            .ThenBy(reference => reference.Id)
            .Take(settings.SelectedReferenceLimit)
            .Select(reference => new AiSelectedReferenceDto(
                reference.Kind.ToString(),
                reference.ReferenceId,
                reference.DisplayName,
                reference.Summary))
            .ToList();

        if (selectedReferences.Count == 0)
        {
            return [];
        }

        var enrichedReferences = new List<AiSelectedReferenceDto>(selectedReferences.Count);
        var providerTrustTier = _providerTrustResolver.Resolve(new AiProviderTrustResolutionContext(
            EndpointUrl: settings.EndpointUrl,
            TenantControlled: false,
            PlatformDefault: true));
        var viewerScope = conversation.ActorId != Guid.Empty
            ? AiViewerScopeEnum.OrganizerTeam
            : AiViewerScopeEnum.Public;
        var grantedFieldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var piiDisclosureEnabled = settings.PiiDisclosureEnabled;

        foreach (AiSelectedReferenceDto reference in selectedReferences)
        {
            if (IsEventReference(reference))
            {
                var eventDetails = await _eventDetailsHandler.QueryAsync(
                    new GetEventDetailsRequest { Id = reference.ReferenceId },
                    cancellationToken);
                if (eventDetails is not null)
                {
                    var sanitizationInput = new AiContextSanitizationInput(
                        EntityName: "Event",
                        Fields: BuildEventReferenceFieldMap(eventDetails),
                        ProviderTrustTier: providerTrustTier,
                        ViewerScope: viewerScope,
                        GrantedFieldKeys: grantedFieldKeys,
                        PiiDisclosureEnabled: piiDisclosureEnabled,
                        MaxSensitivity: (AiContextSensitivityEnum)settings.MaxAiContextSensitivity);
                    var envelope = _contextGateway.Sanitize(sanitizationInput);
                    enrichedReferences.Add(new AiSelectedReferenceDto(
                        AiReferenceKind.Event.ToString(),
                        eventDetails.Id,
                        eventDetails.Title,
                        BuildEventReferenceSummaryFromEnvelope(envelope)));
                    continue;
                }
            }

            enrichedReferences.Add(reference);
        }

        return enrichedReferences;
    }

    private static bool IsEventReference(AiSelectedReferenceDto reference)
        => string.Equals(reference.Kind, AiReferenceKind.Event.ToString(), StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, object?> BuildEventReferenceFieldMap(EventDto eventDetails)
    {
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = eventDetails.EventStatusFullName,
            ["format"] = eventDetails.EventFormatFullName,
            ["visibility"] = eventDetails.VisibilityTypeFullName,
            ["host"] = eventDetails.ActorDisplayName
        };

        if (!string.IsNullOrWhiteSpace(eventDetails.Subtitle))
        {
            fields["subtitle"] = eventDetails.Subtitle.Trim();
        }

        if (!string.IsNullOrWhiteSpace(eventDetails.Description))
        {
            fields["description"] = eventDetails.Description.Trim();
        }

        if (eventDetails.FirstSessionDate.HasValue || eventDetails.LastSessionDate.HasValue)
        {
            var firstSessionDate = eventDetails.FirstSessionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown";
            var lastSessionDate = eventDetails.LastSessionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown";
            fields["dates"] = $"{firstSessionDate} to {lastSessionDate}";
        }

        if (!string.IsNullOrWhiteSpace(eventDetails.Timezone))
        {
            fields["timezone"] = eventDetails.Timezone.Trim();
        }

        if (eventDetails.SessionCount.HasValue)
        {
            fields["sessions"] = eventDetails.SessionCount.Value.ToString(CultureInfo.InvariantCulture);
        }

        return fields;
    }

    private static string BuildEventReferenceSummaryFromEnvelope(AiContextSanitizedEnvelope envelope)
    {
        if (!envelope.Succeeded)
        {
            return "context_unavailable";
        }

        var parts = new List<string>(envelope.DisclosedFields.Count);
        foreach (var field in envelope.DisclosedFields)
        {
            var displayValue = field.Value switch
            {
                null => string.Empty,
                string s => s,
                _ => field.Value.ToString()
            };
            if (!string.IsNullOrWhiteSpace(displayValue))
            {
                parts.Add($"{field.Name}: {displayValue}");
            }
        }

        return string.Join("; ", parts);
    }
}
