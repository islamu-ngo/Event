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
/// Independently observes normative AG04 source and adoption on the original native acquisition.
/// Uses the immutable definition reader, never the stateful publication writer. No collection,
/// assurance vendor, subject evidence, eligibility calculation, Passed or activation lives here.
/// The calendar projection uses only independently adopted source semantics.
/// </summary>
public sealed class ModerationChildAgePolicySourceOwner(
    ExploreDbContext context, ModerationPolicyDefinitionSourceOwner definitions, TimeProvider clock)
{
    public Task<ModerationChildAgePolicySourceObservation?> AcquireSourceAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token) =>
        AcquireSourceAsync(ModerationSourceReadSession.Authenticated(evidenceSession),
            transactionId, profileId, mode, observedAt, token);

    internal async Task<ModerationChildAgePolicySourceObservation?> AcquireSourceAsync(
        ModerationSourceReadSession? session, Guid transactionId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token)
    {
        if (session is null
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context)) return null;
        var requirement = native.Policy.Publication;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            Slot(native.Authority, requirement.PublicationId, profileId, mode), token);
        var id = await context.Set<ModerationChildAgePolicySource>().AsNoTracking()
            .Where(row => row.HostBindingId == native.Authority.Binding.Id && row.TenantId == native.Authority.Tenant.Id
                && row.PublicationId == requirement.PublicationId && row.ProfileId == profileId && row.ModeCode == (int)mode)
            .OrderByDescending(row => row.WriterGeneration).Select(row => (Guid?)row.Id).FirstOrDefaultAsync(token);
        if (id is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationChildAgePolicySource>(context, id.Value, token);
        var source = await context.Set<ModerationChildAgePolicySource>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        if (source is null) return null;
        source.ValidatePersistedState();
        if (source.HostBindingId != native.Authority.Binding.Id || source.TrustDomainId != native.Authority.Binding.TrustDomainId
            || source.TenantId != native.Authority.Tenant.Id || source.PolicyId != requirement.PolicyId
            || source.PolicyRevision != requirement.ProposedRevision || source.PublicationId != requirement.PublicationId
            || source.ProfileId != profileId || source.ModeCode != (int)mode
            || source.RecordedAt > observedAt || source.ValidUntil <= observedAt
            || source.WriterGeneration > native.Authority.Epoch.Revision
            || !await HasReceiptAsync(native.Authority, RecordRequest(source, requirement.Jurisdiction),
                ModerationQualificationSourceKind.ChildAgePolicySource, source.Id, 1, source.RecordedByHumanId,
                source.WriterGeneration, source.RecordedAt, token)) return null;
        var live = await ObserveAsync(session, transactionId, profileId, mode, source.Terms, source.Ownership,
            source.RecordedByHumanId, observedAt, token);
        if (live is null || source.DefinitionId != live.DefinitionId || source.ValidUntil > live.ValidUntil
            || source.ProposedStateDigest != ModerationPolicyPublicationProjection.ProposedDigest(native.Policy.Proposed)
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null
            || clock.GetUtcNow() >= source.ValidUntil) return null;
        return new(transactionId, source, observedAt, source.ValidUntil);
    }

    public Task<ModerationChildAgePolicyAdoptionObservation?> ReadAdoptionAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token) =>
        ReadAdoptionAsync(ModerationSourceReadSession.Authenticated(evidenceSession),
            transactionId, profileId, mode, observedAt, token);

    internal async Task<ModerationChildAgePolicyAdoptionObservation?> ReadAdoptionAsync(
        ModerationSourceReadSession? session, Guid transactionId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token)
    {
        // A retained source observation cannot substitute for independent current source acquisition.
        if (session is null
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context)) return null;
        var current = await AcquireSourceAsync(session, transactionId, profileId, mode, observedAt, token);
        if (current is null) return null;
        var id = await context.Set<ModerationChildAgePolicyAdoption>().AsNoTracking()
            .Where(row => row.SourceId == current.Source.Id).Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (id is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationChildAgePolicyAdoption>(context, id.Value, token);
        var adoption = await context.Set<ModerationChildAgePolicyAdoption>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id, token);
        if (adoption is null) return null;
        adoption.ValidatePersistedState();
        if (!adoption.MatchesCurrent(current.Source, observedAt) || adoption.WriterGeneration > native.Authority.Epoch.Revision
            || !await AdministratorAsync(native.Authority, adoption.AdoptedByHumanId, native.Policy.Publication.Jurisdiction, token)
            || !await HasReceiptAsync(native.Authority, AdoptionRequest(adoption, native.Policy.Publication.Jurisdiction),
                ModerationQualificationSourceKind.ChildAgePolicyAdoption, adoption.Id, 1, adoption.AdoptedByHumanId,
                adoption.WriterGeneration, adoption.AdoptedAt, token)) return null;
        var completed = await AcquireSourceAsync(session, transactionId, profileId, mode, observedAt, token);
        if (completed is null || !adoption.MatchesCurrent(completed.Source, clock.GetUtcNow())
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null) return null;
        return new(transactionId, adoption, observedAt, adoption.ValidUntil);
    }

    public async Task<ModerationChildAgeCalendarObservation?> AcquireCalendarAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token)
    {
        if (evidenceSession is not ModerationConsumerEvidenceSession session
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context)) return null;
        var source = await AcquireSourceAsync(session, transactionId, profileId, mode, observedAt, token);
        var adoption = await ReadAdoptionAsync(session, transactionId, profileId, mode, observedAt, token);
        if (source is null || adoption is null
            || !adoption.Adoption.MatchesCurrent(source.Source, clock.GetUtcNow())
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null
            || clock.GetUtcNow() >= adoption.ValidUntil) return null;
        var policy = new ModerationAgePolicy(source.Source.PolicyId, source.Source.PolicyRevision,
            native.Policy.Publication.Jurisdiction, TimeZoneInfo.Utc, ModerationLeapDayAnniversary.March1);
        return new(transactionId, policy,
            source.Source.Id, source.Source.Revision, source.Source.WriterGeneration,
            adoption.Adoption.Id, adoption.Adoption.Revision, adoption.Adoption.WriterGeneration,
            source.Source.AdulthoodAge, source.Source.DebateMinimumAge, observedAt, adoption.ValidUntil);
    }

    public async Task<ModerationPrerequisiteObservation?> AcquirePrerequisiteAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, ModerationActivationBinding binding,
        DateTimeOffset observedAt, CancellationToken token)
    {
        if (evidenceSession is not ModerationConsumerEvidenceSession session
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context) || !native.Policy.Consumers.Any(row => row.Binding == binding))
            return null;
        var profile = native.Policy.Profiles.SingleOrDefault(row => row.Definition.Rule == binding.Rule);
        if (profile is null) return null;
        var source = await AcquireSourceAsync(session, transactionId, profile.Definition.Id, binding.Mode, observedAt, token);
        var adoption = await ReadAdoptionAsync(session, transactionId, profile.Definition.Id, binding.Mode, observedAt, token);
        if (source is null || adoption is null || !adoption.Adoption.MatchesCurrent(source.Source, clock.GetUtcNow())
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null) return null;
        return new(transactionId, binding, ModerationActivationPrerequisite.ChildAndAgePolicy,
            adoption.Adoption.Id, adoption.Adoption.Revision, source.Source.Id, adoption.Adoption.SourceRevision,
            adoption.Adoption.SourceWriterGeneration.ToString(CultureInfo.InvariantCulture), source.Source.Revision,
            source.Source.WriterGeneration.ToString(CultureInfo.InvariantCulture),
            adoption.Adoption.AdoptedAt, adoption.ValidUntil, observedAt);
    }

    internal Task<ChildAgePolicyLiveState?> ObserveAsync(
        ModerationConsumerEvidenceSession session, Guid transactionId, Guid profileId, ModerationNativeMode mode,
        ModerationChildAgePolicyTerms terms, ModerationChildAgePolicyOwnership ownership,
        Guid recorder, DateTimeOffset observedAt, CancellationToken token) =>
        ObserveAsync((ModerationSourceReadSession)session, transactionId, profileId, mode, terms, ownership, recorder, observedAt, token);

    internal async Task<ChildAgePolicyLiveState?> ObserveAsync(
        ModerationSourceReadSession session, Guid transactionId, Guid profileId, ModerationNativeMode mode,
        ModerationChildAgePolicyTerms terms, ModerationChildAgePolicyOwnership ownership,
        Guid recorder, DateTimeOffset observedAt, CancellationToken token)
    {
        if (await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context) || !Enum.IsDefined(mode)) return null;
        terms.Validate();
        ownership.Validate(recorder);
        var requirement = native.Policy.Publication;
        var definition = native.IsPublicCalendar
            ? await definitions.ReadDefinitionAsync(native, observedAt, token)
            : await definitions.ReadDefinitionAsync(native.Authority, transactionId,
                requirement.Jurisdiction, requirement.PolicyId, requirement.PublicationId, observedAt, token);
        var profile = definition?.Policy.Profiles.SingleOrDefault(row => row.Definition.Id == profileId);
        if (definition is null || profile is null || !definition.Policy.Proposed.Modes.Contains(mode)
            || definition.Policy.Publication.ProposedRevision != requirement.ProposedRevision
            || ModerationPolicyPublicationProjection.ProposedDigest(definition.Policy.Proposed)
                != ModerationPolicyPublicationProjection.ProposedDigest(native.Policy.Proposed)
            || terms.ScopeCeiling < requirement.Jurisdiction.Kind
            || profile.Definition.ScopeKinds.Any(kind => kind < terms.ScopeCeiling)
            || !profile.Definition.Strategies.Any(strategy => strategy is ModerationAdditiveStrategyKind.AgeAudienceEligibility
                or ModerationAdditiveStrategyKind.ClassificationIntegrity)
            || profile.Reference.MinimumAudienceAge is { } floor && terms.DebateMinimumAge < floor)
            return null;
        foreach (var human in new[] { recorder, ownership.ReviewerHumanId, ownership.CollectionOwnerHumanId })
            if (!await AdministratorAsync(native.Authority, human, requirement.Jurisdiction, token)) return null;
        foreach (var controller in new[] { ownership.ReviewerControllerHumanId, ownership.CollectionControllerHumanId, ownership.GovernanceControllerHumanId })
            if (!native.Authority.FencedHumans.Contains(controller)
                || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == controller && !row.IsDeleted, token))
                return null;
        if (await session.AcquireNativeAsync(transactionId, observedAt, token) is null
            || clock.GetUtcNow() >= definition.ValidUntil) return null;
        return new(definition.DefinitionId, definition.ValidUntil);
    }

    internal async Task<bool> AdministratorAsync(
        IModerationSourceProvenance authority, Guid human, AuthorityScope jurisdiction, CancellationToken token)
    {
        if (!authority.FencedHumans.Contains(human) || !authority.Administrators.Contains(human)) return false;
        return await context.PlatformUserRoles.AsNoTracking().AnyAsync(row => row.UserId == human && !row.User.IsDeleted
            && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin", token)
            || jurisdiction == authority.Tenant && await context.TenantUserRoleGrants.AsNoTracking().AnyAsync(row =>
                row.TenantUser.UserId == human && row.TenantId == authority.Tenant.Id && row.RevokedAt == null
                && row.RoleScopeId == (int)RoleScopeEnum.Tenant && row.RoleId == (int)RoleEnum.TenantAdmin
                && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted, token);
    }

    private async Task<bool> HasReceiptAsync(IModerationSourceProvenance authority, ModerationQualificationMutation template,
        ModerationQualificationSourceKind kind, Guid id, long revision, Guid human, long generation,
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
            && receipt.RequestDigest == digest && receipt.PrincipalId == human && receipt.QualificationRevision == generation
            && receipt.CompletedAt == completedAt && generation <= authority.Epoch.Revision
            && receipt.HostBindingId == authority.Binding.Id && receipt.TrustDomainId == authority.Binding.TrustDomainId
            && receipt.TenantId == authority.Tenant.Id;
    }

    internal static RecordModerationChildAgePolicySource RecordRequest(ModerationChildAgePolicySource source, AuthorityScope jurisdiction) => new()
    {
        OperationId = Guid.Empty, Target = jurisdiction, ExpectedQualificationRevision = source.WriterGeneration - 1,
        SourceId = source.Id, PolicyId = source.PolicyId, ExpectedPolicyRevision = source.PolicyRevision,
        PublicationId = source.PublicationId, ProfileId = source.ProfileId, Mode = (ModerationNativeMode)source.ModeCode,
        Terms = source.Terms, Ownership = source.Ownership, ValidUntil = source.ValidUntil
    };
    internal static AdoptModerationChildAgePolicySource AdoptionRequest(ModerationChildAgePolicyAdoption adoption, AuthorityScope jurisdiction) => new()
    {
        OperationId = Guid.Empty, Target = jurisdiction, ExpectedQualificationRevision = adoption.WriterGeneration - 1,
        SourceId = adoption.SourceId, ExpectedSourceRevision = adoption.SourceRevision,
        ExpectedSourceWriterGeneration = adoption.SourceWriterGeneration, AdoptionId = adoption.Id, ValidUntil = adoption.ValidUntil
    };
    internal static string Slot(IModerationSourceProvenance authority, Guid publication, Guid profile, ModerationNativeMode mode) =>
        $"moderation.child-age-policy:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{publication:N}:{profile:N}:{(int)mode}";
    internal sealed record ChildAgePolicyLiveState(Guid DefinitionId, DateTimeOffset ValidUntil);
}
