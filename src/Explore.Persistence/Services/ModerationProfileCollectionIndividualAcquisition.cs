using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>
/// Original existing-Human ProfileCompletion policy metadata, never individual consent,
/// applicability, guardian qualification or permission to accept a profile body.
/// Borrows the real colocated identity owner's transaction; a future writer must retain
/// this acquisition through that owner's outer beforePrimaryCommit, not a nested unit of work.
/// </summary>
internal sealed class ModerationProfileCollectionIndividualAcquisition : IDisposable
{
    private readonly ExploreDbContext context;
    private readonly IAdminContext admin;
    private readonly IMachinePrincipalAccessor machine;
    private readonly IHttpContextAccessor http;
    private readonly IPrivacyIdentityFenceAuthority privacy;
    private readonly TimeProvider clock;
    private readonly IDbContextTransaction transaction;
    private readonly DbConnection connection;
    private readonly ITenantContext tenantContext;
    private readonly ClaimsPrincipal principal;
    private readonly ProviderAccountKey? account;
    private readonly Guid? loginId;
    private readonly ModerationSourceAuthority authority;
    private readonly long epochRevision;
    private readonly ModerationPolicyDefinitionSourceOwner definitions;
    private readonly ModerationProfileCollectionProcedureSourceOwner procedures;
    private readonly ModerationConsumerEvidenceSession session;
    private readonly ModerationPolicyCompiledDraft policy;
    private readonly string policyDigest;
    private readonly string selectionDigest;
    private readonly Guid definitionId;
    private readonly Guid profileId;
    private readonly ModerationNativeMode mode;
    private readonly Guid? originalTrust;
    private ModerationProfileCollectionProcedureObservation? observation;
    private bool disposed;

    private ModerationProfileCollectionIndividualAcquisition(
        ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine,
        IHttpContextAccessor http, IPrivacyIdentityFenceAuthority privacy, TimeProvider clock,
        IDbContextTransaction transaction, ITenantContext tenantContext, ClaimsPrincipal principal,
        ProviderAccountKey? account, Guid? loginId, ModerationSourceAuthority authority,
        ModerationPolicyDefinitionSourceOwner definitions, ModerationPolicyDefinitionSourceOwner.Definition definition,
        ModerationPolicySelection selection, ModerationCapabilityRegistry inventory,
        Guid profileId, ModerationNativeMode mode, DateTimeOffset observedAt, Guid? originalTrust)
    {
        this.context = context;
        this.admin = admin;
        this.machine = machine;
        this.http = http;
        this.privacy = privacy;
        this.clock = clock;
        this.transaction = transaction;
        connection = context.Database.GetDbConnection();
        this.tenantContext = tenantContext;
        this.principal = principal;
        this.account = account;
        this.loginId = loginId;
        this.authority = authority;
        epochRevision = authority.Epoch.Revision;
        this.definitions = definitions;
        definitionId = definition.DefinitionId;
        policy = definition.Policy;
        policyDigest = ModerationPolicyPublicationProjection.Digest(policy);
        selectionDigest = ModerationPolicyPublicationProjection.Digest(selection);
        this.profileId = profileId;
        this.mode = mode;
        ObservedAt = observedAt;
        this.originalTrust = originalTrust;
        procedures = new(context, definitions, clock);
        // The private closure authenticates this acquisition. No caller supplies a currentness verdict.
        session = new(context, transaction, authority, policy, CurrentAuthorityAsync,
            new ModerationPolicyPublicationRepository(context, admin, machine, inventory, null, clock),
            inventory, null, null, clock, null);
    }

    public Guid SubjectId => authority.UserId;
    internal Guid HostBindingId => authority.Binding.Id;
    internal Guid TenantId => authority.Tenant.Id;
    public Guid TransactionId => transaction.TransactionId;
    public AuthorityScope Jurisdiction => policy.Publication.Jurisdiction;
    public Guid PolicyId => policy.Publication.PolicyId;
    public long PolicyRevision => policy.Publication.ProposedRevision;
    public Guid PublicationId => policy.Publication.PublicationId;
    public Guid ProfileId => profileId;
    public ModerationNativeMode Mode => mode;
    public Guid SourceId => observation!.Source.Id;
    public long SourceRevision => observation!.Source.Revision;
    public long SourceWriterGeneration => observation!.Source.WriterGeneration;
    public Guid AdoptionId => observation!.Adoption.Id;
    public long AdoptionRevision => observation!.Adoption.Revision;
    public long AdoptionWriterGeneration => observation!.Adoption.WriterGeneration;
    public ModerationProfileCollectionProcedure Procedure => observation!.Source.Procedure;
    public DateTimeOffset ObservedAt { get; }
    public DateTimeOffset ValidUntil => observation!.ValidUntil;

