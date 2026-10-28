using System.Collections.Immutable;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationRecognitionErasureRepository
{
    /// <summary>
    /// Read-only serializable snapshot, intentionally not the matching writer's fence graph.
    /// Acquiring User/identity/setting locks here, after a resource consumer's locks, would
    /// invert its order. Every qualified fact participates in this original native snapshot.
    /// No receipt is cached between calls; the consumer incorporates Generation and CurrentUntil.
    /// </summary>
    public async Task<ModerationRecognitionContinuityEvaluation> ReadCurrentInTransactionAsync(
        Guid freshSubjectId, CancellationToken token)
    {
        try
        {
            var tenantId = await RequireCurrentContinuityBoundaryAsync(freshSubjectId, token);
            var bindings = await Rows<ModerationRecognitionContinuityBinding>().Where(row => row.FreshSubjectId == freshSubjectId)
                .OrderBy(row => row.Id).ToListAsync(token);
            var evidence = new List<object>();
            var currentBindings = new List<ModerationRecognitionContinuityBinding>();
            var currentActions = new List<ModerationRecognitionCaptureItem>();
            DateTimeOffset? until = null;
            var recovery = await Rows<ModerationRecognitionActionRecovery>()
                .Where(row => row.FreshSubjectId == freshSubjectId && row.ResolvedAt == null).ToListAsync(token);
            if (bindings.Count == 0 && recovery.Count == 0)
                return new() { Generation = ContinuityGeneration(freshSubjectId, tenantId, evidence) };
            var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token)
                ?? throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
            var jurisdiction = await ReadEnrollmentJurisdictionAsync(host, tenantId, token);
            foreach (var unresolved in recovery)
            {
                unresolved.ValidatePersistedState();
                var item = await Rows<ModerationRecognitionCaptureItem>()
                    .SingleOrDefaultAsync(row => row.Id == unresolved.CaptureItemId, token)
                    ?? throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
                item.ValidatePersistedState();
                if (item.ComputeMaterialReceipt() != unresolved.CaptureMaterialReceipt)
                    throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
                if (clock.GetUtcNow() < unresolved.DispositionBy
                    && (item.Scope.Contains(jurisdiction) || jurisdiction.Contains(item.Scope))
                    && ModerationRecognitionContinuityBinding.WithinOriginalInterval(item, clock.GetUtcNow()))
                    throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.ActionRecoveryRequired);
            }
            if (!await Rows<ServicePrincipal>().AnyAsync(row => row.Id == host.MaintenanceServicePrincipalId && !row.IsDeleted, token))
                throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
            foreach (var binding in bindings)
            {
                binding.ValidatePersistedState();
                var item = await Rows<ModerationRecognitionCaptureItem>().SingleOrDefaultAsync(row => row.Id == binding.CaptureItemId, token)
                    ?? throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
                item.ValidatePersistedState();
                if (item.ComputeMaterialReceipt() != binding.CaptureMaterialReceipt)
                    throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
                if (item.TrustDomainId != host.TrustDomainId || item.SubjectInstanceId != host.InstanceId
                    || item.SubjectSolutionId != host.SolutionId)
                    throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
                // Sibling-tenant continuity neither restricts this tenant nor makes its source
                // outage a blanket denial elsewhere. The consumer still checks exact resources.
                if (!item.Scope.Contains(jurisdiction) && !jurisdiction.Contains(item.Scope)) continue;
                if (await ReadCurrentContinuityBindingAsync(host, binding, item, evidence, token) is not { } currentUntil)
                    continue;
                until = Earlier(until, currentUntil);
                currentBindings.Add(binding);
                currentActions.Add(item);
            }
            if (until is { } boundary && clock.GetUtcNow() >= boundary)
                throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
            return new()
            {
                Bindings = currentBindings.ToImmutableArray(),
                CurrentActions = currentActions.ToImmutableArray(),
                CurrentUntil = until,
                Generation = ContinuityGeneration(freshSubjectId, tenantId, evidence)
            };
        }
        catch (ContinuityUnavailable failure) { return new() { ActionFailure = failure.Failure }; }
        catch (Exception exception) when (!token.IsCancellationRequested && exception is DbException or IOException or TimeoutException)
        {
            return new() { ActionFailure = ModerationRecognitionContinuityFailure.StoreUnavailable };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new() { ActionFailure = ModerationRecognitionContinuityFailure.CanonicalUnavailable };
        }
    }

    public async Task<ModerationRecognitionContinuityEvaluation> ReadPublicationInTransactionAsync(
        Guid freshSubjectId, AuthorityScope target, CancellationToken token)
    {
        if (target is null || target.Kind != ModerationScopeKind.Tenant)
            return new() { ActionFailure = ModerationRecognitionContinuityFailure.JurisdictionUnavailable };
        return (await ReadTargetsInTransactionAsync(freshSubjectId, ModerationCapability.Publish, [target], token))[0];
    }

    public async Task<ImmutableArray<ModerationRecognitionContinuityEvaluation>> ReadResourcesInTransactionAsync(
        Guid freshSubjectId, ModerationCapability capability, ImmutableArray<AuthorityScope> targets,
        CancellationToken token)
    {
        if (targets.IsDefaultOrEmpty) return [];
        if (targets.Length > EventResourceAuthorityRequest.MaximumBatchResources
            || capability is not (ModerationCapability.ReadResource or ModerationCapability.Upload)
            || targets.Any(target => target is null || target.Kind != ModerationScopeKind.Resource))
            return targets.Select(_ => new ModerationRecognitionContinuityEvaluation
            { ActionFailure = ModerationRecognitionContinuityFailure.JurisdictionUnavailable }).ToImmutableArray();
        return await ReadTargetsInTransactionAsync(freshSubjectId, capability, targets, token);
    }

    private async Task<ImmutableArray<ModerationRecognitionContinuityEvaluation>> ReadTargetsInTransactionAsync(
        Guid freshSubjectId, ModerationCapability capability, ImmutableArray<AuthorityScope> targets,
        CancellationToken token)
    {
        ImmutableArray<ModerationRecognitionContinuityEvaluation> Closed(ModerationRecognitionContinuityFailure failure) =>
            targets.Select(_ => new ModerationRecognitionContinuityEvaluation { ActionFailure = failure }).ToImmutableArray();
        try
        {
            var tenantId = await RequireCurrentContinuityBoundaryAsync(freshSubjectId, token);
            var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token)
                ?? throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
            var jurisdiction = await ReadEnrollmentJurisdictionAsync(host, tenantId, token);
            if (targets.Any(target => !jurisdiction.Contains(target)))
                return Closed(ModerationRecognitionContinuityFailure.JurisdictionUnavailable);
            var reads = targets.Select(target => new TargetContinuityRead(target, capability)).ToArray();
            var bindings = await Rows<ModerationRecognitionContinuityBinding>()
                .Where(row => row.FreshSubjectId == freshSubjectId).OrderBy(row => row.Id).ToListAsync(token);
            var recovery = await Rows<ModerationRecognitionActionRecovery>()
                .Where(row => row.FreshSubjectId == freshSubjectId && row.ResolvedAt == null).ToListAsync(token);
            var itemIds = bindings.Select(binding => binding.CaptureItemId)
                .Concat(recovery.Select(row => row.CaptureItemId)).Distinct().ToArray();
            var items = await Rows<ModerationRecognitionCaptureItem>().Where(row => itemIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, token);
            foreach (var unresolved in recovery)
            {
                unresolved.ValidatePersistedState();
                if (!items.TryGetValue(unresolved.CaptureItemId, out var item))
                    throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
                item.ValidatePersistedState();
                if (item.ComputeMaterialReceipt() != unresolved.CaptureMaterialReceipt)
                    throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
                foreach (var read in reads.Where(read => unresolved.CouldAffect(item, read.Target, capability, clock.GetUtcNow())))
                {
                    read.Failure = ModerationRecognitionContinuityFailure.ActionRecoveryRequired;
                    read.Evidence.Add(new
                    {
                        Recovery = unresolved.Id,
                        unresolved.CaptureItemId,
                        unresolved.VerifiedBindingId,
                        unresolved.DispositionBy
                    });
                }
            }
            foreach (var binding in bindings)
            {
                // A missing or malformed immutable capture cannot establish irrelevance.
                if (!items.TryGetValue(binding.CaptureItemId, out var item))
                    throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
                item.ValidatePersistedState();
                binding.ValidatePersistedState();
                if (item.ComputeMaterialReceipt() != binding.CaptureMaterialReceipt)
                    throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
                if (item.Purpose != ModerationReferenceRecognition.SeparatelyQualifiedFinalAction
                    || (item.PurposeCapabilityMask & (1 << (int)capability)) == 0)
                    continue;
                var applicable = reads.Where(read => item.Scope.Contains(read.Target)).ToArray();
                if (applicable.Length == 0) continue;

                // Only this binding's targets receive its original source/receipt uncertainty.
                // Qualification queries run once per binding, never once per requested target.
                var evidence = new List<object>();
                DateTimeOffset? currentUntil = null;
                var failure = ModerationRecognitionContinuityFailure.None;
                try
                {
                    binding.ValidatePersistedState();
                    if (item.TrustDomainId != host.TrustDomainId || item.SubjectInstanceId != host.InstanceId
                        || item.SubjectSolutionId != host.SolutionId
                        || !await Rows<ServicePrincipal>().AnyAsync(row =>
                            row.Id == host.MaintenanceServicePrincipalId && !row.IsDeleted, token))
                        throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
                    currentUntil = await ReadCurrentContinuityBindingAsync(host, binding, item, evidence, token);
                }
                catch (ContinuityUnavailable unavailable) { failure = unavailable.Failure; }
                catch (Exception exception) when (!token.IsCancellationRequested && exception is DbException or IOException or TimeoutException)
                {
                    failure = ModerationRecognitionContinuityFailure.StoreUnavailable;
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                    failure = ModerationRecognitionContinuityFailure.CanonicalUnavailable;
                }
                foreach (var read in applicable)
                {
                    read.Evidence.AddRange(evidence);
                    if (failure != ModerationRecognitionContinuityFailure.None)
                    {
                        if (read.Failure == ModerationRecognitionContinuityFailure.None) read.Failure = failure;
                        read.Evidence.Add(new { Binding = binding.Id, Failure = failure });
                    }
                    else if (currentUntil is { } boundary)
                    {
                        read.Until = Earlier(read.Until, boundary);
                        read.Bindings.Add(binding);
                        read.Actions.Add(item);
                    }
                }
            }
            return reads.Select(read =>
            {
                if (read.Until is { } boundary && clock.GetUtcNow() >= boundary)
                    read.Failure = ModerationRecognitionContinuityFailure.SourceUnavailable;
                return new ModerationRecognitionContinuityEvaluation
                {
                    ActionFailure = read.Failure,
                    Bindings = read.Failure == ModerationRecognitionContinuityFailure.None ? read.Bindings.ToImmutableArray() : [],
                    CurrentActions = read.Failure == ModerationRecognitionContinuityFailure.None ? read.Actions.ToImmutableArray() : [],
                    CurrentUntil = read.Failure == ModerationRecognitionContinuityFailure.None ? read.Until : null,
                    Generation = ContinuityGeneration(freshSubjectId, tenantId,
                        read.Evidence.Append(new { Failure = read.Failure }))
                };
            }).ToImmutableArray();
        }
        catch (ContinuityUnavailable failure) { return Closed(failure.Failure); }
        catch (Exception exception) when (!token.IsCancellationRequested && exception is DbException or IOException or TimeoutException)
        {
            return Closed(ModerationRecognitionContinuityFailure.StoreUnavailable);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Closed(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
        }
    }

    private async Task<Guid> RequireCurrentContinuityBoundaryAsync(Guid freshSubjectId, CancellationToken token)
    {
        try { RequireTransaction(); }
        catch (InvalidOperationException)
        {
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.NativeBoundaryRequired);
        }
        if (freshSubjectId == Guid.Empty || context.IsTenantFilterBypassed || HasUncommittedContinuityAuthority()
            || context.ChangeTracker.Entries<ModerationRecognitionContinuityBinding>()
                .Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            || context.ChangeTracker.Entries<ModerationRecognitionActionRecovery>()
                .Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            || context.TenantFilterTenantId is not { } tenantId
            || !await ContinuityProviderTenantMatchesAsync(tenantId, token))
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.NativeBoundaryRequired);
        if (!await Rows<User>().AnyAsync(row => row.Id == freshSubjectId && !row.IsDeleted, token))
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SubjectUnavailable);
        await RequireSettledSubjectAsync(freshSubjectId, token);
        return tenantId;
    }

    private async Task<DateTimeOffset?> ReadCurrentContinuityBindingAsync(ModerationHostBinding host,
        ModerationRecognitionContinuityBinding binding, ModerationRecognitionCaptureItem item,
        List<object> evidence, CancellationToken token)
    {
        evidence.Add(new
        {
            Binding = binding.Id,
            binding.CaptureItemId,
            binding.BoundAt,
            binding.RetainUntil,
            item.DroppedAt,
            item.EndsAt,
            item.ReviewAt
        });
        if (!ModerationRecognitionContinuityBinding.WithinOriginalInterval(item, clock.GetUtcNow())) return null;
        if (!await RequireContinuitySettlementAsync(item, token)) return null;
        var decision = await Rows<ModerationDecision>().SingleOrDefaultAsync(row =>
            row.Id == item.DecisionId && row.SubjectKind == ModerationSubjectKind.Human && row.SubjectId == item.SubjectId, token);
        var sanction = await Rows<ModerationSanction>().SingleOrDefaultAsync(row =>
            row.Id == item.SanctionId && row.SubjectKind == ModerationSubjectKind.Human && row.SubjectId == item.SubjectId, token);
        if (decision is null || sanction is null)
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
        decision.ValidatePersistedState();
        sanction.ValidatePersistedState();
        evidence.Add(new
        {
            Decision = decision.Id,
            DecisionRevision = decision.Revision,
            DecisionState = decision.State,
            Sanction = sanction.Id,
            SanctionRevision = sanction.Revision,
            SanctionState = sanction.State
        });
        if (!binding.IsCurrent(item, decision, sanction, clock.GetUtcNow())) return null;
        await RequireOriginalContinuityCaseAsync(decision, token);
        var finding = await Rows<ModerationFinding>().SingleOrDefaultAsync(row => row.Id == binding.FindingId, token)
            ?? throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
        finding.ValidatePersistedState();
        if (finding.CaseId != decision.CaseId || finding.Subject != decision.Subject
            || finding.Scope != decision.Scope || finding.Rule != decision.Rule)
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
        evidence.Add(new { Finding = finding.Id, FindingRevision = finding.Revision, FindingState = finding.State });
        if (finding.State != ModerationFindingState.Final) return null;
        var source = await Rows<ModerationRecognitionPrivacySource>().Include(row => row.Retention)
            .SingleOrDefaultAsync(row => row.Id == item.SourceId, token)
            ?? throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
        var adoption = await Rows<ModerationRecognitionPrivacyAdoption>()
            .SingleOrDefaultAsync(row => row.Id == item.AdoptionId && row.SourceId == source.Id, token)
            ?? throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
        source.ValidatePersistedState();
        adoption.ValidatePersistedState();
        evidence.Add(new
        {
            Source = source.Id,
            SourceRevision = source.Revision,
            SourceGeneration = source.WriterGeneration,
            Adoption = adoption.Id,
            AdoptionRevision = adoption.Revision,
            AdoptionGeneration = adoption.WriterGeneration,
            adoption.WithdrawnAt,
            SourceUntil = source.ValidUntil,
            AdoptionUntil = adoption.ValidUntil
        });
        if (adoption.WithdrawnAt is not null || clock.GetUtcNow() >= adoption.ValidUntil
            || clock.GetUtcNow() >= source.ValidUntil) return null;
        await ReadCurrentContinuitySourceAsync(host, item, source, adoption, token);
        var currentUntil = ContinuityUntil(item, source, adoption);
        if (clock.GetUtcNow() >= currentUntil)
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
        return currentUntil;
    }

    private sealed class TargetContinuityRead(AuthorityScope target, ModerationCapability capability)
    {
        public AuthorityScope Target { get; } = target;
        public List<object> Evidence { get; } = [new { Capability = capability, Target = target }];
        public List<ModerationRecognitionContinuityBinding> Bindings { get; } = [];
        public List<ModerationRecognitionCaptureItem> Actions { get; } = [];
        public DateTimeOffset? Until { get; set; }
        public ModerationRecognitionContinuityFailure Failure { get; set; }
    }

    private async Task ReadCurrentContinuitySourceAsync(ModerationHostBinding host,
        ModerationRecognitionCaptureItem item, ModerationRecognitionPrivacySource source,
        ModerationRecognitionPrivacyAdoption adoption, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var definition = await ModerationPolicyPublicationGraph.Include(Rows<ModerationPolicyPublication>()
            .Where(row => row.Id == source.DefinitionId)).SingleOrDefaultAsync(token);
        var profile = definition?.Profiles.SingleOrDefault(row => row.ProfileId == source.ProfileId);
        if (definition is null || !MatchesOriginalContinuitySource(item, source, definition, profile, host)
            || source.RecordedAt > now || definition.PolicyId != source.PolicyId || definition.Revision != source.PolicyRevision
            || definition.TenantId != source.TenantId || profile!.Scopes.Any(scope => scope.Kind < source.ScopeCeiling)
            || !adoption.MatchesCurrent(source, now))
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
        RequireOriginalAdoption(item, source, adoption);
        await RequireLatestContinuitySourceAsync(source, token);
        var epoch = await Rows<ModerationQualificationEpoch>().SingleOrDefaultAsync(row =>
            row.HostBindingId == host.Id && row.TenantId == source.TenantId, token);
        if (epoch is null || epoch.Revision < source.WriterGeneration || epoch.Revision < adoption.WriterGeneration
            || !await Rows<Tenant>().AnyAsync(row => row.Id == source.TenantId && row.TenantStatus.IsActiveState, token))
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
        var candidate = await Rows<ModerationPolicyPublicationCandidate>().SingleOrDefaultAsync(row => row.Id == source.PublicationId, token);
        var selection = await Rows<ModerationPolicySelection>().SingleOrDefaultAsync(row => row.Id == source.PolicyId && row.HostBindingId == host.Id, token);
        if (candidate is null)
        {
            var restored = ModerationPolicyPublicationProjection.RestoreHistory(definition);
            if (restored is null || definition.AdoptedRevision != definition.Revision || definition.Id != source.PublicationId
                || selection is null || selection.ActivePublicationId != definition.Id && selection.LatestPublicationId != definition.Id
                || ModerationPolicyPublicationProjection.ProposedDigest(restored.Proposed) != source.ProposedStateDigest
                || !await AdministratorAsync(definition.PrincipalId, definition.Jurisdiction, token))
                throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
        }
        else
        {
            candidate.ValidatePersistedState();
            if (candidate.DefinitionId != definition.Id || candidate.ProposedStateDigest != source.ProposedStateDigest
                || candidate.PreparedAt > now || candidate.HostBindingId != host.Id || candidate.TenantId != source.TenantId
                || candidate.PolicyId != source.PolicyId || candidate.ExpectedRevision + 1 != source.PolicyRevision
                || !await AdministratorAsync(candidate.PreparedByHumanId, definition.Jurisdiction, token)
                || selection?.ActivePublicationId != source.PublicationId
                    && (candidate.ValidUntil <= now || (selection?.Revision ?? 0) != candidate.ExpectedRevision
                        || selection?.LatestPublicationId != candidate.PreviousPublicationId))
                throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
        }
        foreach (var human in new[] { source.RecordedByHumanId, source.PrivacyOwnerHumanId, source.RecoveryOwnerHumanId })
        {
            if (!await AdministratorAsync(human, definition.Jurisdiction, token))
                throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
            await RequireSettledSubjectAsync(human, token);
        }
        foreach (var human in new[] { source.PrivacyControllerHumanId, source.RecoveryControllerHumanId, source.GovernanceControllerHumanId })
        {
            if (!await Rows<User>().AnyAsync(row => row.Id == human && !row.IsDeleted, token))
                throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
            await RequireSettledSubjectAsync(human, token);
        }
        var required = profile.Recognition switch
        {
            ModerationReferenceRecognition.SeparatelyQualifiedFinalAction => ModerationRecognitionPrivacyPurpose.ScopedActionContinuity,
            ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment => ModerationRecognitionPrivacyPurpose.ScopedActionContinuity
                | ModerationRecognitionPrivacyPurpose.CoveredReplacementAdmissionExclusion,
            _ => (ModerationRecognitionPrivacyPurpose)0
        };
        if (required == 0 || source.Purposes != required
            || !await ReadContinuityReceiptAsync(source, ModerationRecognitionPrivacySourceOwner.RecordRequest(source, definition.Jurisdiction),
                ModerationQualificationSourceKind.RecognitionPrivacySource, source.Id, source.RecordedByHumanId,
                source.WriterGeneration, source.RecordedAt, token)
            || !await ReadContinuityReceiptAsync(source, ModerationRecognitionPrivacySourceOwner.AdoptionRequest(adoption, definition.Jurisdiction),
                ModerationQualificationSourceKind.RecognitionPrivacyAdoption, adoption.Id, adoption.AdoptedByHumanId,
                adoption.WriterGeneration, adoption.AdoptedAt, token))
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
    }

    private async Task<bool> ReadContinuityReceiptAsync(ModerationRecognitionPrivacySource source,
        ModerationQualificationMutation template, ModerationQualificationSourceKind kind, Guid id, Guid human,
        long generation, DateTimeOffset completedAt, CancellationToken token)
    {
        var receipt = await Rows<ModerationQualificationMutationReceipt>().SingleOrDefaultAsync(row =>
            row.SourceId == id && row.SourceKind == kind && row.ResultRevision == 1, token);
        if (receipt is null) return false;
        receipt.ValidatePersistedState();
        var request = template with { OperationId = receipt.Id };
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.GetType().FullName + "\n" + JsonSerializer.Serialize(request, request.GetType()))));
        return receipt.RequestDigest == digest && receipt.PrincipalId == human
            && receipt.HostBindingId == source.HostBindingId && receipt.TrustDomainId == source.TrustDomainId
            && receipt.TenantId == source.TenantId && receipt.QualificationRevision == generation && receipt.CompletedAt == completedAt;
    }

    private async Task<bool> ContinuityProviderTenantMatchesAsync(Guid tenantId, CancellationToken token)
    {
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider) return true;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        var value = await command.ExecuteScalarAsync(token);
        return value is string setting && Guid.TryParse(setting, out var current) && current == tenantId;
    }

    private static string ContinuityGeneration(Guid subject, Guid tenant, IEnumerable<object> evidence) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { Version = 1, Subject = subject, Tenant = tenant, Evidence = evidence })));
}
