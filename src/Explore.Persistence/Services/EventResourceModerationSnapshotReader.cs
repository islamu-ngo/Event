using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>
/// Reads the current resource-read overlay in the resource owner's transaction.
/// Installation is provenance only. Adoption retains read protection independently
/// of support for activating new effects, so retiring support cannot erase sanctions.
/// </summary>
public sealed class EventResourceModerationSnapshotReader(
    ExploreDbContext context, IModerationMachineSubjectBindingRepository bindings)
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
                || !request.HasConsistentPrincipal))
            return Enumerable.Repeat(EventResourceModerationFacts.Unavailable("invalid-request"), requests.Count).ToArray();

        var readTargets = requests.Where(IsRead).Select(request => request.ResourceId)
            .Where(id => id != Guid.Empty).Distinct().ToArray();
        if (readTargets.Length > EventResourceAuthorityRequest.MaximumBatchResources)
            throw new ArgumentOutOfRangeException(nameof(requests));
        if (readTargets.Length == 0) return result;
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (host is null) return result;
        if (!await ProviderTenantMatchesAsync(first.TenantId, cancellationToken))
        {
            for (int index = 0; index < requests.Count; index++)
                if (IsRead(requests[index]))
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
            var policies = await context.ModerationPolicies.AsNoTracking().Include(policy => policy.Rules)
                .Where(policy => (policy.JurisdictionSolutionId == null || policy.JurisdictionSolutionId == host.SolutionId)
                    && (policy.JurisdictionInstanceId == null || policy.JurisdictionInstanceId == host.InstanceId)
                    && (policy.JurisdictionTenantId == null || policy.JurisdictionTenantId == first.TenantId)
                    && policy.JurisdictionCommunityId == null
                    && (policy.JurisdictionResourceId == null || readTargets.Contains(policy.JurisdictionResourceId.Value)))
                .ToListAsync(cancellationToken);
            foreach (var policy in policies) policy.ValidatePersistedState();
            var adopted = policies.Where(policy => policy.Rules.Any(rule =>
                rule.Capabilities.Contains(ModerationCapability.ReadResource))).ToArray();
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
            for (int index = 0; index < requests.Count; index++)
            {
                var request = requests[index];
                if (!IsRead(request) || request.ResourceId == Guid.Empty) continue;
                var target = AuthorityScope.Resource(tenant, request.ResourceId);
                var applicable = adopted.Where(policy => policy.Jurisdiction.Contains(target)).ToArray();
                if (applicable.Length == 0) continue;
                var generation = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Version = 1, Host = host.Id, Target = target, Subject = subject,
                    Epoch = epoch?.Id, EpochRevision = epoch?.Revision,
                    Binding = binding?.Id, BindingRevision = binding?.Revision,
                    Policies = applicable.OrderBy(policy => policy.Id)
                        .Select(policy => new { policy.Id, policy.Revision, policy.PublicationId }),
                    Sanctions = sanctions.OrderBy(row => row.Id).Select(row => new { row.Id, row.Revision }),
                    Protection = protection.OrderBy(row => row.Id).Select(row => new { row.Id, row.Revision })
                })));
                result[index] = subject is null
                    ? EventResourceModerationFacts.Unavailable(generation)
                    : EventResourceModerationFacts.Qualified(subject,
                        EffectiveRestrictions.Evaluate(subject, target, ModerationCapability.ReadResource,
                            evaluationUtc, sanctions, protection, ModerationAuthorityState.Known).Outcome,
                        generation, binding?.Id, binding?.Revision);
            }
            return result;
        }
        finally
        {
            context.ModerationTrustDomainId = priorTrustDomain;
        }
    }

    private static bool IsRead(EventResourceAuthorityRequest request) =>
        request.Action is "view" or "view-management" or "view-audit" or "access" or "download" or "export";

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
