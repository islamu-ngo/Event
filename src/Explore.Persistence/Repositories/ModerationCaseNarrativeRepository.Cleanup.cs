using System.Data;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationCaseNarrativeRepository
{
    /// <summary>
    /// Bounded monotone physical cleanup, requiring neither a live custody policy nor a
    /// decryptor. The scheduler may only remove already-expired or withdrawn custody.
    /// Original acknowledgement and material provenance remain to prevent replay resurrection.
    /// </summary>
    public Task<int> PurgeExpiredAsync(int maximumRows, CancellationToken cancellationToken) =>
        PurgeAsync(null, maximumRows, cancellationToken);

    /// <summary>
    /// Ordinary erasure follows an already durable native erasure authority fact, independent
    /// of moderation policy, adjudication, recognition availability and key availability.
    /// This bounded selected-tenant cleanup cannot settle a global saga. The privacy applier
    /// owns complete cross-tenant disposal through IUserPrivacyErasureRepository.
    /// </summary>
    public Task<int> PurgeForErasureAsync(Guid reporterUserId, int maximumRows, CancellationToken cancellationToken) =>
        reporterUserId == Guid.Empty ? Task.FromResult(0) : PurgeAsync(reporterUserId, maximumRows, cancellationToken);

    private async Task<int> PurgeAsync(Guid? reporter, int maximumRows, CancellationToken token)
    {
        if (maximumRows is < 1 or > 1000 || !context.Database.IsRelational()
            || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || context.ChangeTracker.Entries().Any() || context.IsTenantFilterBypassed
            || context.TenantFilterTenantId is not { } tenant || tenant == Guid.Empty
            || context.ModerationTrustDomainId is not null)
            return 0;
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                if (!await new ModerationQualificationAuthority(context, admin, machine).ProviderTenantMatchesAsync(tenant, token))
                    return 0;
                if (reporter is { } user)
                {
                    await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, user, token);
                    if (!await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => row.SubjectId == user, token))
                        return 0;
                }
                var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
                if (host is null) return 0;
                context.ModerationTrustDomainId = host.TrustDomainId;
                var now = clock.GetUtcNow();
                var assessmentRemoved = await PurgeIncidentAssessmentsAsync(
                    reporter, maximumRows, host, tenant, now, token);
                var sqlite = context.Database.ProviderName == RelationalNamedLock.SqliteProvider;
                // SQLite cannot translate relational DateTimeOffset comparisons. Preserve
                // parameterized SQL eligibility and compose the same native query filters.
                var candidates = reporter is null && sqlite
                    ? context.Set<ModerationCaseNarrative>().FromSqlInterpolated($"""
                        SELECT n.* FROM moderation_case_narratives AS n
                        WHERE julianday(n.purge_at) <= julianday({now})
                           OR EXISTS (
                               SELECT 1 FROM moderation_case_narrative_adoptions AS a
                               WHERE a.id = n.adoption_id AND a.tenant_id = {tenant}
                                 AND a.trust_domain_id = {host.TrustDomainId}
                                 AND a.withdrawn_at IS NOT NULL
                                 AND julianday(a.withdrawn_at) <= julianday({now}))
                        """)
                    : context.Set<ModerationCaseNarrative>().AsQueryable();
                var eligible = candidates.Where(row =>
                    row.TenantId == tenant && row.TrustDomainId == host.TrustDomainId && row.HostBindingId == host.Id);
                // A bounded page may leave further dependent assessments. Never delete their
                // original custody first or turn a physical-cleanup pass into an FK failure.
                eligible = eligible.Where(row => !context.ModerationParticipantIncidentResponsibilitySources
                    .Any(assessment => assessment.OriginalNarrativeId == row.Id));
                if (reporter is { } owner)
                    eligible = eligible.Where(row => row.ReporterUserId == owner);
                else if (!sqlite)
                    eligible = eligible.Where(row => row.PurgeAt <= now || context.Set<ModerationCaseNarrativeAdoption>().Any(adoption =>
                        adoption.Id == row.AdoptionId && adoption.TenantId == tenant
                        && adoption.TrustDomainId == host.TrustDomainId && adoption.WithdrawnAt != null && adoption.WithdrawnAt <= now));
                var ids = await eligible.AsNoTracking().OrderBy(row => row.Id)
                    .Take(maximumRows - assessmentRemoved).Select(row => row.Id).ToArrayAsync(token);
                foreach (var id in ids.Order())
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCaseNarrative>(context, id, token);
                var removed = assessmentRemoved + await eligible.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync(token);
                if (removed > 0)
                    await context.RecordModerationRecoveryBulkMutationAsync(token);
                if (context.TenantFilterTenantId != tenant || context.IsTenantFilterBypassed
                    || context.ModerationTrustDomainId != host.TrustDomainId || clock.GetUtcNow() < now
                    || !await new ModerationQualificationAuthority(context, admin, machine).ProviderTenantMatchesAsync(tenant, token))
                    return 0;
                await transaction.CommitAsync(token);
                return removed;
            });
        }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }
    }

    private async Task<int> PurgeIncidentAssessmentsAsync(Guid? reporter, int maximumRows,
        ModerationHostBinding host, Guid tenant, DateTimeOffset now, CancellationToken token)
    {
        var candidates = context.ModerationParticipantIncidentResponsibilitySources.AsQueryable();
        if (reporter is null)
        {
            var ticks = now.UtcTicks;
            candidates = context.Database.ProviderName == RelationalNamedLock.SqliteProvider
                ? context.ModerationParticipantIncidentResponsibilitySources.FromSqlInterpolated($"""
                    SELECT s.* FROM moderation_participant_incident_responsibility_sources AS s
                    WHERE s.purge_at <= {ticks} OR s.withdrawn_at <= {ticks}
                       OR EXISTS (
                           SELECT 1 FROM moderation_case_narrative_adoptions AS a
                           WHERE a.id = s.custody_adoption_id AND a.tenant_id = {tenant}
                             AND a.trust_domain_id = {host.TrustDomainId}
                             AND julianday(a.withdrawn_at) <= julianday({now}))
                       OR EXISTS (
                           SELECT 1 FROM moderation_case_narratives AS n
                           JOIN moderation_case_narrative_adoptions AS a ON a.id = n.adoption_id
                           WHERE n.id = s.original_narrative_id AND n.tenant_id = {tenant}
                             AND n.trust_domain_id = {host.TrustDomainId}
                             AND a.tenant_id = {tenant} AND a.trust_domain_id = {host.TrustDomainId}
                             AND julianday(a.withdrawn_at) <= julianday({now}))
                    """)
                : context.ModerationParticipantIncidentResponsibilitySources.FromSqlInterpolated($"""
                    SELECT s.* FROM moderation_participant_incident_responsibility_sources AS s
                    WHERE s.purge_at <= {ticks} OR s.withdrawn_at <= {ticks}
                       OR EXISTS (
                           SELECT 1 FROM moderation_case_narrative_adoptions AS a
                           WHERE a.id = s.custody_adoption_id AND a.tenant_id = {tenant}
                             AND a.trust_domain_id = {host.TrustDomainId} AND a.withdrawn_at <= {now})
                       OR EXISTS (
                           SELECT 1 FROM moderation_case_narratives AS n
                           JOIN moderation_case_narrative_adoptions AS a ON a.id = n.adoption_id
                           WHERE n.id = s.original_narrative_id AND n.tenant_id = {tenant}
                             AND n.trust_domain_id = {host.TrustDomainId}
                             AND a.tenant_id = {tenant} AND a.trust_domain_id = {host.TrustDomainId}
                             AND a.withdrawn_at <= {now})
                    """);
        }
        var eligible = candidates.Where(row => row.HostBindingId == host.Id
            && row.TenantId == tenant && row.TrustDomainId == host.TrustDomainId);
        if (reporter is { } owner)
            eligible = eligible.Where(row => context.ModerationCaseNarratives.Any(original =>
                original.Id == row.OriginalNarrativeId && original.TenantId == tenant
                && original.TrustDomainId == host.TrustDomainId && original.HostBindingId == host.Id
                && original.ReporterUserId == owner));
        var selected = await eligible.AsNoTracking().OrderBy(row => row.Id).Take(maximumRows)
            .Select(row => new
            {
                row.Id,
                row.OriginalNarrativeId,
                row.PurgeAt,
                row.WithdrawnAt,
                InvestigationWithdrawnAt = context.ModerationCaseNarrativeAdoptions
                    .Where(adoption => adoption.Id == row.CustodyAdoptionId
                        && adoption.TenantId == tenant && adoption.TrustDomainId == host.TrustDomainId)
                    .Select(adoption => adoption.WithdrawnAt).SingleOrDefault(),
                OriginalWithdrawnAt = (
                    from original in context.ModerationCaseNarratives
                    join adoption in context.ModerationCaseNarrativeAdoptions on original.AdoptionId equals adoption.Id
                    where original.Id == row.OriginalNarrativeId && original.TenantId == tenant
                        && original.TrustDomainId == host.TrustDomainId
                        && adoption.TenantId == tenant && adoption.TrustDomainId == host.TrustDomainId
                    select adoption.WithdrawnAt).SingleOrDefault()
            }).ToArrayAsync(token);
        // SQLite's Julian-day discovery loses sub-millisecond precision. The bounded
        // projection contains only identifiers/deadlines: exact recorded UTC instants,
        // not rounded discovery matches, determine whether physical disposal is due.
        selected = selected.Where(row => reporter is not null || row.PurgeAt <= now
            || row.WithdrawnAt is { } withdrawal && withdrawal <= now
            || row.InvestigationWithdrawnAt is { } investigation && investigation <= now
            || row.OriginalWithdrawnAt is { } original && original <= now).ToArray();
        foreach (var parent in selected.Select(row => row.OriginalNarrativeId).Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCaseNarrative>(context, parent, token);
        foreach (var row in selected)
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationParticipantIncidentResponsibilitySource>(
                context, row.Id, token);
        var ids = selected.Select(row => row.Id).ToArray();
        return await eligible.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync(token);
    }
}
