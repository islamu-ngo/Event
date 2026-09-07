using Explore.Application.Contracts.Webhooks;

namespace Explore.API.Services;

public sealed class IncomingWebhookVerifierRegistry : IIncomingWebhookVerifierRegistry
{
    private readonly IReadOnlyDictionary<string, IIncomingWebhookVerifier> _verifiers;

    public IncomingWebhookVerifierRegistry(IEnumerable<IIncomingWebhookVerifier> verifiers)
    {
        Dictionary<string, IIncomingWebhookVerifier> resolved = new(StringComparer.OrdinalIgnoreCase);
        foreach (var verifier in verifiers)
        {
            if (!resolved.TryAdd(verifier.Provider, verifier))
            {
                throw new InvalidOperationException($"Duplicate incoming webhook verifier for provider '{verifier.Provider}'.");
            }
        }

        _verifiers = resolved;
    }

    public IIncomingWebhookVerifier GetRequired(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new InvalidOperationException("Incoming webhook provider is required.");
        }

        return _verifiers.TryGetValue(provider.Trim(), out var verifier)
            ? verifier
            : throw new InvalidOperationException($"Incoming webhook provider '{provider}' is not registered.");
    }
}
