using Explore.Application.Contracts.Operations;
using Explore.Application.Features.EventResources.Requests.Queries;
using Explore.Application.Services;

namespace Explore.Application.Features.EventResources.Handlers.Queries;

public sealed class GetEventResourceAccessQueryHandler(EventResourceAccessService access)
    : IQueryHandler<GetEventResourceAccessQuery, EventResourceAuthorityResult>
{
    public Task<EventResourceAuthorityResult> QueryAsync(GetEventResourceAccessQuery query,
        CancellationToken cancellationToken = default) =>
        access.PrepareAsync(query.ResourceId, query.DeadlineUtc, cancellationToken);
}

public sealed class GetEventResourceAccessHeadersQueryHandler(EventResourceAuthorityOrchestrator authority)
    : IQueryHandler<GetEventResourceAccessHeadersQuery, EventResourceHeaderResult>
{
    public async Task<EventResourceHeaderResult> QueryAsync(GetEventResourceAccessHeadersQuery query,
        CancellationToken cancellationToken = default)
    {
        var lease = query.Lease;
        var request = lease.Request with
        {
            ExpectedResourceVersion = lease.Snapshot.Facts.ResourceVersion,
            ExpectedAttachmentGeneration = lease.Snapshot.Facts.AttachmentGeneration,
            ExpectedDisclosure = lease.Snapshot.Evaluation.Disclosure
        };
        var outcome = (await authority.AuthorizeCapabilitiesAsync([request], cancellationToken))[0];
        return outcome == EventResourceAuthorityOutcome.Allowed
            ? await authority.CompleteHeadersAsync(lease, cancellationToken)
            : new EventResourceHeaderResult(outcome);
    }
}
