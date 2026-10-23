using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Current operator-selected capacity terms, never an action permission or service mandate.</summary>
internal sealed class ModerationOperationalBudgetReader(ExploreDbContext context, TimeProvider clock)
{
    internal async Task<ModerationOperationalBudgetSource?> ReadSourceAsync(
        ModerationSourceAuthority authority, ModerationOperationalAction action, CancellationToken token)
    {
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context, Slot(authority, action), token);
        var source = await context.Set<ModerationOperationalBudgetSource>().AsNoTracking()
            .Where(row => row.HostBindingId == authority.Binding.Id && row.TrustDomainId == authority.Binding.TrustDomainId
                && row.TenantId == authority.Tenant.Id && row.SubjectKind == ModerationSubjectKind.Human && row.Action == action)
            .OrderByDescending(row => row.WriterGeneration).FirstOrDefaultAsync(token);
        if (source is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationOperationalBudgetSource>(context, source.Id, token);
        source.ValidatePersistedState();
        if (!await OperatorsCurrentAsync(authority, source.OperatorHumanId, source.RecordedByHumanId, token)
            || !await ReceiptAsync(authority, RecordRequest(source, authority.Tenant),
                ModerationQualificationSourceKind.OperationalBudgetSource, source.Id, 1,
                source.RecordedByHumanId, source.WriterGeneration, source.RecordedAt, token)
            || clock.GetUtcNow() < source.RecordedAt || clock.GetUtcNow() >= source.ValidUntil)
            return null;
        return source;
    }

    internal async Task<(ModerationOperationalBudgetSource Source, ModerationOperationalBudgetAdoption Adoption)?> ReadAsync(
        ModerationSourceAuthority authority, ModerationOperationalAction action, CancellationToken token)
    {
        var source = await ReadSourceAsync(authority, action, token);
        if (source is null) return null;
        var adoption = await context.Set<ModerationOperationalBudgetAdoption>().AsNoTracking().SingleOrDefaultAsync(row =>
            row.SourceId == source.Id && row.HostBindingId == authority.Binding.Id
            && row.TrustDomainId == authority.Binding.TrustDomainId && row.TenantId == authority.Tenant.Id, token);
        if (adoption is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationOperationalBudgetAdoption>(context, adoption.Id, token);
        adoption.ValidatePersistedState();
        if (!adoption.MatchesCurrent(source, clock.GetUtcNow())
            || !await ReceiptAsync(authority, AdoptionRequest(adoption, authority.Tenant),
                ModerationQualificationSourceKind.OperationalBudgetAdoption, adoption.Id, 1,
                adoption.AdoptedByHumanId, adoption.WriterGeneration, adoption.AdoptedAt, token)
            || !await OperatorsCurrentAsync(authority, source.OperatorHumanId, source.RecordedByHumanId, token)
            || !adoption.MatchesCurrent(source, clock.GetUtcNow()))
            return null;
        return (source, adoption);
    }

    internal async Task<bool> OperatorsCurrentAsync(
        ModerationSourceAuthority authority, Guid operatorHumanId, Guid recorder, CancellationToken token)
    {
        var humans = new[] { operatorHumanId, recorder };
        return operatorHumanId != recorder && humans.All(id =>
                id != Guid.Empty && authority.FencedHumans.Contains(id) && authority.Administrators.Contains(id))
            && await context.Users.AsNoTracking().CountAsync(row => humans.Contains(row.Id) && !row.IsDeleted, token) == 2
            && !await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => humans.Contains(row.SubjectId), token);
    }

    internal async Task<bool> ReceiptAsync(ModerationSourceAuthority authority, ModerationQualificationMutation template,
        ModerationQualificationSourceKind kind, Guid sourceId, long revision, Guid humanId,
        long generation, DateTimeOffset at, CancellationToken token)
    {
        var receipt = await context.ModerationQualificationMutationReceipts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.SourceId == sourceId && row.SourceKind == kind && row.ResultRevision == revision
            && row.HostBindingId == authority.Binding.Id && row.TrustDomainId == authority.Binding.TrustDomainId
            && row.TenantId == authority.Tenant.Id, token);
        if (receipt is null) return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationMutationReceipt>(context, receipt.Id, token);
        receipt.ValidatePersistedState();
        var request = template with { OperationId = receipt.Id };
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.GetType().FullName + "\n" + JsonSerializer.Serialize(request, request.GetType()))));
        return receipt.RequestDigest == digest && receipt.PrincipalId == humanId
            && receipt.QualificationRevision == generation && receipt.CompletedAt == at
            && generation <= authority.Epoch.Revision;
    }

    internal static RecordModerationOperationalBudgetSource RecordRequest(
        ModerationOperationalBudgetSource source, AuthorityScope tenant) => new()
    {
        OperationId = Guid.Empty, Target = tenant, ExpectedQualificationRevision = source.WriterGeneration - 1,
        SourceId = source.Id, Terms = source.Terms, OperatorHumanId = source.OperatorHumanId, ValidUntil = source.ValidUntil
    };

    internal static AdoptModerationOperationalBudgetSource AdoptionRequest(
        ModerationOperationalBudgetAdoption adoption, AuthorityScope tenant) => new()
    {
        OperationId = Guid.Empty, Target = tenant, ExpectedQualificationRevision = adoption.WriterGeneration - 1,
        SourceId = adoption.SourceId, ExpectedSourceRevision = adoption.SourceRevision,
        ExpectedSourceWriterGeneration = adoption.SourceWriterGeneration, AdoptionId = adoption.Id, ValidUntil = adoption.ValidUntil
    };

    internal static string Slot(ModerationSourceAuthority authority, ModerationOperationalAction action) =>
        $"moderation.operational-budget-source:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{(int)ModerationSubjectKind.Human}:{(int)action}";
}
