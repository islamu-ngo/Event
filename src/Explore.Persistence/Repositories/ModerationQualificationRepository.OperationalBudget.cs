using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private async Task<QualifiedSource> RecordOperationalBudgetAsync(RecordModerationOperationalBudgetSource request,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        var reader = new ModerationOperationalBudgetReader(context, clock);
        if (!ModerationOperationalBudgetQualification.IsValid(request) || request.Target != authority.Tenant
            || request.ValidUntil <= now
            || request.Terms.Window.Ticks >= now.UtcTicks
            || request.Terms.Window.Ticks >= DateTimeOffset.MaxValue.UtcTicks - now.UtcTicks
            || !await reader.OperatorsCurrentAsync(authority, request.OperatorHumanId, authority.UserId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationOperationalBudgetReader.Slot(authority, request.Terms.Action), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationOperationalBudgetSource>(context, request.SourceId, token);
        if (await context.Set<ModerationOperationalBudgetSource>().AsNoTracking()
            .AnyAsync(row => row.Id == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var source = ModerationOperationalBudgetSource.Record(request.SourceId, authority.Binding.Id,
            authority.Binding.TrustDomainId, authority.Tenant.Id, request.Terms, request.OperatorHumanId,
            authority.UserId, checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        context.Set<ModerationOperationalBudgetSource>().Add(source);
        return new(ModerationQualificationSourceKind.OperationalBudgetSource, source.Id, 1, MutationValidUntil: source.ValidUntil);
    }

    private async Task<QualifiedSource> AdoptOperationalBudgetAsync(AdoptModerationOperationalBudgetSource request,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (!ModerationOperationalBudgetQualification.IsValid(request) || request.Target != authority.Tenant)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var discovered = await context.Set<ModerationOperationalBudgetSource>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.SourceId && row.TenantId == authority.Tenant.Id
                && row.HostBindingId == authority.Binding.Id, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var source = await new ModerationOperationalBudgetReader(context, clock)
            .ReadSourceAsync(authority, discovered.Action, token);
        if (source?.Id != request.SourceId || source.Revision != request.ExpectedSourceRevision
            || source.WriterGeneration != request.ExpectedSourceWriterGeneration || source.OperatorHumanId != authority.UserId
            || request.ValidUntil <= now || request.ValidUntil > source.ValidUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationOperationalBudgetAdoption>(context, request.AdoptionId, token);
        if (await context.Set<ModerationOperationalBudgetAdoption>().AsNoTracking()
            .AnyAsync(row => row.Id == request.AdoptionId || row.SourceId == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var adoption = ModerationOperationalBudgetAdoption.Adopt(request.AdoptionId, source,
            authority.UserId, checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        context.Set<ModerationOperationalBudgetAdoption>().Add(adoption);
        return new(ModerationQualificationSourceKind.OperationalBudgetAdoption, adoption.Id, 1, MutationValidUntil: adoption.ValidUntil);
    }

    private async Task<QualifiedSource> WithdrawOperationalBudgetAsync(WithdrawModerationOperationalBudgetAdoption request,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (!ModerationOperationalBudgetQualification.IsValid(request) || request.Target != authority.Tenant)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var adoption = await context.Set<ModerationOperationalBudgetAdoption>().SingleOrDefaultAsync(row =>
            row.Id == request.AdoptionId && row.HostBindingId == authority.Binding.Id
            && row.TrustDomainId == authority.Binding.TrustDomainId && row.TenantId == authority.Tenant.Id, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var source = await context.Set<ModerationOperationalBudgetSource>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == adoption.SourceId && row.HostBindingId == authority.Binding.Id
                && row.TrustDomainId == authority.Binding.TrustDomainId && row.TenantId == authority.Tenant.Id, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationOperationalBudgetReader.Slot(authority, source.Action), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationOperationalBudgetAdoption>(context, adoption.Id, token);
        source.ValidatePersistedState();
        adoption.ValidatePersistedState();
        var reader = new ModerationOperationalBudgetReader(context, clock);
        // Removing capacity authority remains possible after its old owners depart or expire.
        if (!await reader.ReceiptAsync(authority, ModerationOperationalBudgetReader.RecordRequest(source, authority.Tenant),
                ModerationQualificationSourceKind.OperationalBudgetSource, source.Id, 1, source.RecordedByHumanId,
                source.WriterGeneration, source.RecordedAt, token)
            || !await reader.ReceiptAsync(authority, ModerationOperationalBudgetReader.AdoptionRequest(adoption, authority.Tenant),
                ModerationQualificationSourceKind.OperationalBudgetAdoption, adoption.Id, 1, adoption.AdoptedByHumanId,
                adoption.WriterGeneration, adoption.AdoptedAt, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (adoption.Revision != request.ExpectedAdoptionRevision || adoption.WithdrawnAt is not null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        adoption.Withdraw(request.ExpectedAdoptionRevision, authority.UserId, now);
        return new(ModerationQualificationSourceKind.OperationalBudgetAdoption, adoption.Id, adoption.Revision);
    }

    private async Task VerifyOperationalBudgetWriteAsync(ModerationQualificationMutation request,
        ModerationSourceAuthority authority, CancellationToken token)
    {
        if (request is not (RecordModerationOperationalBudgetSource or AdoptModerationOperationalBudgetSource
            or WithdrawModerationOperationalBudgetAdoption)) return;
        var current = await new ModerationQualificationAuthority(context, admin, machine).AcquireAsync(request, token);
        if (current is null || current.UserId != authority.UserId || current.Binding.Id != authority.Binding.Id
            || current.Tenant != authority.Tenant || current.Epoch.Id != authority.Epoch.Id
            || current.Epoch.Revision != authority.Epoch.Revision)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is WithdrawModerationOperationalBudgetAdoption withdrawal)
        {
            if (!await context.Set<ModerationOperationalBudgetAdoption>().AsNoTracking().AnyAsync(row =>
                row.Id == withdrawal.AdoptionId && row.HostBindingId == current.Binding.Id
                && row.TenantId == current.Tenant.Id && row.TrustDomainId == current.Binding.TrustDomainId
                && row.Revision == 2 && row.WithdrawnByHumanId == current.UserId && row.WithdrawnAt != null, token))
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            return;
        }
        var reader = new ModerationOperationalBudgetReader(context, clock);
        if (request is RecordModerationOperationalBudgetSource record)
        {
            if ((await reader.ReadSourceAsync(current, record.Terms.Action, token))?.Id != record.SourceId)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            return;
        }
        var adopt = (AdoptModerationOperationalBudgetSource)request;
        var source = await context.Set<ModerationOperationalBudgetSource>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == adopt.SourceId && row.TenantId == current.Tenant.Id, token);
        if (source is null || (await reader.ReadAsync(current, source.Action, token))?.Adoption.Id != adopt.AdoptionId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }
}
