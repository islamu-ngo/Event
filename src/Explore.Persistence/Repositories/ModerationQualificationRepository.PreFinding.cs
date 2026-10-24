using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository : IModerationPreFindingRepository
{
    private DateTimeOffset _preFindingUntil = DateTimeOffset.MaxValue;

    public async Task<ModerationPreFindingWriteResult> ExecutePreFindingAsync(
        ModerationPreFindingOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (context.TenantFilterTenantId is not { } tenant || !ContentResponseEnvironment(tenant)
            || responseProtection is null || caseAttribution is null
            || operation is IssueModerationPreFindingNotice && nativeNotices is null)
            return new(ModerationQualificationWriteOutcome.Unavailable);
        if (operation.OperationId == Guid.Empty || operation.OperationId.Version != 7
            || operation.OperationId.Variant is < 8 or > 11 || operation.CaseId == Guid.Empty
            || operation.ExpectedCaseRevision < 1)
            return new(ModerationQualificationWriteOutcome.Denied);
        Func<CancellationToken, Task> final = _ => Task.CompletedTask;
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(() =>
                privacy.ExecuteEnrollmentSerializedAsync(async token =>
                {
                    final = _ => Task.CompletedTask;
                    _preFindingUntil = DateTimeOffset.MaxValue;
                    _attributionAcquired = false;
                    context.ChangeTracker.Clear();
                    context.ModerationTrustDomainId = null;
                    if (context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    await using IDbContextTransaction? owned = context.IdentityFenceOwnsTransaction ? null
                        : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                    var transaction = context.Database.CurrentTransaction
                        ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    var requested = operation switch
                    {
                        ProposeModerationPreFindingCustody proposal => new[] { proposal.PrivacyOwnerHumanId },
                        IssueModerationPreFindingNotice issue => new[] { issue.Recipient.HumanId },
                        _ => Array.Empty<Guid>()
                    };
                    var authority = await new ModerationQualificationAuthority(context, admin, machine)
                        .AcquirePreFindingAsync(operation.CaseId, requested, token,
                            requestedRecipient: (operation as IssueModerationPreFindingNotice)?.Recipient)
                        ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
                        $"moderation.pre-finding:{authority.Binding.Id:D}:{operation.CaseId:D}", token);
                    var digest = PreFindingDigest(operation);
                    var receipt = await context.Set<ModerationPreFindingReceipt>().AsNoTracking()
                        .SingleOrDefaultAsync(row => row.Id == operation.OperationId, token);
                    if (receipt is not null && (receipt.RequestDigest != digest
                        || receipt.ActorHumanId != authority.UserId || receipt.CaseId != operation.CaseId
                        || receipt.TenantId != authority.Tenant.Id || receipt.HostBindingId != authority.Binding.Id))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    await RequirePreFindingOperationAsync(operation, authority, receipt is not null, token);
                    if (receipt is null)
                    {
                        var result = await ApplyPreFindingAsync(operation, authority, token);
                        var now = PreFindingTime();
                        receipt = ModerationPreFindingReceipt.Complete(operation.OperationId, authority.Binding.Id,
                            authority.Binding.TrustDomainId, tenant, operation.CaseId, authority.UserId,
                            result.Id, result.Revision, digest, now);
                        context.Set<ModerationPreFindingReceipt>().Add(receipt);
                        context.Attach(authority.Epoch);
                        authority.Epoch.Advance(authority.Epoch.Revision);
                        await context.SaveChangesAsync(token);
                    }
                    var retained = receipt;
                    final = async finalToken =>
                    {
                        await RequirePreFindingOperationAsync(operation, authority, true, finalToken);
                        await RequirePreFindingTerminalAsync(authority, transaction, finalToken);
                        if (!await context.Set<ModerationPreFindingReceipt>().AsNoTracking().AnyAsync(row =>
                            row.Id == retained.Id && row.RequestDigest == retained.RequestDigest
                            && row.ActorHumanId == retained.ActorHumanId && row.ResultId == retained.ResultId
                            && row.ResultRevision == retained.ResultRevision && row.CompletedAt == retained.CompletedAt, finalToken))
                            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                        await RequireCurrentCaseAttributionAsync(finalToken);
                        if (clock.GetUtcNow() < retained.CompletedAt || clock.GetUtcNow() >= _preFindingUntil)
                            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    };
                    await final(token);
                    if (owned is not null) await owned.CommitAsync(token);
                    return new ModerationPreFindingWriteResult(ModerationQualificationWriteOutcome.Known,
                        receipt.ResultId, receipt.ResultRevision);
                }, token => final(token), cancellationToken));
        }
        catch (AbortQualification aborted) { return new(aborted.Outcome); }
        catch (DbUpdateConcurrencyException) { return new(ModerationQualificationWriteOutcome.Conflict); }
        catch (DbUpdateException exception) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(exception))
        { return new(ModerationQualificationWriteOutcome.Conflict); }
        catch (DbUpdateException exception) when (exception.InnerException is DbException)
        { return new(ModerationQualificationWriteOutcome.Unavailable); }
        catch (DbException) { return new(ModerationQualificationWriteOutcome.Unavailable); }
        catch (CryptographicException) { return new(ModerationQualificationWriteOutcome.Unavailable); }
        catch (ArgumentException) { return new(ModerationQualificationWriteOutcome.Denied); }
        catch (InvalidOperationException) { return new(ModerationQualificationWriteOutcome.Conflict); }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }
    }

    private async Task<(Guid Id, long Revision)> ApplyPreFindingAsync(
        ModerationPreFindingOperation operation, ModerationSourceAuthority authority, CancellationToken token)
    {
        var now = PreFindingTime();
        switch (operation)
        {
            case ProposeModerationPreFindingCustody proposal:
                {
                    var owner = await ReadCaseAsync(operation.CaseId, authority.Tenant,
                        operation.ExpectedCaseRevision, authority, token);
                    var attribution = await ReadCurrentCaseAttributionAsync(owner, authority, token);
                    var custody = ModerationPreFindingCustody.Propose(proposal.OperationId, authority.Binding.Id,
                        owner, attribution, authority.UserId, proposal.PrivacyOwnerHumanId, proposal.Terms, now, proposal.ValidUntil);
                    context.Add(custody);
                    return (custody.Id, custody.Revision);
                }
            case AcceptModerationPreFindingCustody acceptance:
                {
                    var custody = await context.Set<ModerationPreFindingCustody>().SingleAsync(row => row.Id == acceptance.CustodyId, token);
                    custody.Accept(authority.UserId, acceptance.ExpectedCustodyRevision, now);
                    return (custody.Id, custody.Revision);
                }
            case IssueModerationPreFindingNotice issue:
                {
                    var owner = await ReadCaseAsync(operation.CaseId, authority.Tenant,
                        operation.ExpectedCaseRevision, authority, token);
                    var attribution = await ReadCurrentCaseAttributionAsync(owner, authority, token);
                    var custody = await ReadPreFindingCustodyAsync(issue.CustodyId, authority, owner, token);
                    var reviewer = await RequirePreFindingReviewerAsync(owner, authority.UserId, authority, token);
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(issue.Text);
                    try
                    {
                        RequirePreFindingSize(bytes.Length, custody.MaximumTextBytes);
                        var cipher = PreFindingProtector(issue.OperationId, owner.Id, custody.Id,
                            custody.AttributionSourceId, issue.Recipient.HumanId, issue.RetainUntil, "notice").Protect(bytes);
                        try
                        {
                            var notice = ModerationPreFindingNotice.Issue(issue.OperationId, owner, attribution, custody,
                                authority.UserId, reviewer.Id, reviewer.Revision, issue.Recipient, cipher, now, issue.RetainUntil);
                            var recipient = await context.Users.AsNoTracking()
                                .SingleOrDefaultAsync(row => row.Id == notice.RecipientHumanId, token);
                            if (recipient is null)
                                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                            context.Add(notice);
                            if (!await nativeNotices!.MaterializePreFindingAsync(notice, recipient, token))
                                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                            return (notice.Id, notice.Revision);
                        }
                        finally { CryptographicOperations.ZeroMemory(cipher); }
                    }
                    finally { CryptographicOperations.ZeroMemory(bytes); }
                }
            case AcknowledgeModerationPreFindingNotice acknowledge:
                {
                    var notice = await context.Set<ModerationPreFindingNotice>().SingleAsync(row => row.Id == acknowledge.NoticeId, token);
                    notice.Acknowledge(authority.UserId, acknowledge.ExpectedNoticeRevision, now);
                    return (notice.Id, notice.Revision);
                }
            case RespondToModerationPreFindingNotice response:
                {
                    var notice = await context.Set<ModerationPreFindingNotice>().SingleAsync(row => row.Id == response.NoticeId, token);
                    var bytes = Encoding.UTF8.GetBytes(response.Statement);
                    try
                    {
                        var cipher = PreFindingProtector(notice.Id, notice.CaseId, notice.CustodyId,
                            notice.AttributionSourceId, notice.RecipientHumanId, notice.RetainUntil, "response").Protect(bytes);
                        try { notice.Respond(response.OperationId, authority.UserId, response.ExpectedNoticeRevision, cipher, now); }
                        finally { CryptographicOperations.ZeroMemory(cipher); }
                    }
                    finally { CryptographicOperations.ZeroMemory(bytes); }
                    return (notice.Id, notice.Revision);
                }
            case WithdrawModerationPreFindingCustody withdrawal:
                {
                    var custody = await context.Set<ModerationPreFindingCustody>().SingleAsync(row => row.Id == withdrawal.CustodyId, token);
                    custody.Withdraw(authority.UserId, withdrawal.ExpectedCustodyRevision, now);
                    // Do not load expired private ciphertext just to delete it.
                    await context.Set<ModerationPreFindingNotice>().Where(row => row.CustodyId == custody.Id && row.PurgedAt == null)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(row => EF.Property<byte[]>(row, "protectedNotice"), Array.Empty<byte>())
                            .SetProperty(row => EF.Property<byte[]>(row, "protectedResponse"), Array.Empty<byte>())
                            .SetProperty(row => row.PurgedAt, (DateTimeOffset?)now)
                            .SetProperty(row => row.Withdrawn, true)
                            .SetProperty(row => row.Revision, row => row.Revision + 1), token);
                    await ModerationPreFindingNotificationCleanup.StageAsync(
                        context, authority.Tenant.Id, custody.Id, null, token);
                    return (custody.Id, custody.Revision);
                }
            default:
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
    }

    private async Task RequirePreFindingOperationAsync(ModerationPreFindingOperation operation,
        ModerationSourceAuthority authority, bool replay, CancellationToken token)
    {
        if (operation is WithdrawModerationPreFindingCustody withdrawal)
        {
            var custody = await context.Set<ModerationPreFindingCustody>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == withdrawal.CustodyId, token);
            if (custody is null || custody.CaseId != operation.CaseId || custody.HostBindingId != authority.Binding.Id
                || custody.PrivacyOwnerHumanId != authority.UserId || !authority.Administrators.Contains(authority.UserId)
                || !replay && custody.Revision != withdrawal.ExpectedCustodyRevision
                || replay && custody.WithdrawnAt is null)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
            return;
        }
        var owner = await ReadCaseAsync(operation.CaseId, authority.Tenant,
            operation.ExpectedCaseRevision, authority, token);
        if (owner.AttributionSourceId is null || owner.OccurrenceCaseId is null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var attribution = await ReadCurrentCaseAttributionAsync(owner, authority, token);
        BoundPreFinding(attribution.ValidUntil);
        switch (operation)
        {
            case ProposeModerationPreFindingCustody proposal:
                proposal.Terms.Validate();
                BoundPreFinding(proposal.ValidUntil);
                if (proposal.ValidUntil <= clock.GetUtcNow() || proposal.ValidUntil > attribution.ValidUntil
                    || !authority.Administrators.Contains(proposal.PrivacyOwnerHumanId))
                    throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                await RequirePreFindingControllersAsync(owner, authority.UserId, proposal.PrivacyOwnerHumanId, authority, token);
                if (replay)
                {
                    var proposed = await context.Set<ModerationPreFindingCustody>().AsNoTracking()
                        .SingleOrDefaultAsync(row => row.Id == proposal.OperationId, token);
                    if (proposed is null || proposed.CaseId != owner.Id || proposed.CaseRevision != owner.Revision
                        || proposed.AttributionSourceId != attribution.Id
                        || proposed.AttributionGeneration != attribution.Generation
                        || proposed.EvidenceRevision != attribution.EvidenceCaseRevision || proposed.Terms != proposal.Terms
                        || proposed.ProposedByHumanId != authority.UserId || proposed.PrivacyOwnerHumanId != proposal.PrivacyOwnerHumanId
                        || proposed.ValidUntil != proposal.ValidUntil || proposed.WithdrawnAt is not null)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                }
                break;
            case AcceptModerationPreFindingCustody acceptance:
                {
                    var custody = await ReadPreFindingCustodyAsync(acceptance.CustodyId, authority, owner, token, requireAccepted: replay);
                    if (custody.PrivacyOwnerHumanId != authority.UserId
                        || !replay && custody.Revision != acceptance.ExpectedCustodyRevision)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    break;
                }
            case IssueModerationPreFindingNotice issue:
                {
                    if (await context.Set<ModerationPreFindingErasureTombstone>().AsNoTracking()
                        .AnyAsync(row => row.CaseId == owner.Id, token))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    if (!replay && await context.Set<ModerationPreFindingNotice>().AsNoTracking()
                        .CountAsync(row => row.CaseId == owner.Id && row.HostBindingId == authority.Binding.Id
                            && row.TenantId == authority.Tenant.Id, token) >= ModerationQualificationAuthority.MaximumPreFindingNotices)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    var custody = await ReadPreFindingCustodyAsync(issue.CustodyId, authority, owner, token);
                    if (custody.ProposedByHumanId != authority.UserId || issue.Text is null
                        || issue.Text.Allegation is null || issue.Text.ApplicableRule is null || issue.Text.ResponseInstructions is null
                        || issue.RetainUntil <= clock.GetUtcNow() || issue.RetainUntil > custody.ValidUntil
                        || !replay && issue.RetainUntil - clock.GetUtcNow() > custody.Terms.MaximumRetention)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    if (issue.Recipient.HumanId == custody.PrivacyOwnerHumanId)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    BoundPreFinding(issue.RetainUntil);
                    await RequirePreFindingRecipientAsync(owner, issue.Recipient, authority, issue.RetainUntil, token);
                    if (replay)
                    {
                        var notice = await ReadPreFindingNoticeAsync(issue.OperationId, authority, owner, token);
                        await RequirePreFindingNotificationGraphAsync(notice, token);
                        if (notice.CustodyId != issue.CustodyId || notice.IssuedByHumanId != authority.UserId
                            || notice.RetainUntil != issue.RetainUntil
                            || notice.RecipientHumanId != issue.Recipient.HumanId
                            || notice.RecipientAssignmentId != issue.Recipient.AssignmentId
                            || notice.RecipientAssignmentRevision != issue.Recipient.AssignmentRevision
                            || !PreFindingPayloadMatches(notice, JsonSerializer.SerializeToUtf8Bytes(issue.Text), "notice"))
                            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    }
                    break;
                }
            case AcknowledgeModerationPreFindingNotice acknowledgement:
                {
                    var notice = await ReadPreFindingNoticeAsync(acknowledgement.NoticeId, authority, owner, token);
                    if (notice.RecipientHumanId != authority.UserId
                        || !replay && notice.Revision != acknowledgement.ExpectedNoticeRevision
                        || replay && notice.AcknowledgedAt is null)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    break;
                }
            case RespondToModerationPreFindingNotice response:
                {
                    var notice = await ReadPreFindingNoticeAsync(response.NoticeId, authority, owner, token);
                    var custody = await ReadPreFindingCustodyAsync(notice.CustodyId, authority, owner, token);
                    if (notice.RecipientHumanId != authority.UserId || string.IsNullOrWhiteSpace(response.Statement)
                        || !replay && notice.Revision != response.ExpectedNoticeRevision
                        || replay && notice.ResponseId != response.OperationId)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    RequirePreFindingSize(Encoding.UTF8.GetByteCount(response.Statement), custody.MaximumTextBytes);
                    if (replay && !PreFindingPayloadMatches(notice, Encoding.UTF8.GetBytes(response.Statement), "response"))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    break;
                }
            default:
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
    }

    private async Task<ModerationPreFindingCustody> ReadPreFindingCustodyAsync(Guid id,
        ModerationSourceAuthority authority, ModerationCase owner, CancellationToken token, bool requireAccepted = true)
    {
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationPreFindingCustody>(context, id, token);
        var custody = await context.Set<ModerationPreFindingCustody>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id && row.CaseId == owner.Id
                && row.HostBindingId == authority.Binding.Id && row.TenantId == authority.Tenant.Id, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        custody.ValidatePersistedState();
        BoundPreFinding(custody.ValidUntil);
        var attribution = await ReadCurrentCaseAttributionAsync(owner, authority, token);
        BoundPreFinding(attribution.ValidUntil);
        if (custody.AttributionSourceId != attribution.Id || custody.AttributionGeneration != attribution.Generation
            || custody.EvidenceRevision != attribution.EvidenceCaseRevision || custody.CaseRevision != owner.Revision
            || custody.WithdrawnAt is not null || clock.GetUtcNow() < custody.ProposedAt
            || clock.GetUtcNow() >= custody.ValidUntil || requireAccepted && !custody.IsCurrentAt(clock.GetUtcNow())
            || !authority.Administrators.Contains(custody.PrivacyOwnerHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RequirePreFindingControllersAsync(owner, custody.ProposedByHumanId, custody.PrivacyOwnerHumanId, authority, token);
        var proposal = new ProposeModerationPreFindingCustody(custody.Id, custody.CaseId, custody.CaseRevision,
            custody.PrivacyOwnerHumanId, custody.Terms, custody.ValidUntil);
        await RequirePreFindingReceiptAsync(proposal, custody.Id, 1, custody.ProposedByHumanId,
            custody.ProposedAt, authority, token);
        if (custody.AcceptedAt is { } accepted)
        {
            var receipt = await context.Set<ModerationPreFindingReceipt>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.ResultId == custody.Id && row.ResultRevision == 2, token)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            await RequirePreFindingReceiptAsync(new AcceptModerationPreFindingCustody(
                receipt.Id, custody.CaseId, custody.CaseRevision, custody.Id, 1), custody.Id, 2,
                custody.PrivacyOwnerHumanId, accepted, authority, token);
        }
        return custody;
    }

    private async Task<ModerationReviewerAssignment> RequirePreFindingReviewerAsync(
        ModerationCase owner, Guid human, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (!authority.Administrators.Contains(human))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var id = await context.ModerationReviewerAssignments.AsNoTracking()
            .Where(row => row.CaseId == owner.Id && row.ReviewerHumanId == human && row.WithdrawnAt == null)
            .Select(row => row.Id).SingleOrDefaultAsync(token);
        await ReadReviewerAsync(id, owner.Scope, null, authority, token);
        var reviewer = await context.ModerationReviewerAssignments.AsNoTracking().SingleAsync(row => row.Id == id, token);
        reviewer.ValidatePersistedState();
        var now = clock.GetUtcNow();
        if (!reviewer.IsQualifiedAt(now) || !authority.FencedHumans.Contains(reviewer.ReviewerHumanId)
            || reviewer.ControllerHumanId is not { } controller || !authority.FencedHumans.Contains(controller)
            || reviewer.QualifiedByHumanId is not { } qualifier || !authority.FencedHumans.Contains(qualifier)
            || reviewer.AssessedByHumanId is not { } assessor || !authority.FencedHumans.Contains(assessor))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        if (await context.ModerationCases.AsNoTracking().AnyAsync(row =>
            row.OccurrenceCaseId == owner.OccurrenceCaseId && row.SubjectKind == ModerationSubjectKind.Human
            && (row.SubjectId == human || row.SubjectId == reviewer.ControllerHumanId), token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var policy = await RequireQualificationPolicyAsync(owner, authority, now, token);
        BoundPreFinding(reviewer.QualifiedUntil!.Value);
        BoundPreFinding(policy.Current.ValidUntil);
        await RequireReviewerEvidenceAsync(reviewer, authority, policy.Current.MaximumReviewerLifetime, now, token);
        if (owner.SubjectKind == ModerationSubjectKind.Content)
        {
            var participants = new[] { reviewer.ReviewerHumanId, reviewer.ControllerHumanId!.Value };
            var roles = await context.EventRoleAssignments.AsNoTracking()
                .Where(row => row.EventId == owner.SubjectId && row.TenantId == authority.Tenant.Id
                    && participants.Contains(row.UserId) && row.RevokedAtUtc == null
                    && (row.RoleId == (int)RoleEnum.EventOwner || row.RoleId == (int)RoleEnum.EventManager))
                .ToArrayAsync(token);
            foreach (var role in roles.OrderBy(row => row.Id))
                await RelationalEntityRowFence.AcquireAsync<EventRoleAssignment>(
                    context, authority.Tenant.Id, row => row.Id, role.Id, token);
            if (roles.Any(row => row.IsEffectiveAt(now.UtcDateTime))
                || await context.Events.AsNoTracking().AnyAsync(row => row.Id == owner.SubjectId
                    && row.TenantId == authority.Tenant.Id && row.SubmittedByUserId != null
                    && participants.Contains(row.SubmittedByUserId.Value), token))
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
        return reviewer;
    }

    private async Task RequirePreFindingControllersAsync(ModerationCase owner, Guid proposer, Guid privacyOwner,
        ModerationSourceAuthority authority, CancellationToken token)
    {
        var first = await RequirePreFindingReviewerAsync(owner, proposer, authority, token);
        var second = await RequirePreFindingReviewerAsync(owner, privacyOwner, authority, token);
        var participants = new[] { proposer, privacyOwner, first.ControllerHumanId!.Value, second.ControllerHumanId!.Value };
        if (!first.IsIndependentOf(second, clock.GetUtcNow())
            || await context.ModerationCases.AsNoTracking().AnyAsync(row =>
                row.OccurrenceCaseId == owner.OccurrenceCaseId && row.SubjectKind == ModerationSubjectKind.Human
                && participants.Contains(row.SubjectId!.Value), token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
    }

    private async Task RequirePreFindingRecipientAsync(ModerationCase owner, ModerationNoticeContentRepresentative recipient,
        ModerationSourceAuthority authority, DateTimeOffset until, CancellationToken token)
    {
        if (!authority.FencedHumans.Contains(recipient.HumanId)
            || await privacy.IsSubjectFencedAsync(recipient.HumanId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (owner.SubjectKind == ModerationSubjectKind.Human)
        {
            if (recipient.HumanId != owner.SubjectId || recipient.AssignmentId != Guid.Empty || recipient.AssignmentRevision != 0)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
            return;
        }
        if (owner.SubjectKind != ModerationSubjectKind.Content
            || await context.ModerationCases.AsNoTracking().AnyAsync(row =>
                row.OccurrenceCaseId == owner.OccurrenceCaseId && row.SubjectKind == ModerationSubjectKind.Human
                && row.SubjectId == recipient.HumanId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        if (authority.PreFinding is not { RepresentativeAuthority: { } plan } original
            || !original.Matches(owner, recipient))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        // Only this originally nominated human's assignments belong to this recipient proof.
        var recipientPlan = new Explore.Persistence.Database.ProviderPrimitives.EventAuthorityProviderOperations.AuthorityPlan(
            plan.Parents.Where(row => row.Id == owner.SubjectId).ToList(),
            plan.Assignments.Where(row => row.EventId == owner.SubjectId && row.UserId == recipient.HumanId).ToList());
        var qualifiedUntil = await ModerationNoticeContentCustody.AcquireAsync(context, privacy, owner.Subject!, authority.Tenant,
            [recipient], authority.FencedHumans, clock.GetUtcNow(), until, token, recipientPlan);
        if (qualifiedUntil is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        BoundPreFinding(qualifiedUntil.Value);
    }

    private async Task<ModerationPreFindingNotice> ReadPreFindingNoticeAsync(Guid id,
        ModerationSourceAuthority authority, ModerationCase owner, CancellationToken token, bool requireReader = true)
    {
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationPreFindingNotice>(context, id, token);
        var metadata = await context.Set<ModerationPreFindingNotice>().AsNoTracking()
            .Where(row => row.Id == id && row.CaseId == owner.Id && row.TenantId == authority.Tenant.Id)
            .Select(row => new
            {
                row.CustodyId,
                row.IssuedAt,
                row.RetainUntil,
                row.PurgedAt,
                row.RecipientHumanId,
                row.RecipientAssignmentId,
                row.RecipientAssignmentRevision
            }).SingleOrDefaultAsync(token);
        var now = clock.GetUtcNow();
        if (metadata is null || metadata.PurgedAt is not null || now < metadata.IssuedAt || now >= metadata.RetainUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        BoundPreFinding(metadata.RetainUntil);
        await ReadPreFindingCustodyAsync(metadata.CustodyId, authority, owner, token);
        await RequirePreFindingRecipientAsync(owner, new(metadata.RecipientAssignmentId,
            metadata.RecipientAssignmentRevision, metadata.RecipientHumanId), authority, metadata.RetainUntil, token);
        var notice = await context.Set<ModerationPreFindingNotice>().AsNoTracking().SingleAsync(row =>
            row.Id == id && row.PurgedAt == null && row.RetainUntil == metadata.RetainUntil, token);
        notice.ValidatePersistedState();
        var reviewer = await RequirePreFindingReviewerAsync(owner, notice.IssuedByHumanId, authority, token);
        var attribution = await ReadCurrentCaseAttributionAsync(owner, authority, token);
        if (notice.AttributionSourceId != attribution.Id || notice.AttributionGeneration != attribution.Generation
            || notice.CaseRevision != owner.Revision
            || notice.OccurrenceCaseId != owner.OccurrenceCaseId || notice.EvidenceRevision != attribution.EvidenceCaseRevision
            || notice.SubjectKind != owner.SubjectKind || notice.SubjectId != owner.SubjectId
            || reviewer.Id != notice.ReviewerAssignmentId || reviewer.Revision != notice.ReviewerAssignmentRevision
            || clock.GetUtcNow() >= notice.RetainUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (requireReader && authority.UserId != notice.RecipientHumanId)
            await RequirePreFindingReviewerAsync(owner, authority.UserId, authority, token);
        var originalBytes = PreFindingProtector(notice.Id, notice.CaseId, notice.CustodyId,
            notice.AttributionSourceId, notice.RecipientHumanId, notice.RetainUntil, "notice")
            .Unprotect(notice.ProtectedNotice.ToArray());
        try
        {
            var originalText = JsonSerializer.Deserialize<ModerationPreFindingText>(originalBytes)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            var original = new IssueModerationPreFindingNotice(notice.Id, notice.CaseId, notice.CaseRevision,
                notice.CustodyId, new(notice.RecipientAssignmentId, notice.RecipientAssignmentRevision, notice.RecipientHumanId),
                originalText, notice.RetainUntil);
            await RequirePreFindingReceiptAsync(original, notice.Id, 1, notice.IssuedByHumanId, notice.IssuedAt, authority, token);
        }
        finally { CryptographicOperations.ZeroMemory(originalBytes); }
        return notice;
    }

    private async Task RequirePreFindingReceiptAsync(ModerationPreFindingOperation operation,
        Guid resultId, long resultRevision, Guid actor, DateTimeOffset capturedAt,
        ModerationSourceAuthority authority, CancellationToken token)
    {
        var receipt = await context.Set<ModerationPreFindingReceipt>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == operation.OperationId, token);
        if (receipt is null || receipt.ResultId != resultId || receipt.ResultRevision != resultRevision
            || receipt.ActorHumanId != actor || receipt.CaseId != operation.CaseId
            || receipt.HostBindingId != authority.Binding.Id || receipt.TrustDomainId != authority.Binding.TrustDomainId
            || receipt.TenantId != authority.Tenant.Id || receipt.CompletedAt < capturedAt
            || receipt.CompletedAt > clock.GetUtcNow() || receipt.RequestDigest != PreFindingDigest(operation))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }

    private async Task RequirePreFindingTerminalAsync(ModerationSourceAuthority authority,
        IDbContextTransaction transaction, CancellationToken token,
        ModerationAuthorityParticipant.FindingProvenanceSession? provenance = null)
    {
        if (provenance is null
                ? machine.IsMachineCaller || await admin.ResolveUserIdAsync(token) != authority.UserId
                : await provenance.AcquireAsync(context, provenance.CaseId, token) != authority)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (!ReferenceEquals(context.Database.CurrentTransaction, transaction) || context.ChangeTracker.HasChanges()
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.ModerationTrustDomainId != authority.Binding.TrustDomainId
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId != authority.Tenant.Id
            || !await new ModerationQualificationAuthority(context, admin, machine)
                .ProviderTenantMatchesAsync(authority.Tenant.Id, token)
            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row =>
                row.Id == authority.Epoch.Id && row.Revision == authority.Epoch.Revision, token)
            || !await context.ModerationHostBindings.AsNoTracking().AnyAsync(row =>
                row.Id == authority.Binding.Id && row.TrustDomainId == authority.Binding.TrustDomainId
                && row.InstanceId == authority.Binding.InstanceId && row.SolutionId == authority.Binding.SolutionId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        foreach (var human in authority.FencedHumans)
            if (await privacy.IsSubjectFencedAsync(human, token)
                || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == human && !row.IsDeleted, token))
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        foreach (var administrator in authority.Administrators)
            if (!await context.PlatformUserRoles.AsNoTracking().AnyAsync(row => row.UserId == administrator
                && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin", token)
                && !await context.TenantUserRoleGrants.AsNoTracking().AnyAsync(row =>
                    row.TenantId == authority.Tenant.Id && row.TenantUser.UserId == administrator
                    && row.RevokedAt == null && row.RoleScopeId == (int)RoleScopeEnum.Tenant
                    && row.RoleId == (int)RoleEnum.TenantAdmin && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant
                    && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active && !row.TenantUser.IsDeleted, token))
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RequireCurrentCaseAttributionAsync(token);
    }

    private IDataProtector PreFindingProtector(Guid notice, Guid caseId, Guid custody, Guid attribution,
        Guid recipient, DateTimeOffset until, string purpose) =>
        responseProtection!.CreateProtector("ISLAMU.Moderation.PreFinding.v1", purpose,
            FormattableString.Invariant($"{notice:D}/{caseId:D}/{custody:D}/{attribution:D}/{recipient:D}/{until:O}"));

    private static string PreFindingDigest(ModerationPreFindingOperation operation)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(operation, operation.GetType());
        try { return Convert.ToHexStringLower(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private bool PreFindingPayloadMatches(ModerationPreFindingNotice notice, byte[] expected, string purpose)
    {
        byte[]? actual = null;
        try
        {
            actual = PreFindingProtector(notice.Id, notice.CaseId, notice.CustodyId, notice.AttributionSourceId,
                notice.RecipientHumanId, notice.RetainUntil, purpose)
                .Unprotect((purpose == "notice" ? notice.ProtectedNotice : notice.ProtectedResponse).ToArray());
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            if (actual is not null) CryptographicOperations.ZeroMemory(actual);
        }
    }

    private DateTimeOffset PreFindingTime()
    {
        var now = clock.GetUtcNow();
        return new(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    }

    private void BoundPreFinding(DateTimeOffset until)
    {
        if (until < _preFindingUntil) _preFindingUntil = until;
    }

    private static void RequirePreFindingSize(int bytes, int maximum)
    {
        if (bytes is < 1 || bytes > maximum)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
    }
}
