using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSeries.Requests.Commands;

namespace Explore.Application.Features.EventSeries.Authorization;

/// <summary>Resolves Actor:update from the persisted Series, including unpublished Series.</summary>
public sealed class UpdateEventSeriesAuthorizationContextEnricher(
    IEventSeriesRepository seriesRepository,
    ITenantContext tenantContext) : IAuthorizationContextEnricher<UpdateEventSeriesCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(UpdateEventSeriesCommand request, CancellationToken cancellationToken)
    {
        var series = await seriesRepository.GetForUpdateAsync(request.EventSeriesId, tenantContext.TenantId, cancellationToken);
        if (series is null)
            throw new AuthorizationException(ResourceKinds.Actor, AuthorizationActions.Update);

        return new AuthorizationContext(series.ActorId.ToString(),
            new ActorAuthorizationFacts(series.TenantId, series.ActorId));
    }
}
