using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

/// <summary>
/// Requalifies substantive governance on the original native acquisition. Constitution adoption
/// proves neither consumer conformance nor case-specific adjudication or publication permission.
/// </summary>
public sealed class ModerationConstitutionSourceOwner(
    ExploreDbContext context, ModerationPolicyDefinitionSourceOwner definitions,
    TimeProvider clock)
{
    public Task<ModerationConstitutionSourceObservation?> AcquireSourceAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token) =>
        AcquireSourceAsync(ModerationSourceReadSession.Authenticated(evidenceSession),
            transactionId, profileId, mode, observedAt, token);

    internal async Task<ModerationConstitutionSourceObservation?> AcquireSourceAsync(
        ModerationSourceReadSession? session, Guid transactionId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token)
    {
        if (session is null
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context)) return null;
        var requirement = native.Policy.Publication;
        var original = native.OriginalRecipe;
        if (original is not null && (original.Binding.Mode != mode
            || !native.Policy.Profiles.Any(row => row.Definition.Id == profileId
                && row.Definition.Rule == original.Binding.Rule))) return null;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            Slot(native.Authority, requirement.PublicationId, profileId, mode), token);
        var id = await context.Set<ModerationConstitutionSource>().AsNoTracking()
            .Where(row => row.HostBindingId == native.Authority.Binding.Id && row.TenantId == native.Authority.Tenant.Id
                && row.PublicationId == requirement.PublicationId && row.ProfileId == profileId && row.ModeCode == (int)mode
                && (row.OriginalEffectId == null || original != null
                    && row.OriginalEffectId == original.Anchor.EffectId
                    && row.OriginalSnapshotHash == original.Anchor.SnapshotHash
                    && row.OriginalEffectBindingHash == original.Anchor.EffectBindingHash))
            .OrderByDescending(row => row.WriterGeneration).Select(row => (Guid?)row.Id).FirstOrDefaultAsync(token);
        if (id is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConstitutionSource>(context, id.Value, token);
        var source = await context.Set<ModerationConstitutionSource>().AsNoTracking()
            .Include(row => row.Reviewers.OrderBy(item => item.Ordinal)).SingleOrDefaultAsync(row => row.Id == id, token);
        if (source is null) return null;
        source.ValidatePersistedState();
        if (source.HostBindingId != native.Authority.Binding.Id || source.TrustDomainId != native.Authority.Binding.TrustDomainId
            || source.TenantId != native.Authority.Tenant.Id || source.PolicyId != requirement.PolicyId
            || source.PolicyRevision != requirement.ProposedRevision || source.PublicationId != requirement.PublicationId
            || source.ProfileId != profileId || source.ModeCode != (int)mode
            || !MatchesPurpose(source, original)
            || source.RecordedAt > observedAt || source.ValidUntil <= observedAt
            || source.WriterGeneration > native.Authority.Epoch.Revision
            || !await AdministratorAsync(native.Authority, source.RecordedByHumanId, requirement.Jurisdiction, token)
            || !await HasReceiptAsync(native.Authority, RecordRequest(source, requirement.Jurisdiction),
                ModerationQualificationSourceKind.ConstitutionSource, source.Id, 1, source.RecordedByHumanId,
                source.WriterGeneration, source.RecordedAt, token)) return null;
        var live = await ObserveAsync(session, transactionId, profileId, mode, source.Terms,
            source.Reviewers.Select(row => row.ToTerms()).ToImmutableArray(), source.RecordedByHumanId, observedAt, token);
        if (live is null || source.DefinitionId != live.DefinitionId || source.ValidUntil > live.ValidUntil
            || source.ProposedStateDigest != ModerationPolicyPublicationProjection.ProposedDigest(native.Policy.Proposed)
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null
            || clock.GetUtcNow() >= source.ValidUntil) return null;
        return new(transactionId, source, observedAt,
            original is not null && original.ValidUntil < source.ValidUntil ? original.ValidUntil : source.ValidUntil);
    }

    public Task<ModerationConstitutionAdoptionObservation?> ReadAdoptionAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token) =>
        ReadAdoptionAsync(ModerationSourceReadSession.Authenticated(evidenceSession),
            transactionId, profileId, mode, observedAt, token);

    internal async Task<ModerationConstitutionAdoptionObservation?> ReadAdoptionAsync(
        ModerationSourceReadSession? session, Guid transactionId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token)
    {
        var current = await AcquireSourceAsync(session, transactionId, profileId, mode, observedAt, token);
        if (current is null || session is null
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native) return null;
        var id = await context.Set<ModerationConstitutionAdoption>().AsNoTracking()
            .Where(row => row.SourceId == current.Source.Id).Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (id is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConstitutionAdoption>(context, id.Value, token);
        var adoption = await context.Set<ModerationConstitutionAdoption>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id, token);
        if (adoption is null) return null;
        adoption.ValidatePersistedState();
        if (!adoption.MatchesCurrent(current.Source, observedAt) || adoption.WriterGeneration > native.Authority.Epoch.Revision
            || !await AdministratorAsync(native.Authority, adoption.AdoptedByHumanId, native.Policy.Publication.Jurisdiction, token)
            || !await HasReceiptAsync(native.Authority, AdoptionRequest(adoption, native.Policy.Publication.Jurisdiction, current.Source),
                ModerationQualificationSourceKind.ConstitutionAdoption, adoption.Id, 1, adoption.AdoptedByHumanId,
                adoption.WriterGeneration, adoption.AdoptedAt, token)) return null;
        var completed = await AcquireSourceAsync(session, transactionId, profileId, mode, observedAt, token);
        if (completed is null || !adoption.MatchesCurrent(completed.Source, clock.GetUtcNow())
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null) return null;
        return new(transactionId, adoption, observedAt,
            current.ValidUntil < adoption.ValidUntil ? current.ValidUntil : adoption.ValidUntil);
    }

    public async Task<ModerationPrerequisiteObservation?> AcquirePrerequisiteAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, ModerationActivationBinding binding,
        DateTimeOffset observedAt, CancellationToken token)
    {
        if (evidenceSession is not ModerationConsumerEvidenceSession session
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context)
            || !native.Policy.Consumers.Any(row => row.Binding == binding)) return null;
        var profile = native.Policy.Profiles.SingleOrDefault(row => row.Definition.Rule == binding.Rule);
        if (profile is null) return null;
        var current = await AcquireSourceAsync(session, transactionId, profile.Definition.Id, binding.Mode, observedAt, token);
        var adopted = await ReadAdoptionAsync(session, transactionId, profile.Definition.Id, binding.Mode, observedAt, token);
        if (current is null || adopted is null || !adopted.Adoption.MatchesCurrent(current.Source, clock.GetUtcNow())
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null) return null;
        return new(transactionId, binding, ModerationActivationPrerequisite.ConstitutionAndReviewers,
            adopted.Adoption.Id, adopted.Adoption.Revision, current.Source.Id, adopted.Adoption.SourceRevision,
            adopted.Adoption.SourceWriterGeneration.ToString(CultureInfo.InvariantCulture), current.Source.Revision,
            current.Source.WriterGeneration.ToString(CultureInfo.InvariantCulture),
            adopted.Adoption.AdoptedAt, adopted.ValidUntil, observedAt);
    }

    internal Task<ConstitutionLiveState?> ObserveAsync(
        ModerationConsumerEvidenceSession session, Guid transactionId, Guid profileId, ModerationNativeMode mode,
        ModerationConstitutionTerms terms, ImmutableArray<ModerationConstitutionReviewerTerms> reviewers,
        Guid recorder, DateTimeOffset observedAt, CancellationToken token) =>
        ObserveAsync((ModerationSourceReadSession)session, transactionId, profileId, mode, terms, reviewers, recorder, observedAt, token);

    internal async Task<ConstitutionLiveState?> ObserveAsync(
        ModerationSourceReadSession session, Guid transactionId, Guid profileId, ModerationNativeMode mode,
        ModerationConstitutionTerms terms, ImmutableArray<ModerationConstitutionReviewerTerms> reviewers,
        Guid recorder, DateTimeOffset observedAt, CancellationToken token)
    {
        if (await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context) || !Enum.IsDefined(mode) || reviewers.IsDefaultOrEmpty) return null;
        terms.Validate();
        var requirement = native.Policy.Publication;
        var definition = await definitions.ReadDefinitionAsync(native, observedAt, token);
        var profile = definition?.Policy.Profiles.SingleOrDefault(row => row.Definition.Id == profileId)?.Definition;
        if (definition is null || profile is null || !definition.Policy.Proposed.Modes.Contains(mode)
            || definition.Policy.Publication.ProposedRevision != requirement.ProposedRevision
            || ModerationPolicyPublicationProjection.ProposedDigest(definition.Policy.Proposed)
                != ModerationPolicyPublicationProjection.ProposedDigest(native.Policy.Proposed)
            || terms.ScopeCeiling < requirement.Jurisdiction.Kind
            || profile.ScopeKinds.Any(kind => kind < terms.ScopeCeiling)
            || terms.MinimumIndependentReviewers < profile.RequiredApprovals
            || terms.ProtectedComplaintCalendarMonths < profile.ContestationCalendarMonths
            || profile.Timing.Kind == ModerationReferenceTimingKind.ReviewedIndefinite
                && (terms.MinimumIndependentReviewers < 2 || profile.Timing.Duration > terms.MaximumReviewInterval)
            || !await AdministratorAsync(native.Authority, recorder, requirement.Jurisdiction, token)
            || !await AdministratorAsync(native.Authority, terms.CoordinatorHumanId, requirement.Jurisdiction, token)
            || !await AdministratorAsync(native.Authority, terms.FallbackHumanId, requirement.Jurisdiction, token))
            return null;
        var until = definition.ValidUntil;
        foreach (var reviewer in reviewers.OrderBy(row => row.AvailabilityId))
        {
            reviewer.Validate();
            if (reviewer.ReviewerHumanId == recorder || reviewer.ControllerHumanId == recorder
                || !await AdministratorAsync(native.Authority, reviewer.ReviewerHumanId, requirement.Jurisdiction, token)
                || !native.Authority.FencedHumans.Contains(reviewer.ControllerHumanId)
                || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == reviewer.ControllerHumanId && !row.IsDeleted, token))
                return null;
            var availability = await ReadAvailabilityAsync(native.Authority, reviewer.AvailabilityId, observedAt, token);
            if (availability is null || availability.ReviewerHumanId != reviewer.ReviewerHumanId
                || availability.Revision != reviewer.AvailabilityRevision
                || availability.WriterGeneration != reviewer.AvailabilityWriterGeneration) return null;
            // Qualification writers and lifecycle writers share the original authority/epoch fences.
            // Count all live assignments, including unresolved ones, rather than hiding their workload.
            var caseload = await (from assignment in context.ModerationReviewerAssignments.AsNoTracking()
                join allegation in context.ModerationCases.AsNoTracking() on assignment.CaseId equals allegation.Id
                where assignment.WithdrawnAt == null && allegation.State == ModerationCaseState.Open
                    && (assignment.ReviewerHumanId == reviewer.ReviewerHumanId || assignment.ControllerHumanId == reviewer.ControllerHumanId)
                select assignment.CaseId).Distinct().CountAsync(token);
            if (caseload >= availability.MaximumCaseload) return null;
            if (availability.ValidUntil < until) until = availability.ValidUntil;
        }
        if (reviewers.Length < terms.MinimumIndependentReviewers
            || reviewers.Select(row => row.ControllerHumanId).Distinct().Count() != reviewers.Length
            || reviewers.Select(row => row.ReviewerHumanId).Distinct().Count() != reviewers.Length
            || reviewers.Any(a => reviewers.Any(b => a.AvailabilityId != b.AvailabilityId
                && (a.ReviewerHumanId == b.ControllerHumanId || a.ControllerHumanId == b.ReviewerHumanId)))
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null || clock.GetUtcNow() >= until)
            return null;
        return new(definition.DefinitionId, until);
    }

    internal async Task<ModerationConstitutionAvailability?> ReadAvailabilityAsync(
        IModerationSourceProvenance authority, Guid id, DateTimeOffset observedAt, CancellationToken token)
    {
        var discovered = await context.Set<ModerationConstitutionAvailability>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        if (discovered is null) return null;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context, AvailabilitySlot(authority, discovered.ReviewerHumanId), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConstitutionAvailability>(context, id, token);
        var row = await context.Set<ModerationConstitutionAvailability>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, token);
        if (row is null) return null;
        row.ValidatePersistedState();
        if (row.HostBindingId != authority.Binding.Id || row.TrustDomainId != authority.Binding.TrustDomainId
            || row.TenantId != authority.Tenant.Id || !row.IsCurrent(observedAt) || row.WriterGeneration > authority.Epoch.Revision
            || !await AdministratorAsync(authority, row.ReviewerHumanId, authority.Tenant, token)
            || await context.Set<ModerationConstitutionAvailability>().AsNoTracking().AnyAsync(item =>
                item.HostBindingId == row.HostBindingId && item.TenantId == row.TenantId
                && item.ReviewerHumanId == row.ReviewerHumanId && item.WriterGeneration > row.WriterGeneration, token)
            || !await HasReceiptAsync(authority, AvailabilityRequest(row, authority.Tenant),
                ModerationQualificationSourceKind.ConstitutionAvailability, row.Id, 1, row.ReviewerHumanId,
                row.WriterGeneration, row.RecordedAt, token) || clock.GetUtcNow() >= row.ValidUntil) return null;
        return row;
    }

    internal async Task<bool> AdministratorAsync(
        IModerationSourceProvenance authority, Guid humanId, AuthorityScope jurisdiction, CancellationToken token)
    {
        if (!authority.FencedHumans.Contains(humanId) || !authority.Administrators.Contains(humanId)) return false;
        return await context.PlatformUserRoles.AsNoTracking().AnyAsync(row => row.UserId == humanId && !row.User.IsDeleted
            && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin", token)
            || jurisdiction == authority.Tenant && await context.TenantUserRoleGrants.AsNoTracking().AnyAsync(row =>
                row.TenantUser.UserId == humanId && row.TenantId == authority.Tenant.Id && row.RevokedAt == null
                && row.RoleScopeId == (int)RoleScopeEnum.Tenant && row.RoleId == (int)RoleEnum.TenantAdmin
                && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant
                && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted, token);
    }

    internal async Task<bool> HasReceiptAsync(IModerationSourceProvenance authority, ModerationQualificationMutation template,
        ModerationQualificationSourceKind kind, Guid id, long revision, Guid humanId, long generation,
        DateTimeOffset completedAt, CancellationToken token)
    {
        var receiptId = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .Where(row => row.SourceId == id && row.SourceKind == kind && row.ResultRevision == revision)
            .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (receiptId is null) return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationMutationReceipt>(context, receiptId.Value, token);
        var receipt = await context.ModerationQualificationMutationReceipts.AsNoTracking().SingleOrDefaultAsync(row => row.Id == receiptId, token);
        if (receipt is null) return false;
        receipt.ValidatePersistedState();
        var request = template with { OperationId = receipt.Id };
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.GetType().FullName + "\n" + JsonSerializer.Serialize(request, request.GetType()))));
        return receipt.SourceId == id && receipt.SourceKind == kind && receipt.ResultRevision == revision
            && receipt.RequestDigest == digest && receipt.PrincipalId == humanId && receipt.QualificationRevision == generation
            && receipt.CompletedAt == completedAt && generation <= authority.Epoch.Revision
            && receipt.HostBindingId == authority.Binding.Id && receipt.TrustDomainId == authority.Binding.TrustDomainId
            && receipt.TenantId == authority.Tenant.Id;
    }

    internal static RecordModerationConstitutionSource RecordRequest(ModerationConstitutionSource source, AuthorityScope jurisdiction) => new()
    {
        OriginalEffect = OriginalAnchor(source),
        OperationId = Guid.Empty, Target = jurisdiction, ExpectedQualificationRevision = source.WriterGeneration - 1,
        SourceId = source.Id, PolicyId = source.PolicyId, ExpectedPolicyRevision = source.PolicyRevision,
        PublicationId = source.PublicationId, ProfileId = source.ProfileId, Mode = (ModerationNativeMode)source.ModeCode,
        Terms = source.Terms, Reviewers = source.Reviewers.Select(row => row.ToTerms()).ToImmutableArray(), ValidUntil = source.ValidUntil
    };
    internal static AdoptModerationConstitutionSource AdoptionRequest(ModerationConstitutionAdoption adoption,
        AuthorityScope jurisdiction, ModerationConstitutionSource source) => new()
    {
        OriginalEffect = OriginalAnchor(source),
        OperationId = Guid.Empty, Target = jurisdiction, ExpectedQualificationRevision = adoption.WriterGeneration - 1,
        SourceId = adoption.SourceId, ExpectedSourceRevision = adoption.SourceRevision, ExpectedSourceWriterGeneration = adoption.SourceWriterGeneration,
        AdoptionId = adoption.Id, ValidUntil = adoption.ValidUntil
    };
    internal static RecordModerationConstitutionAvailability AvailabilityRequest(ModerationConstitutionAvailability row, AuthorityScope jurisdiction) => new()
    {
        OperationId = Guid.Empty, Target = jurisdiction, ExpectedQualificationRevision = row.WriterGeneration - 1,
        AvailabilityId = row.Id, MaximumCaseload = row.MaximumCaseload, ValidUntil = row.ValidUntil
    };
    internal static string Slot(IModerationSourceProvenance authority, Guid publicationId, Guid profileId, ModerationNativeMode mode) =>
        $"moderation.constitution:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{publicationId:N}:{profileId:N}:{(int)mode}";
    internal static string AvailabilitySlot(IModerationSourceProvenance authority, Guid humanId) =>
        $"moderation.constitution-reviewer:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{humanId:N}";
    internal static ModerationOriginalEffectAnchor? OriginalAnchor(ModerationConstitutionSource source) =>
        source.OriginalEffectId is { } effect
            ? new(effect, source.OriginalSnapshotHash!, source.OriginalEffectBindingHash!) : null;
    internal static bool MatchesPurpose(ModerationConstitutionSource source, ModerationOriginalRecipeAuthority? original) =>
        source.OriginalEffectId is null || original is not null
            && OriginalAnchor(source) == original.Anchor && source.OriginalCustodyUntil == original.ValidUntil
            && source.ValidUntil <= original.ValidUntil;
    internal sealed record ConstitutionLiveState(Guid DefinitionId, DateTimeOffset ValidUntil);
}
