using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationRecognitionErasureRepository
{
    private async Task<CaptureAuthority> AcquireSubjectAsync(PrivacyErasureRequest request, CancellationToken token)
    {
        RequireTransaction();
        // Every native adapter rejects this lookup without its original active identity gate.
        // It is a provenance check, not a claim that an already-erased subject may enroll.
        _ = await identity.IsSubjectFencedAsync(request.SubjectId, token);
        if (request.SubjectKind != PrivacyErasureSubjectKind.User || request.IdentityFences.Count != 0)
            throw new InvalidOperationException("moderation_recognition_request_unavailable");
        // Discovery is only an inventory of native writer anchors. No discovery result or
        // caller-supplied subject GUID becomes an administrator/evidence session.
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        var sources = host is null ? [] : await Rows<ModerationRecognitionPrivacySource>()
            .Where(row => row.HostBindingId == host.Id && row.TrustDomainId == host.TrustDomainId).ToListAsync(token);
        var publicationIds = sources.Select(row => row.PublicationId).Distinct().ToArray();
        var definitionIds = sources.Select(row => row.DefinitionId).Distinct().ToArray();
        var preparers = await Rows<ModerationPolicyPublicationCandidate>()
            .Where(row => publicationIds.Contains(row.Id)).Select(row => row.PreparedByHumanId).ToArrayAsync(token);
        var publishers = await Rows<ModerationPolicyPublication>()
            .Where(row => definitionIds.Contains(row.Id)).Select(row => row.PrincipalId).ToArrayAsync(token);
        var recapture = await ReadRecaptureInventoryAsync(request.SubjectId, token);
        var humans = sources.SelectMany(row => new[]
        {
            row.RecordedByHumanId, row.PrivacyOwnerHumanId, row.RecoveryOwnerHumanId,
            row.PrivacyControllerHumanId, row.RecoveryControllerHumanId, row.GovernanceControllerHumanId
        }).Concat(preparers).Concat(publishers)
            .Concat(recapture.Items.SelectMany(row => new[] { row.SubjectId, row.ErasureSubjectId }))
            .Append(request.SubjectId).Distinct().Order().ToArray();
        foreach (var human in humans)
            await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, human, token);
        var subject = await Rows<User>().SingleOrDefaultAsync(row => row.Id == request.SubjectId, token);
        if (subject is null) throw new InvalidOperationException("moderation_recognition_subject_unavailable");
        var logins = await Rows<UserExternalLogin>().Where(row => humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in logins)
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, id, token);
        var grants = await Rows<PlatformUserRole>().Where(row => humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var grant in grants)
            await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, grant.Id, token);
        var memberships = await Rows<TenantUser>().Where(row => humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in memberships)
            await RelationalEntityRowFence.AcquireGlobalAsync<TenantUser>(context, id, token);
        var tenantGrants = await Rows<TenantUserRoleGrant>().Where(row => memberships.Contains(row.TenantUserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var grant in tenantGrants)
            await RelationalEntityRowFence.AcquireGlobalAsync<TenantUserRoleGrant>(context, grant.Id, token);
        foreach (var id in grants.Select(row => row.RoleId).Concat(tenantGrants.Select(row => row.RoleId)).Distinct().Order())
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [id], token);
        if (host is not null)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, token);
            var currentHost = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
            if (currentHost is null || currentHost.Id != host.Id || currentHost.TrustDomainId != host.TrustDomainId
                || currentHost.SolutionId != host.SolutionId || currentHost.InstanceId != host.InstanceId)
                throw new InvalidOperationException("moderation_recognition_host_unavailable");
            await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, host.MaintenanceServicePrincipalId, token);
            var tenants = sources.Select(row => row.TenantId).Distinct().Order().ToArray();
            foreach (var tenant in tenants)
                await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenant, token);
            var epochs = await Rows<ModerationQualificationEpoch>().Where(row => row.HostBindingId == host.Id)
                .OrderBy(row => row.TenantId).ToArrayAsync(token);
            foreach (var epoch in epochs)
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, epoch.Id, token);
            // Actual publication writers use these exact slots, including first insertion.
            var jurisdictions = sources.Select(row => row.TenantId).Append(host.InstanceId).Distinct().Order();
            foreach (var jurisdiction in jurisdictions)
                _ = await RelationalNamedLock.AcquireTransactionAsync(context,
                    $"moderation.publication:{host.Id:N}:{jurisdiction:N}", token);
        }
        var bindings = await Rows<SecretBinding>()
            .Where(row => row.SettingKey == Explore.Application.Contracts.Moderation.ModerationRecognitionKeyRing.SecretSettingKey)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in bindings)
            await RelationalEntityRowFence.AcquireGlobalAsync<SecretBinding>(context, id, token);
        // Subject writers and canonical moderation writers already take User first. The
        // operation slot handles absent pending rows without inventing a global admission ban.
        _ = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.recognition-erasure:{request.SubjectId:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionPending>(context, request.IntentId, token);
        var authority = new CaptureAuthority(host, humans)
        {
            RecaptureBindings = recapture.Bindings,
            CaptureItems = recapture.Items
        };
        await AcquireCaptureCanonicalAnchorsAsync(request.SubjectId, authority, token);
        return authority;
    }

    private async Task<List<QualifiedContribution>> ContributionsAsync(
        CaptureAuthority authority, Guid subjectId, CancellationToken token)
    {
        var sanctions = await Rows<ModerationSanction>().Where(row =>
            row.SubjectKind == ModerationSubjectKind.Human && row.SubjectId == subjectId)
            .OrderBy(row => row.DecisionId).ThenBy(row => row.Id).ToListAsync(token);
        var result = new List<QualifiedContribution>();
        var now = clock.GetUtcNow();
        foreach (var discovered in sanctions)
        {
            var canonical = await CanonicalAsync(subjectId, discovered.DecisionId, discovered.Id, token)
                ?? throw new CaptureUnavailable(ModerationRecognitionPendingFailure.SourceUnavailable);
            var (decision, sanction) = canonical;
            if (!sanction.IsEffectiveAt(now) || decision.FindingId is null
                || decision.State is not (ModerationDecisionState.Active or ModerationDecisionState.Scheduled))
                continue;
            if (!await HasFinalFindingAsync(decision, token)) continue;
            var host = authority.Host ?? throw new CaptureUnavailable(ModerationRecognitionPendingFailure.SourceUnavailable);
            var instance = AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
            if (decision.Subject.Origin != instance || decision.Subject != sanction.Subject
                || decision.Scope != sanction.Scope || decision.Rule != sanction.Rule
                || decision.Scope.TrustDomainId != host.TrustDomainId)
                throw new CaptureUnavailable(ModerationRecognitionPendingFailure.SourceUnavailable);
            // The exact original policy/profile establishes whether recognition was selected.
            // Missing policy meaning is unavailable, never a warnings-only/ordinary bypass.
            var definitions = await ModerationPolicyPublicationGraph.Include(Rows<ModerationPolicyPublication>()
                .Where(row => row.HostBindingId == host.Id && row.TrustDomainId == host.TrustDomainId
                    && (row.JurisdictionTenantId == decision.RuleOwnerId
                        || row.JurisdictionTenantId == null && row.InstanceId == decision.RuleOwnerId)))
                .ToListAsync(token);
            var profiles = definitions.SelectMany(definition => definition.Profiles
                .Where(profile => profile.RuleCode == decision.RuleCode && profile.RuleRevision == decision.RuleRevision)
                .Select(profile => (Definition: definition, Profile: profile))).ToArray();
            if (profiles.Length == 0)
                throw new CaptureUnavailable(ModerationRecognitionPendingFailure.SourceUnavailable);
            if (profiles.All(row => row.Profile.Recognition == ModerationReferenceRecognition.Never)) continue;
            var sourceCandidates = await Rows<ModerationRecognitionPrivacySource>().Include(row => row.Retention)
                .Where(row => row.HostBindingId == host.Id && row.TrustDomainId == host.TrustDomainId)
                .OrderByDescending(row => row.WriterGeneration).ToListAsync(token);
            var matching = sourceCandidates.Where(source => profiles.Any(pair =>
                pair.Definition.Id == source.DefinitionId && pair.Profile.ProfileId == source.ProfileId))
                .GroupBy(source => (source.TenantId, source.PublicationId, source.ProfileId, source.ModeCode))
                .Select(group => group.First()).ToArray();
            if (matching.Length != 1)
                throw new CaptureUnavailable(ModerationRecognitionPendingFailure.SourceUnavailable);
            var selected = matching[0];
            var definitionPair = profiles.Single(row => row.Definition.Id == selected.DefinitionId
                && row.Profile.ProfileId == selected.ProfileId);
            var adoption = await QualifySourceAsync(authority, selected, definitionPair.Definition,
                definitionPair.Profile, now, token);
            if (adoption is null)
                throw new CaptureUnavailable(ModerationRecognitionPendingFailure.SourceUnavailable);
            var contribution = new QualifiedContribution(decision, sanction, selected, adoption, definitionPair.Profile.Recognition);
            if (Purposes(contribution).Any()) result.Add(contribution);
        }
        result.AddRange(await BoundContributionsAsync(authority, subjectId, token));
        return result;
    }

    private async Task<ModerationRecognitionPrivacyAdoption?> QualifySourceAsync(CaptureAuthority authority,
        ModerationRecognitionPrivacySource discovered, ModerationPolicyPublication definition,
        ModerationPolicyPublicationProfile profile, DateTimeOffset observedAt, CancellationToken token)
    {
        var host = authority.Host!;
        _ = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.recognition-privacy:{host.Id:N}:{discovered.TenantId:N}:{discovered.PublicationId:N}:{discovered.ProfileId:N}:{discovered.ModeCode}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionPrivacySource>(context, discovered.Id, token);
        var source = await Rows<ModerationRecognitionPrivacySource>().Include(row => row.Retention)
            .SingleAsync(row => row.Id == discovered.Id, token);
        source.ValidatePersistedState();
        if (source.Id != discovered.Id || source.WriterGeneration != discovered.WriterGeneration
            || source.ValidUntil <= observedAt || source.RecordedAt > observedAt
            || definition.Id != source.DefinitionId || definition.PolicyId != source.PolicyId
            || definition.Revision != source.PolicyRevision || definition.TrustDomainId != source.TrustDomainId
            || definition.HostBindingId != source.HostBindingId || definition.TenantId != source.TenantId
            || profile.Scopes.Any(scope => scope.Kind < source.ScopeCeiling))
            return null;
        var epoch = await Rows<ModerationQualificationEpoch>().SingleOrDefaultAsync(row =>
            row.HostBindingId == host.Id && row.TenantId == source.TenantId, token);
        if (epoch is null || epoch.Revision < source.WriterGeneration) return null;
        var candidate = await Rows<ModerationPolicyPublicationCandidate>()
            .SingleOrDefaultAsync(row => row.Id == source.PublicationId, token);
        var selection = await Rows<ModerationPolicySelection>().SingleOrDefaultAsync(row =>
            row.Id == source.PolicyId && row.HostBindingId == host.Id, token);
        if (candidate is null)
        {
            var restored = ModerationPolicyPublicationProjection.RestoreHistory(definition);
            if (restored is null || definition.AdoptedRevision != definition.Revision
                || definition.Id != source.PublicationId
                || selection is null || selection.ActivePublicationId != definition.Id && selection.LatestPublicationId != definition.Id
                || ModerationPolicyPublicationProjection.ProposedDigest(restored.Proposed) != source.ProposedStateDigest
                || !authority.Humans.Contains(definition.PrincipalId)
                || !await AdministratorAsync(definition.PrincipalId, definition.Jurisdiction, token))
                return null;
        }
        else
        {
            candidate.ValidatePersistedState();
            if (candidate.DefinitionId != definition.Id
                || candidate.ProposedStateDigest != source.ProposedStateDigest || candidate.PreparedAt > observedAt
                || candidate.HostBindingId != host.Id || candidate.TenantId != source.TenantId
                || candidate.PolicyId != source.PolicyId || candidate.ExpectedRevision + 1 != source.PolicyRevision
                || !authority.Humans.Contains(candidate.PreparedByHumanId)
                || !await AdministratorAsync(candidate.PreparedByHumanId, definition.Jurisdiction, token)
                || (selection?.ActivePublicationId != source.PublicationId
                    && (candidate.ValidUntil <= observedAt || (selection?.Revision ?? 0) != candidate.ExpectedRevision
                        || selection?.LatestPublicationId != candidate.PreviousPublicationId)))
                return null;
        }
        foreach (var human in new[] { source.RecordedByHumanId, source.PrivacyOwnerHumanId, source.RecoveryOwnerHumanId })
            if (!authority.Humans.Contains(human) || !await AdministratorAsync(human, definition.Jurisdiction, token))
                return null;
        foreach (var human in new[] { source.PrivacyControllerHumanId, source.RecoveryControllerHumanId, source.GovernanceControllerHumanId })
            if (!authority.Humans.Contains(human) || !await Rows<User>().AnyAsync(row => row.Id == human && !row.IsDeleted, token))
                return null;
        var required = profile.Recognition switch
        {
            ModerationReferenceRecognition.SeparatelyQualifiedFinalAction => ModerationRecognitionPrivacyPurpose.ScopedActionContinuity,
            ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment => ModerationRecognitionPrivacyPurpose.ScopedActionContinuity
                | ModerationRecognitionPrivacyPurpose.CoveredReplacementAdmissionExclusion,
            _ => (ModerationRecognitionPrivacyPurpose)0
        };
        if (required == 0 || source.Purposes != required || !await ReceiptAsync(source,
            ModerationRecognitionPrivacySourceOwner.RecordRequest(source, definition.Jurisdiction),
            ModerationQualificationSourceKind.RecognitionPrivacySource, source.Id, source.RecordedByHumanId,
            source.WriterGeneration, source.RecordedAt, token))
            return null;
        var adoption = await Rows<ModerationRecognitionPrivacyAdoption>().SingleOrDefaultAsync(row => row.SourceId == source.Id, token);
        if (adoption is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionPrivacyAdoption>(context, adoption.Id, token);
        adoption = await Rows<ModerationRecognitionPrivacyAdoption>().SingleAsync(row => row.Id == adoption.Id, token);
        adoption.ValidatePersistedState();
        return adoption.MatchesCurrent(source, clock.GetUtcNow()) && adoption.WriterGeneration <= epoch.Revision
            && await ReceiptAsync(source, ModerationRecognitionPrivacySourceOwner.AdoptionRequest(adoption, definition.Jurisdiction),
                ModerationQualificationSourceKind.RecognitionPrivacyAdoption, adoption.Id, adoption.AdoptedByHumanId,
                adoption.WriterGeneration, adoption.AdoptedAt, token) ? adoption : null;
    }

    private async Task<bool> ReceiptAsync(ModerationRecognitionPrivacySource source, ModerationQualificationMutation request,
        ModerationQualificationSourceKind kind, Guid sourceId, Guid human, long generation,
        DateTimeOffset completedAt, CancellationToken token)
    {
        var receipt = await Rows<ModerationQualificationMutationReceipt>().SingleOrDefaultAsync(row =>
            row.SourceId == sourceId && row.SourceKind == kind && row.ResultRevision == 1, token);
        if (receipt is null) return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationMutationReceipt>(context, receipt.Id, token);
        receipt = await Rows<ModerationQualificationMutationReceipt>().SingleAsync(row => row.Id == receipt.Id, token);
        receipt.ValidatePersistedState();
        request = request with { OperationId = receipt.Id };
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.GetType().FullName + "\n" + JsonSerializer.Serialize(request, request.GetType()))));
        return receipt.RequestDigest == digest && receipt.PrincipalId == human
            && receipt.HostBindingId == source.HostBindingId && receipt.TrustDomainId == source.TrustDomainId
            && receipt.TenantId == source.TenantId && receipt.QualificationRevision == generation && receipt.CompletedAt == completedAt;
    }

    private async Task<bool> AdministratorAsync(Guid human, AuthorityScope jurisdiction, CancellationToken token) =>
        await Rows<PlatformUserRole>().AnyAsync(row => row.UserId == human && !row.User.IsDeleted
            && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin", token)
        || jurisdiction.Kind == ModerationScopeKind.Tenant
            && await Rows<TenantUserRoleGrant>().AnyAsync(row => row.TenantId == jurisdiction.Id
                && row.TenantUser.UserId == human && row.RevokedAt == null && row.RoleId == (int)RoleEnum.TenantAdmin
                && row.RoleScopeId == (int)RoleScopeEnum.Tenant && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant
                && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted, token);

    private async Task<(ModerationDecision Decision, ModerationSanction Sanction)?> CanonicalAsync(
        Guid subject, Guid decisionId, Guid sanctionId, CancellationToken token)
    {
        var discovered = await Rows<ModerationDecision>().SingleOrDefaultAsync(row =>
            row.Id == decisionId && row.SubjectKind == ModerationSubjectKind.Human && row.SubjectId == subject, token);
        if (discovered is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, discovered.CaseId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(context, decisionId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationSanction>(context, sanctionId, token);
        var decision = await Rows<ModerationDecision>().SingleOrDefaultAsync(row =>
            row.Id == decisionId && row.SubjectKind == ModerationSubjectKind.Human && row.SubjectId == subject, token);
        var sanction = await Rows<ModerationSanction>().SingleOrDefaultAsync(row =>
            row.Id == sanctionId && row.SubjectKind == ModerationSubjectKind.Human && row.SubjectId == subject, token);
        decision?.ValidatePersistedState();
        sanction?.ValidatePersistedState();
        return decision is not null && sanction is not null ? (decision, sanction) : null;
    }

    private async Task<bool> HasFinalFindingAsync(ModerationDecision decision, CancellationToken token)
    {
        if (decision.FindingId is not { } id) return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationFinding>(context, id, token);
        var finding = await Rows<ModerationFinding>().SingleOrDefaultAsync(row => row.Id == id, token)
            ?? throw new CaptureUnavailable(ModerationRecognitionPendingFailure.SourceUnavailable);
        finding.ValidatePersistedState();
        if (finding.CaseId != decision.CaseId || finding.Subject != decision.Subject
            || finding.Scope != decision.Scope || finding.Rule != decision.Rule)
            throw new CaptureUnavailable(ModerationRecognitionPendingFailure.SourceUnavailable);
        return finding.State == ModerationFindingState.Final;
    }

    private static IEnumerable<ModerationReferenceRecognition> Purposes(QualifiedContribution contribution)
    {
        if (contribution.Parent is { } parent)
        {
            yield return parent.Purpose;
            yield break;
        }
        if (contribution.Sanction.Capabilities.Any(capability => capability is ModerationCapability.Publish
            or ModerationCapability.Upload or ModerationCapability.ReadResource or ModerationCapability.Manage or ModerationCapability.Participate))
            yield return ModerationReferenceRecognition.SeparatelyQualifiedFinalAction;
        if (contribution.Recognition == ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment
            && contribution.Sanction.Capabilities.Any(capability => capability is ModerationCapability.Enrollment
                or ModerationCapability.Admission or ModerationCapability.PrivateLogistics))
            yield return ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment;
    }

    private sealed record CaptureAuthority(ModerationHostBinding? Host, Guid[] Humans)
    {
        public ModerationRecognitionContinuityBinding[] RecaptureBindings { get; init; } = [];
        public ModerationRecognitionCaptureItem[] CaptureItems { get; init; } = [];
    }
    private sealed record QualifiedContribution(ModerationDecision Decision, ModerationSanction Sanction,
        ModerationRecognitionPrivacySource Source, ModerationRecognitionPrivacyAdoption Adoption,
        ModerationReferenceRecognition Recognition, ModerationRecognitionCaptureItem? Parent = null,
        ModerationRecognitionContinuityBinding? Binding = null);
}
