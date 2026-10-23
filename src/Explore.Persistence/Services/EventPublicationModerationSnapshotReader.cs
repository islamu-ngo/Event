using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

internal sealed record EventPublicationModerationFacts(
    bool IsAdopted, ModerationRestrictionOutcome Outcome, string Generation, DateTimeOffset? CurrentUntil)
{
    internal Guid? QualificationEpochId { get; init; }
    internal long QualificationRevision { get; init; }
    internal SubjectReference? Subject { get; init; }

    internal static EventPublicationModerationFacts NotAdopted { get; } =
        new(false, ModerationRestrictionOutcome.Unrestricted, "not-adopted", null);

    internal static EventPublicationModerationFacts Unavailable(string generation) =>
        new(true, ModerationRestrictionOutcome.Unavailable, generation, null);

    internal bool PermitsAt(DateTimeOffset now) => now.Offset == TimeSpan.Zero
        && Outcome == ModerationRestrictionOutcome.Unrestricted
        && (CurrentUntil is null || now < CurrentUntil);
}

/// <summary>
/// Reads only the current Human's Publish overlay at the actual Tenant scope. The native
/// publication boundary owns installation, subject, host, tenant, epoch and both policy namespace
/// fences, including empty slots. This reader acquires no later locks or synthetic resource target.
/// </summary>
internal sealed class EventPublicationModerationSnapshotReader(
    ExploreDbContext context, IModerationRecognitionContinuityReader continuity)
{
    internal async Task<EventPublicationModerationFacts> ReadAsync(
        Guid callerUserId, DateTimeOffset evaluationUtc, CancellationToken token)
    {
        var priorTrustDomain = context.ModerationTrustDomainId;
        try
        {
            token.ThrowIfCancellationRequested();
            if (callerUserId == Guid.Empty || evaluationUtc.Offset != TimeSpan.Zero
                || context.Database.CurrentTransaction is not { } transaction
                || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
                || transaction.GetDbTransaction().Connection != context.Database.GetDbConnection()
                || context.IsTenantFilterBypassed
                || context.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty
                || !await ProviderTenantMatchesAsync(tenantId, token))
                return EventPublicationModerationFacts.Unavailable("native-boundary-unavailable");

            if (!await context.Users.AsNoTracking().AnyAsync(row => row.Id == callerUserId && !row.IsDeleted, token)
                || await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => row.SubjectId == callerUserId, token)
                || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId
                    && row.TenantStatus.IsActiveState, token))
                return EventPublicationModerationFacts.Unavailable("native-subject-or-tenant-unavailable");

            var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
            // Only a positively absent installation permits this shortcut; failures below never do.
            if (host is null) return EventPublicationModerationFacts.NotAdopted;
            if (priorTrustDomain is { } trust && trust != host.TrustDomainId
                || !await context.ServicePrincipals.AsNoTracking().AnyAsync(
                    row => row.Id == host.MaintenanceServicePrincipalId && !row.IsDeleted, token))
                return EventPublicationModerationFacts.Unavailable("host-unavailable");

            context.ModerationTrustDomainId = host.TrustDomainId;
            var epoch = await context.ModerationQualificationEpochs.AsNoTracking().SingleOrDefaultAsync(
                row => row.HostBindingId == host.Id && row.TenantId == tenantId, token);
            if (epoch is not { Revision: > 0 })
                return EventPublicationModerationFacts.Unavailable("qualification-epoch-unavailable");

            var instance = AuthorityScope.Instance(
                AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
            var target = AuthorityScope.Tenant(instance, tenantId);
            var subject = SubjectReference.Create(ModerationSubjectKind.Human, callerUserId, instance);

            // Normal filters retain both instance and actual-tenant slots. Do not use the active
            // pointer or current activation conformance to decide whether restrictions survive.
            var selections = await context.ModerationPolicySelections.AsNoTracking().ToListAsync(token);
            var selectedIds = selections.Select(row => row.LatestPublicationId).ToArray();
            var publications = await ModerationPolicyPublicationGraph.Include(
                context.ModerationPolicyPublications.AsNoTracking().Where(row => selectedIds.Contains(row.Id)))
                .ToListAsync(token);
            var declarations = publications.Select(publication => new
            {
                Publication = publication,
                Draft = ModerationPolicyPublicationProjection.RestoreHistory(publication)
            }).ToArray();
            if (selections.Any(selection => selection.Revision is < 1 or long.MaxValue
                || selection.HostBindingId != host.Id || selection.TrustDomainId != host.TrustDomainId
                || !declarations.Any(declared => declared.Draft is not null
                    && declared.Publication.Id == selection.LatestPublicationId
                    && declared.Publication.PolicyId == selection.Id
                    && declared.Publication.Revision == selection.Revision
                    && declared.Publication.AdoptedRevision == selection.Revision
                    && declared.Publication.HostBindingId == host.Id
                    && declared.Publication.TrustDomainId == host.TrustDomainId
                    && declared.Publication.SolutionId == host.SolutionId
                    && declared.Publication.InstanceId == host.InstanceId
                    && declared.Publication.Jurisdiction.Id == selection.JurisdictionId
                    && declared.Publication.JurisdictionTenantId == selection.JurisdictionTenantId
                    && (declared.Publication.JurisdictionTenantId is null || declared.Publication.TenantId == tenantId)
                    && declared.Publication.AuthorityRevision is > 0 and < long.MaxValue
                    && declared.Publication.QualificationRevision == declared.Publication.AuthorityRevision + 1
                    && declared.Publication.PublishedAt <= evaluationUtc
                    && (declared.Publication.Jurisdiction == instance || declared.Publication.Jurisdiction == target))))
                return EventPublicationModerationFacts.Unavailable("selected-declaration-unavailable");

            var hasProfile = declarations.Any(declared => declared.Draft!.Profiles.Any(profile =>
                profile.Definition.SubjectKinds.Contains(ModerationSubjectKind.Human)
                && profile.Definition.ScopeKinds.Contains(ModerationScopeKind.Tenant)
                && profile.Definition.Effects.Any(effect => effect.Capability == ModerationCapability.Publish)
                && profile.Reference.AcceptsScope(declared.Publication.Jurisdiction, target)));

            // Historical contributions are independent of today's selected revision or support.
            // Read before declaring nonadoption, including scheduled contributions not effective yet.
            var sanctions = await context.ModerationSanctions.AsNoTracking()
                .Where(row => row.SubjectKind == ModerationSubjectKind.Human && row.SubjectId == callerUserId)
                .ToListAsync(token);
            var protection = await context.ModerationProtectiveMeasures.AsNoTracking()
                .Where(row => row.SubjectKind == ModerationSubjectKind.Human && row.SubjectId == callerUserId)
                .ToListAsync(token);
            foreach (var sanction in sanctions) sanction.ValidatePersistedState();
            foreach (var measure in protection) measure.ValidatePersistedState();
            var applicableSanctions = sanctions.Where(row => row.Subject == subject && row.Scope.Contains(target)
                && row.Capabilities.Contains(ModerationCapability.Publish) && row.IsEffectiveAt(row.StartsAt)
                && (row.EndsAt is null || evaluationUtc < row.EndsAt)).ToArray();
            var applicableProtection = protection.Where(row => row.Subject == subject && row.Scope.Contains(target)
                && row.Capabilities.Contains(ModerationCapability.Publish) && row.IsEffectiveAt(row.StartsAt)
                && evaluationUtc < row.EndsAt).ToArray();

            var recognized = await continuity.ReadPublicationInTransactionAsync(callerUserId, target, token);
            if (recognized.ActionFailure != ModerationRecognitionContinuityFailure.None
                || string.IsNullOrWhiteSpace(recognized.Generation) || recognized.Actions.IsDefault
                || recognized.CurrentUntil is { } bound && (bound.Offset != TimeSpan.Zero || evaluationUtc >= bound))
                return EventPublicationModerationFacts.Unavailable("continuity-authority-unavailable");

            var outcome = EffectiveRestrictions.Evaluate(subject, target, ModerationCapability.Publish,
                evaluationUtc, applicableSanctions, applicableProtection, ModerationAuthorityState.Known).Outcome;
            if (!recognized.Actions.IsEmpty) outcome = ModerationRestrictionOutcome.Denied;
            var adopted = hasProfile || applicableSanctions.Length != 0 || applicableProtection.Length != 0
                || !recognized.Actions.IsEmpty || recognized.CurrentUntil is not null;
            var currentUntil = recognized.CurrentUntil;
            var boundaries = applicableSanctions.SelectMany(row => new DateTimeOffset?[] { row.StartsAt, row.EndsAt })
                .Concat(applicableProtection.SelectMany(row => new DateTimeOffset?[] { row.StartsAt, row.EndsAt }));
            foreach (var boundary in boundaries)
                if (boundary is { } time && time > evaluationUtc && (currentUntil is null || time < currentUntil))
                    currentUntil = time;

            var generation = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                Version = 1,
                Host = host.Id,
                host.TrustDomainId,
                host.SolutionId,
                host.InstanceId,
                host.MaintenanceServicePrincipalId,
                Subject = subject,
                Target = target,
                Epoch = epoch.Id,
                Policies = declarations.OrderBy(declared => declared.Publication.PolicyId).Select(declared => new
                {
                    declared.Publication.PolicyId,
                    declared.Publication.Revision,
                    PublicationId = declared.Publication.Id,
                    declared.Publication.DeclaredStateDigest
                }),
                Sanctions = sanctions.OrderBy(row => row.Id).Select(row => new { row.Id, row.Revision }),
                Protection = protection.OrderBy(row => row.Id).Select(row => new { row.Id, row.Revision }),
                Continuity = recognized.Generation,
                CurrentUntil = currentUntil
            })));
            return new(adopted, outcome, generation, currentUntil)
            {
                QualificationEpochId = epoch.Id,
                QualificationRevision = epoch.Revision,
                Subject = subject
            };
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            // Neither malformed persisted state nor an unavailable store proves absence of adoption.
            return EventPublicationModerationFacts.Unavailable("publication-snapshot-unavailable");
        }
        finally
        {
            context.ModerationTrustDomainId = priorTrustDomain;
        }
    }

    private async Task<bool> ProviderTenantMatchesAsync(Guid tenantId, CancellationToken token)
    {
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider) return true;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        var value = await command.ExecuteScalarAsync(token);
        return value is string setting && Guid.TryParse(setting, out var providerTenant) && providerTenant == tenantId;
    }
}
