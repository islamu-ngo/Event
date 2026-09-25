using Explore.Application.Contracts.Operations;
using Explore.Application.Features.EventResourceProviderActivation.Requests.Queries;
using Explore.Application.Services;
using Explore.Application.Settings;

namespace Explore.Application.Features.EventResourceProviderActivation.Handlers.Queries;

public sealed class GetEventResourceProviderBindingsQueryHandler(EventResourceProviderControlPlane controlPlane)
    : IQueryHandler<GetEventResourceProviderBindingsQuery, EventResourceProviderBindingDocument>
{
    public Task<EventResourceProviderBindingDocument> QueryAsync(GetEventResourceProviderBindingsQuery query, CancellationToken cancellationToken = default) =>
        controlPlane.ReadBindingsAsync(cancellationToken);
}
