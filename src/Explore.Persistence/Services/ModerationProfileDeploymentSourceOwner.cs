using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

/// <summary>
/// Joins the original lifecycle's adopted contribution configuration to independently current,
/// separately adopted native deployment. This read supplies no prerequisite or conformance verdict.
/// </summary>
public sealed class ModerationProfileDeploymentSourceOwner(
    ExploreDbContext context, ModerationDeploymentSourceOwner deployments, TimeProvider clock)
{
    public async Task<ModerationActivationProfileObservation?> AcquireProfileAsync(
        IModerationConsumerEvidenceSession evidenceSession,
        Guid hostBindingId, Guid transactionId, ModerationPolicyCompiledDraft policy,
        ModerationPolicyCompiledProfile profile, ModerationDecision decision,
        DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        if (evidenceSession is not ModerationConsumerEvidenceSession session
            || await session.AcquireNativeAsync(transactionId, observedAt, cancellationToken) is not { } native
            || !ReferenceEquals(native.Context, context) || !ReferenceEquals(native.Policy, policy)
            || !ReferenceEquals(native.Transaction, context.Database.CurrentTransaction)
            || native.Authority.Binding.Id != hostBindingId || policy.Publication.HostBindingId != hostBindingId
            || !policy.Profiles.Contains(profile) || decision.Rule != profile.Definition.Rule
            || !policy.Proposed.SupportedProfiles.Contains(profile.Definition.Rule)
            || !ModerationNativeActivationQualification.Permits(policy, profile, decision.Subject, decision.Scope,
                decision.Capabilities, decision.StartsAt, decision.EndsAt, decision.ReviewAt, decision.RequiredApprovals)
            || !await AuthorityCurrentAsync())
            return null;

        // The original session's lifecycle callback retains the contribution/profile writer fences.
        // A publication borrow has no callback and must never be upgraded into lifecycle authority.
        var publication = await context.ModerationPolicyPublications.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == policy.Publication.PublicationId, cancellationToken);
        if (publication is null) return null;
        var configuration = await session.ReadProfileConfigurationAsync(
            transactionId, publication, decision, observedAt, cancellationToken);
        if (configuration is null || !MatchesConfiguration(configuration)) return null;
        var configurationDigest = ModerationPolicyPublicationProjection.Digest(configuration);

        var current = await deployments.AcquireSourceAsync(
            evidenceSession, transactionId, profile.Definition.Id, configuration.Mode, observedAt, cancellationToken);
        if (current is null || current.TransactionId != transactionId || current.ObservedAt != observedAt
            || current.Source.HostBindingId != hostBindingId || current.Source.PolicyId != policy.Publication.PolicyId
            || current.Source.PolicyRevision != policy.Publication.ProposedRevision
            || current.Source.PublicationId != policy.Publication.PublicationId
            || current.Source.ProfileId != profile.Definition.Id || current.Source.ModeCode != (int)configuration.Mode
            || current.Source.TrustDomainId != native.Authority.Binding.TrustDomainId
            || current.Source.TenantId != native.Authority.Tenant.Id
            || current.Source.ProposedStateDigest != ModerationPolicyPublicationProjection.ProposedDigest(policy.Proposed)
            || current.Source.InventoryFingerprint != policy.Proposed.InventoryFingerprint
            || current.ValidUntil != current.Source.ValidUntil)
            return null;
        var sourceDigest = ModerationPolicyPublicationProjection.Digest(current.Source);
        var adopted = await deployments.ReadAdoptionAsync(
            evidenceSession, transactionId, profile.Definition.Id, configuration.Mode, observedAt, cancellationToken);
        if (adopted is null || adopted.TransactionId != transactionId || adopted.ObservedAt != observedAt
            || adopted.ValidUntil != adopted.Adoption.ValidUntil
            || !adopted.Adoption.MatchesCurrent(current.Source, observedAt))
            return null;
        var adoptionDigest = ModerationPolicyPublicationProjection.Digest(adopted.Adoption);
        var until = new[] { configuration.ValidUntil, current.ValidUntil, adopted.ValidUntil }.Min();

        var repeatedConfiguration = await session.ReadProfileConfigurationAsync(
            transactionId, publication, decision, observedAt, cancellationToken);
        if (repeatedConfiguration is null
            || ModerationPolicyPublicationProjection.Digest(repeatedConfiguration) != configurationDigest)
            return null;
        // Reread deployment after the configuration reread: runtime artifacts are host-owned,
        // not relational rows, and must still match the retained complete native coverage.
        var repeatedSource = await deployments.AcquireSourceAsync(
            evidenceSession, transactionId, profile.Definition.Id, configuration.Mode, observedAt, cancellationToken);
        var repeatedAdoption = await deployments.ReadAdoptionAsync(
            evidenceSession, transactionId, profile.Definition.Id, configuration.Mode, observedAt, cancellationToken);
        if (repeatedSource is null || repeatedAdoption is null
            || ModerationPolicyPublicationProjection.Digest(repeatedSource.Source) != sourceDigest
            || ModerationPolicyPublicationProjection.Digest(repeatedAdoption.Adoption) != adoptionDigest
            || !repeatedAdoption.Adoption.MatchesCurrent(repeatedSource.Source, clock.GetUtcNow())
            || !await AuthorityCurrentAsync()
            || await session.AcquireNativeAsync(transactionId, observedAt, cancellationToken) is not { } completed
            || !ReferenceEquals(completed.Context, native.Context)
            || !ReferenceEquals(completed.Transaction, native.Transaction)
            || !ReferenceEquals(completed.Authority, native.Authority)
            || !ReferenceEquals(completed.Policy, native.Policy))
            return null;
        var completedAt = clock.GetUtcNow();
        return completedAt >= observedAt && completedAt < until
            ? configuration with { ValidUntil = until } : null;

        async Task<bool> AuthorityCurrentAsync()
        {
            // The original acquisition already fenced these humans and grants. Reread their
            // substantive roles without acquiring a replacement authority or widening its set.
            var ids = native.Authority.Administrators.ToArray();
            var platform = await context.PlatformUserRoles.AsNoTracking()
                .Where(row => ids.Contains(row.UserId) && !row.User.IsDeleted
                    && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin")
                .Select(row => row.UserId).Distinct().ToListAsync(cancellationToken);
            var tenant = await context.TenantUserRoleGrants.AsNoTracking()
                .Where(row => row.TenantId == native.Authority.Tenant.Id && ids.Contains(row.TenantUser.UserId)
                    && row.RevokedAt == null && row.RoleScopeId == (int)RoleScopeEnum.Tenant
                    && row.RoleId == (int)RoleEnum.TenantAdmin && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant
                    && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                    && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted)
                .Select(row => row.TenantUser.UserId).Distinct().ToListAsync(cancellationToken);
            return platform.Contains(native.Authority.UserId) == native.Authority.IsInstanceAdmin
                && ids.All(id => native.Authority.FencedHumans.Contains(id)
                    && (platform.Contains(id) || tenant.Contains(id)));
        }

        bool MatchesConfiguration(ModerationActivationProfileObservation value)
        {
            var requirement = policy.Publication;
            var prerequisites = policy.Prerequisites.SingleOrDefault(row =>
                row.Rule == profile.Definition.Rule && row.Mode == value.Mode);
            if (value.TransactionId != transactionId || value.HostBindingId != hostBindingId
                || value.PolicyId != requirement.PolicyId || value.PolicyRevision != requirement.ProposedRevision
                || value.PublicationId != requirement.PublicationId || value.Jurisdiction != requirement.Jurisdiction
                || value.ProfileId != profile.Definition.Id || value.Rule != profile.Definition.Rule
                || value.DecisionId != decision.Id || value.DecisionRevision != decision.Revision
                || value.ObservedAt != observedAt || !Enum.IsDefined(value.Mode) || !policy.Proposed.Modes.Contains(value.Mode)
                || value.SourceId == Guid.Empty || value.SourceId.Version != 7 || value.SourceId.Variant is < 8 or > 11
                || value.SourceRevision < 1 || string.IsNullOrWhiteSpace(value.WriterGeneration)
                || value.WriterGeneration.Length > 256 || value.WriterGeneration != value.WriterGeneration.Trim()
                || value.WriterGeneration.Any(char.IsControl)
                || value.ValidUntil.Offset != TimeSpan.Zero || value.ValidUntil == DateTimeOffset.MaxValue
                || value.ValidUntil <= observedAt
                || prerequisites is null || value.RequiredPrerequisites.IsDefaultOrEmpty
                || value.RequiredPrerequisites.Any(item => !Enum.IsDefined(item))
                || value.RequiredPrerequisites.Distinct().Count() != value.RequiredPrerequisites.Length
                || !value.RequiredPrerequisites.Contains(ModerationActivationPrerequisite.ConstitutionAndReviewers)
                || !value.RequiredPrerequisites.Contains(ModerationActivationPrerequisite.MeasuredOperations)
                || !value.RequiredPrerequisites.Order().SequenceEqual(prerequisites.ActivationGates.Order())
                || value.Capabilities.IsDefaultOrEmpty || value.Capabilities.Any(item => !Enum.IsDefined(item))
                || value.Capabilities.Distinct().Count() != value.Capabilities.Length
                || !value.Capabilities.Order().SequenceEqual(decision.Capabilities.Order())
                || value.Contribution is not { } contribution || contribution.Id != decision.Id
                || contribution.Rule != decision.Rule || contribution.Scope != decision.Scope
                || contribution.SubjectKind != decision.Subject.Kind || contribution.SubjectId != decision.Subject.Id
                || contribution.StartsAt != decision.StartsAt)
                return false;
            var composed = ModerationPolicyProposedStateCompiler.Compose(policy, [contribution], observedAt);
            return composed.Outcome == ModerationPolicyCompositionOutcome.Preview
                && !composed.Effects.IsEmpty && !composed.Consequences.IsEmpty
                && composed.Effects.All(row => row.Interval.StartsAt == decision.StartsAt
                    && row.Interval.EndsAt == decision.EndsAt && row.Interval.ReviewAt == decision.ReviewAt)
                && composed.Consequences.All(row => row.Interval.StartsAt == decision.StartsAt
                    && row.Interval.EndsAt == decision.EndsAt && row.Interval.ReviewAt == decision.ReviewAt);
        }
    }
}
