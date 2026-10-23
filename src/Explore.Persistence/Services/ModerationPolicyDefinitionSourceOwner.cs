using System.Data;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>
/// Fenced immutable publication and candidate definitions on the caller's original native transaction.
/// Owns no publication writes, evidence qualification, deployment observations or lifecycle permission.
/// </summary>
public sealed class ModerationPolicyDefinitionSourceOwner(
    ExploreDbContext context, ModerationCapabilityRegistry inventory, TimeProvider clock)
{
    public ModerationPolicyDefinitionSourceOwner(
        ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine,
        ModerationCapabilityRegistry inventory, TimeProvider clock) : this(context, inventory, clock)
    {
        ArgumentNullException.ThrowIfNull(admin);
        ArgumentNullException.ThrowIfNull(machine);
    }

    public Task<ModerationPolicyPublication?> AcquireSelectedAsync(
        Guid transactionId, Guid hostBindingId, AuthorityScope jurisdiction, Guid policyId,
        bool activeOnly, CancellationToken cancellationToken = default) =>
        AcquireSelectedInTransactionAsync(context, transactionId, hostBindingId, jurisdiction, policyId, activeOnly, cancellationToken);

    internal static async Task<ModerationPolicyPublication?> AcquireSelectedInTransactionAsync(
        ExploreDbContext context, Guid transactionId, Guid hostBindingId, AuthorityScope jurisdiction, Guid policyId,
        bool activeOnly, CancellationToken cancellationToken)
    {
        if (!CurrentTransaction(context, transactionId) || context.TenantFilterTenantId is not { } tenant
            || context.ModerationTrustDomainId != jurisdiction.TrustDomainId
            || jurisdiction.Kind is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant)
            || jurisdiction.Kind == ModerationScopeKind.Tenant && jurisdiction.Id != tenant)
            return null;
        if (!await ModerationSourceReadSession.ProviderTenantMatchesAsync(context, tenant, cancellationToken)) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, hostBindingId, cancellationToken);
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var instance = jurisdiction.Kind == ModerationScopeKind.Instance ? jurisdiction : jurisdiction.Parent!;
        if (host is null || host.Id != hostBindingId || host.TrustDomainId != jurisdiction.TrustDomainId
            || host.InstanceId != instance.Id || host.SolutionId != instance.Parent!.Id)
            return null;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.publication:{hostBindingId:N}:{jurisdiction.Id:N}", cancellationToken);
        var selection = await context.ModerationPolicySelections.AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == policyId && row.HostBindingId == hostBindingId && row.JurisdictionId == jurisdiction.Id, cancellationToken);
        var selectedId = activeOnly ? selection?.ActivePublicationId : selection?.LatestPublicationId;
        if (selectedId is null) return null;
        var publication = await ModerationPolicyPublicationGraph.Include(context.ModerationPolicyPublications.AsNoTracking())
            .SingleOrDefaultAsync(row => row.Id == selectedId, cancellationToken);
        return CurrentTransaction(context, transactionId) && publication is not null
            && publication.AdoptedRevision == publication.Revision
            && publication.HostBindingId == hostBindingId && publication.PolicyId == policyId
            && publication.Jurisdiction == jurisdiction && (!activeOnly || publication.PublishedActive)
            && await ModerationSourceReadSession.ProviderTenantMatchesAsync(context, tenant, cancellationToken) ? publication : null;
    }

    public async Task<ModerationPolicyPublicationCandidate?> AcquireCandidateAsync(
        Guid transactionId, Guid hostBindingId, AuthorityScope jurisdiction, Guid policyId,
        Guid publicationId, DateTimeOffset observedAt, CancellationToken cancellationToken = default)
    {
        if (!CurrentTransaction(transactionId) || context.TenantFilterTenantId is not { } tenant
            || context.ModerationTrustDomainId != jurisdiction.TrustDomainId
            || jurisdiction.Kind is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant)
            || jurisdiction.Kind == ModerationScopeKind.Tenant && jurisdiction.Id != tenant
            || observedAt.Offset != TimeSpan.Zero || observedAt == DateTimeOffset.MinValue
            || observedAt == DateTimeOffset.MaxValue)
            return null;
        await using var slot = await AcquireSlotAsync(hostBindingId, jurisdiction, cancellationToken);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationPolicyPublicationCandidate>(
            context, publicationId, cancellationToken);
        var candidate = await CandidateGraph().SingleOrDefaultAsync(row => row.Id == publicationId, cancellationToken);
        var published = candidate is not null && await context.ModerationPolicySelections.AsNoTracking()
            .AnyAsync(row => row.Id == policyId
                && row.ActivePublicationId == publicationId && row.Revision >= candidate.ExpectedRevision + 1,
                cancellationToken);
        if (published && !await context.ModerationPolicyPublications.AsNoTracking().AnyAsync(row =>
            row.Id == publicationId && row.AdoptedRevision == row.Revision && row.PublishedActive
                && row.DeclaredStateDigest == candidate!.ProposedStateDigest, cancellationToken))
            return null;
        if (candidate is null || candidate.HostBindingId != hostBindingId || candidate.PolicyId != policyId
            || candidate.Jurisdiction != jurisdiction || candidate.TenantId != tenant
            || !await CandidateCurrentAsync(candidate, observedAt, cancellationToken, published)
            || !CurrentTransaction(transactionId) || clock.GetUtcNow() < observedAt
            || clock.GetUtcNow() >= candidate.ValidUntil)
            return null;
        return candidate;
    }

    internal IQueryable<ModerationPolicyPublicationCandidate> CandidateGraph() =>
        context.ModerationPolicyPublicationCandidates.AsNoTracking().AsSplitQuery()
            .Include(row => row.Definition!).ThenInclude(row => row.Classes)
            .Include(row => row.Definition!).ThenInclude(row => row.Reasons)
            .Include(row => row.Definition!).ThenInclude(row => row.Modes)
            .Include(row => row.Definition!).ThenInclude(row => row.Evidence)
            .Include(row => row.Definition!).ThenInclude(row => row.Profiles).ThenInclude(row => row.Scopes)
            .Include(row => row.Definition!).ThenInclude(row => row.Profiles).ThenInclude(row => row.Subjects)
            .Include(row => row.Definition!).ThenInclude(row => row.Profiles).ThenInclude(row => row.Effects)
            .Include(row => row.Definition!).ThenInclude(row => row.Profiles).ThenInclude(row => row.Plans)
            .Include(row => row.Definition!).ThenInclude(row => row.Profiles).ThenInclude(row => row.Consumers)
            .Include(row => row.Definition!).ThenInclude(row => row.Profiles).ThenInclude(row => row.Prerequisites);

    internal async Task<bool> CandidateCurrentAsync(
        ModerationPolicyPublicationCandidate candidate, DateTimeOffset now, CancellationToken token,
        bool published = false)
    {
        candidate.ValidatePersistedState();
        if (candidate.PreparedAt > now || candidate.ValidUntil <= now
            || ModerationPolicyPublicationProjection.RestoreCandidate(candidate, inventory) is null)
            return false;
        var selection = await context.ModerationPolicySelections.AsNoTracking().SingleOrDefaultAsync(
            row => row.HostBindingId == candidate.HostBindingId && row.JurisdictionId == candidate.Jurisdiction.Id, token);
        if (published
            ? selection is null || selection.Revision < candidate.ExpectedRevision + 1
                || selection.ActivePublicationId != candidate.Id || selection.Id != candidate.PolicyId
            : (selection?.Revision ?? 0) != candidate.ExpectedRevision
            || selection?.LatestPublicationId != candidate.PreviousPublicationId
            || selection is not null && selection.Id != candidate.PolicyId)
            return false;
        var epoch = await context.ModerationQualificationEpochs.AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == candidate.AuthoritySourceId, token);
        if (epoch is null || epoch.HostBindingId != candidate.HostBindingId || epoch.TenantId != candidate.TenantId
            || epoch.Revision < candidate.WriterGeneration)
            return false;
        return await context.PlatformUserRoles.AsNoTracking().AnyAsync(row =>
            row.UserId == candidate.PreparedByHumanId && !row.User.IsDeleted
            && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin", token)
            || candidate.Jurisdiction.Kind == ModerationScopeKind.Tenant
            && await context.TenantUserRoleGrants.AsNoTracking().AnyAsync(row =>
                row.TenantId == candidate.TenantId && row.TenantUser.UserId == candidate.PreparedByHumanId
                && row.RevokedAt == null && row.RoleId == (int)RoleEnum.TenantAdmin
                && row.RoleScopeId == (int)RoleScopeEnum.Tenant && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant
                && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted, token);
    }

    /// <summary>Reads the immutable candidate without an active pointer, or the exact selected publication after adoption.</summary>
    internal async Task<Definition?> ReadDefinitionAsync(
        IModerationSourceProvenance authority, Guid transactionId, AuthorityScope jurisdiction,
        Guid policyId, Guid publicationId, DateTimeOffset observedAt, CancellationToken token)
    {
        if (jurisdiction != authority.Tenant && jurisdiction != authority.Instance) return null;
        var selected = await AcquireSelectedAsync(transactionId, authority.Binding.Id,
            jurisdiction, policyId, false, token);
        if (selected?.Id != publicationId)
            selected = await AcquireSelectedAsync(transactionId, authority.Binding.Id,
                jurisdiction, policyId, true, token);
        if (selected is not null && selected.Id == publicationId)
        {
            var policy = ModerationPolicyPublicationProjection.RestoreHistory(selected);
            if (policy is null || selected.HostBindingId != authority.Binding.Id || selected.Jurisdiction != jurisdiction
                || selected.PublishedAt > observedAt || selected.InventoryFingerprint != inventory.Fingerprint) return null;
            var prepared = await context.ModerationPolicyPublicationCandidates.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == publicationId, token);
            if (prepared is null)
                return new(policy, selected.Id, DateTimeOffset.MaxValue);
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationPolicyPublicationCandidate>(context, prepared.Id, token);
            prepared = await context.ModerationPolicyPublicationCandidates.AsNoTracking()
                .SingleAsync(row => row.Id == publicationId, token);
            prepared.ValidatePersistedState();
            if (prepared.HostBindingId != authority.Binding.Id || prepared.TrustDomainId != authority.Binding.TrustDomainId
                || prepared.TenantId != authority.Tenant.Id || prepared.Jurisdiction != jurisdiction
                || prepared.PolicyId != policyId || prepared.ExpectedRevision + 1 != selected.Revision
                || prepared.ProposedStateDigest != ModerationPolicyPublicationProjection.ProposedDigest(policy.Proposed)
                || prepared.PreparedAt > observedAt
                || !Administrator(authority, prepared.PreparedByHumanId)) return null;
            // A published selection establishes this definition's currentness independently of
            // its former preparation lease. Existing deployment/adoption leases never extend.
            return new(policy, prepared.DefinitionId, DateTimeOffset.MaxValue);
        }
        var candidate = await AcquireCandidateAsync(transactionId, authority.Binding.Id,
            jurisdiction, policyId, publicationId, observedAt, token);
        if (candidate is null || !Administrator(authority, candidate.PreparedByHumanId)) return null;
        var draft = ModerationPolicyPublicationProjection.RestoreCandidate(candidate, inventory);
        return draft is null ? null : new(draft, candidate.DefinitionId, candidate.ValidUntil);
    }

    /// <summary>
    /// Only a borrowed original-effect acquisition can qualify a retained definition.
    /// Ordinary source writes, publication candidates and activation retain the selected path.
    /// </summary>
    internal async Task<Definition?> ReadDefinitionAsync(
        ModerationNativeEvidenceAcquisition native, DateTimeOffset observedAt, CancellationToken token)
        => await ReadDefinitionAsync(new ModerationSourceReadAcquisition(native.Context, native.Transaction,
            native.Authority, native.Policy, false, native.OriginalRecipe), observedAt, token);

    internal async Task<Definition?> ReadDefinitionAsync(
        ModerationSourceReadAcquisition native, DateTimeOffset observedAt, CancellationToken token)
    {
        if (!ReferenceEquals(native.Context, context) || !CurrentTransaction(native.Transaction.TransactionId))
            return null;
        var requirement = native.Policy.Publication;
        if (native.IsPublicCalendar)
        {
            if (native.OriginalRecipe is not null || native.Authority is ModerationSourceAuthority) return null;
            var selected = await AcquireSelectedAsync(native.Transaction.TransactionId, native.Authority.Binding.Id,
                requirement.Jurisdiction, requirement.PolicyId, true, token);
            if (selected is null || selected.Id != requirement.PublicationId || selected.PublishedAt > observedAt
                || selected.Revision != requirement.ProposedRevision || selected.CatalogueRevision != 2
                || selected.InventoryFingerprint != inventory.Fingerprint) return null;
            var current = await ReadDefinitionAsync(native.Authority, native.Transaction.TransactionId,
                requirement.Jurisdiction, requirement.PolicyId, requirement.PublicationId, observedAt, token);
            return current is not null && current.Policy.Publication.PublicationId == selected.Id
                && ModerationPolicyPublicationProjection.ProposedDigest(current.Policy.Proposed) == selected.DeclaredStateDigest
                ? current : null;
        }
        if (native.OriginalRecipe is not { } original)
            return await ReadDefinitionAsync(native.Authority, native.Transaction.TransactionId,
                requirement.Jurisdiction, requirement.PolicyId, requirement.PublicationId, observedAt, token);
        if (original.Policy.Publication != requirement || observedAt > clock.GetUtcNow()
            || !await original.CurrentAsync(token)) return null;
        // This is an immutable definition, not renewed source evidence. The session enforces
        // original R; each source, signature and adoption still enforces its own finite expiry.
        return new(original.Policy, original.DefinitionId, DateTimeOffset.MaxValue);
    }

    internal IQueryable<ModerationPolicyPublication> Graph() =>
        ModerationPolicyPublicationGraph.Include(context.ModerationPolicyPublications.AsNoTracking());

    internal Task<IAsyncDisposable> AcquireSlotAsync(Guid host, AuthorityScope scope, CancellationToken token) =>
        RelationalNamedLock.AcquireTransactionAsync(context, $"moderation.publication:{host:N}:{scope.Id:N}", token);

    internal bool CurrentTransaction(Guid id) => CurrentTransaction(context, id);

    private static bool CurrentTransaction(ExploreDbContext context, Guid id) =>
        context.Database.CurrentTransaction is { } transaction && transaction.TransactionId == id
        && transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable
        && transaction.GetDbTransaction().Connection == context.Database.GetDbConnection()
        && !context.IsTenantFilterBypassed;

    private static bool Administrator(IModerationSourceProvenance authority, Guid humanId) =>
        authority.FencedHumans.Contains(humanId) && authority.Administrators.Contains(humanId);

    internal sealed record Definition(ModerationPolicyCompiledDraft Policy, Guid DefinitionId, DateTimeOffset ValidUntil);
}
