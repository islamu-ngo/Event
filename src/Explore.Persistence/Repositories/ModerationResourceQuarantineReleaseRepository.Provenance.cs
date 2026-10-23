using System.Data;
using Explore.Domain;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationResourceQuarantineReleaseRepository
{
    private IDbContextTransaction? _resourceSafetyTransaction;
    private object? _resourceSafetyOperation;
    private readonly Dictionary<Guid, ResourceSafetyProvenanceSession> _resourceSafetySessions = [];
    private DateTimeOffset ResourceSafetyTime => clock.GetUtcNow();

    private ResourceSafetyProvenanceSession OpenResourceSafetyProvenance(
        object operation, ModerationSourceAuthority authority, ModerationCase allegation,
        EventResourceModerationQuarantine cause, ModerationResourceGeneration native,
        ModerationQualificationPolicySource policy, ModerationQualificationPolicyAdoption adoption,
        IReadOnlyDictionary<Guid, ModerationReviewerAssignment> reviewers,
        IReadOnlyList<EventResourceModerationQuarantineReview> chain, Guid? assignmentId,
        DateTimeOffset now, DateTimeOffset until, Guid evidenceResourceStamp, Guid? ownWithdrawalId)
    {
        var transaction = context.Database.CurrentTransaction ?? throw new SafetyUnavailable();
        if (!ReferenceEquals(_resourceSafetyTransaction, transaction))
        {
            _resourceSafetySessions.Clear();
            _resourceSafetyTransaction = transaction;
            _resourceSafetyOperation = operation;
        }
        if (!ReferenceEquals(_resourceSafetyOperation, operation)) throw new SafetyUnavailable();
        var session = new ResourceSafetyProvenanceSession(this, transaction, operation, authority,
            allegation, cause, native, policy, adoption, reviewers, chain, assignmentId, now, until,
            evidenceResourceStamp, ownWithdrawalId);
        _resourceSafetySessions[cause.Id] = session;
        return session;
    }

    /// <summary>
    /// Metadata custody of this repository's actual live acquisition. It cannot be supplied
    /// through an application port, mint a disclosure receipt, or authorize a finding.
    /// The owner registers only sessions created after current safety qualification.
    /// </summary>
    internal sealed class ResourceSafetyProvenanceSession(
        ModerationResourceQuarantineReleaseRepository owner, IDbContextTransaction transaction,
        object operation, ModerationSourceAuthority authority, ModerationCase allegation,
        EventResourceModerationQuarantine cause, ModerationResourceGeneration native,
        ModerationQualificationPolicySource policy, ModerationQualificationPolicyAdoption adoption,
        IReadOnlyDictionary<Guid, ModerationReviewerAssignment> reviewers,
        IReadOnlyList<EventResourceModerationQuarantineReview> chain, Guid? assignmentId,
        DateTimeOffset observedAt, DateTimeOffset validUntil, Guid evidenceResourceStamp, Guid? ownWithdrawalId)
    {
        internal IDbContextTransaction Transaction => transaction;
        internal object Operation => operation;
        internal ModerationSourceAuthority Authority => authority;
        internal ModerationCase Case => allegation;
        internal EventResourceModerationQuarantine Cause => cause;
        internal ModerationResourceGeneration Native => native;
        internal ModerationQualificationPolicySource Policy => policy;
        internal ModerationQualificationPolicyAdoption Adoption => adoption;
        internal IReadOnlyDictionary<Guid, ModerationReviewerAssignment> Reviewers => reviewers;
        internal IReadOnlyList<EventResourceModerationQuarantineReview> Chain => chain;
        internal Guid? AssignmentId => assignmentId;
        internal DateTimeOffset ObservedAt => observedAt;
        internal DateTimeOffset ValidUntil => validUntil;
        internal Guid EvidenceResourceStamp => evidenceResourceStamp;
        internal Guid? OwnWithdrawalId => ownWithdrawalId;
        internal DateTimeOffset CurrentTime => owner.ResourceSafetyTime;

        internal Task<bool> IsCurrentAsync(ExploreDbContext receivingContext, CancellationToken token) =>
            owner.ResourceSafetySessionCurrentAsync(this, receivingContext, token);
    }

    private async Task<bool> ResourceSafetySessionCurrentAsync(
        ResourceSafetyProvenanceSession session, ExploreDbContext receivingContext, CancellationToken token)
    {
        var authority = session.Authority;
        var native = session.Native;
        if (!ReferenceEquals(context, receivingContext)
            || !ReferenceEquals(context.Database.CurrentTransaction, session.Transaction)
            || !ReferenceEquals(_resourceSafetyTransaction, session.Transaction)
            || !ReferenceEquals(_resourceSafetyOperation, session.Operation)
            || !_resourceSafetySessions.TryGetValue(session.Cause.Id, out var held) || !ReferenceEquals(held, session)
            || session.Transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId != authority.Tenant.Id
            || context.ModerationTrustDomainId != authority.Binding.TrustDomainId
            || machine.IsMachineCaller || context.ChangeTracker.HasChanges()
            || await admin.ResolveUserIdAsync(token) != authority.UserId
            || clock.GetUtcNow() < session.ObservedAt || clock.GetUtcNow() >= session.ValidUntil)
            return false;
        var humans = await DiscoverResourceSafetyHumansAsync(
            native.ResourceId, session.Case.Id, session.AssignmentId, session.Chain, token, includeMaterialHumans: false);
        if (!humans.SequenceEqual(authority.FencedHumans.Order())
            || await context.Users.AsNoTracking().CountAsync(row => humans.Contains(row.Id) && !row.IsDeleted, token) != humans.Length
            || await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => humans.Contains(row.SubjectId), token))
            return false;
        // These are already-held human, role, host, service, tenant and epoch fences.
        // Discovery equality above rejects a new human before any reacquisition.
        var current = await new ModerationQualificationAuthority(context, admin, machine)
            .AcquireAsync(AuthorityScope.Resource(authority.Tenant, native.ResourceId), token);
        if (current is null || current.UserId != authority.UserId || current.Tenant != authority.Tenant
            || current.Binding.Id != authority.Binding.Id || current.Binding.TrustDomainId != authority.Binding.TrustDomainId
            || current.Binding.SolutionId != authority.Binding.SolutionId || current.Binding.InstanceId != authority.Binding.InstanceId
            || current.Binding.MaintenanceServicePrincipalId != authority.Binding.MaintenanceServicePrincipalId
            || current.Epoch.Id != authority.Epoch.Id || current.Epoch.Revision != authority.Epoch.Revision)
            return false;
        foreach (var human in authority.Administrators)
        {
            if (!await context.PlatformUserRoles.AsNoTracking().AnyAsync(row => row.UserId == human
                    && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin", token)
                && !await context.TenantUserRoleGrants.AsNoTracking().AnyAsync(row =>
                    row.TenantId == authority.Tenant.Id && row.TenantUser.UserId == human && row.RevokedAt == null
                    && row.RoleScopeId == (int)RoleScopeEnum.Tenant && row.RoleId == (int)RoleEnum.TenantAdmin
                    && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant
                    && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active && !row.TenantUser.IsDeleted, token))
                return false;
        }
        var policy = await context.ModerationQualificationPolicySources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == session.Policy.Id, token);
        var adoption = await context.ModerationQualificationPolicyAdoptions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == session.Adoption.Id, token);
        if (policy is null || adoption is null || Digest(policy) != Digest(session.Policy)
            || Digest(adoption) != Digest(session.Adoption)
            || !adoption.MatchesCurrent(policy, session.Case.Revision, clock.GetUtcNow()))
            return false;
        foreach (var reviewer in session.Reviewers.Values)
        {
            var actual = await ReadReviewerAsync(reviewer.Id, session.Case, policy, authority, clock.GetUtcNow(), token);
            if (Digest(actual) != Digest(reviewer)) return false;
        }
        foreach (var row in session.Chain)
        {
            var persisted = await context.Set<EventResourceModerationQuarantineReview>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == row.Id && value.TenantId == authority.Tenant.Id, token);
            if (persisted is null || Digest(persisted) != Digest(row) || persisted.ValidUntil <= clock.GetUtcNow())
                return false;
        }
        if (session.Chain.Count != 0 && await context.Set<EventResourceModerationQuarantineReview>().AsNoTracking()
            .AnyAsync(row => row.TenantId == authority.Tenant.Id && row.PurposeId == session.Chain[0].Id
                && row.Step == ResourceSafetyReviewStep.WithdrawPurpose && row.Id != session.OwnWithdrawalId, token))
            return false;
        var material = await ModerationResourceGenerationReader.AcquireAsync(context, session.Cause.Id, native.ResourceId,
            native.HostBindingId, native.TrustDomainId, native.TenantId, 1, authority.UserId, authority.Epoch.Revision,
            session.ObservedAt, session.ValidUntil, token, expected: native);
        return material is not null && native.HasSameNativeVersion(material)
            && await context.EventResourceModerationQuarantines.AsNoTracking().AnyAsync(row =>
                row.Id == session.Cause.Id && row.TenantId == native.TenantId && row.EventId == native.EventId
                && row.EventResourceId == native.ResourceId && row.MaterialVersionId == native.MaterialVersionId
                && row.EffectId == session.Cause.EffectId && row.BindingOrdinal == session.Cause.BindingOrdinal
                && row.AppliedAt == session.Cause.AppliedAt && row.ReleaseId == session.Cause.ReleaseId, token)
            && clock.GetUtcNow() >= session.ObservedAt && clock.GetUtcNow() < session.ValidUntil;
    }

    private async Task<bool> RecheckResourceSafetyProvenanceAsync(
        QualifiedSafety acquired, QualifiedSafety current, CancellationToken token)
    {
        if (acquired.Attribution != current.Attribution) return false;
        if (current.Provenance is { } session
            && await ModerationCaseTargetProofOwner.AcquireResourceSafetyAttributionAsync(context, session, token)
                != acquired.Attribution)
            return false;
        return clock.GetUtcNow() < acquired.ValidUntil && clock.GetUtcNow() < current.ValidUntil;
    }
}
