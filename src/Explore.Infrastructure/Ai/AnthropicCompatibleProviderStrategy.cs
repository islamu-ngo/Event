using Explore.Application.Contracts.Infrastructure.Ai;

namespace Explore.Infrastructure.Ai;

public sealed class AnthropicCompatibleProviderStrategy : IAiProviderStrategy
{
    private readonly AnthropicCompatibleChatProvider _provider;

    public AnthropicCompatibleProviderStrategy(AnthropicCompatibleChatProvider provider)
    {
        _provider = provider;
    }

    public int ProviderId => AiProviderSettings.ProviderAnthropicCompatible;

    public bool SupportsProvider(int providerId) => providerId == ProviderId;

    public Task<AiChatProviderResult> SendAsync(AiChatPayload request, CancellationToken cancellationToken = default) =>
        _provider.SendAsync(request, cancellationToken);

    public Task<IReadOnlyList<AiModelDescriptor>> ListAvailableModelsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AiModelDescriptor>>([]);

    public AiProviderHealth CheckHealth(IReadOnlyDictionary<string, object> data) =>
        new(true, true, "configured_no_probe",
            "Anthropic-compatible AI provider settings are valid; network probing is deferred to the adapter.", data);
}
