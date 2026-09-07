using Explore.Application.Contracts.Infrastructure.Ai;

namespace Explore.Infrastructure.Ai;

public sealed class FakeAiProviderStrategy : IAiProviderStrategy
{
    private readonly FakeAiChatProvider _provider;

    public FakeAiProviderStrategy(FakeAiChatProvider provider)
    {
        _provider = provider;
    }

    public int ProviderId => AiProviderSettings.ProviderFake;

    public bool SupportsProvider(int providerId) => providerId == ProviderId;

    public Task<AiChatProviderResult> SendAsync(AiChatPayload request, CancellationToken cancellationToken = default) =>
        _provider.SendAsync(request, cancellationToken);

    public Task<IReadOnlyList<AiModelDescriptor>> ListAvailableModelsAsync(CancellationToken cancellationToken = default) =>
        _provider.ListAvailableModelsAsync(cancellationToken);

    public AiProviderHealth CheckHealth(IReadOnlyDictionary<string, object> data) =>
        new(true, true, "healthy_fake",
            "Deterministic fake AI provider is enabled for tests or local workflows.", data);
}
