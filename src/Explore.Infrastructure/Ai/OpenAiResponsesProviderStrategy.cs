using Explore.Application.Contracts.Infrastructure.Ai;

namespace Explore.Infrastructure.Ai;

public sealed class OpenAiResponsesProviderStrategy : IAiProviderStrategy
{
    private readonly OpenAiResponsesChatProvider _provider;

    public OpenAiResponsesProviderStrategy(OpenAiResponsesChatProvider provider)
    {
        _provider = provider;
    }

    public int ProviderId => AiProviderSettings.ProviderOpenAi;

    public bool SupportsProvider(int providerId) => providerId == ProviderId;

    public Task<AiChatProviderResult> SendAsync(AiChatPayload request, CancellationToken cancellationToken = default) =>
        _provider.SendAsync(request, cancellationToken);

    public Task<IReadOnlyList<AiModelDescriptor>> ListAvailableModelsAsync(CancellationToken cancellationToken = default) =>
        _provider.ListAvailableModelsAsync(cancellationToken);

    public AiProviderHealth CheckHealth(IReadOnlyDictionary<string, object> data) =>
        new(true, true, "configured_no_probe",
            "OpenAI Responses API provider settings are valid; network probing is deferred to the adapter.", data);
}
