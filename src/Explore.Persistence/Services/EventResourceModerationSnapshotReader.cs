using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>
/// Reads resource-read/upload overlays and the current Human's Tenant publication overlay.
/// Installation is provenance only. Adoption retains read protection independently
/// of support for activating new effects, so retiring support cannot erase sanctions.
/// </summary>
public sealed class EventResourceModerationSnapshotReader(
    ExploreDbContext context, IModerationMachineSubjectBindingRepository bindings,
    IModerationRecognitionContinuityReader continuity)
    : IEventResourceModerationSnapshotReader
{
    public async Task<EventResourceModerationFacts> ReadAsync(
        EventResourceAuthorityRequest request, DateTimeOffset evaluationUtc,
        CancellationToken cancellationToken) =>
        (await ReadBatchAsync([request], evaluationUtc, cancellationToken))[0];

    public async Task<IReadOnlyList<EventResourceModerationFacts>> ReadBatchAsync(
        IReadOnlyList<EventResourceAuthorityRequest> requests, DateTimeOffset evaluationUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count > EventResourceAuthorityRequest.MaximumBatchChecks)
            throw new ArgumentOutOfRangeException(nameof(requests));
        if (requests.Count == 0) return [];
        EventResourceModerationFacts[] result =
            Enumerable.Repeat(EventResourceModerationFacts.NotAdopted, requests.Count).ToArray();
        var first = requests[0];
        if (evaluationUtc.Offset != TimeSpan.Zero || first.TenantId == Guid.Empty
            || !first.HasConsistentPrincipal
            || requests.Any(request => request.TenantId != first.TenantId
                || request.SubjectUserId != first.SubjectUserId
                || request.MachinePrincipal != first.MachinePrincipal
                || request.IsMachineCaller != first.IsMachineCaller
                || !request.HasConsistentPrincipal || !request.HasConsistentUploadPurpose))
            return Enumerable.Repeat(EventResourceModerationFacts.Unavailable("invalid-request"), requests.Count).ToArray();

        if (requests.Any(request => request.Action == "publish"))
        {
            var publication = first.IsMachineCaller || first.SubjectUserId is not { } humanId
                ? EventPublicationModerationFacts.Unavailable("publication-human-required")
                : await new EventPublicationModerationSnapshotReader(context, continuity)
                    .ReadAsync(humanId, evaluationUtc, cancellationToken);
            // Resource leases bind the actual epoch revision; creation's own-epoch exception does not apply here.
            var generation = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                Version = 1,
                publication.Generation,
                publication.QualificationEpochId,
                publication.QualificationRevision
            })));
            var facts = publication.Outcome == ModerationRestrictionOutcome.Unavailable
                ? EventResourceModerationFacts.Unavailable(generation)
                : !publication.IsAdopted
                    ? EventResourceModerationFacts.QualifiedNonadoption(generation)
                    : publication.Subject is not { } subject
                        ? EventResourceModerationFacts.Unavailable(generation)
                        : EventResourceModerationFacts.Qualified(subject, publication.Outcome,
                            generation, currentUntil: publication.CurrentUntil);
            for (int index = 0; index < requests.Count; index++)
                if (requests[index].Action == "publish") result[index] = facts;
        }

        var readTargets = requests.Where(request => Capability(request) is not null).Select(request => request.ResourceId)
            .Where(id => id != Guid.Empty).Distinct().ToArray();
        if (readTargets.Length > EventResourceAuthorityRequest.MaximumBatchResources)
            throw new ArgumentOutOfRangeException(nameof(requests));
        if (readTargets.Length == 0) return result;
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (host is null) return result;
        if (!await ProviderTenantMatchesAsync(first.TenantId, cancellationToken))
        {
            for (int index = 0; index < requests.Count; index++)
                if (Capability(requests[index]) is not null)
                    result[index] = EventResourceModerationFacts.Unavailable("provider-tenant-unavailable");
            return result;
        }
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId),
            host.InstanceId);
        var tenant = AuthorityScope.Tenant(instance, first.TenantId);
        var priorTrustDomain = context.ModerationTrustDomainId;
        try
        {
            context.ModerationTrustDomainId = host.TrustDomainId;
            var selections = await context.ModerationPolicySelections.AsNoTracking()
                .Where(row => row.HostBindingId == host.Id
                    && (row.JurisdictionId == host.InstanceId || row.JurisdictionId == first.TenantId))
                .ToListAsync(cancellationToken);
            if (selections.Count == 0) return result;
            var selectedIds = selections.Select(row => row.LatestPublicationId).ToArray();
            var publications = await ModerationPolicyPublicationGraph.Include(
                context.ModerationPolicyPublications.AsNoTracking().Where(row => selectedIds.Contains(row.Id)))
                .ToListAsync(cancellationToken);
            var declarations = publications.Select(publication => new
            {
                Publication = publication,
                // Read protection is retained declaration, not permission to activate new effects.
                // A current inventory revision must not erase an already installed restriction.
                Draft = ModerationPolicyPublicationProjection.RestoreHistory(publication)
            }).ToArray();
            if (selections.Any(selection => !declarations.Any(declared =>
                declared.Draft is not null && declared.Publication.Id == selection.LatestPublicationId
                && declared.Publication.PolicyId == selection.Id && declared.Publication.Revision == selection.Revision
                && declared.Publication.HostBindingId == host.Id && declared.Publication.TrustDomainId == host.TrustDomainId
                && declared.Publication.SolutionId == host.SolutionId && declared.Publication.InstanceId == host.InstanceId
                && declared.Publication.Jurisdiction.Id == selection.JurisdictionId
                && declared.Publication.JurisdictionTenantId == selection.JurisdictionTenantId
                && (declared.Publication.Jurisdiction == instance || declared.Publication.Jurisdiction == tenant))))
            {
                for (int index = 0; index < requests.Count; index++)
                    if (Capability(requests[index]) is not null)
                        result[index] = EventResourceModerationFacts.Unavailable("selected-declaration-unavailable");
                return result;
            }
            var adopted = declarations.Where(declared => declared.Draft!.Profiles.Any(profile =>
                profile.Definition.Effects.Any(effect => requests.Any(request => Capability(request) == effect.Capability))
                && profile.Definition.ScopeKinds.Contains(ModerationScopeKind.Resource)
                && profile.Reference.AcceptsScope(declared.Publication.Jurisdiction,
                    AuthorityScope.Resource(tenant, readTargets[0])))).ToArray();
            if (adopted.Length == 0) return result;

            bool qualifiedContext = context.Database.CurrentTransaction is not null
                && !context.IsTenantFilterBypassed && context.TenantFilterTenantId == first.TenantId;
            var epoch = qualifiedContext
                ? await context.ModerationQualificationEpochs.AsNoTracking().SingleOrDefaultAsync(
                    row => row.HostBindingId == host.Id && row.TenantId == first.TenantId, cancellationToken)
                : null;
            SubjectReference? subject = null;
            ModerationMachineSubjectBinding? binding = null;
            if (qualifiedContext && epoch is { Revision: > 0 })
            {
                if (first.IsMachineCaller && first.MachinePrincipal is { } principal)
                {
                    ModerationMachineCredentialSource? source = principal.CredentialSource switch
                    {
                        ApiKeyCredentialSource.Persisted => ModerationMachineCredentialSource.Persisted,
                        ApiKeyCredentialSource.Configured => ModerationMachineCredentialSource.Configured,
                        _ => null
                    };
                    if (source is { } credentialSource)
                        binding = await bindings.ReadCurrentAsync(principal.KeyId, credentialSource, cancellationToken);
                    if (binding is not null && binding.HostBindingId == host.Id
                        && binding.AuthorityTenantId == principal.AuthorityTenantId
                        && binding.OwnerType == principal.OwnerType && binding.OwnerId == principal.OwnerId)
                        subject = SubjectReference.Create(ModerationSubjectKind.Service, binding.ServicePrincipalId, instance);
                }
                else if (first.SubjectUserId is { } userId
                    && await context.Users.AsNoTracking().AnyAsync(
                        user => user.Id == userId && !user.IsDeleted, cancellationToken))
                    subject = SubjectReference.Create(ModerationSubjectKind.Human, userId, instance);
            }

            var sanctions = subject is null ? [] : await context.ModerationSanctions.AsNoTracking()
                .Where(row => row.SubjectKind == subject.Kind && row.SubjectId == subject.Id)
                .ToListAsync(cancellationToken);
            var protection = subject is null ? [] : await context.ModerationProtectiveMeasures.AsNoTracking()
                .Where(row => row.SubjectKind == subject.Kind && row.SubjectId == subject.Id)
                .ToListAsync(cancellationToken);
            foreach (var sanction in sanctions) sanction.ValidatePersistedState();
            foreach (var measure in protection) measure.ValidatePersistedState();
            Dictionary<(ModerationCapability Capability, Guid ResourceId), ModerationRecognitionContinuityResult> recognized = [];
            if (subject is { Kind: ModerationSubjectKind.Human })
            {
                // Repeated metadata/payload checks share one target qualification.
                // Larger read families use bounded chunks, not resource-per-target queries.
                foreach (var family in requests.Where(request => Capability(request) is not null)
                    .GroupBy(request => Capability(request)!.Value))
                    foreach (var ids in family.Select(request => request.ResourceId).Where(id => id != Guid.Empty)
                        .Distinct().Chunk(EventResourceAuthorityRequest.MaximumBatchResources))
                    {
                        var targets = ids.Select(id => AuthorityScope.Resource(tenant, id)).ToImmutableArray();
                        var current = await continuity.ReadResourcesInTransactionAsync(subject.Id,
                            family.Key, targets, cancellationToken);
                        if (current.Length != ids.Length)
                            return Enumerable.Repeat(EventResourceModerationFacts.Unavailable("continuity-batch-unavailable"),
                                requests.Count).ToArray();
                        for (var index = 0; index < ids.Length; index++) recognized.Add((family.Key, ids[index]), current[index]);
                    }
            }
            for (int index = 0; index < requests.Count; index++)
            {
                var request = requests[index];
                if (Capability(request) is not { } capability || request.ResourceId == Guid.Empty) continue;
                var target = AuthorityScope.Resource(tenant, request.ResourceId);
                var applicable = adopted.Where(declared => declared.Publication.Jurisdiction.Contains(target)
                    && declared.Draft!.Profiles.Any(profile => profile.Definition.ScopeKinds.Contains(ModerationScopeKind.Resource)
                        && profile.Definition.Effects.Any(effect => effect.Capability == capability)
                        && profile.Reference.AcceptsScope(declared.Publication.Jurisdiction, target))).ToArray();
                if (applicable.Length == 0) continue;
                var contribution = recognized.GetValueOrDefault((capability, request.ResourceId));
                if (contribution is not null && (contribution.ActionFailure != ModerationRecognitionContinuityFailure.None
                    || contribution.Generation is null
                    || contribution.CurrentUntil is { } bound && evaluationUtc >= bound))
                {
                    result[index] = EventResourceModerationFacts.Unavailable("continuity-authority-unavailable");
                    continue;
                }
                var outcome = subject is null ? ModerationRestrictionOutcome.Unavailable :
                    EffectiveRestrictions.Evaluate(subject, target, capability,
                        evaluationUtc, sanctions, protection, ModerationAuthorityState.Known).Outcome;
                if (outcome == ModerationRestrictionOutcome.Unrestricted && contribution is { Actions.IsEmpty: false })
                    outcome = ModerationRestrictionOutcome.Denied;
                var currentUntil = contribution?.CurrentUntil;
                var boundaries = sanctions.Where(row => row.Subject.Equals(subject) && row.Scope.Contains(target)
                        && row.Capabilities.Contains(capability) && row.IsEffectiveAt(row.StartsAt))
                    .SelectMany(row => new DateTimeOffset?[] { row.StartsAt, row.EndsAt })
                    .Concat(protection.Where(row => row.Subject.Equals(subject) && row.Scope.Contains(target)
                            && row.Capabilities.Contains(capability) && row.IsEffectiveAt(row.StartsAt))
                        .SelectMany(row => new DateTimeOffset?[] { row.StartsAt, row.EndsAt }));
                foreach (var boundary in boundaries)
                    if (boundary is { } time && time > evaluationUtc && (currentUntil is null || time < currentUntil))
                        currentUntil = time;
                var generation = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Version = 3,
                    Capability = capability,
                    Host = host.Id,
                    Target = target,
                    Subject = subject,
                    Epoch = epoch?.Id,
                    EpochRevision = epoch?.Revision,
                    Binding = binding?.Id,
                    BindingRevision = binding?.Revision,
                    Policies = applicable.OrderBy(declared => declared.Publication.PolicyId)
                        .Select(declared => new
                        {
                            declared.Publication.PolicyId,
                            declared.Publication.Revision,
                            PublicationId = declared.Publication.Id,
                            declared.Publication.DeclaredStateDigest
                        }),
                    Sanctions = sanctions.OrderBy(row => row.Id).Select(row => new { row.Id, row.Revision }),
                    Protection = protection.OrderBy(row => row.Id).Select(row => new { row.Id, row.Revision }),
                    Continuity = contribution?.Generation,
                    CurrentUntil = currentUntil
                })));
                result[index] = subject is null
                    ? EventResourceModerationFacts.Unavailable(generation)
                    : EventResourceModerationFacts.Qualified(subject,
                        outcome, generation, binding?.Id, binding?.Revision, currentUntil);
            }
            return result;
        }
        finally
        {
            context.ModerationTrustDomainId = priorTrustDomain;
        }
    }

    private static ModerationCapability? Capability(EventResourceAuthorityRequest request) =>
        request.IsUploadMutation ? ModerationCapability.Upload
        : request.Action is "view" or "view-management" or "view-audit" or "access" or "download" or "export"
            ? ModerationCapability.ReadResource : null;

    private async Task<bool> ProviderTenantMatchesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider) return true;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string setting && Guid.TryParse(setting, out var providerTenant)
            && providerTenant == tenantId;
    }
}
