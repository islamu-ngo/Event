using System.Security.Cryptography;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private async Task<QualifiedSource> RecordParticipantInvestigationAsync(
        RecordModerationParticipantIncidentResponsibilitySource request,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (responseProtection is null || caseAttribution is not ModerationCaseTargetProofOwner owner
            || !ModerationParticipantInvestigationQualification.IsValid(request)
            || request.Target != authority.Tenant)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var qualifiedAt = new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
        var facts = await owner.AcquireParticipantInvestigationAsync(
            context, request, authority, responseProtection, qualifiedAt, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.participant-investigation:{authority.Binding.Id:N}:{request.SourceId:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationParticipantIncidentResponsibilitySource>(
            context, request.SourceId, token);
        if (await context.ModerationParticipantIncidentResponsibilitySources.AsNoTracking()
            .AnyAsync(row => row.Id == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        byte[]? plaintext = null;
        byte[]? ciphertext = null;
        try
        {
            plaintext = request.Grounds.Encode(facts.CustodySource.MaximumMaterialBytes);
            var digest = Convert.ToHexStringLower(SHA256.HashData(plaintext));
            // The storage factory only validates coordinates. This untracked envelope supplies
            // the immutable protector binding; no placeholder ciphertext is staged or saved.
            var coordinates = InvestigationRow(request, facts, qualifiedAt, new byte[32], digest);
            ciphertext = ModerationParticipantInvestigationAuthentication.Protector(
                responseProtection, coordinates, facts.AuthorityDigest).Protect(plaintext);
            var source = InvestigationRow(request, facts, qualifiedAt, ciphertext, digest);
            context.ModerationParticipantIncidentResponsibilitySources.Add(source);
            return new(ModerationQualificationSourceKind.ParticipantIncidentResponsibilitySource,
                source.Id, source.Revision, MutationValidUntil: source.ValidUntil);
        }
        catch (CryptographicException)
        {
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        }
        catch (ArgumentException)
        {
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            if (ciphertext is not null) CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    private async Task VerifyParticipantInvestigationWriteAsync(
        ModerationQualificationMutation request, ModerationSourceAuthority authority, CancellationToken token)
    {
        // The closed mutation dispatch owns other source types. This one must always authenticate
        // the actual retained row and reacquire its original tuple, including post-flush and replay.
        if (request is not RecordModerationParticipantIncidentResponsibilitySource investigation) return;
        if (responseProtection is null || caseAttribution is not ModerationCaseTargetProofOwner owner
            || context.ChangeTracker.HasChanges()
            || await admin.ResolveUserIdAsync(token) != authority.UserId || machine.IsMachineCaller)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var transaction = context.Database.CurrentTransaction
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationParticipantIncidentResponsibilitySource>(
            context, investigation.SourceId, token);
        var retained = await context.ModerationParticipantIncidentResponsibilitySources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == investigation.SourceId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var now = clock.GetUtcNow();
        retained.ValidatePersistedState();
        if (!retained.IsCurrentAt(now) || retained.Revision != 1 || now >= retained.PurgeAt)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var facts = await owner.AcquireParticipantInvestigationAsync(
            context, investigation, authority, responseProtection, retained.QualifiedAt, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        byte[]? asserted = null;
        byte[]? ciphertext = null;
        byte[]? actual = null;
        try
        {
            asserted = investigation.Grounds.Encode(facts.CustodySource.MaximumMaterialBytes);
            var digest = Convert.ToHexStringLower(SHA256.HashData(asserted));
            var current = InvestigationRow(investigation, facts, retained.QualifiedAt, new byte[32], digest);
            if (ModerationParticipantInvestigationAuthentication.Binding(current)
                    != ModerationParticipantInvestigationAuthentication.Binding(retained)
                || digest != retained.AssessmentDigest)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            ciphertext = retained.ProtectedAssessment.ToArray();
            actual = ModerationParticipantInvestigationAuthentication.Protector(
                responseProtection, retained, facts.AuthorityDigest).Unprotect(ciphertext);
            if (!CryptographicOperations.FixedTimeEquals(actual, asserted)
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(actual), Convert.FromHexString(retained.AssessmentDigest))
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction)
                || await admin.ResolveUserIdAsync(token) != authority.UserId
                || machine.IsMachineCaller || clock.GetUtcNow() < now || clock.GetUtcNow() >= retained.ValidUntil
                || context.TenantFilterTenantId != authority.Tenant.Id || context.IsTenantFilterBypassed
                || context.ModerationTrustDomainId != authority.Binding.TrustDomainId)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        }
        catch (CryptographicException)
        {
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        }
        catch (ArgumentException)
        {
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        }
        finally
        {
            if (asserted is not null) CryptographicOperations.ZeroMemory(asserted);
            if (ciphertext is not null) CryptographicOperations.ZeroMemory(ciphertext);
            if (actual is not null) CryptographicOperations.ZeroMemory(actual);
        }
    }

    private static ModerationParticipantIncidentResponsibilitySource InvestigationRow(
        RecordModerationParticipantIncidentResponsibilitySource request,
        ModerationCaseTargetProofOwner.ParticipantInvestigationFacts facts, DateTimeOffset qualifiedAt,
        ReadOnlySpan<byte> ciphertext, string digest) =>
        ModerationParticipantIncidentResponsibilitySource.Record(request.SourceId, facts.Host, facts.Occurrence,
            request.EvidenceCaseRevision, facts.Material, facts.Original, facts.Session, facts.Order, facts.Participant,
            facts.Subject, facts.AssessorRoleGrantId, facts.Policy, facts.CustodySource, facts.CustodyAdoption, facts.Grant,
            request.IncidentWindowFrom, request.IncidentWindowUntil, qualifiedAt, request.ValidUntil, request.PurgeAt,
            ciphertext, digest);

}