    internal bool OwnsContext(ExploreDbContext candidate) => ReferenceEquals(context, candidate) && FrameCurrent();

    /// <summary>
    /// Captures actual self from this original acquisition only. No bound identity, parent,
    /// stamp, observation time or lifetime can be supplied by the nomination caller.
    /// </summary>
    internal async Task<ModerationProfileCollectionSubjectObservation?> ObserveSubjectAsync(
        long sequence, CancellationToken token)
    {
        if (!await RevalidateAsync(token)) return null;
        var parents = await procedures.AcquireParentsAsync(session, TransactionId, profileId, mode, ObservedAt, token);
        if (parents is null || !await RevalidateAsync(token)) return null;
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(authority.Binding.TrustDomainId), authority.Binding.SolutionId),
            authority.Binding.InstanceId);
        return ModerationProfileCollectionSubjectObservation.Record(
            Guid.CreateVersion7(), SubjectId, loginId, instance, Jurisdiction, authority.Epoch.Id, epochRevision,
            observation!.Source, observation.Adoption, parents.Source, parents.Adoption,
            sequence, Guid.CreateVersion7(), ObservedAt, ValidUntil,
            ModerationPolicyPublicationProjection.Digest(observation.Source), policyDigest, selectionDigest);
    }

    /// <summary>
    /// Matches retained originals to this real acquisition's current policy and parent graph.
    /// A later actor is not substituted for the original subject; independent actor authority
    /// remains the assessment owner's separate obligation.
    /// </summary>
    internal async Task<bool> MatchesSubjectObservationAsync(
        ModerationProfileCollectionSubjectObservation original, CancellationToken token)
    {
        original.ValidatePersistedState();
        if (!await RevalidateAsync(token) || !original.Covers(clock.GetUtcNow())
            || original.HostBindingId != authority.Binding.Id || original.TrustDomainId != authority.Binding.TrustDomainId
            || original.SolutionId != authority.Binding.SolutionId || original.InstanceId != authority.Binding.InstanceId
            || original.TenantId != authority.Tenant.Id || original.EpochId != authority.Epoch.Id
            || original.EpochRevision != epochRevision || original.ToBinding().Jurisdiction != Jurisdiction
            || original.PolicyId != PolicyId || original.PolicyRevision != PolicyRevision
            || original.PublicationId != PublicationId || original.DefinitionId != definitionId
            || original.ProfileId != profileId || original.ModeCode != (int)mode
            || original.OriginalPolicyDigest != policyDigest || original.OriginalSelectionDigest != selectionDigest
            || original.OriginalProcedureDigest != ModerationPolicyPublicationProjection.Digest(observation!.Source)
            || original.OriginalProcedureDigest != ModerationPolicyPublicationProjection.Digest(original.ProcedureSource)
            || !original.ProcedureSource.Matches(observation.Source)
            || original.ProcedureSourceId != SourceId || original.ProcedureSourceRevision != SourceRevision
            || original.ProcedureSourceWriterGeneration != SourceWriterGeneration
            || original.ProcedureAdoptionId != AdoptionId || original.ProcedureAdoptionRevision != AdoptionRevision
            || original.ProcedureAdoptionWriterGeneration != AdoptionWriterGeneration
            || original.ProcedureAdoptedByHumanId != observation.Adoption.AdoptedByHumanId
            || original.ProcedureAdoptedAt != observation.Adoption.AdoptedAt
            || original.ProcedureAdoptionValidUntil != observation.Adoption.ValidUntil)
            return false;
        // Ask the actual source owner at the original time as well as the current time.
        // Reacquiring this actor at a later time cannot extend the subject's original window.
        var parents = await procedures.AcquireParentsAsync(
            session, TransactionId, profileId, mode, original.ObservedAt, token);
        return parents is not null
            && observation.Adoption.MatchesCurrent(observation.Source, parents.Source, parents.Adoption, original.ObservedAt)
            && observation.Adoption.MatchesCurrent(observation.Source, parents.Source, parents.Adoption, clock.GetUtcNow())
            && original.Covers(clock.GetUtcNow()) && await RevalidateAsync(token);
    }

    /// <summary>
    /// Returns live original parents only on this actual actor's retained acquisition.
    /// The supplied observation remains the original subject's; no actor binding is substituted.
    /// </summary>
    internal async Task<AssessmentParents?> ReadAssessmentParentsAsync(
        ModerationProfileCollectionSubjectObservation original, CancellationToken token)
    {
        if (!await MatchesSubjectObservationAsync(original, token)) return null;
        var parents = await procedures.AcquireParentsAsync(
            session, TransactionId, profileId, mode, original.ObservedAt, token);
        return parents is not null && await MatchesSubjectObservationAsync(original, token)
            ? new(observation!.Source, observation.Adoption, parents.Source, parents.Adoption) : null;
    }

    internal sealed record AssessmentParents(
        ModerationProfileCollectionProcedureSource ProcedureSource,
        ModerationProfileCollectionProcedureAdoption ProcedureAdoption,
        ModerationChildAgePolicySource AgeSource, ModerationChildAgePolicyAdoption AgeAdoption);

    /// <summary>
    /// Profile and native mode are nominations only. Host, tenant, Human, selected ACTIVE
    /// publication, latest procedure and accepted parents are discovered from native state.
    /// No transaction is created, committed or adopted here.
    /// </summary>
    internal static async Task<ModerationProfileCollectionIndividualAcquisition?> AcquireAsync(
        ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine,
        IHttpContextAccessor http, IPrivacyIdentityFenceAuthority privacy,
        ModerationCapabilityRegistry inventory, TimeProvider clock, Guid profileId,
        ModerationNativeMode mode, CancellationToken token)
    {
        if (!context.Database.IsRelational() || !context.IdentityFenceOwnsTransaction
            || context.IdentityFenceTransactionFailed || context.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || transaction.GetDbTransaction().Connection != context.Database.GetDbConnection()
            || context.Database.GetDbConnection().State != ConnectionState.Open
            || context.IsTenantFilterBypassed || context.TenantContext is not { } tenantContext
            || context.TenantFilterTenantId is not { } tenant || tenant == Guid.Empty
            || machine.IsMachineCaller || !Enum.IsDefined(mode) || profileId == Guid.Empty
            || http.HttpContext?.User is not { } principal || principal.Identity?.IsAuthenticated != true)
            return null;
        var account = principal.GetProviderIdentity()?.AccountKey;
        var human = await admin.ResolveUserIdAsync(token);
        if (human is null || human == Guid.Empty || await privacy.IsSubjectFencedAsync(human.Value, token))
            return null;
        if (account is null && principal.GetPlatformUserId() != human) return null;
        var loginId = account is null ? null : await context.UserExternalLogins.AsNoTracking()
            .Where(row => row.UserId == human && row.AuthenticationProviderId == (int)account.ProviderKind
                && row.ProviderKey == account.Value).Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (account is not null && loginId is null) return null;
        var originalTrust = context.ModerationTrustDomainId;
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (host is null || originalTrust is { } trust && trust != host.TrustDomainId) return null;
        var observedAt = clock.GetUtcNow();
        if (observedAt.Offset != TimeSpan.Zero || observedAt == DateTimeOffset.MinValue
            || observedAt == DateTimeOffset.MaxValue) return null;
        ModerationProfileCollectionIndividualAcquisition? acquired = null;
        var handedOff = false;
        try
        {
            context.ModerationTrustDomainId = host.TrustDomainId;
            // Ambiguous instance/tenant selections do not invent precedence or substantive law.
            var selections = await context.ModerationPolicySelections.AsNoTracking()
                .Where(row => row.HostBindingId == host.Id && row.TrustDomainId == host.TrustDomainId
                    && (row.JurisdictionId == tenant || row.JurisdictionId == host.InstanceId)
                    && row.ActivePublicationId != null
                    && context.ModerationPolicyPublications.Any(publication =>
                        publication.Id == row.ActivePublicationId && publication.PublishedActive
                        && publication.Profiles.Any(profile => profile.ProfileId == profileId)
                        && publication.Modes.Any(native => native.Mode == (int)mode)))
                .Take(2).ToListAsync(token);
            if (selections.Count != 1) return null;
            var selection = selections[0];
            var instance = AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
            var jurisdiction = selection.JurisdictionId == tenant ? AuthorityScope.Tenant(instance, tenant) : instance;
            var qualification = new ModerationQualificationAuthority(context, admin, machine);
            var authority = await qualification.AcquireProfileCollectionIndividualAsync(
                jurisdiction, selection.ActivePublicationId!.Value, token);
            if (authority is null || authority.UserId != human || authority.Binding.Id != host.Id
                || authority.Binding.TrustDomainId != host.TrustDomainId || authority.Binding.SolutionId != host.SolutionId
                || authority.Binding.InstanceId != host.InstanceId || authority.Tenant.Id != tenant
                || authority.Epoch.Revision is < 1 or long.MaxValue)
                return null;
            var definitions = new ModerationPolicyDefinitionSourceOwner(context, admin, machine, inventory, clock);
            var selected = await definitions.AcquireSelectedAsync(
                transaction.TransactionId, host.Id, jurisdiction, selection.Id, true, token);
            if (selected is null || selected.Id != selection.ActivePublicationId || !selected.PublishedActive)
                return null;
            var definition = await definitions.ReadDefinitionAsync(authority, transaction.TransactionId,
                jurisdiction, selection.Id, selected.Id, observedAt, token);
            if (definition is null || definition.Policy.Proposed.RequestedState != ModerationPolicyRequestedState.Active
                || !definition.Policy.Proposed.Modes.Contains(mode)
                || !definition.Policy.Profiles.Any(profile => profile.Definition.Id == profileId
                    && profile.Definition.SubjectKinds.Contains(ModerationSubjectKind.Human))
                || ModerationPolicyPublicationProjection.Restore(selected, inventory) is null)
                return null;
            acquired = new(context, admin, machine, http, privacy, clock, transaction, tenantContext,
                principal, account, loginId, authority, definitions, definition, selection, inventory,
                profileId, mode, observedAt, originalTrust);
            acquired.observation = await acquired.procedures.AcquireCurrentAsync(
                acquired.session, transaction.TransactionId, profileId, mode, observedAt, token);
            if (acquired.observation is null
                || acquired.observation.Source.Procedure.Purpose != ModerationIndividualPurpose.ProfileCompletion
                || acquired.observation.ValidUntil == DateTimeOffset.MaxValue
                || !await acquired.RevalidateAsync(token)) return null;
            handedOff = true;
            return acquired;
        }
        finally
        {
            if (!handedOff)
            {
                acquired?.Dispose();
                if (context.ModerationTrustDomainId == host.TrustDomainId)
                    context.ModerationTrustDomainId = originalTrust;
            }
        }
    }

    /// <summary>Rereads original finite observations; time passing never renews this acquisition.</summary>
    public async Task<bool> RevalidateAsync(CancellationToken token)
    {
        if (observation is null || !await CurrentAuthorityAsync(token)) return false;
        var current = await procedures.AcquireCurrentAsync(
            session, TransactionId, profileId, mode, ObservedAt, token);
        return current is not null && current.TransactionId == observation.TransactionId
            && current.ObservedAt == ObservedAt && current.ValidUntil == ValidUntil
            && current.Source.Matches(observation.Source)
            && current.Adoption.Id == observation.Adoption.Id
            && current.Adoption.Revision == observation.Adoption.Revision
            && current.Adoption.WriterGeneration == observation.Adoption.WriterGeneration
            && current.Adoption.AdoptedByHumanId == observation.Adoption.AdoptedByHumanId
            && current.Adoption.AdoptedAt == observation.Adoption.AdoptedAt
            && await CurrentAuthorityAsync(token);
    }

    private async Task<bool> CurrentAuthorityAsync(CancellationToken token)
    {
        if (!FrameCurrent() || machine.IsMachineCaller
            || await admin.ResolveUserIdAsync(token) != SubjectId
            || !Equals(principal.GetProviderIdentity()?.AccountKey, account)
            || account is null && principal.GetPlatformUserId() != SubjectId
            || await privacy.IsSubjectFencedAsync(SubjectId, token)
            || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == SubjectId && !row.IsDeleted, token))
            return false;
        if (account is not null && !await context.UserExternalLogins.AsNoTracking().AnyAsync(row =>
            row.Id == loginId && row.UserId == SubjectId && row.AuthenticationProviderId == (int)account.ProviderKind
                && row.ProviderKey == account.Value, token)) return false;
        if (!await new ModerationQualificationAuthority(context, admin, machine)
                .ProviderTenantMatchesAsync(authority.Tenant.Id, token)
            || !await context.Tenants.AsNoTracking().AnyAsync(
                row => row.Id == authority.Tenant.Id && row.TenantStatus.IsActiveState, token)
            || !await context.ModerationHostBindings.AsNoTracking().AnyAsync(row =>
                row.Id == authority.Binding.Id && row.TrustDomainId == authority.Binding.TrustDomainId
                && row.SolutionId == authority.Binding.SolutionId && row.InstanceId == authority.Binding.InstanceId
                && row.MaintenanceServicePrincipalId == authority.Binding.MaintenanceServicePrincipalId, token)
            || !await context.ServicePrincipals.AsNoTracking().AnyAsync(
                row => row.Id == authority.Binding.MaintenanceServicePrincipalId && !row.IsDeleted, token)
            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row =>
                row.Id == authority.Epoch.Id && row.HostBindingId == authority.Binding.Id
                && row.TenantId == authority.Tenant.Id && row.Revision == epochRevision, token))
            return false;
        var parentOwner = new ModerationChildAgePolicySourceOwner(context, definitions, clock);
        foreach (var administrator in authority.Administrators)
            if (!await parentOwner.AdministratorAsync(authority, administrator, policy.Publication.Jurisdiction, token))
                return false;
        var selection = await context.ModerationPolicySelections.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == policy.Publication.PolicyId, token);
        if (selection is null || ModerationPolicyPublicationProjection.Digest(selection) != selectionDigest)
            return false;
        var selected = await definitions.AcquireSelectedAsync(TransactionId, authority.Binding.Id,
            policy.Publication.Jurisdiction, policy.Publication.PolicyId, true, token);
        if (selected is null || selected.Id != policy.Publication.PublicationId || !selected.PublishedActive)
            return false;
        var definition = await definitions.ReadDefinitionAsync(authority, TransactionId, policy.Publication.Jurisdiction,
            policy.Publication.PolicyId, policy.Publication.PublicationId, ObservedAt, token);
        return definition is not null && definition.DefinitionId == definitionId
            && ModerationPolicyPublicationProjection.Digest(definition.Policy) == policyDigest
            && FrameCurrent() && !machine.IsMachineCaller
            && await admin.ResolveUserIdAsync(token) == SubjectId
            && Equals(principal.GetProviderIdentity()?.AccountKey, account)
            && (account is not null || principal.GetPlatformUserId() == SubjectId)
            && FrameCurrent();
    }

    private bool FrameCurrent() =>
        !disposed && context.IdentityFenceOwnsTransaction && !context.IdentityFenceTransactionFailed
        && ReferenceEquals(context.Database.CurrentTransaction, transaction)
        && ReferenceEquals(context.Database.GetDbConnection(), connection)
        && ReferenceEquals(transaction.GetDbTransaction().Connection, connection)
        && connection.State == ConnectionState.Open
        && transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable
        && ReferenceEquals(context.TenantContext, tenantContext) && !context.IsTenantFilterBypassed
        && context.TenantFilterTenantId == authority.Tenant.Id
        && context.ModerationTrustDomainId == authority.Binding.TrustDomainId
        && ReferenceEquals(http.HttpContext?.User, principal) && principal.Identity?.IsAuthenticated == true
        && clock.GetUtcNow() >= ObservedAt && (observation is null || clock.GetUtcNow() < observation.ValidUntil);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        session.Dispose();
        if (context.ModerationTrustDomainId == authority.Binding.TrustDomainId)
            context.ModerationTrustDomainId = originalTrust;
    }
}
