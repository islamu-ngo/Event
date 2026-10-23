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
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>
/// Observes only normative AG03 obligations and independent adoption on the original native acquisition.
/// Uses immutable definitions, never the stateful publication writer. A binding identifies no concrete
/// event or participant: current permissions, consenting stewardship, premises and settlement availability
/// must be qualified by the native consequence owner. This source provides no conformance or activation.
/// </summary>
public sealed class ModerationEventRightsSourceOwner(
    ExploreDbContext context, ModerationPolicyDefinitionSourceOwner definitions, TimeProvider clock)
{
    public async Task<ModerationEventRightsSourceObservation?> AcquireSourceAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token)
    {
        if (evidenceSession is not ModerationConsumerEvidenceSession session
            || await AcquireOriginalAsync(session, transactionId, observedAt, token) is not { } native) return null;
        var requirement = native.Policy.Publication;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            Slot(native.Authority, requirement.PublicationId, profileId, mode), token);
        var id = await context.Set<ModerationEventRightsSource>().AsNoTracking()
            .Where(row => row.HostBindingId == native.Authority.Binding.Id && row.TenantId == native.Authority.Tenant.Id
                && row.PublicationId == requirement.PublicationId && row.ProfileId == profileId && row.ModeCode == (int)mode)
            .OrderByDescending(row => row.WriterGeneration).Select(row => (Guid?)row.Id).FirstOrDefaultAsync(token);
        if (id is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEventRightsSource>(context, id.Value, token);
        var source = await context.Set<ModerationEventRightsSource>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        if (source is null) return null;
        source.ValidatePersistedState();
        if (source.HostBindingId != native.Authority.Binding.Id || source.TrustDomainId != native.Authority.Binding.TrustDomainId
            || source.TenantId != native.Authority.Tenant.Id || source.PolicyId != requirement.PolicyId
            || source.PolicyRevision != requirement.ProposedRevision || source.PublicationId != requirement.PublicationId
            || source.ProfileId != profileId || source.ModeCode != (int)mode
            || source.RecordedAt > observedAt || source.ValidUntil <= observedAt
            || source.WriterGeneration > native.Authority.Epoch.Revision
            || !await HasReceiptAsync(native.Authority, RecordRequest(source, requirement.Jurisdiction),
                ModerationQualificationSourceKind.EventRightsSource, source.Id, 1, source.RecordedByHumanId,
                source.WriterGeneration, source.RecordedAt, token)) return null;
        var live = await ObserveAsync(session, transactionId, profileId, mode, source.Terms, source.Ownership,
            source.RecordedByHumanId, observedAt, token);
        if (live is null || source.DefinitionId != live.DefinitionId || source.ValidUntil > live.ValidUntil
            || source.ProposedStateDigest != ModerationPolicyPublicationProjection.ProposedDigest(native.Policy.Proposed)
            || await AcquireOriginalAsync(session, transactionId, observedAt, token) is null
            || clock.GetUtcNow() >= source.ValidUntil) return null;
        return new(transactionId, source, observedAt, source.ValidUntil);
    }

    public async Task<ModerationEventRightsAdoptionObservation?> ReadAdoptionAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token)
    {
        if (evidenceSession is not ModerationConsumerEvidenceSession session
            || await AcquireOriginalAsync(session, transactionId, observedAt, token) is not { } native) return null;
        // Retaining a source observation never substitutes for this independent current acquisition.
        var current = await AcquireSourceAsync(session, transactionId, profileId, mode, observedAt, token);
        if (current is null) return null;
        var id = await context.Set<ModerationEventRightsAdoption>().AsNoTracking()
            .Where(row => row.SourceId == current.Source.Id).Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (id is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEventRightsAdoption>(context, id.Value, token);
        var adoption = await context.Set<ModerationEventRightsAdoption>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id, token);
        if (adoption is null) return null;
        adoption.ValidatePersistedState();
        if (!adoption.MatchesCurrent(current.Source, observedAt) || adoption.WriterGeneration > native.Authority.Epoch.Revision
            || !await AdministratorAsync(native.Authority, adoption.AdoptedByHumanId, native.Policy.Publication.Jurisdiction, token)
            || !await HasReceiptAsync(native.Authority, AdoptionRequest(adoption, native.Policy.Publication.Jurisdiction),
                ModerationQualificationSourceKind.EventRightsAdoption, adoption.Id, 1, adoption.AdoptedByHumanId,
                adoption.WriterGeneration, adoption.AdoptedAt, token)) return null;
        var completed = await AcquireSourceAsync(session, transactionId, profileId, mode, observedAt, token);
        if (completed is null || !adoption.MatchesCurrent(completed.Source, clock.GetUtcNow())
            || await AcquireOriginalAsync(session, transactionId, observedAt, token) is null) return null;
        return new(transactionId, adoption, observedAt, adoption.ValidUntil);
    }

    public async Task<ModerationPrerequisiteObservation?> AcquirePrerequisiteAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, ModerationActivationBinding binding,
        DateTimeOffset observedAt, CancellationToken token)
    {
        if (evidenceSession is not ModerationConsumerEvidenceSession session
            || await AcquireOriginalAsync(session, transactionId, observedAt, token) is not { } native
            || !native.Policy.Consumers.Any(row => row.Binding == binding)) return null;
        var profile = native.Policy.Profiles.SingleOrDefault(row => row.Definition.Rule == binding.Rule);
        if (profile is null) return null;
        var source = await AcquireSourceAsync(session, transactionId, profile.Definition.Id, binding.Mode, observedAt, token);
        var adoption = await ReadAdoptionAsync(session, transactionId, profile.Definition.Id, binding.Mode, observedAt, token);
        if (source is null || adoption is null || !adoption.Adoption.MatchesCurrent(source.Source, clock.GetUtcNow())
            || await AcquireOriginalAsync(session, transactionId, observedAt, token) is null) return null;
        return new(transactionId, binding, ModerationActivationPrerequisite.EventRightsAndPremises,
            adoption.Adoption.Id, adoption.Adoption.Revision, source.Source.Id, adoption.Adoption.SourceRevision,
            adoption.Adoption.SourceWriterGeneration.ToString(CultureInfo.InvariantCulture), source.Source.Revision,
            source.Source.WriterGeneration.ToString(CultureInfo.InvariantCulture),
            adoption.Adoption.AdoptedAt, adoption.ValidUntil, observedAt);
    }

    internal async Task<EventRightsLiveState?> ObserveAsync(
        ModerationConsumerEvidenceSession session, Guid transactionId, Guid profileId, ModerationNativeMode mode,
        ModerationEventRightsTerms terms, ModerationEventRightsOwnership ownership,
        Guid recorder, DateTimeOffset observedAt, CancellationToken token)
    {
        if (await AcquireOriginalAsync(session, transactionId, observedAt, token) is not { } native
            || !Enum.IsDefined(mode)) return null;
        terms.Validate();
        ownership.Validate(recorder);
        var requirement = native.Policy.Publication;
        var definition = await definitions.ReadDefinitionAsync(native.Authority, transactionId,
            requirement.Jurisdiction, requirement.PolicyId, requirement.PublicationId, observedAt, token);
        var profile = definition?.Policy.Profiles.SingleOrDefault(row => row.Definition.Id == profileId);
        if (definition is null || profile is null || !definition.Policy.Proposed.Modes.Contains(mode)
            || definition.Policy.Publication.ProposedRevision != requirement.ProposedRevision
            || ModerationPolicyPublicationProjection.ProposedDigest(definition.Policy.Proposed)
                != ModerationPolicyPublicationProjection.ProposedDigest(native.Policy.Proposed)
            || terms.ScopeCeiling < requirement.Jurisdiction.Kind
            || profile.Definition.ScopeKinds.Any(kind => kind < terms.ScopeCeiling)
            || !profile.Reference.Prerequisites.Contains(ModerationReferencePrerequisite.NativeSettlementOwnerAndRightsMapping)
                && !profile.Reference.Prerequisites.Contains(ModerationReferencePrerequisite.EssentialOperationFallbackSteward))
            return null;
        // Native administrative standing is necessary but not the substantive mandate. Terms and
        // ownership preserve independently adopted review/mapping grounds without claiming operational authority.
        foreach (var human in new[] { recorder, ownership.ReviewerHumanId,
            ownership.ContinuityMappingOwnerHumanId, ownership.SettlementMappingOwnerHumanId })
            if (!await AdministratorAsync(native.Authority, human, requirement.Jurisdiction, token)) return null;
        foreach (var controller in new[] { ownership.ReviewerControllerHumanId, ownership.ContinuityMappingControllerHumanId,
            ownership.SettlementMappingControllerHumanId, ownership.GovernanceControllerHumanId })
            if (!native.Authority.FencedHumans.Contains(controller)
                || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == controller && !row.IsDeleted, token))
                return null;
        if (await AcquireOriginalAsync(session, transactionId, observedAt, token) is null
            || clock.GetUtcNow() >= definition.ValidUntil) return null;
        return new(definition.DefinitionId, definition.ValidUntil);
    }

    private async Task<ModerationNativeEvidenceAcquisition?> AcquireOriginalAsync(
        ModerationConsumerEvidenceSession session, Guid transactionId, DateTimeOffset observedAt, CancellationToken token)
    {
        var native = await session.AcquireNativeAsync(transactionId, observedAt, token);
        if (native is null || !ReferenceEquals(native.Context, context)
            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row =>
                row.Id == native.Authority.Epoch.Id && row.HostBindingId == native.Authority.Binding.Id
                && row.TenantId == native.Authority.Tenant.Id && row.Revision == native.Authority.Epoch.Revision, token))
            return null;
        if (context.Database.ProviderName == RelationalNamedLock.PostgreSqlProvider)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = native.Transaction.GetDbTransaction();
            command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
            var value = await command.ExecuteScalarAsync(token);
            if (!Guid.TryParse(value?.ToString(), out var tenant) || tenant != native.Authority.Tenant.Id) return null;
        }
        // This is the caller's original concrete session, never a substitute or private snapshot.
        return await session.AcquireNativeAsync(transactionId, observedAt, token);
    }

    internal async Task<bool> AdministratorAsync(
        ModerationSourceAuthority authority, Guid human, AuthorityScope jurisdiction, CancellationToken token)
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

    private async Task<bool> HasReceiptAsync(ModerationSourceAuthority authority, ModerationQualificationMutation template,
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

    internal static RecordModerationEventRightsSource RecordRequest(ModerationEventRightsSource source, AuthorityScope jurisdiction) => new()
    {
        OperationId = Guid.Empty, Target = jurisdiction, ExpectedQualificationRevision = source.WriterGeneration - 1,
        SourceId = source.Id, PolicyId = source.PolicyId, ExpectedPolicyRevision = source.PolicyRevision,
        PublicationId = source.PublicationId, ProfileId = source.ProfileId, Mode = (ModerationNativeMode)source.ModeCode,
        Terms = source.Terms, Ownership = source.Ownership, ValidUntil = source.ValidUntil
    };
    internal static AdoptModerationEventRightsSource AdoptionRequest(ModerationEventRightsAdoption adoption, AuthorityScope jurisdiction) => new()
    {
        OperationId = Guid.Empty, Target = jurisdiction, ExpectedQualificationRevision = adoption.WriterGeneration - 1,
        SourceId = adoption.SourceId, ExpectedSourceRevision = adoption.SourceRevision,
        ExpectedSourceWriterGeneration = adoption.SourceWriterGeneration, AdoptionId = adoption.Id, ValidUntil = adoption.ValidUntil
    };
    internal static string Slot(ModerationSourceAuthority authority, Guid publication, Guid profile, ModerationNativeMode mode) =>
        $"moderation.event-rights:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{publication:N}:{profile:N}:{(int)mode}";
    internal sealed record EventRightsLiveState(Guid DefinitionId, DateTimeOffset ValidUntil);
}
