using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private async Task<QualifiedSource> RecordCaseNarrativeAsync(RecordModerationCaseNarrativePolicySource request,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        var reader = new ModerationCaseNarrativePolicyReader(context, clock);
        if (!ModerationCaseNarrativeQualification.IsValid(request) || request.Target != authority.Tenant
            || request.ValidUntil <= now
            || !await reader.OwnersCurrentAsync(authority, request.Ownership, authority.UserId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationCaseNarrativePolicyReader.Slot(authority, request.TargetKind, request.Terms.Purpose), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCaseNarrativePolicySource>(context, request.SourceId, token);
        if (await context.Set<ModerationCaseNarrativePolicySource>().AsNoTracking().AnyAsync(row => row.Id == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var source = ModerationCaseNarrativePolicySource.Record(request.SourceId, authority.Binding.Id,
            authority.Binding.TrustDomainId, authority.Tenant.Id, request.TargetKind, request.Terms, request.Ownership,
            authority.UserId, checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        context.Set<ModerationCaseNarrativePolicySource>().Add(source);
        return new(ModerationQualificationSourceKind.CaseNarrativePolicySource, source.Id, 1, MutationValidUntil: source.ValidUntil);
    }

    private async Task<QualifiedSource> AdoptCaseNarrativeAsync(AdoptModerationCaseNarrativePolicySource request,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (!ModerationCaseNarrativeQualification.IsValid(request) || request.Target != authority.Tenant)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var discovered = await context.Set<ModerationCaseNarrativePolicySource>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.SourceId && row.TenantId == authority.Tenant.Id
                && row.HostBindingId == authority.Binding.Id, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var reader = new ModerationCaseNarrativePolicyReader(context, clock);
        var source = await reader.ReadSourceAsync(authority, discovered.TargetKind, token, discovered.Purpose);
        if (source?.Id != request.SourceId || source.Revision != request.ExpectedSourceRevision
            || source.WriterGeneration != request.ExpectedSourceWriterGeneration || source.PrivacyOwnerHumanId != authority.UserId
            || request.ValidUntil <= now || request.ValidUntil > source.ValidUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCaseNarrativeAdoption>(context, request.AdoptionId, token);
        if (await context.Set<ModerationCaseNarrativeAdoption>().AsNoTracking()
            .AnyAsync(row => row.Id == request.AdoptionId || row.SourceId == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var adoption = ModerationCaseNarrativeAdoption.Adopt(request.AdoptionId, source,
            authority.UserId, checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        context.Set<ModerationCaseNarrativeAdoption>().Add(adoption);
        return new(ModerationQualificationSourceKind.CaseNarrativeAdoption, adoption.Id, 1, MutationValidUntil: adoption.ValidUntil);
    }

    private async Task<QualifiedSource> WithdrawCaseNarrativeAsync(WithdrawModerationCaseNarrativeAdoption request,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (!ModerationCaseNarrativeQualification.IsValid(request) || request.Target != authority.Tenant)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var adoption = await context.Set<ModerationCaseNarrativeAdoption>().SingleOrDefaultAsync(row =>
            row.Id == request.AdoptionId && row.HostBindingId == authority.Binding.Id
            && row.TrustDomainId == authority.Binding.TrustDomainId && row.TenantId == authority.Tenant.Id, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var source = await context.Set<ModerationCaseNarrativePolicySource>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == adoption.SourceId && row.HostBindingId == authority.Binding.Id
                && row.TrustDomainId == authority.Binding.TrustDomainId && row.TenantId == authority.Tenant.Id, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationCaseNarrativePolicyReader.Slot(authority, source.TargetKind, source.Purpose), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCaseNarrativeAdoption>(context, adoption.Id, token);
        source.ValidatePersistedState();
        adoption.ValidatePersistedState();
        var reader = new ModerationCaseNarrativePolicyReader(context, clock);
        // Withdrawal remains available after owner departure/expiry; immutable writer receipts
        // still prove exactly what was adopted, without granting current custody authority.
        if (!await reader.ReceiptAsync(authority, ModerationCaseNarrativePolicyReader.RecordRequest(source, authority.Tenant),
                ModerationQualificationSourceKind.CaseNarrativePolicySource, source.Id, 1, source.RecordedByHumanId,
                source.WriterGeneration, source.RecordedAt, token)
            || !await reader.ReceiptAsync(authority, ModerationCaseNarrativePolicyReader.AdoptionRequest(adoption, authority.Tenant),
                ModerationQualificationSourceKind.CaseNarrativeAdoption, adoption.Id, 1, adoption.AdoptedByHumanId,
                adoption.WriterGeneration, adoption.AdoptedAt, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (adoption.Revision != request.ExpectedAdoptionRevision || adoption.WithdrawnAt is not null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        adoption.Withdraw(request.ExpectedAdoptionRevision, authority.UserId, now);
        return new(ModerationQualificationSourceKind.CaseNarrativeAdoption, adoption.Id, adoption.Revision);
    }

    private async Task VerifyCaseNarrativeWriteAsync(ModerationQualificationMutation request,
        ModerationSourceAuthority authority, CancellationToken token)
    {
        if (request is not (RecordModerationCaseNarrativePolicySource or AdoptModerationCaseNarrativePolicySource
            or WithdrawModerationCaseNarrativeAdoption)) return;
        var current = await new ModerationQualificationAuthority(context, admin, machine).AcquireAsync(request, token);
        if (current is null || current.UserId != authority.UserId || current.Binding.Id != authority.Binding.Id
            || current.Tenant != authority.Tenant || current.Epoch.Id != authority.Epoch.Id
            || current.Epoch.Revision != authority.Epoch.Revision)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is WithdrawModerationCaseNarrativeAdoption withdrawal)
        {
            if (!await context.Set<ModerationCaseNarrativeAdoption>().AsNoTracking().AnyAsync(row =>
                row.Id == withdrawal.AdoptionId && row.HostBindingId == current.Binding.Id
                && row.TenantId == current.Tenant.Id && row.TrustDomainId == current.Binding.TrustDomainId
                && row.Revision == 2 && row.WithdrawnByHumanId == current.UserId && row.WithdrawnAt != null, token))
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            return;
        }
        var reader = new ModerationCaseNarrativePolicyReader(context, clock);
        if (request is RecordModerationCaseNarrativePolicySource record)
        {
            if ((await reader.ReadSourceAsync(current, record.TargetKind, token, record.Terms.Purpose))?.Id != record.SourceId)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            return;
        }
        var adopt = (AdoptModerationCaseNarrativePolicySource)request;
        var source = await context.Set<ModerationCaseNarrativePolicySource>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == adopt.SourceId && row.TenantId == current.Tenant.Id, token);
        if (source is null || (await reader.ReadAsync(current, source.TargetKind, token, source.Purpose))?.Adoption.Id != adopt.AdoptionId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }
}
