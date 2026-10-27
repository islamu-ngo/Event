using System.Data;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class UserLocationPrivacyErasureRepository
{
    public async Task EraseModerationCaseNarrativesAsync(
        PrivacyErasureIntent intent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        RequireId(intent.SubjectId, nameof(intent));
        if (intent.SubjectKind != PrivacyErasureSubjectKind.User || intent.IsLegalHoldPseudonymized
            || !dbContext.Database.IsRelational() || dbContext.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Narrative erasure requires native serializable privacy settlement.");

        // The workflow verified the retained fact under its original identity/recognition
        // fences. Require that exact durable local saga, not a caller-selected user or a
        // moderation discovery marker, before crossing any original reporter partition.
        await RelationalEntityRowFence.AcquireGlobalAsync<User>(dbContext, intent.SubjectId, cancellationToken);
        if (!await dbContext.PrivacyErasureSagas.AsNoTracking().AnyAsync(row =>
                row.IntentId == intent.IntentId && row.SubjectId == intent.SubjectId
                && row.SubjectKind == intent.SubjectKind && row.PolicyVersion == intent.PolicyVersion
                && row.FenceToken == intent.AuthoritySequence, cancellationToken))
            throw new InvalidOperationException("Narrative erasure requires the exact native privacy saga.");

        await EraseProfileCollectionMetadataAsync(intent.SubjectId, cancellationToken);

        const int batchSize = 100;
        string reason = TenantFilterBypassReasons.UserPrivacyErasure;
        string? previousTenant = await PostgresTransactionTenantSetting.ReadAsync(dbContext, cancellationToken);

        // Tenants are the complete global inventory, including inactive tenants and
        // former memberships. Narrative rows cannot be discovered across PostgreSQL RLS
        // partitions by disabling EF filters. Inventory selects work, never authority.
        Guid? afterTenant = null;
        while (true)
        {
            Guid[] tenants = await dbContext.Tenants.IgnoreAllFilters(reason).AsNoTracking()
                .Where(row => afterTenant == null || row.Id.CompareTo(afterTenant.Value) > 0)
                .OrderBy(row => row.Id).Select(row => row.Id).Take(batchSize).ToArrayAsync(cancellationToken);
            if (tenants.Length == 0) break;
            foreach (Guid tenant in tenants)
            {
                await PostgresTransactionTenantSetting.SetAsync(dbContext, tenant.ToString("D"), cancellationToken);
                // Global user erasure is independent of the current host, policy, trust
                // selection, deadline and decryptor. Every read/delete retains both exact
                // reporter and tenant predicates; no accusation or ciphertext is selected.
                var owned = dbContext.ModerationCaseNarratives.IgnoreAllFilters(reason)
                    .Where(row => row.TenantId == tenant && row.ReporterUserId == intent.SubjectId);
                // OriginalNarrativeId is restrictive: dispose dependent assessment custody
                // first, including separately alleged, assessing and read-issuing Humans.
                // Purchase, organizer, participant links and policy ownership confer no
                // assessment ancestry and must not broaden this exact erasure predicate.
                var assessments = dbContext.ModerationParticipantIncidentResponsibilitySources
                    .IgnoreAllFilters(reason)
                    .Where(row => row.TenantId == tenant
                        && (row.SubjectId == intent.SubjectId || row.AssessorHumanId == intent.SubjectId
                            || row.ReadIssuerHumanId == intent.SubjectId
                            || owned.Any(original => original.Id == row.OriginalNarrativeId)));
                bool disposedAssessments = false;
                while (true)
                {
                    Guid[] ids = await assessments.AsNoTracking().OrderBy(row => row.Id)
                        .Select(row => row.Id).Take(batchSize).ToArrayAsync(cancellationToken);
                    if (ids.Length == 0) break;
                    int removed = await assessments.Where(row => ids.Contains(row.Id))
                        .ExecuteDeleteAsync(cancellationToken);
                    if (removed != ids.Length)
                        throw new InvalidOperationException("Incident assessment erasure did not settle its selected batch.");
                    disposedAssessments = true;
                }
                if (disposedAssessments)
                    await dbContext.RecordModerationRecoveryBulkMutationAsync(cancellationToken);
                while (true)
                {
                    Guid[] ids = await owned.AsNoTracking().OrderBy(row => row.Id)
                        .Select(row => row.Id).Take(batchSize).ToArrayAsync(cancellationToken);
                    if (ids.Length == 0) break;
                    int removed = await owned.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync(cancellationToken);
                    if (removed != ids.Length)
                        throw new InvalidOperationException("Narrative erasure did not settle its selected batch.");
                }
                await EraseContentResponsesInTenantAsync(intent.SubjectId, tenant, cancellationToken);
                await ErasePreFindingInTenantAsync(intent.SubjectId, tenant, cancellationToken);
                await EraseAdjudicatedReadGrantsInTenantAsync(intent.SubjectId, tenant, cancellationToken);
            }
            afterTenant = tenants[^1];
        }

        // Preserve the caller's pooled-session identity. On failure the owning settlement
        // rolls back SET LOCAL with all deletes; never command an aborted transaction.
        await PostgresTransactionTenantSetting.SetAsync(dbContext, previousTenant!, cancellationToken);
    }
}
