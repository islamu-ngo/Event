using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository : IModerationContentResponseRepository
{
    private IDataProtector ContentResponseProtector(
        Guid responseId, Guid hostId, Guid tenantId, Guid effectId, Guid noticeId, Guid caseId,
        Guid contentId, Guid representativeId, Guid assignmentId, long assignmentRevision,
        string snapshotHash, DateTimeOffset retainUntil) =>
        responseProtection!.CreateProtector("ISLAMU.Moderation.OriginalContentResponse.v1",
            FormattableString.Invariant(
                $"{responseId:D}/{hostId:D}/{tenantId:D}/{effectId:D}/{noticeId:D}/{caseId:D}/{contentId:D}/{representativeId:D}/{assignmentId:D}/{assignmentRevision}/{snapshotHash}/{retainUntil:O}"));

    public async Task<ModerationContentResponseWriteResult> SubmitContentResponseAsync(
        ModerationContentResponseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ContentResponseEnvironment(request.TenantId) || responseProtection is null)
            return new(ModerationQualificationWriteOutcome.Unavailable);
        if (request.NoticeId == Guid.Empty || request.OperationId == Guid.Empty
            || request.OperationId.Version != 7 || request.OperationId.Variant is < 8 or > 11
            || request.AssignmentId == Guid.Empty || request.AssignmentRevision is < 1 or long.MaxValue
            || request.SnapshotHash is not { Length: 64 } || !request.SnapshotHash.All(char.IsAsciiHexDigitLower)
            || string.IsNullOrWhiteSpace(request.Statement)
            || Encoding.UTF8.GetByteCount(request.Statement) > ModerationContentResponse.MaximumStatementBytes)
            return new(ModerationQualificationWriteOutcome.Denied);
        Func<CancellationToken, Task> beforeCommit = _ => Task.CompletedTask;
        try
        {
            // Selected retained privacy authority is acquired before the primary transaction.
            // Embedded authority can adopt that transaction and perform its own final flush.
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(() =>
                privacy.ExecuteEnrollmentSerializedAsync(async token =>
                {
                    beforeCommit = _ => Task.CompletedTask;
                    context.ChangeTracker.Clear();
                    context.ModerationTrustDomainId = null;
                    if (context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    await using IDbContextTransaction? owned = context.IdentityFenceOwnsTransaction ? null
                        : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                    var transaction = context.Database.CurrentTransaction
                        ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    var proof = await ReadContentResponseProofAsync(request.TenantId, request.NoticeId, null, token)
                        ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                    if (proof.Availability.SnapshotHash != request.SnapshotHash
                        || proof.Representative.AssignmentId != request.AssignmentId
                        || proof.Representative.AssignmentRevision != request.AssignmentRevision)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    var digestBytes = JsonSerializer.SerializeToUtf8Bytes(request);
                    string digest;
                    try { digest = Convert.ToHexStringLower(SHA256.HashData(digestBytes)); }
                    finally { CryptographicOperations.ZeroMemory(digestBytes); }
                    var response = await context.Set<ModerationContentResponse>().AsNoTracking()
                        .SingleOrDefaultAsync(row => row.Id == request.OperationId, token);
                    if (response is not null)
                    {
                        response.ValidatePersistedState();
                        if (!ResponseMatchesProof(response, proof) || response.RequestDigest != digest)
                            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    }
                    else
                    {
                        var plaintext = Encoding.UTF8.GetBytes(request.Statement);
                        try
                        {
                            // Row-specific purpose prevents ciphertext transplantation. No plaintext
                            // enters finding evidence, outbox, generic idempotency storage or telemetry.
                            var cipher = ContentResponseProtector(request.OperationId, proof.Authority.Binding.Id,
                                request.TenantId, proof.Source.EffectId, proof.Notice.Id, proof.Approval.CaseId,
                                proof.Source.SubjectId, proof.Authority.UserId, request.AssignmentId,
                                request.AssignmentRevision, request.SnapshotHash, proof.Source.RetainUntil!.Value)
                                .Protect(plaintext);
                            try
                            {
                                var now = clock.GetUtcNow();
                                now = new(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
                                response = ModerationContentResponse.Capture(request.OperationId, proof.Notice,
                                    proof.Source, proof.Approval, proof.Representative, digest, cipher, now);
                                context.Add(response);
                            }
                            finally { CryptographicOperations.ZeroMemory(cipher); }
                        }
                        finally { CryptographicOperations.ZeroMemory(plaintext); }
                        await context.SaveChangesAsync(token);
                    }
                    var accepted = response;
                    beforeCommit = finalToken => RequireContentResponseCommitAsync(
                        request, proof, accepted, transaction, finalToken);
                    await beforeCommit(token);
                    if (owned is not null) await owned.CommitAsync(token);
                    return new ModerationContentResponseWriteResult(ModerationQualificationWriteOutcome.Known, accepted);
                }, token => beforeCommit(token), cancellationToken));
        }
        catch (AbortQualification aborted) { return new(aborted.Outcome); }
        catch (DbUpdateConcurrencyException) { return new(ModerationQualificationWriteOutcome.Conflict); }
        catch (DbUpdateException exception) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(exception))
        { return new(ModerationQualificationWriteOutcome.Conflict); }
        catch (DbUpdateException exception) when (exception.InnerException is DbException)
        { return new(ModerationQualificationWriteOutcome.Unavailable); }
        catch (DbException) { return new(ModerationQualificationWriteOutcome.Unavailable); }
        catch (CryptographicException) { return new(ModerationQualificationWriteOutcome.Unavailable); }
        catch (ArgumentException) { return new(ModerationQualificationWriteOutcome.Unavailable); }
        catch (JsonException) { return new(ModerationQualificationWriteOutcome.Unavailable); }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }
    }

    public async Task<ModerationQualificationWriteOutcome> DiscloseContentResponseAvailabilityAsync(
        Guid tenantId, Guid noticeId,
        Func<ModerationContentResponseAvailability, CancellationToken, Task<ReadOnlyMemory<byte>>> prepare,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        if (!ContentResponseEnvironment(tenantId) || responseProtection is null || noticeId == Guid.Empty)
            return ModerationQualificationWriteOutcome.Unavailable;
        try
        {
            return await privacy.ExecuteSerializedAsync(async token =>
            {
                if (context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
                    return ModerationQualificationWriteOutcome.Unavailable;
                await using IDbContextTransaction? owned = context.IdentityFenceOwnsTransaction ? null
                    : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var transaction = context.Database.CurrentTransaction;
                var proof = await ReadContentResponseProofAsync(tenantId, noticeId, null, token);
                if (proof is null) return ModerationQualificationWriteOutcome.NotFound;
                var prepared = await prepare(proof.Availability, token);
                var current = await ReadContentResponseProofAsync(tenantId, noticeId, proof, token)
                    ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                if (!SameResponseAuthority(proof, current)
                    || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
                    || context.ChangeTracker.HasChanges() || clock.GetUtcNow() < proof.ObservedAt
                    || clock.GetUtcNow() >= current.Availability.QualifiedUntil)
                    throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                await write(prepared, token);
                return ModerationQualificationWriteOutcome.Known;
            }, cancellationToken);
        }
        catch (AbortQualification aborted) { return aborted.Outcome; }
        catch (DbUpdateConcurrencyException) { return ModerationQualificationWriteOutcome.Conflict; }
        catch (ArgumentException) { return ModerationQualificationWriteOutcome.Unavailable; }
        catch (JsonException) { return ModerationQualificationWriteOutcome.Unavailable; }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }
    }

    private bool ContentResponseEnvironment(Guid tenantId) =>
        tenantId != Guid.Empty && context.Database.IsRelational()
        && context.Database.CurrentTransaction is null && System.Transactions.Transaction.Current is null
        && !context.ChangeTracker.HasChanges() && context.ModerationTrustDomainId is null
        && !context.IsTenantFilterBypassed && context.TenantFilterTenantId == tenantId;

    private async Task RequireContentResponseCommitAsync(ModerationContentResponseRequest request,
        ContentResponseProof acquired, ModerationContentResponse expected,
        IDbContextTransaction transaction, CancellationToken token)
    {
        var current = await ReadContentResponseProofAsync(request.TenantId, request.NoticeId, acquired, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (!SameResponseAuthority(acquired, current)
            || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
            || context.ChangeTracker.HasChanges() || clock.GetUtcNow() < acquired.ObservedAt
            || clock.GetUtcNow() < expected.SubmittedAt || clock.GetUtcNow() >= current.Availability.QualifiedUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var retained = await context.Set<ModerationContentResponse>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == expected.Id, token);
        if (retained is null || !ResponseMatchesProof(retained, current)
            || retained.RequestDigest != expected.RequestDigest || retained.SubmittedAt != expected.SubmittedAt
            || !CryptographicOperations.FixedTimeEquals(retained.ProtectedStatement.Span, expected.ProtectedStatement.Span)
            || !await new ModerationQualificationAuthority(context, admin, machine)
                .ProviderTenantMatchesAsync(request.TenantId, token)
            || await admin.ResolveUserIdAsync(token) != acquired.Authority.UserId || machine.IsMachineCaller
            || context.ModerationTrustDomainId != acquired.Source.TrustDomainId
            || clock.GetUtcNow() < acquired.ObservedAt || clock.GetUtcNow() < expected.SubmittedAt
            || clock.GetUtcNow() >= current.Availability.QualifiedUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }

    private static bool ResponseMatchesProof(ModerationContentResponse response, ContentResponseProof proof) =>
        response.HostBindingId == proof.Authority.Binding.Id && response.TrustDomainId == proof.Source.TrustDomainId
        && response.TenantId == proof.Authority.Tenant.Id && response.NoticeId == proof.Notice.Id
        && response.EffectId == proof.Source.EffectId && response.CaseId == proof.Approval.CaseId
        && response.ContentSubjectId == proof.Source.SubjectId && response.RepresentativeHumanId == proof.Authority.UserId
        && response.AssignmentId == proof.Representative.AssignmentId
        && response.AssignmentRevision == proof.Representative.AssignmentRevision
        && response.SnapshotHash == proof.Source.SnapshotHash && response.RetainUntil == proof.Source.RetainUntil
        && response.SubmittedAt >= proof.Source.CapturedAt && response.SubmittedAt < response.RetainUntil;

    private static bool SameResponseAuthority(ContentResponseProof before, ContentResponseProof after) =>
        before.Availability == after.Availability && before.Authority.UserId == after.Authority.UserId
        && before.Authority.Binding.Id == after.Authority.Binding.Id
        && before.Authority.Tenant == after.Authority.Tenant
        && before.QualificationRevision == after.QualificationRevision;

    private sealed record ContentResponseProof(
        ModerationSourceAuthority Authority, long QualificationRevision, ModerationNotice Notice,
        ModerationNoticeSource Source, ModerationNoticeDisclosureApproval Approval,
        ModerationNoticeContentRepresentative Representative, ModerationContentResponseAvailability Availability,
        DateTimeOffset ObservedAt);
}
