using Explore.Application.Contracts.Infrastructure.Ai;

namespace Explore.Infrastructure.Ai;

public sealed class MicrosoftExtensionsProviderStrategy : IAiProviderStrategy
{
    private readonly MicrosoftExtensionsAiChatProvider _provider;

    public MicrosoftExtensionsProviderStrategy(MicrosoftExtensionsAiChatProvider provider)
    {
        _provider = provider;
    }

    public int ProviderId => AiProviderSettings.ProviderAzureOpenAi;

    public bool SupportsProvider(int providerId) =>
        providerId == AiProviderSettings.ProviderAzureOpenAi;

    public Task<AiChatProviderResult> SendAsync(AiChatPayload request, CancellationToken cancellationToken = default) =>
        _provider.SendAsync(request, cancellationToken);

    public Task<IReadOnlyList<AiModelDescriptor>> ListAvailableModelsAsync(CancellationToken cancellationToken = default) =>
        _provider.ListAvailableModelsAsync(cancellationToken);

    public AiProviderHealth CheckHealth(IReadOnlyDictionary<string, object> data) =>
        new(true, true, "configured_no_probe",
            "SDK-backed AI provider settings are valid; network probing is deferred to the adapter.", data);
}
