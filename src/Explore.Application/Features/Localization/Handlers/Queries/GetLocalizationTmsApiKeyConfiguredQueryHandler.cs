using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Localization.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Localization.Handlers.Queries;

public sealed class GetLocalizationTmsApiKeyConfiguredQueryHandler(
    ILocalizationSecretStatusReader secretStatusReader)
    : IQueryHandler<GetLocalizationTmsApiKeyConfiguredQuery, bool>
{
    public Task<bool> QueryAsync(
        GetLocalizationTmsApiKeyConfiguredQuery request,
        CancellationToken cancellationToken) =>
        secretStatusReader.IsConfiguredAsync(cancellationToken);
}
