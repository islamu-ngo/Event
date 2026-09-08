using Explore.Application.Contracts.Webhooks;

namespace Explore.API.Services;

public interface IIncomingWebhookVerifierRegistry
{
    IIncomingWebhookVerifier GetRequired(string provider);
}
