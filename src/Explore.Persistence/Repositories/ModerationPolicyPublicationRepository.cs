using System.Data;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Sole rich native publication writer. Human/tenant/host acquisition precedes replay; one
/// serializable transaction owns the epoch, jurisdiction slot, immutable graph and both pointers.
/// Definition receipts never supply active permission. No structural-policy mirror is written.
/// </summary>
public sealed partial class ModerationPolicyPublicationRepository(
    ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine,
    ModerationCapabilityRegistry inventory, IModerationPolicyPublicationEvidenceOwner? evidenceOwner,
    TimeProvider clock, IEnumerable<IModerationConsumerRuntimeSource>? consumerSources = null,
    IModerationMeasurementReceiptVerifier? measurementReceipts = null) : IModerationPolicyPublicationRepository,
    IModerationClassificationMaterialInventoryReadRepository, IModerationClassificationMaterialReviewReadRepository
{
    private readonly ModerationPolicyDefinitionSourceOwner definitions = new(context, admin, machine, inventory, clock);

    internal ModerationPolicyPublicationRepository(
        ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine,
        ModerationCapabilityRegistry inventory, IModerationPolicyPublicationEvidenceOwner? evidenceOwner,
        TimeProvider clock, ExploreDbContextExecutionLease executionLease, IHttpContextAccessor httpContext,
        IInstanceAdministratorCommitAuthority instanceAuthority,
        IEnumerable<IModerationConsumerRuntimeSource>? consumerSources = null,
        IModerationMeasurementReceiptVerifier? measurementReceipts = null)
        : this(context, admin, machine, inventory, evidenceOwner, clock, consumerSources, measurementReceipts)
    {
        ArgumentNullException.ThrowIfNull(executionLease);
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(instanceAuthority);
        instanceExportLease = executionLease;
        instanceExportHttp = httpContext;
        instanceExportAuthority = instanceAuthority;
        instanceExportScope = executionLease.HumanScope
            ?? throw new UnauthorizedAccessException("Instance export requires the original selected human.");
        instanceExportLifetime = executionLease.HumanLifetime
            ?? throw new UnauthorizedAccessException("Instance export requires original validated credential custody.");
    }

    public async Task<ModerationPolicyPublicationResult> PublishAsync(
        ModerationPolicyPublicationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Publication owns a complete clean native unit of work.");
        if (machine.IsMachineCaller) return new(ModerationPolicyPublicationOutcome.Denied);
        if (await admin.ResolveUserIdAsync(cancellationToken) is null)
            return new(ModerationPolicyPublicationOutcome.Unauthenticated);
        if (!Version7(request.OperationId) || !Version7(request.ExpectedHostBindingId)
            || !Version7(request.ExpectedAuthoritySourceId) || request.ExpectedQualificationRevision is < 1 or long.MaxValue
            || request.ExpectedWriterGeneration != ModerationPolicyPublicationProjection.WriterGeneration
            || request.Proposed?.Jurisdiction is null
            || !Version7(request.Proposed.PolicyId) || !Version7(request.Proposed.PublicationId)
            || request.Proposed.ExpectedRevision is < 0 or long.MaxValue
            || !Enum.IsDefined(request.Proposed.RequestedState)
            || request.Proposed.Classes.IsDefaultOrEmpty || request.Proposed.Classes.Length > 256
            || request.Proposed.Classes.Any(item => item is null || item.Reference is null
                || string.IsNullOrWhiteSpace(item.Meaning) || item.Meaning.Length > 4000)
            || request.Proposed.Reasons.IsDefaultOrEmpty || request.Proposed.Reasons.Length > 256
            || request.Proposed.Reasons.Any(item => item is null || item.Reference is null || item.Class is null
                || string.IsNullOrWhiteSpace(item.Meaning) || item.Meaning.Length > 4000)
            || request.Proposed.Profiles.IsDefaultOrEmpty || request.Proposed.Profiles.Length > 256
            || request.Proposed.Profiles.Any(profile => profile is null
                || profile.ScopeKinds.IsDefault || profile.SubjectKinds.IsDefault
                || profile.Effects.IsDefault || profile.Strategies.IsDefault
                || profile.ScopeKinds.Length > 6 || profile.SubjectKinds.Length > 4
                || profile.Effects.Length > 26 || profile.Strategies.Length > 3)
            || request.Proposed.Modes.IsDefaultOrEmpty || request.Proposed.SupportedProfiles.IsDefault
            || request.Proposed.Modes.Length > 2 || request.Proposed.SupportedProfiles.Length > 256
            || string.IsNullOrWhiteSpace(request.Proposed.InventoryFingerprint)
            || request.Proposed.InventoryFingerprint.Length > 64
            || request.Proposed.Jurisdiction.Kind is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant)
            || request.ExpectedPublicationId is { } expected && !Version7(expected))
            return new(ModerationPolicyPublicationOutcome.Invalid);

        // Proposed arrays and their elements are immutable compiler contracts; no caller-owned
        // mutable rules/receipts are retained across retry.
        var digest = ModerationPolicyPublicationProjection.Digest(request);
        var authority = new ModerationQualificationAuthority(context, admin, machine);
        var priorTrustDomain = context.ModerationTrustDomainId;
        ModerationPolicyPublication? accepted = null;
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(
                operation: async token =>
                {
                    accepted = null;
                    context.ChangeTracker.Clear();
                    var current = await AcquireAuthorityAsync(token);
                    await using var slot = await AcquireSlotAsync(current.Binding.Id, request.Proposed.Jurisdiction, token);
                    var receipt = await Graph().SingleOrDefaultAsync(row => row.OperationId == request.OperationId, token);
                    if (receipt is not null)
                    {
                        if (!Matches(receipt, current)) throw new AbortPublication(ModerationPolicyPublicationOutcome.Conflict);
                        if (receipt.PublishedActive && !await PublishedCandidateCurrentAsync(receipt, current, token))
                            throw new AbortPublication(ModerationPolicyPublicationOutcome.Unavailable);
                        accepted = receipt;
                        return new ModerationPolicyPublicationResult(ModerationPolicyPublicationOutcome.Known, receipt);
                    }
                    if (current.Epoch.Revision != request.ExpectedQualificationRevision)
                        throw new AbortPublication(ModerationPolicyPublicationOutcome.Conflict);
                    var scopeId = request.Proposed.Jurisdiction.Id;
                    var selection = await context.ModerationPolicySelections.SingleOrDefaultAsync(
                        row => row.HostBindingId == current.Binding.Id && row.JurisdictionId == scopeId, token);
                    if ((selection?.Revision ?? 0) != request.Proposed.ExpectedRevision
                        || selection is not null && selection.Id != request.Proposed.PolicyId
                        || selection?.LatestPublicationId != request.ExpectedPublicationId)
                        throw new AbortPublication(ModerationPolicyPublicationOutcome.Conflict);
                    var prior = selection is null ? null :
                        await Graph().SingleAsync(row => row.Id == selection.LatestPublicationId, token);
                    var priorDraft = prior is null ? null : ModerationPolicyPublicationProjection.RestoreHistory(prior);
                    if (prior is not null && priorDraft is null)
                        throw new AbortPublication(ModerationPolicyPublicationOutcome.Unavailable);
                    var compilation = ModerationPolicyProposedStateCompiler.Compile(request.Proposed,
                        new(current.Binding.Id, current.Epoch.Id, current.Epoch.Revision,
                            ModerationPolicyPublicationProjection.WriterGeneration,
                            ModerationPolicyPublicationProjection.AuthoringBounds(request.Proposed.Jurisdiction), priorDraft), inventory);
                    if (compilation.Draft is not { } draft)
                        throw new AbortPublication(ModerationPolicyPublicationOutcome.Invalid);
                    if (compilation.Outcome == ModerationPolicyCompilationOutcome.ActiveRejected)
                        throw new AbortPublication(ModerationPolicyPublicationOutcome.Unavailable);
                    var observedAt = clock.GetUtcNow();
                    ModerationPolicyPublicationCandidate? candidate = null;
                    if (request.Proposed.RequestedState == ModerationPolicyRequestedState.Active)
                    {
                        candidate = await AcquireCandidateAsync(context.Database.CurrentTransaction!.TransactionId,
                            current.Binding.Id, request.Proposed.Jurisdiction, request.Proposed.PolicyId,
                            request.Proposed.PublicationId, observedAt, token);
                        if (candidate is null || !current.FencedHumans.Contains(candidate.PreparedByHumanId)
                            || !current.Administrators.Contains(candidate.PreparedByHumanId)
                            || candidate.PreviousPublicationId != request.ExpectedPublicationId
                            || candidate.ProposedStateDigest != ModerationPolicyPublicationProjection.ProposedDigest(request.Proposed))
                            throw new AbortPublication(ModerationPolicyPublicationOutcome.Unavailable);
                    }
                    using var consumerEvidence = BorrowConsumerEvidence(current, draft);
                    var evidence = request.Proposed.RequestedState == ModerationPolicyRequestedState.Active
                        ? await QualifyCompleteAsync(consumerEvidence, draft, observedAt, token)
                        : new List<ModerationPolicyPublicationEvidence>();
                    var publication = Snapshot(request, draft, current, digest, observedAt, evidence,
                        preparationValidUntil: candidate?.ValidUntil);
                    context.ModerationPolicyPublications.Add(publication);
                    if (selection is null)
                        context.ModerationPolicySelections.Add(ModerationPolicySelection.Start(publication));
                    else
                        selection.Replace(publication, request.Proposed.ExpectedRevision, request.ExpectedPublicationId);
                    current.Epoch.Advance(request.ExpectedQualificationRevision);
                    await context.SaveChangesAsync(token);
                    // Includes the case where a native owner flushed before returning unavailable.
                    // Every rejection throws out of the strategy transaction, never commits a prefix.
                    if (!await authority.ProviderTenantMatchesAsync(current.Tenant.Id, token)
                        || context.ModerationTrustDomainId != current.Binding.TrustDomainId
                        || clock.GetUtcNow() < observedAt
                        || publication.PublishedActive && (clock.GetUtcNow() >= publication.EvidenceValidUntil
                            || candidate is null || !await CandidateCurrentAsync(candidate, clock.GetUtcNow(), token, published: true)))
                        throw new AbortPublication(ModerationPolicyPublicationOutcome.Unavailable);
                    accepted = publication;
                    return new ModerationPolicyPublicationResult(ModerationPolicyPublicationOutcome.Known, publication);
                },
                verifySucceeded: async token =>
                {
                    context.ChangeTracker.Clear();
                    await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                    var current = await AcquireAuthorityAsync(token);
                    await using var slot = await AcquireSlotAsync(current.Binding.Id, request.Proposed.Jurisdiction, token);
                    var receipt = await Graph().SingleOrDefaultAsync(row => row.OperationId == request.OperationId, token);
                    return accepted is not null && receipt is not null && receipt.Id == accepted.Id
                        && Matches(receipt, current)
                        && (!receipt.PublishedActive || await PublishedCandidateCurrentAsync(receipt, current, token));
                },
                isolationLevel: IsolationLevel.Serializable, cancellationToken: cancellationToken);
        }
        catch (AbortPublication aborted) { return new(aborted.Outcome); }
        catch (DbUpdateConcurrencyException) { return new(ModerationPolicyPublicationOutcome.Conflict); }
        catch (DbUpdateException exception) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(exception))
        {
            // The named jurisdiction slot serializes same-operation retries. A remaining unique
            // collision is a different policy/publication identity, never duplicate-success.
            return new(ModerationPolicyPublicationOutcome.Conflict);
        }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = priorTrustDomain;
        }

        async Task<ModerationSourceAuthority> AcquireAuthorityAsync(CancellationToken token)
        {
            var current = await authority.AcquireAsync(request.Proposed.Jurisdiction,
                request.Proposed.RequestedState == ModerationPolicyRequestedState.Active ? request.Proposed.PublicationId : null, token)
                ?? throw new AbortPublication(ModerationPolicyPublicationOutcome.Denied);
            if (current.Binding.Id != request.ExpectedHostBindingId || current.Epoch.Id != request.ExpectedAuthoritySourceId)
                throw new AbortPublication(ModerationPolicyPublicationOutcome.Conflict);
            return current;
        }
        bool Matches(ModerationPolicyPublication receipt, ModerationSourceAuthority current) =>
            receipt.RequestDigest == digest && receipt.PrincipalId == current.UserId
            && receipt.TenantId == current.Tenant.Id && receipt.HostBindingId == current.Binding.Id
            && receipt.TrustDomainId == current.Binding.TrustDomainId && receipt.AuthoritySourceId == current.Epoch.Id
            && receipt.AuthorityRevision == request.ExpectedQualificationRevision
            && receipt.QualificationRevision == checked(request.ExpectedQualificationRevision + 1)
            && receipt.Jurisdiction == request.Proposed.Jurisdiction && receipt.PolicyId == request.Proposed.PolicyId
            && receipt.Id == request.Proposed.PublicationId && receipt.Revision == request.Proposed.ExpectedRevision + 1;
    }

    public Task<ModerationPolicyPublication?> AcquireSelectedAsync(
        Guid transactionId, Guid hostBindingId, AuthorityScope jurisdiction, Guid policyId,
        bool activeOnly, CancellationToken cancellationToken = default) =>
        definitions.AcquireSelectedAsync(transactionId, hostBindingId, jurisdiction, policyId, activeOnly, cancellationToken);

    public async Task<DateTimeOffset?> RequalifyActivationAsync(
        IModerationConsumerEvidenceSession evidenceSession,
        Guid transactionId, Guid hostBindingId, AuthorityScope jurisdiction, Guid policyId,
        Guid publicationId, DateTimeOffset observedAt, CancellationToken cancellationToken = default)
    {
        var publication = await AcquireSelectedAsync(transactionId, hostBindingId, jurisdiction,
            policyId, true, cancellationToken);
        if (publication is null || publication.Id != publicationId || observedAt < publication.PublishedAt)
            return null;
        var candidate = await CandidateGraph().SingleOrDefaultAsync(row => row.Id == publicationId, cancellationToken);
        if (candidate is null || candidate.ProposedStateDigest != publication.DeclaredStateDigest
            || !await CandidateCurrentAsync(candidate, observedAt, cancellationToken, published: true))
            return null;
        var draft = ModerationPolicyPublicationProjection.Restore(publication, inventory);
        if (draft is null) return null;
        try
        {
            var evidence = await QualifyCompleteAsync(evidenceSession, draft, observedAt, cancellationToken);
            var validUntil = evidence.Min(row => row.ValidUntil);
            if (candidate.ValidUntil < validUntil) validUntil = candidate.ValidUntil;
            return CurrentTransaction(transactionId) && clock.GetUtcNow() < validUntil ? validUntil : null;
        }
        catch (AbortPublication)
        {
            // The caller owns this transaction. An absent result requires rollback, including any
            // partial source flush; it must never be translated into an inactive permission.
            return null;
        }
    }

    private IQueryable<ModerationPolicyPublication> Graph() => definitions.Graph();

    private async Task<bool> PublishedCandidateCurrentAsync(
        ModerationPolicyPublication publication, ModerationSourceAuthority authority, CancellationToken token)
    {
        var candidate = await CandidateGraph().SingleOrDefaultAsync(row => row.Id == publication.Id, token);
        if (candidate is null || candidate.ProposedStateDigest != publication.DeclaredStateDigest
            || !authority.FencedHumans.Contains(candidate.PreparedByHumanId)
            || !authority.Administrators.Contains(candidate.PreparedByHumanId)
            || !await CandidateCurrentAsync(candidate, clock.GetUtcNow(), token, published: true))
            return false;
        var draft = ModerationPolicyPublicationProjection.Restore(publication, inventory);
        if (draft is null) return false;
        using var consumerEvidence = BorrowConsumerEvidence(authority, draft);
        var evidence = await QualifyCompleteAsync(consumerEvidence, draft, clock.GetUtcNow(), token);
        return evidence.Count > 0 && clock.GetUtcNow() < evidence.Min(row => row.ValidUntil)
            && clock.GetUtcNow() < candidate.ValidUntil;
    }

    private Task<IAsyncDisposable> AcquireSlotAsync(Guid host, AuthorityScope scope, CancellationToken token) =>
        definitions.AcquireSlotAsync(host, scope, token);

    private bool CurrentTransaction(Guid id) => definitions.CurrentTransaction(id);

    private static bool Version7(Guid id) => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11;
    private sealed class AbortPublication(ModerationPolicyPublicationOutcome outcome) : Exception
    {
        public ModerationPolicyPublicationOutcome Outcome { get; } = outcome;
    }
}
