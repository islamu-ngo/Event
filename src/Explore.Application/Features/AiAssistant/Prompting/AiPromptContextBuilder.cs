using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant;
using Explore.Application.Settings.Groups;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Prompting;

public sealed class AiPromptContextBuilder
{
    private const int MaxProviderMessages = 32;

    private readonly AiSystemPromptFactory _systemPromptFactory;
    private readonly IAiTokenEstimator _tokenEstimator;
    private readonly AiReferencePromptPacker _referencePromptPacker;

    public AiPromptContextBuilder()
        : this(new AiSystemPromptFactory())
    {
    }

    public AiPromptContextBuilder(
        AiSystemPromptFactory systemPromptFactory,
        IAiTokenEstimator? tokenEstimator = null)
    {
        _systemPromptFactory = systemPromptFactory;
        _tokenEstimator = tokenEstimator ?? new ApproximateAiTokenEstimator();
        _referencePromptPacker = new AiReferencePromptPacker(_tokenEstimator);
    }

    public AiChatPayload Build(
        AiConversation conversation,
        AiAssistantSettingGroup settings,
        string modelId,
        bool allowToolProposals = true,
        IReadOnlyList<AiSelectedReferenceDto>? selectedReferences = null)
    {
        var budget = AiPromptTokenBudget.Create(settings.MaxInputTokens);
        var requestedToolProposals = settings.ToolProposalsEnabled && allowToolProposals;
        var hasSelectedReferences = (selectedReferences?.Count ?? 0) > 0 || conversation.References.Count > 0;
        string systemPrompt = _systemPromptFactory.CreateSystemPrompt(
            allowToolProposals,
            toolSchemaAvailable: requestedToolProposals,
            hasSelectedReferences: hasSelectedReferences);
        budget.ConsumeBestEffort(systemPrompt, _tokenEstimator);

        var actionSchema = _systemPromptFactory.CreateActionSchema(
            settings,
            requestedToolProposals,
            budget.RemainingTokens,
            _tokenEstimator);

        if (actionSchema is null && requestedToolProposals)
        {
            budget = AiPromptTokenBudget.Create(settings.MaxInputTokens);
            systemPrompt = _systemPromptFactory.CreateSystemPrompt(allowToolProposals, toolSchemaAvailable: false, hasSelectedReferences: hasSelectedReferences);
            budget.ConsumeBestEffort(systemPrompt, _tokenEstimator);
        }

        if (actionSchema is not null)
        {
            budget.ConsumeBestEffort(actionSchema.JsonSchema, _tokenEstimator);
        }

        var toolProposalsEnabled = requestedToolProposals && actionSchema is not null;
        var candidateMessages = conversation.Messages
            .OrderByDescending(message => message.Sequence)
            .Where(message => message.Role is AiMessageRole.System or AiMessageRole.User or AiMessageRole.Assistant)
            .Take(MaxProviderMessages)
            .ToList();
        var messages = PackMessages(candidateMessages, budget);
        var selectedReferencesContext = PackSelectedReferences(conversation, settings, budget, selectedReferences);
        if (!string.IsNullOrWhiteSpace(selectedReferencesContext))
        {
            messages = [new AiChatMessage(AiMessageRole.System, selectedReferencesContext), .. messages];
        }

        return new AiChatPayload(
            modelId,
            messages,
            systemPrompt,
            new AiChatOptions(
                settings.MaxInputTokens,
                settings.MaxOutputTokens,
                settings.Temperature,
                AiAssistantAvailability.ResolveTimeoutSeconds(settings),
                toolProposalsEnabled,
                StreamingEnabled: false),
            actionSchema,
            ProviderConfiguration: BuildProviderConfiguration(settings, modelId));
    }

    private static AiChatProviderConfiguration BuildProviderConfiguration(
        AiAssistantSettingGroup settings,
        string modelId)
    {
        var provider = AiAssistantAvailability.NormalizeProvider(settings.Provider);
        return new AiChatProviderConfiguration(
            AiProviderDefaults.ProviderNameToId(provider),
            settings.EndpointUrl?.Trim() ?? string.Empty,
            settings.ApiKey?.Trim() ?? string.Empty,
            modelId.Trim());
    }

    private IReadOnlyList<AiChatMessage> PackMessages(
        IReadOnlyList<AiMessage> candidateMessages,
        AiPromptTokenBudget budget)
    {
        var selectedMessages = new List<AiChatMessage>();

        foreach (AiMessage message in candidateMessages)
        {
            var images = AiMessageImageAttachmentSerializer.DeserializeForProvider(message.ImageAttachmentsJson);
            string wrappedContent = WrapMessageContent(
                message.Role,
                string.IsNullOrWhiteSpace(message.Content) && images.Count > 0
                    ? "[Image attached]"
                    : message.Content);

            if (budget.TryConsume(wrappedContent, _tokenEstimator))
            {
                selectedMessages.Add(new AiChatMessage(message.Role, wrappedContent, images: images));
                continue;
            }

            if (selectedMessages.Count == 0)
            {
                string truncatedContent = CreateTruncatedWrappedMessage(message.Role, message.Content, budget.RemainingTokens);
                if (truncatedContent.Length > 0 && budget.TryConsume(truncatedContent, _tokenEstimator))
                {
                    selectedMessages.Add(new AiChatMessage(message.Role, truncatedContent, images: images));
                }
            }

            break;
        }

        selectedMessages.Reverse();
        return selectedMessages;
    }

    private string PackSelectedReferences(
        AiConversation conversation,
        AiAssistantSettingGroup settings,
        AiPromptTokenBudget budget,
        IReadOnlyList<AiSelectedReferenceDto>? selectedReferences)
    {
        var references = selectedReferences?.ToList()
            ?? conversation.References
                .OrderBy(reference => reference.CreatedAt)
                .ThenBy(reference => reference.Id)
                .Select(reference => new AiSelectedReferenceDto(
                    reference.Kind.ToString(),
                    reference.ReferenceId,
                    reference.DisplayName,
                    reference.Summary))
                .ToList();

        if (references.Count == 0)
        {
            return string.Empty;
        }

        string context = _referencePromptPacker.Pack(
            references,
            maxReferences: settings.SelectedReferenceLimit,
            maxTotalTokens: budget.RemainingTokens);

        return budget.TryConsume(context, _tokenEstimator) ? context : string.Empty;
    }

    private string CreateTruncatedWrappedMessage(AiMessageRole role, string content, int maxTokens)
    {
        if (maxTokens <= 0)
        {
            return string.Empty;
        }

        string trimmed = content.Trim();
        var low = 0;
        var high = trimmed.Length;
        var best = string.Empty;

        while (low <= high)
        {
            int midpoint = low + ((high - low) / 2);
            string candidate = WrapMessageContent(role, trimmed[..midpoint]);
            if (_tokenEstimator.CountTokens(candidate) <= maxTokens)
            {
                best = candidate;
                low = midpoint + 1;
            }
            else
            {
                high = midpoint - 1;
            }
        }

        return best;
    }

    private static string WrapMessageContent(AiMessageRole role, string content)
        => $"<message role=\"{role}\">\n{content.Trim()}\n</message>";
}
