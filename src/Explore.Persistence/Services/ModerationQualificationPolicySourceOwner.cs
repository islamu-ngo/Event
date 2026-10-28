using System.Data;
using System.Globalization;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>Reads actual current procedural governance and separately retained adoption under their native writer fences.</summary>
public sealed class ModerationQualificationPolicySourceOwner(ExploreDbContext context, TimeProvider clock)
    : IModerationQualificationPolicySourceOwner
{
    public async Task<ModerationQualificationPolicyCurrentSource?> AcquireQualificationPolicyAsync(
        Guid hostBindingId, Guid transactionId, Guid caseId, AuthorityScope scope,
        DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        var source = await ReadSourceAsync(hostBindingId, transactionId, caseId, scope, observedAt, cancellationToken);
        return source is null ? null : new(transactionId, hostBindingId, caseId, scope,
            source.Id, source.Revision, Generation(source.WriterGeneration),
            source.MaximumReviewerLifetime, source.MaximumUrgencyLifetime, observedAt, source.ValidUntil);
    }

    public async Task<ModerationQualificationPolicyApproval?> ReadQualificationPolicyApprovalAsync(
        Guid hostBindingId, Guid transactionId, Guid caseId, AuthorityScope scope,
        DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        var source = await ReadSourceAsync(hostBindingId, transactionId, caseId, scope, observedAt, cancellationToken);
        if (source is null) return null;
        var discovered = await context.ModerationQualificationPolicyAdoptions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.SourceId == source.Id && row.SourceRevision == source.Revision,
                cancellationToken);
        if (discovered is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationPolicyAdoption>(
            context, discovered.Id, cancellationToken);
        var adoption = await context.ModerationQualificationPolicyAdoptions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered.Id, cancellationToken);
        if (adoption is null || !adoption.MatchesCurrent(source, source.CaseRevision, observedAt)
            || !adoption.MatchesCurrent(source, source.CaseRevision, clock.GetUtcNow())
            || !await HasReceiptAsync(ModerationQualificationSourceKind.QualificationPolicyAdoption,
                adoption.Id, 1, adoption.HostBindingId, adoption.TrustDomainId, adoption.TenantId,
                adoption.WriterGeneration, adoption.AdoptedByHumanId, adoption.AdoptedAt, cancellationToken)
            || !adoption.MatchesCurrent(source, source.CaseRevision, clock.GetUtcNow()))
            return null;
        return new(transactionId, hostBindingId, caseId, scope, adoption.Id, adoption.Revision,
            adoption.SourceId, adoption.SourceRevision, Generation(adoption.WriterGeneration),
            adoption.MaximumReviewerLifetime, adoption.MaximumUrgencyLifetime,
            adoption.AdoptedAt, adoption.ValidUntil, observedAt);
    }

    private async Task<ModerationQualificationPolicySource?> ReadSourceAsync(
        Guid hostBindingId, Guid transactionId, Guid caseId, AuthorityScope scope,
        DateTimeOffset observedAt, CancellationToken token)
    {
        var transaction = context.Database.CurrentTransaction;
        if (transaction is null || transaction.TransactionId != transactionId
            || !context.Database.IsRelational()
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || transaction.GetDbTransaction().Connection != context.Database.GetDbConnection()
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId is not { } tenantId
            || context.ModerationTrustDomainId != scope.TrustDomainId
            || observedAt.Offset != TimeSpan.Zero || clock.GetUtcNow() < observedAt)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, caseId, token);
        var allegation = await context.ModerationCases.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == caseId, token);
        var host = await context.ModerationHostBindings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == hostBindingId && row.TrustDomainId == scope.TrustDomainId, token);
        if (allegation is null || host is null) return null;
        allegation.ValidatePersistedState();
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        var tenant = AuthorityScope.Tenant(instance, tenantId);
        if (allegation.State != ModerationCaseState.Open || allegation.Scope != scope
            || scope != instance && !tenant.Contains(scope) || observedAt < allegation.CreatedAt)
            return null;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.qualification-policy:{hostBindingId:N}:{caseId:N}", token);
        var discovered = await context.ModerationQualificationPolicySources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostBindingId == hostBindingId && row.CaseId == caseId, token);
        if (discovered is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationPolicySource>(context, discovered.Id, token);
        var source = await context.ModerationQualificationPolicySources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered.Id, token);
        if (source is null) return null;
        source.ValidatePersistedState();
        var now = clock.GetUtcNow();
        if (source.HostBindingId != hostBindingId || source.TrustDomainId != scope.TrustDomainId
            || source.TenantId != tenantId || source.CaseId != caseId || source.CaseRevision != allegation.Revision
            || observedAt < source.PublishedAt || observedAt >= source.ValidUntil
            || now < observedAt || now >= source.ValidUntil
            || !await HasReceiptAsync(ModerationQualificationSourceKind.QualificationPolicy,
                source.Id, source.Revision, source.HostBindingId, source.TrustDomainId, source.TenantId,
                source.WriterGeneration, source.PublishedByHumanId, source.PublishedAt, token)
            || clock.GetUtcNow() >= source.ValidUntil)
            return null;
        return source;
    }

    private Task<bool> HasReceiptAsync(
        ModerationQualificationSourceKind kind, Guid sourceId, long revision,
        Guid hostId, Guid trustDomainId, Guid tenantId, long generation, Guid humanId, DateTimeOffset completedAt,
        CancellationToken token) =>
        context.ModerationQualificationMutationReceipts.AsNoTracking().AnyAsync(row =>
            row.SourceKind == kind && row.SourceId == sourceId && row.ResultRevision == revision
            && row.HostBindingId == hostId && row.TrustDomainId == trustDomainId && row.TenantId == tenantId
            && (kind == ModerationQualificationSourceKind.QualificationPolicy
                ? row.QualificationRevision == generation : row.QualificationRevision > generation)
            && row.PrincipalId == humanId && row.CompletedAt == completedAt, token);

    private static string Generation(long generation) =>
        "event.moderation.qualification-policy.v1:" + generation.ToString(CultureInfo.InvariantCulture);
}
