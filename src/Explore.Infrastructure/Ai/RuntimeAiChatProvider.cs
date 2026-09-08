using Explore.Application.Contracts.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Ai;

public sealed class RuntimeAiChatProvider : IAiChatProvider, IAiModelCatalog
{
    private readonly IOptions<AiProviderSettings> _options;
    private readonly AiProviderSettingsValidator _validator;
    private readonly IAiProviderStrategyResolver _resolver;

    public RuntimeAiChatProvider(
        IOptions<AiProviderSettings> options,
        AiProviderSettingsValidator validator,
        IAiProviderStrategyResolver resolver)
    {
        _options = options;
        _validator = validator;
        _resolver = resolver;
    }

    public Task<IReadOnlyList<AiModelDescriptor>> ListAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolved = TryResolveStrategy(out var strategy, out _);
        if (!resolved || strategy is null)
            return Task.FromResult<IReadOnlyList<AiModelDescriptor>>([]);

        return strategy.ListAvailableModelsAsync(cancellationToken);
    }

    public Task<AiChatProviderResult> SendAsync(AiChatPayload request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.ProviderConfiguration is { Provider: > 0 })
        {
            var overrideStrategy = _resolver.Resolve(request.ProviderConfiguration.Provider);
            if (overrideStrategy is not null)
                return overrideStrategy.SendAsync(request, cancellationToken);
        }

        if (!TryResolveStrategy(out var strategy, out var failure))
            return Task.FromResult(failure!);

        return strategy.SendAsync(request, cancellationToken);
    }

    private bool TryResolveStrategy(out IAiProviderStrategy? strategy, out AiChatProviderResult? failure)
    {
        strategy = null;
        failure = null;

        var settings = _options.Value;
        if (!settings.Enabled)
        {
            failure = AiChatProviderResult.Failure(
                "provider_disabled",
                "AI provider integration is disabled.");
            return false;
        }

        var validation = _validator.Validate(null, settings);
        if (!validation.Succeeded)
        {
            failure = AiChatProviderResult.Failure(
                "invalid_settings",
                "AI provider settings are invalid.");
            return false;
        }

        strategy = _resolver.Resolve(settings.Provider);
        if (strategy is null)
        {
            failure = AiChatProviderResult.Failure(
                "provider_not_configured",
                "No runnable AI provider is configured.");
            return false;
        }

        return true;
    }
}
