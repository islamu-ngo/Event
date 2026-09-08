using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant.Actors;
using Explore.Application.Features.AiAssistant.Requests.Queries;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Handlers.Queries;

public sealed class GetAiAssistantBootstrapQueryHandler : IRequestHandler<GetAiAssistantBootstrapQuery, AiAssistantBootstrapDto>
{
    private readonly ITenantContext _tenantContext;
    private readonly IHierarchicalSettingsResolver _settingsResolver;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAiAssistantActorContextService _actorContextService;

    public GetAiAssistantBootstrapQueryHandler(
        ITenantContext tenantContext,
        IHierarchicalSettingsResolver settingsResolver,
        ICurrentUserService currentUserService,
        IAiAssistantActorContextService actorContextService)
    {
        _tenantContext = tenantContext;
        _settingsResolver = settingsResolver;
        _currentUserService = currentUserService;
        _actorContextService = actorContextService;
    }

    public async Task<AiAssistantBootstrapDto> Handle(
        GetAiAssistantBootstrapQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var settings = await _settingsResolver.ResolveGroupAsync<AiAssistantSettingGroup>(
            new SettingContext(TenantId: tenantId), cancellationToken);

        var provider = NormalizeProvider(settings.Provider);
        var models = BuildModels(provider, settings);
        var disabledReason = ResolveDisabledReason(provider, settings, models);
        var defaultModelId = ResolveDefaultModelId(settings, models);
        var actorContexts = await BuildActorContextsAsync(tenantId, cancellationToken);

        return new AiAssistantBootstrapDto
        {
            TenantId = tenantId,
            Enabled = settings.Enabled,
            Available = disabledReason is null,
            DisabledReason = disabledReason,
            Provider = provider,
            DefaultModelId = defaultModelId,
            ActorContexts = actorContexts,
            Models = models,
            Features = new AiAssistantFeatureFlagsDto
            {
                ToolProposalsEnabled = disabledReason is null && settings.ToolProposalsEnabled,
                StreamingEnabled = disabledReason is null && settings.StreamingEnabled
            },
            Limits = new AiAssistantLimitsDto
            {
                MaxInputTokens = settings.MaxInputTokens,
                MaxOutputTokens = settings.MaxOutputTokens,
                Temperature = settings.Temperature,
                TimeoutSeconds = AiAssistantAvailability.ResolveTimeoutSeconds(settings),
                DailyMessageLimit = settings.DailyMessageLimit
            },
            RetentionDays = settings.RetentionDays
        };
    }

    private Task<IReadOnlyList<AiAssistantActorContextDto>> BuildActorContextsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
        => !_currentUserService.IsAuthenticated || _currentUserService.UserId is not Guid userId
            ? Task.FromResult<IReadOnlyList<AiAssistantActorContextDto>>([])
            : _actorContextService.ListAuthorizedActorContextsAsync(tenantId, userId, cancellationToken);

    private static string NormalizeProvider(string? provider)
    {
        return string.IsNullOrWhiteSpace(provider)
            ? AiProviderDefaults.ProviderNone
            : provider.Trim().ToLowerInvariant();
    }

    private static IReadOnlyList<AiAssistantModelDto> BuildModels(string provider, AiAssistantSettingGroup settings)
    {
        if (provider == AiProviderDefaults.ProviderFake)
        {
            return
            [
                new AiAssistantModelDto
                {
                    Id = AiProviderDefaults.FakeModelId,
                    DisplayName = AiProviderDefaults.FakeModelDisplayName,
                    MaxInputTokens = AiProviderDefaults.DefaultMaxInputTokens,
                    MaxOutputTokens = AiProviderDefaults.DefaultMaxOutputTokens,
                    SupportsToolProposals = true,
                    SupportsStreaming = false
                }
            ];
        }

        if ((provider == AiProviderDefaults.ProviderOpenAi
                || provider == AiProviderDefaults.ProviderOpenAiCompatible
                || provider == AiProviderDefaults.ProviderAnthropic
                || provider == AiProviderDefaults.ProviderAnthropicCompatible)
            && !string.IsNullOrWhiteSpace(settings.ModelId))
        {
            return AiAssistantAvailability.ResolveAllowedModelIds(settings)
                .Select(modelId => new AiAssistantModelDto
                {
                    Id = modelId,
                    DisplayName = modelId,
                    MaxInputTokens = settings.MaxInputTokens,
                    MaxOutputTokens = settings.MaxOutputTokens,
                    SupportsToolProposals = settings.ToolProposalsEnabled,
                    SupportsStreaming = settings.StreamingEnabled
                })
                .ToList();
        }

        return [];
    }

    private static string? ResolveDisabledReason(
        string provider,
        AiAssistantSettingGroup settings,
        IReadOnlyCollection<AiAssistantModelDto> models)
    {
        if (!settings.Enabled)
            return "disabled";

        if (provider == AiProviderDefaults.ProviderNone)
            return "provider_not_configured";

        if (provider != AiProviderDefaults.ProviderFake
            && provider != AiProviderDefaults.ProviderOpenAi
            && provider != AiProviderDefaults.ProviderOpenAiCompatible
            && provider != AiProviderDefaults.ProviderAnthropic
            && provider != AiProviderDefaults.ProviderAnthropicCompatible)
            return "provider_unsupported";

        if (provider == AiProviderDefaults.ProviderOpenAi || provider == AiProviderDefaults.ProviderAnthropic)
        {
            if (!settings.HasApiKey)
                return "api_key_not_configured";

            if (!settings.HasModel)
                return "model_not_configured";
        }

        if (provider == AiProviderDefaults.ProviderOpenAiCompatible || provider == AiProviderDefaults.ProviderAnthropicCompatible)
        {
            if (string.IsNullOrWhiteSpace(settings.EndpointUrl))
                return "endpoint_not_configured";

            if (!settings.HasModel)
                return "model_not_configured";
        }

        return models.Count == 0 ? "model_not_configured" : null;
    }

    private static string? ResolveDefaultModelId(
        AiAssistantSettingGroup settings,
        IReadOnlyList<AiAssistantModelDto> models)
    {
        if (models.Count == 0)
        {
            return null;
        }

        var configuredModelId = AiAssistantAvailability.ResolveModelId(settings);
        return models.FirstOrDefault(model => string.Equals(
                model.Id,
                configuredModelId,
                StringComparison.OrdinalIgnoreCase))?.Id
            ?? models[0].Id;
    }
}
