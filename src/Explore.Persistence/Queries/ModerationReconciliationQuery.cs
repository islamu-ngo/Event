using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Scheduling;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Queries;

/// <summary>Bounded discovery inside explicitly selected native tenancy, retaining every authority query filter.</summary>
public sealed class ModerationReconciliationQuery(
    ExploreDbContext context, IMachinePrincipalAccessor machine, IModerationMachineSubjectBindingRepository bindings)
    : IModerationReconciliationQuery
{
    public async Task<ModerationReconciliationBatch> ReadDueAsync(
        Guid tenantId, DateTimeOffset now, int batchSize, Guid? afterSanctionId, CancellationToken cancellationToken)
    {
        var host = await ReadHostAsync(tenantId, batchSize, cancellationToken);
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        var tenant = AuthorityScope.Tenant(instance, tenantId);
        try
        {
            context.ModerationTrustDomainId = host.TrustDomainId;
            var due = ModerationReconciliationTemporalQueries.DueSanctions(context, host, tenantId, now);
            var rows = await (
                from sanction in due.AsNoTracking()
                join decision in context.ModerationDecisions.AsNoTracking() on sanction.DecisionId equals decision.Id
                where sanction.ScopeTrustDomainId == host.TrustDomainId
                    && sanction.ScopeSolutionId == host.SolutionId && sanction.ScopeInstanceId == host.InstanceId
                    && (sanction.ScopeTenantId == tenantId || sanction.ScopeKind == ModerationScopeKind.Instance)
                    && (sanction.ScopeKind == ModerationScopeKind.Instance || sanction.ScopeKind == ModerationScopeKind.Tenant
                        || sanction.ScopeKind == ModerationScopeKind.Resource && sanction.ScopeCommunityId == null)
                    && (decision.State == ModerationDecisionState.Scheduled || decision.State == ModerationDecisionState.Active)
                    && decision.ScopeTrustDomainId == sanction.ScopeTrustDomainId
                    && decision.ScopeSolutionId == sanction.ScopeSolutionId && decision.ScopeInstanceId == sanction.ScopeInstanceId
                    && decision.ScopeTenantId == sanction.ScopeTenantId && decision.ScopeCommunityId == sanction.ScopeCommunityId
                    && decision.ScopeResourceId == sanction.ScopeResourceId && decision.ScopeEventId == sanction.ScopeEventId
                    && decision.ScopeKind == sanction.ScopeKind
                    && decision.SubjectKind == sanction.SubjectKind && decision.SubjectId == sanction.SubjectId
                    && decision.SubjectOriginKind == sanction.SubjectOriginKind
                    && decision.SubjectOriginTrustDomainId == sanction.SubjectOriginTrustDomainId
                    && decision.SubjectOriginSolutionId == sanction.SubjectOriginSolutionId
                    && decision.SubjectOriginInstanceId == sanction.SubjectOriginInstanceId
                    && decision.SubjectOriginTenantId == sanction.SubjectOriginTenantId
                    && decision.SubjectOriginCommunityId == sanction.SubjectOriginCommunityId
                    && decision.SubjectOriginResourceId == sanction.SubjectOriginResourceId
                    && decision.SubjectOriginEventId == sanction.SubjectOriginEventId
                    && decision.RuleOwnerId == sanction.RuleOwnerId && decision.RuleCode == sanction.RuleCode
                    && decision.RuleRevision == sanction.RuleRevision
                    && (afterSanctionId == null || sanction.Id.CompareTo(afterSanctionId.Value) > 0)
                orderby sanction.Id
                select new
                {
                    sanction.Id,
                    sanction.DecisionId,
                    SanctionRevision = sanction.Revision,
                    DecisionRevision = decision.Revision,
                    sanction.SubjectKind,
                    sanction.SubjectId,
                    sanction.SubjectOriginKind,
                    sanction.SubjectOriginTrustDomainId,
                    sanction.SubjectOriginSolutionId,
                    sanction.SubjectOriginInstanceId,
                    sanction.SubjectOriginTenantId,
                    sanction.SubjectOriginCommunityId,
                    sanction.SubjectOriginResourceId,
                    sanction.SubjectOriginEventId,
                    sanction.ScopeKind,
                    sanction.ScopeTrustDomainId,
                    sanction.ScopeSolutionId,
                    sanction.ScopeInstanceId,
                    sanction.ScopeTenantId,
                    sanction.ScopeCommunityId,
                    sanction.ScopeResourceId,
                    sanction.ScopeEventId,
                    sanction.RuleOwnerId,
                    sanction.RuleCode,
                    sanction.RuleRevision,
                    sanction.StartsAt,
                    sanction.EndsAt,
                    sanction.State
                }).Take(batchSize).ToListAsync(cancellationToken);
            var targets = new List<ModerationReconciliationTarget>();
            foreach (var row in rows)
            {
                var scope = Coordinates(row.ScopeKind, row.ScopeTrustDomainId, row.ScopeSolutionId,
                    row.ScopeInstanceId, row.ScopeTenantId, row.ScopeCommunityId, row.ScopeResourceId, row.ScopeEventId);
                if (scope is null || scope != instance && !tenant.Contains(scope)) continue;
                var origin = Coordinates(row.SubjectOriginKind, row.SubjectOriginTrustDomainId,
                    row.SubjectOriginSolutionId, row.SubjectOriginInstanceId, row.SubjectOriginTenantId,
                    row.SubjectOriginCommunityId, row.SubjectOriginResourceId, row.SubjectOriginEventId);
                if (origin is null || row.SubjectId == Guid.Empty || !Enum.IsDefined(row.SubjectKind)) continue;
                var subject = SubjectReference.Create(row.SubjectKind, row.SubjectId, origin);
                var jurisdiction = row.RuleOwnerId == instance.Id ? instance
                    : row.RuleOwnerId == tenant.Id ? tenant : null;
                if (jurisdiction is null || !jurisdiction.Contains(scope)) continue;
                Guid? jurisdictionTenantId = jurisdiction.Kind == ModerationScopeKind.Tenant ? tenantId : null;
                var selection = await context.ModerationPolicySelections.AsNoTracking().SingleOrDefaultAsync(
                    policy => policy.HostBindingId == host.Id && policy.TrustDomainId == host.TrustDomainId
                        && policy.JurisdictionId == jurisdiction.Id
                        && policy.JurisdictionTenantId == jurisdictionTenantId, cancellationToken);
                if (selection is null) continue;
                var publications = await ModerationPolicyPublicationGraph.Include(
                    context.ModerationPolicyPublications.AsNoTracking().Where(publication =>
                        publication.PolicyId == selection.Id && publication.HostBindingId == host.Id
                        && publication.TrustDomainId == host.TrustDomainId
                        && publication.SolutionId == host.SolutionId && publication.InstanceId == host.InstanceId
                        && publication.JurisdictionTenantId == jurisdictionTenantId
                        && (publication.Id == selection.LatestPublicationId
                            || publication.Id == selection.ActivePublicationId)))
                    .ToListAsync(cancellationToken);
                // Latest definitions may retire support without erasing existing sanctions.
                // A later draft must also leave the previously selected active declaration available.
                // Neither retained publication evidence nor discovery certifies live activation.
                var needsActivation = row.State == ModerationSanctionState.Scheduled
                    && row.StartsAt <= now && (row.EndsAt is null || row.EndsAt > now);
                foreach (var publication in publications.OrderByDescending(
                    publication => publication.Id == (needsActivation
                        ? selection.ActivePublicationId ?? selection.LatestPublicationId
                        : selection.LatestPublicationId)))
                {
                    if (publication.Revision < 1 || publication.Revision > selection.Revision
                        || publication.Id == selection.LatestPublicationId && publication.Revision != selection.Revision
                        || publication.Id != selection.LatestPublicationId && !publication.PublishedActive
                        || publication.Jurisdiction != jurisdiction) continue;
                    Explore.Application.Features.Moderation.Policy.ModerationPolicyCompiledDraft? draft;
                    try
                    {
                        draft = ModerationPolicyPublicationProjection.RestoreHistory(publication);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }
                    var profile = draft?.Profiles.SingleOrDefault(profile =>
                        profile.Definition.Rule.OwnerId == row.RuleOwnerId
                        && profile.Definition.Rule.Code == row.RuleCode
                        && profile.Definition.Rule.Revision == row.RuleRevision);
                    if (profile is null || !profile.Definition.ScopeKinds.Contains(scope.Kind)
                        || !profile.Definition.SubjectKinds.Contains(subject.Kind)
                        || !profile.Reference.AcceptsScope(jurisdiction, scope)) continue;
                    targets.Add(new(host.Id, tenantId, host.MaintenanceServicePrincipalId, subject, scope,
                        row.DecisionId, row.DecisionRevision, row.Id, row.SanctionRevision,
                        publication.PolicyId, publication.Revision,
                        row.EndsAt is { } end && end <= now ? end : row.StartsAt));
                    break;
                }
            }
            return new(targets.AsReadOnly(), rows.Count == 0 ? null : rows[^1].Id);
        }
        finally
        {
            context.ModerationTrustDomainId = null;
        }
    }

    public async Task<ModerationEffectRecoveryBatch> ReadRecoverableEffectsAsync(
        Guid tenantId, DateTimeOffset now, int batchSize, Guid? afterEffectId, CancellationToken cancellationToken)
    {
        var host = await ReadHostAsync(tenantId, batchSize, cancellationToken);
        try
        {
            context.ModerationTrustDomainId = host.TrustDomainId;
            var pending = ModerationReconciliationTemporalQueries.RecoverableEffects(context, now);
            var rows = await (
                from effect in pending.AsNoTracking()
                join decision in context.ModerationDecisions.AsNoTracking() on effect.DecisionId equals decision.Id
                where effect.TrustDomainId == host.TrustDomainId
                    && decision.ScopeTrustDomainId == host.TrustDomainId
                    && decision.ScopeSolutionId == host.SolutionId && decision.ScopeInstanceId == host.InstanceId
                    && (decision.ScopeTenantId == tenantId || decision.ScopeKind == ModerationScopeKind.Instance)
                    && effect.ScopeKind == (int)decision.ScopeKind
                    && effect.SubjectKind == (int)decision.SubjectKind && effect.SubjectId == decision.SubjectId
                    && (decision.ScopeKind != ModerationScopeKind.Instance
                        || context.ModerationNoticeSources.Any(source =>
                            source.EffectId == effect.Id && source.TenantId == tenantId))
                    && (afterEffectId == null || effect.Id.CompareTo(afterEffectId.Value) > 0)
                    && !context.OutboxMessages.Any(message => message.Id == effect.Id
                        && message.Status != OutboxMessageStatus.Completed)
                orderby effect.Id
                select new ModerationEffectRecoveryTarget(effect.Id, effect.CreatedAt))
                .Take(batchSize).ToListAsync(cancellationToken);
            // Discovery never establishes completion or source authority. A denied leading
            // effect still advances the scan, without changing its intent or outbox status.
            return new(rows.AsReadOnly(), rows.Count == 0 ? null : rows[^1].EffectId);
        }
        finally
        {
            context.ModerationTrustDomainId = null;
        }
    }

    private async Task<ModerationHostBinding> ReadHostAsync(
        Guid tenantId, int batchSize, CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (tenantId == Guid.Empty || context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed
            || context.ModerationTrustDomainId is not null || context.ChangeTracker.Entries().Any()
            || machine.Current is not { CredentialSource: ApiKeyCredentialSource.Persisted } principal)
            throw new InvalidOperationException("Moderation reconciliation discovery requires a clean authenticated tenant scope.");
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var binding = await bindings.ReadCurrentAsync(
            principal.KeyId, ModerationMachineCredentialSource.Persisted, cancellationToken);
        if (host is null || binding is null || binding.HostBindingId != host.Id
            || binding.ServicePrincipalId != host.MaintenanceServicePrincipalId
            || binding.AuthorityTenantId != principal.TenantId || binding.OwnerType != principal.OwnerType
            || binding.OwnerId != principal.OwnerId
            || principal.TenantId is { } credentialTenant && credentialTenant != tenantId
            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(
                row => row.HostBindingId == host.Id && row.TenantId == tenantId && row.Revision > 0, cancellationToken)
            || !await context.Tenants.AsNoTracking().AnyAsync(
                row => row.Id == tenantId && row.TenantStatus.IsActiveState, cancellationToken))
            throw new InvalidOperationException("Moderation reconciliation maintenance installation or tenant qualification is unavailable.");
        return host;
    }

    private static AuthorityScope? Coordinates(
        ModerationScopeKind kind, Guid trustDomainId, Guid? solutionId, Guid? instanceId,
        Guid? tenantId, Guid? communityId, Guid? resourceId, Guid? eventId)
    {
        // Every parent is an actual persisted coordinate, never inferred from a target's GUID.
        try
        {
            return AuthorityScope.FromCoordinates(kind, trustDomainId, solutionId, instanceId,
                tenantId, communityId, resourceId, eventId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
