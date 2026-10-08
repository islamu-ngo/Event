using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class ModerationQualificationRepository(
    ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine, TimeProvider clock)
    : IModerationQualificationRepository
{
    public async Task<ModerationQualificationWriteResult> ExecuteAsync(
        ModerationQualificationMutation mutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Source qualification owns its complete clean unit of work.");
        if (machine.IsMachineCaller)
            return new(ModerationQualificationWriteOutcome.Denied);
        if (await admin.ResolveUserIdAsync(cancellationToken) is null)
            return new(ModerationQualificationWriteOutcome.Unauthenticated);

        // Preserve authorable collection values across the execution strategy's complete replay.
        if (mutation is AdoptModerationPolicySource adoption)
            mutation = adoption with
            {
                Rules = Array.AsReadOnly(adoption.Rules.Select(rule => rule with
                {
                    Purposes = Array.AsReadOnly(rule.Purposes.ToArray()),
                    Capabilities = Array.AsReadOnly(rule.Capabilities.ToArray())
                }).ToArray())
            };
        var request = mutation;
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.GetType().FullName + "\n" + JsonSerializer.Serialize(request, request.GetType()))));
        var authority = new ModerationQualificationAuthority(context, admin, machine);
        var priorTrustDomain = context.ModerationTrustDomainId;
        ModerationQualificationMutationReceipt? accepted = null;
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(
                operation: async token =>
                {
                    context.ChangeTracker.Clear();
                    var current = await authority.AcquireAsync(request, token);
                    if (current is null)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    var existing = await ReadReceiptAsync(request.OperationId, token);
                    if (existing is not null)
                    {
                        var settled = MatchReceipt(existing, request, digest, current);
                        if (settled.Outcome == ModerationQualificationWriteOutcome.Known)
                            accepted = existing;
                        return settled;
                    }
                    if (current.Epoch.Revision != request.ExpectedQualificationRevision)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);

                    var now = clock.GetUtcNow();
                    var source = await ApplyAsync(request, current, now, token);
                    current.Epoch.Advance(request.ExpectedQualificationRevision);
                    accepted = ModerationQualificationMutationReceipt.Complete(
                        request.OperationId, digest, current.Binding.Id, current.Binding.TrustDomainId,
                        current.Tenant.Id, source.Kind, source.Id, current.UserId,
                        source.Revision, current.Epoch.Revision, now);
                    context.ModerationQualificationMutationReceipts.Add(accepted);
                    await context.SaveChangesAsync(token);
                    if (!await authority.ProviderTenantMatchesAsync(accepted.TenantId, token))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    return new ModerationQualificationWriteResult(ModerationQualificationWriteOutcome.Known, accepted);
                },
                verifySucceeded: async token =>
                {
                    context.ChangeTracker.Clear();
                    await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                    var current = await authority.AcquireAsync(request, token);
                    if (current is null || accepted is null)
                        return false;
                    var receipt = await ReadReceiptAsync(request.OperationId, token);
                    return receipt is not null && receipt.ResultRevision == accepted.ResultRevision
                        && receipt.QualificationRevision == accepted.QualificationRevision
                        && MatchReceipt(receipt, request, digest, current).Outcome == ModerationQualificationWriteOutcome.Known;
                },
                isolationLevel: IsolationLevel.Serializable,
                cancellationToken: cancellationToken);
        }
        catch (AbortQualification aborted)
        {
            return new(aborted.Outcome);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(ModerationQualificationWriteOutcome.Conflict);
        }
        catch (DbUpdateException exception) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(exception))
        {
            context.ChangeTracker.Clear();
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var current = await authority.AcquireAsync(request, token);
                if (current is null)
                    return new ModerationQualificationWriteResult(ModerationQualificationWriteOutcome.Denied);
                var receipt = await ReadReceiptAsync(request.OperationId, token);
                var result = receipt is null ? new(ModerationQualificationWriteOutcome.Conflict)
                    : MatchReceipt(receipt, request, digest, current);
                await transaction.CommitAsync(token);
                return result;
            }, cancellationToken);
        }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = priorTrustDomain;
        }
    }

    private async Task<QualifiedSource> ApplyAsync(
        ModerationQualificationMutation mutation, ModerationSourceAuthority authority,
        DateTimeOffset now, CancellationToken token)
    {
        switch (mutation)
        {
            case OpenModerationCaseSource source:
            {
                var subject = await ResolveSubjectAsync(source, authority, token);
                var created = ModerationCase.Open(source.CaseId, subject, source.Target, source.OccurrenceId, now);
                context.ModerationCases.Add(created);
                return new(ModerationQualificationSourceKind.Case, created.Id, created.Revision);
            }
            case RecordModerationCaseResponse source:
            {
                var moderationCase = await ReadCaseAsync(source.CaseId, source.Target, source.ExpectedCaseRevision, token);
                if (moderationCase.SubjectKind != ModerationSubjectKind.Human || moderationCase.SubjectId != authority.UserId)
                    throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                var evidence = ModerationQualificationEvidence.RecordResponse(
                    source.EvidenceId, moderationCase.Id, authority.Binding.TrustDomainId, authority.Tenant.Id,
                    moderationCase.SubjectId, authority.UserId, source.Statement, now);
                context.ModerationQualificationEvidence.Add(evidence);
                return new(ModerationQualificationSourceKind.Evidence, evidence.Id, evidence.Revision);
            }
            case AssignModerationReviewerSource source:
            {
                var moderationCase = await ReadCaseAsync(source.CaseId, source.Target, source.ExpectedCaseRevision, token);
                var assignment = ModerationReviewerAssignment.Assign(
                    source.AssignmentId, moderationCase, source.ExpectedCaseRevision, source.ReviewerHumanId, authority.UserId, now);
                context.ModerationReviewerAssignments.Add(assignment);
                return new(ModerationQualificationSourceKind.Reviewer, assignment.Id, assignment.Revision);
            }
            case QualifyModerationReviewerController source:
            {
                var assignment = await ReadReviewerAsync(source.AssignmentId, source.Target, source.ExpectedAssignmentRevision, authority, token);
                await RequireValidityCeilingAsync(source.Target, source.QualifiedUntil, now, token);
                RequireIndependentAssessor(assignment, authority.UserId);
                var evidence = ModerationQualificationEvidence.QualifyController(
                    source.EvidenceId, assignment.CaseId, authority.Binding.TrustDomainId, authority.Tenant.Id,
                    assignment.ReviewerHumanId, source.ControllerHumanId, authority.UserId, source.Justification, source.QualifiedUntil, now);
                assignment.QualifyController(source.ExpectedAssignmentRevision, source.ControllerHumanId,
                    evidence.Id, authority.UserId, source.QualifiedUntil, now);
                context.ModerationQualificationEvidence.Add(evidence);
                return new(ModerationQualificationSourceKind.Reviewer, assignment.Id, assignment.Revision);
            }
            case AssessModerationReviewerConflict source:
            {
                var assignment = await ReadReviewerAsync(source.AssignmentId, source.Target, source.ExpectedAssignmentRevision, authority, token);
                RequireIndependentAssessor(assignment, authority.UserId);
                if (assignment.ControllerHumanId is not { } controller || assignment.QualifiedUntil is not { } until || until <= now)
                    throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                await RequireValidityCeilingAsync(source.Target, until, now, token);
                var evidence = ModerationQualificationEvidence.AssessConflict(
                    source.EvidenceId, assignment.CaseId, authority.Binding.TrustDomainId, authority.Tenant.Id,
                    assignment.ReviewerHumanId, controller, authority.UserId, (int)source.State, source.Justification, until, now);
                assignment.RecordConflictAssessment(source.ExpectedAssignmentRevision, source.State, evidence.Id, authority.UserId, now);
                context.ModerationQualificationEvidence.Add(evidence);
                return new(ModerationQualificationSourceKind.Reviewer, assignment.Id, assignment.Revision);
            }
            case WithdrawModerationReviewerSource source:
            {
                var assignment = await ReadReviewerAsync(source.AssignmentId, source.Target, source.ExpectedAssignmentRevision, authority, token);
                assignment.Withdraw(source.ExpectedAssignmentRevision, authority.UserId, now);
                return new(ModerationQualificationSourceKind.Reviewer, assignment.Id, assignment.Revision);
            }
            case QualifyModerationCaseUrgency source:
            {
                var moderationCase = await ReadCaseAsync(source.CaseId, source.Target, source.ExpectedCaseRevision, token);
                await RequireValidityCeilingAsync(source.Target, source.ValidUntil, now, token);
                if (moderationCase.SubjectKind == ModerationSubjectKind.Human && moderationCase.SubjectId == authority.UserId)
                    throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                var evidence = ModerationQualificationEvidence.QualifyUrgency(
                    source.EvidenceId, moderationCase.Id, authority.Binding.TrustDomainId, authority.Tenant.Id,
                    moderationCase.SubjectKind == ModerationSubjectKind.Human ? moderationCase.SubjectId : null,
                    authority.UserId, source.Justification, source.ValidUntil, now);
                context.ModerationQualificationEvidence.Add(evidence);
                return new(ModerationQualificationSourceKind.Evidence, evidence.Id, evidence.Revision);
            }
            case FinalizeModerationFindingSource source:
                return await FinalizeFindingAsync(source, authority, now, token);
            case ReverseModerationFindingSource source:
            {
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationFinding>(context, source.FindingId, token);
                var finding = await context.ModerationFindings.SingleOrDefaultAsync(row => row.Id == source.FindingId, token)
                    ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                finding.ValidatePersistedState();
                if (finding.Scope != source.Target)
                    throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                if (finding.SubjectKind == ModerationSubjectKind.Human && finding.SubjectId == authority.UserId)
                    throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                if (finding.Revision != source.ExpectedFindingRevision || finding.State != ModerationFindingState.Final)
                    throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                finding.Reverse(source.ReversalReferenceId, SubjectReference.Create(ModerationSubjectKind.Human,
                    authority.UserId, authority.Instance), source.Justification, source.ExpectedFindingRevision, now);
                return new(ModerationQualificationSourceKind.Finding, finding.Id, finding.Revision);
            }
            case AdoptModerationPolicySource source:
                return await AdoptPolicyAsync(source, now, token);
            default:
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
    }

    private async Task<QualifiedSource> FinalizeFindingAsync(
        FinalizeModerationFindingSource source, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationPolicy>(context, source.PolicyId, token);
        var policy = await context.ModerationPolicies.Include(row => row.Rules)
            .SingleOrDefaultAsync(row => row.Id == source.PolicyId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        policy.ValidatePersistedState();
        if (policy.Revision != source.ExpectedPolicyRevision)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var rule = policy.Rules.SingleOrDefault(row => row.Rule == source.Rule);
        if (!policy.Jurisdiction.Contains(source.Target) || rule is null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var moderationCase = await ReadCaseAsync(source.CaseId, source.Target, source.ExpectedCaseRevision, token);
        var assignmentId = await context.ModerationReviewerAssignments.AsNoTracking()
            .Where(row => row.CaseId == source.CaseId && row.ReviewerHumanId == authority.UserId && row.WithdrawnAt == null)
            .Select(row => row.Id).SingleOrDefaultAsync(token);
        var assignment = await ReadReviewerAsync(assignmentId, source.Target, null, authority, token);
        if (!assignment.IsQualifiedAt(now))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        await RequireReviewerEvidenceAsync(assignment, authority, rule.MaximumReviewDelay, now, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEvidence>(context, source.EvidenceId, token);
        var evidence = await context.ModerationQualificationEvidence.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.EvidenceId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        evidence.ValidatePersistedState();
        if (evidence.CaseId != moderationCase.Id)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        ModerationFindingEvidenceReference reference;
        if (evidence.Purpose == ModerationQualificationEvidencePurpose.Response
            && moderationCase.SubjectKind == ModerationSubjectKind.Human && evidence.SubjectHumanId == moderationCase.SubjectId)
            reference = ModerationFindingEvidenceReference.Response(evidence.Id, moderationCase.Id, moderationCase.Revision, evidence.RecordedAt);
        else if (evidence.Purpose == ModerationQualificationEvidencePurpose.Urgency && evidence.ValidUntil > now
            && evidence.ValidUntil - evidence.RecordedAt <= rule.MaximumReviewDelay)
            reference = ModerationFindingEvidenceReference.QualifiedUrgency(
                evidence.Id, evidence.Id, moderationCase.Id, moderationCase.Revision, evidence.RecordedAt);
        else
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var finding = ModerationFinding.Finalize(source.FindingId, moderationCase, source.ExpectedCaseRevision,
            source.Rule, rule.Rule, SubjectReference.Create(ModerationSubjectKind.Human, authority.UserId, authority.Instance),
            reference, source.Justification, now);
        context.ModerationFindings.Add(finding);
        return new(ModerationQualificationSourceKind.Finding, finding.Id, finding.Revision);
    }

    private async Task<QualifiedSource> AdoptPolicyAsync(AdoptModerationPolicySource source, DateTimeOffset now, CancellationToken token)
    {
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationPolicy>(context, source.PolicyId, token);
        var current = await context.ModerationPolicies.Include(row => row.Rules)
            .SingleOrDefaultAsync(row => row.Id == source.PolicyId, token);
        if ((current?.Revision ?? 0) != source.ExpectedPolicyRevision)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        if (current is not null && (current.Jurisdiction != source.Target || current.PublicationId == source.PublicationId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var rules = source.Rules.Select(rule => ModerationPolicyRule.Define(rule.Id,
            ModerationRuleReference.Create(source.Target.Id, rule.Code, rule.Revision), rule.Meaning,
            rule.Purposes, rule.Capabilities, rule.MaximumFiniteDuration, rule.AllowsIndefinite,
            rule.MaximumReviewDelay, rule.MinimumApprovals, rule.MaximumApprovals)).ToArray();
        if (current is null)
        {
            var created = ModerationPolicy.Adopt(source.PolicyId, source.Target, rules, source.PublicationId, now);
            context.ModerationPolicies.Add(created);
            return new(ModerationQualificationSourceKind.Policy, created.Id, created.Revision);
        }
        ModerationPolicy replacement;
        try
        {
            replacement = current.ReplaceAdoption(source.Target, source.ExpectedPolicyRevision, rules, source.PublicationId, now);
        }
        catch (ArgumentException)
        {
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        }
        var existingRules = current.Rules.ToDictionary(rule => rule.Id);
        if (current.Rules.Any(rule => !replacement.Rules.Any(candidate => candidate.Id == rule.Id && candidate.Rule == rule.Rule)))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        context.Entry(current).CurrentValues.SetValues(replacement);
        foreach (var rule in replacement.Rules)
        {
            if (existingRules.TryGetValue(rule.Id, out var tracked))
                context.Entry(tracked).CurrentValues.SetValues(rule);
            else
                context.ModerationPolicyRules.Add(rule);
        }
        return new(ModerationQualificationSourceKind.Policy, current.Id, current.Revision);
    }

    private async Task<ModerationCase> ReadCaseAsync(Guid id, AuthorityScope target, long? revision, CancellationToken token)
    {
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, id, token);
        var source = await context.ModerationCases.SingleOrDefaultAsync(row => row.Id == id, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        source.ValidatePersistedState();
        if (source.Scope != target)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        if (source.State != ModerationCaseState.Open || revision is { } expected && source.Revision != expected)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        return source;
    }

    private async Task<ModerationReviewerAssignment> ReadReviewerAsync(
        Guid id, AuthorityScope target, long? revision, ModerationSourceAuthority authority, CancellationToken token)
    {
        var discovered = await context.ModerationReviewerAssignments.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        await ReadCaseAsync(discovered.CaseId, target, null, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationReviewerAssignment>(context, id, token);
        var source = await context.ModerationReviewerAssignments.SingleAsync(row => row.Id == id, token);
        source.ValidatePersistedState();
        if (source.CaseId != discovered.CaseId || !authority.FencedHumans.Contains(source.ReviewerHumanId)
            || source.ControllerHumanId is { } controller && !authority.FencedHumans.Contains(controller))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (source.WithdrawnAt is not null || revision is { } expected && source.Revision != expected)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        return source;
    }

    private async Task RequireValidityCeilingAsync(AuthorityScope target, DateTimeOffset until, DateTimeOffset now, CancellationToken token)
    {
        var discovered = await context.ModerationPolicies.AsNoTracking()
            .SingleOrDefaultAsync(row => row.JurisdictionKind == target.Kind && row.JurisdictionId == target.Id, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationPolicy>(context, discovered.Id, token);
        var policy = await context.ModerationPolicies.Include(row => row.Rules).SingleAsync(row => row.Id == discovered.Id, token);
        policy.ValidatePersistedState();
        if (policy.Jurisdiction != target || until <= now || until - now > policy.Rules.Min(rule => rule.MaximumReviewDelay))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
    }

    private async Task RequireReviewerEvidenceAsync(
        ModerationReviewerAssignment assignment, ModerationSourceAuthority authority,
        TimeSpan ceiling, DateTimeOffset now, CancellationToken token)
    {
        if (assignment.IdentityEvidenceId is not { } identityId || assignment.ConflictEvidenceId is not { } conflictId
            || assignment.QualifiedByHumanId is not { } qualifier || assignment.AssessedByHumanId is not { } assessor
            || !authority.Administrators.Contains(qualifier) || !authority.Administrators.Contains(assessor))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        foreach (var id in new[] { identityId, conflictId }.Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEvidence>(context, id, token);
        var identity = await context.ModerationQualificationEvidence.AsNoTracking().SingleOrDefaultAsync(row => row.Id == identityId, token);
        var conflict = await context.ModerationQualificationEvidence.AsNoTracking().SingleOrDefaultAsync(row => row.Id == conflictId, token);
        if (identity is null || conflict is null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        identity.ValidatePersistedState();
        conflict.ValidatePersistedState();
        if (identity.Purpose != ModerationQualificationEvidencePurpose.Controller
            || conflict.Purpose != ModerationQualificationEvidencePurpose.Conflict
            || identity.CaseId != assignment.CaseId || conflict.CaseId != assignment.CaseId
            || identity.ReviewerHumanId != assignment.ReviewerHumanId || conflict.ReviewerHumanId != assignment.ReviewerHumanId
            || identity.ControllerHumanId != assignment.ControllerHumanId || conflict.ControllerHumanId != assignment.ControllerHumanId
            || identity.AssertedByHumanId != qualifier || conflict.AssertedByHumanId != assessor
            || conflict.ConflictState != (int)ModerationReviewerConflictState.Independent
            || identity.ValidUntil != assignment.QualifiedUntil || conflict.ValidUntil != assignment.QualifiedUntil
            || identity.ValidUntil <= now || conflict.ValidUntil <= now
            || identity.ValidUntil - identity.RecordedAt > ceiling || conflict.ValidUntil - conflict.RecordedAt > ceiling
            || identity.RecordedAt > conflict.RecordedAt || conflict.RecordedAt > now)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
    }

    private async Task<SubjectReference> ResolveSubjectAsync(
        OpenModerationCaseSource source, ModerationSourceAuthority authority, CancellationToken token)
    {
        var tenantId = authority.Tenant.Id;
        switch (source.SubjectKind)
        {
            case ModerationSubjectKind.Human:
                if (source.Target == authority.Tenant && !await context.TenantUsers.AsNoTracking()
                    .AnyAsync(row => row.TenantId == tenantId && row.UserId == source.SubjectId && !row.IsDeleted, token))
                    throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                return SubjectReference.Create(source.SubjectKind, source.SubjectId, authority.Instance);
            case ModerationSubjectKind.Service:
                if (!authority.IsInstanceAdmin) throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, source.SubjectId, token);
                if (!await context.ServicePrincipals.AsNoTracking().AnyAsync(row => row.Id == source.SubjectId && !row.IsDeleted, token))
                    throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                return SubjectReference.Create(source.SubjectKind, source.SubjectId, authority.Instance);
            case ModerationSubjectKind.Organization:
                if (!authority.IsInstanceAdmin) throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                await RelationalEntityRowFence.AcquireGlobalAsync<Organization>(context, source.SubjectId, token);
                if (!await context.Organizations.AsNoTracking().AnyAsync(row => row.Id == source.SubjectId && !row.IsDeleted, token))
                    throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                return SubjectReference.Create(source.SubjectKind, source.SubjectId, authority.Instance);
            case ModerationSubjectKind.Content:
                await RelationalEntityRowFence.AcquireAsync<Explore.Domain.Event>(context, tenantId, row => row.Id, source.SubjectId, token);
                if (!await context.Events.AsNoTracking().AnyAsync(row => row.Id == source.SubjectId && row.TenantId == tenantId && !row.IsDeleted, token))
                    throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                return SubjectReference.Create(source.SubjectKind, source.SubjectId, authority.Tenant);
            default:
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
    }

    private Task<ModerationQualificationMutationReceipt?> ReadReceiptAsync(Guid operationId, CancellationToken token) =>
        context.ModerationQualificationMutationReceipts.AsNoTracking().SingleOrDefaultAsync(row => row.Id == operationId, token);

    private static ModerationQualificationWriteResult MatchReceipt(
        ModerationQualificationMutationReceipt receipt, ModerationQualificationMutation request, string digest, ModerationSourceAuthority authority)
    {
        receipt.ValidatePersistedState();
        var expected = request switch
        {
            OpenModerationCaseSource source => new QualifiedSource(ModerationQualificationSourceKind.Case, source.CaseId, 1),
            RecordModerationCaseResponse source => new(ModerationQualificationSourceKind.Evidence, source.EvidenceId, 1),
            AssignModerationReviewerSource source => new(ModerationQualificationSourceKind.Reviewer, source.AssignmentId, 1),
            QualifyModerationReviewerController source => new(ModerationQualificationSourceKind.Reviewer, source.AssignmentId, checked(source.ExpectedAssignmentRevision + 1)),
            AssessModerationReviewerConflict source => new(ModerationQualificationSourceKind.Reviewer, source.AssignmentId, checked(source.ExpectedAssignmentRevision + 1)),
            WithdrawModerationReviewerSource source => new(ModerationQualificationSourceKind.Reviewer, source.AssignmentId, checked(source.ExpectedAssignmentRevision + 1)),
            QualifyModerationCaseUrgency source => new(ModerationQualificationSourceKind.Evidence, source.EvidenceId, 1),
            FinalizeModerationFindingSource source => new(ModerationQualificationSourceKind.Finding, source.FindingId, 1),
            ReverseModerationFindingSource source => new(ModerationQualificationSourceKind.Finding, source.FindingId, checked(source.ExpectedFindingRevision + 1)),
            AdoptModerationPolicySource source => new(ModerationQualificationSourceKind.Policy, source.PolicyId, checked(source.ExpectedPolicyRevision + 1)),
            _ => null
        };
        return receipt.RequestDigest == digest && receipt.PrincipalId == authority.UserId
            && receipt.HostBindingId == authority.Binding.Id && receipt.TrustDomainId == authority.Binding.TrustDomainId
            && receipt.TenantId == authority.Tenant.Id
            && expected is not null && receipt.SourceKind == expected.Kind && receipt.SourceId == expected.Id
            && receipt.ResultRevision == expected.Revision && receipt.QualificationRevision == checked(request.ExpectedQualificationRevision + 1)
            ? new(ModerationQualificationWriteOutcome.Known, receipt)
            : new(ModerationQualificationWriteOutcome.Conflict);
    }

    private static void RequireIndependentAssessor(ModerationReviewerAssignment assignment, Guid userId)
    {
        if (assignment.SubjectHumanId == userId || assignment.ReviewerHumanId == userId || assignment.ControllerHumanId == userId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
    }

    private sealed class AbortQualification(ModerationQualificationWriteOutcome outcome) : Exception
    {
        public ModerationQualificationWriteOutcome Outcome { get; } = outcome;
    }

    private sealed record QualifiedSource(ModerationQualificationSourceKind Kind, Guid Id, long Revision);
}
