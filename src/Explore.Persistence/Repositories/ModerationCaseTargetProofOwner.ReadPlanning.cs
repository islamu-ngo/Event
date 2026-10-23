using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationCaseTargetProofOwner
{
    private ReadIssuancePlan? _readIssuancePlan;

    private async Task<bool> AcquireReadIssuancePlanAsync(Guid grantId, Guid callerId, Guid tenantId, CancellationToken token)
    {
        // Discovery retains coordinates, not read permission or locks ahead of the ordered Actor anchors.
        var plan = await EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(context, async planning =>
        {
            var host = await planning.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
            if (host is null || planning.ModerationTrustDomainId is { } trust && trust != host.TrustDomainId)
                return null;
            var previousTrust = planning.ModerationTrustDomainId;
            try
            {
                planning.ModerationTrustDomainId = host.TrustDomainId;
                var grant = await planning.ModerationCaseReadGrants.AsNoTracking().SingleOrDefaultAsync(row =>
                    row.Id == grantId && row.CallerUserId == callerId && row.TenantId == tenantId
                    && row.TrustDomainId == host.TrustDomainId, token);
                if (grant is null) return null;
                var source = await planning.ModerationCaseTargetSources.AsNoTracking().SingleOrDefaultAsync(row =>
                    row.Id == grant.SourceId && row.TenantId == tenantId && row.TrustDomainId == host.TrustDomainId, token);
                if (source is null) return null;
                EventActorPlan? ancestry = null;
                if (source.NativeKind == ModerationCaseTargetKind.EventVersion
                    || source.EventId is not null
                    && source.NativeKind is (ModerationCaseTargetKind.Actor or ModerationCaseTargetKind.Service))
                {
                    ancestry = await planning.Events.AsNoTracking()
                        .Where(row => row.Id == source.EventId && row.TenantId == tenantId)
                        .Select(row => new EventActorPlan(row.Id, row.ActorId, row.OrganizerActorId)).SingleOrDefaultAsync(token);
                    if (ancestry is null) return null;
                }
                return new ReadIssuancePlan(ReadHostCoordinates.From(host), ReadGrantCoordinates.From(grant),
                    ReadSourceCoordinates.From(source), ancestry);
            }
            finally { planning.ModerationTrustDomainId = previousTrust; }
        }, token);
        if (plan is null) return false;
        var actors = plan.Event is { } parent
            ? new[] { (Guid?)parent.ActorId, parent.OrganizerActorId }.OfType<Guid>().Distinct().Order().ToArray()
            : [];
        if (_readIssuancePlan is { } held)
            return held == plan && actors.All(id => _fences.Contains((typeof(Actor), id)));
        if (_fences.Contains((typeof(Tenant), tenantId))) return false;
        _readIssuancePlan = plan;
        foreach (var actorId in actors)
            if (!await FenceAsync<Actor>(actorId, true, token)) return false;
        return true;
    }

    private async Task<bool> ReadIssuanceCoordinatesMatchAsync(CancellationToken token)
    {
        if (_readIssuancePlan is not { } plan) return false;
        var grant = await context.ModerationCaseReadGrants.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == plan.Grant.Id, token);
        if (grant is null || ReadGrantCoordinates.From(grant) != plan.Grant) return false;
        var source = await context.ModerationCaseTargetSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == plan.Source.Id, token);
        return source is not null && ReadSourceCoordinates.From(source) == plan.Source;
    }

    private sealed record ReadIssuancePlan(ReadHostCoordinates Host, ReadGrantCoordinates Grant,
        ReadSourceCoordinates Source, EventActorPlan? Event);

    private sealed record ReadHostCoordinates(Guid Id, Guid TrustDomainId, Guid SolutionId, Guid InstanceId,
        Guid MaintenanceServicePrincipalId)
    {
        public static ReadHostCoordinates From(ModerationHostBinding host) =>
            new(host.Id, host.TrustDomainId, host.SolutionId, host.InstanceId, host.MaintenanceServicePrincipalId);
    }

    private sealed record ReadGrantCoordinates(Guid Id, Guid TenantId, Guid TrustDomainId, Guid CaseId,
        long CaseRevision, Guid SourceId, Guid CallerUserId, Guid IssuerUserId, Guid IssuerRoleGrantId,
        bool IssuerPlatformGrant, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt, long Revision)
    {
        public static ReadGrantCoordinates From(ModerationCaseReadGrant grant) =>
            new(grant.Id, grant.TenantId, grant.TrustDomainId, grant.CaseId, grant.CaseRevision, grant.SourceId,
                grant.CallerUserId, grant.IssuerUserId, grant.IssuerRoleGrantId, grant.IssuerPlatformGrant,
                grant.IssuedAt, grant.ExpiresAt, grant.RevokedAt, grant.Revision);
    }

    private sealed record ReadSourceCoordinates(Guid Id, Guid Version, Guid TenantId, Guid TrustDomainId,
        Guid? EventId, Guid? EventVersion, ModerationCaseTargetKind Kind, ModerationCaseTargetKind NativeKind,
        Guid NativeTargetId, Guid NativeTargetVersion, string NativeFactsDigest, Guid OccurrenceId,
        Guid OccurrenceVersion, Guid MaterialFactId, string OccurrenceDigest, string MaterialDigest, DateTimeOffset RecordedAt)
    {
        public static ReadSourceCoordinates From(ModerationCaseTargetSource source) =>
            new(source.Id, source.Version, source.TenantId, source.TrustDomainId, source.EventId, source.EventVersion,
                source.Kind, source.NativeKind, source.NativeTargetId, source.NativeTargetVersion, source.NativeFactsDigest,
                source.OccurrenceId, source.OccurrenceVersion, source.MaterialFactId, source.OccurrenceDigest,
                source.MaterialDigest, source.RecordedAt);
    }
}
