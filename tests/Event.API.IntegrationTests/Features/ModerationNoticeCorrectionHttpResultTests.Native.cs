using System.Data;
using System.Data.Common;
using System.IO.Pipelines;
using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Features.Moderation.Notices;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    [Test]
    [Arguments(false, "none")]
    [Arguments(true, "none")]
    [Arguments(false, "request")]
    [Arguments(true, "query")]
    public async Task Correction_http_completion_retains_native_authority_until_the_terminal_consumer_returns(
        bool colocated, string cancellationSource)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var storageGuard = new CorrectionHttpStorageGuard();
        await using var reader = fixture.Database.Context(storageGuard);
        await using var colocatedPrivacy = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(reader) : null;
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture, colocatedPrivacy);
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);
        var privacy = (colocatedPrivacy ?? fixture.Privacy).Authority(reader, fixture.Clock);
        IModerationNoticeCorrectionDisclosure disclosure = CorrectionDisclosureReader(
            reader, fixture, fixture.Database.ReviewerId, privacy);
        var accessor = CorrectionHttpIdentity(fixture.Database.ReviewerId);
        using var queryCancellation = new CancellationTokenSource();
        using var requestCancellation = new CancellationTokenSource();
        var lifetime = new CorrectionHttpLifetime { RequestAborted = requestCancellation.Token };
        var http = accessor.HttpContext!;
        http.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        var completion = new CorrectionHttpCompletion(reader, async () =>
            !await privacy.IsSubjectFencedAsync(fixture.Database.ReviewerId, default));
        await using var body = completion.Body;
        http.Features.Set<IHttpResponseBodyFeature>(completion);
        var handler = new ReadModerationNoticeCorrectionQueryHandler(disclosure, new UserContext(accessor));
        storageGuard.Guarded = true;
        var prepared = await handler.QueryAsync(
            CorrectionHttpQuery(CorrectionPreparationRequest(pending, grant)), queryCancellation.Token);
        storageGuard.Guarded = false;

        await Assert.That(storageGuard.Observed).IsFalse();
        await Assert.That(prepared.IsValid).IsTrue();
        await Assert.That(prepared.AuthenticationRequired).IsFalse();
        await Assert.That(reader.Database.CurrentTransaction).IsNull();
        await Assert.That(reader.ModerationTrustDomainId).IsNull();
        await Assert.That(body.Length).IsEqualTo(0L);

        var running = new ModerationNoticeCorrectionHttpResult(prepared)
            .ExecuteResultAsync(new() { HttpContext = http });
        Exception? observed = null;
        long written = 0;
        try
        {
            await completion.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            written = body.Length;
            await Assert.That(written).IsGreaterThan(0L);
            await AssertCorrectionExplanationAsync(body.ToArray(), pending.Request.Intent);
            await Assert.That(body.TransactionAtWrite.HasValue).IsTrue();
            await Assert.That(completion.TransactionAtEntry).IsEqualTo(body.TransactionAtWrite);
            await Assert.That(reader.Database.CurrentTransaction!.TransactionId).IsEqualTo(body.TransactionAtWrite!.Value);
            await Assert.That(reader.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel)
                .IsEqualTo(IsolationLevel.Serializable);
            await Assert.That(reader.ModerationTrustDomainId).IsEqualTo(fixture.Database.Scope.TrustDomainId);
            await Assert.That(reader.IdentityFenceOwnsTransaction).IsEqualTo(colocated);
            await Assert.That(completion.PrivacyGateObserved).IsTrue();
            await Assert.That(running.IsCompleted).IsFalse();
            await AssertCorrectionHttpHeadersAsync(http);

            if (cancellationSource != "none")
            {
                if (cancellationSource == "query") queryCancellation.Cancel();
                else requestCancellation.Cancel();
                await Assert.That(lifetime.Aborted).IsTrue();
                await Assert.That(running.IsCompleted).IsFalse();
                await Assert.That(reader.Database.CurrentTransaction!.TransactionId).IsEqualTo(body.TransactionAtWrite.Value);
                await Assert.That(reader.ModerationTrustDomainId).IsEqualTo(fixture.Database.Scope.TrustDomainId);
                await Assert.That(reader.IdentityFenceOwnsTransaction).IsEqualTo(colocated);
            }
        }
        finally
        {
            completion.Release.TrySetResult();
            try { await running.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException exception) when (cancellationSource != "none") { observed = exception; }
        }

        await Assert.That(observed is OperationCanceledException).IsEqualTo(cancellationSource != "none");
        await Assert.That(lifetime.Aborted).IsEqualTo(cancellationSource != "none");
        await Assert.That(completion.TransactionAtExit).IsEqualTo(body.TransactionAtWrite);
        await Assert.That(completion.TrustDomainAtExit).IsEqualTo(fixture.Database.Scope.TrustDomainId);
        await Assert.That(completion.IdentityFenceAtExit).IsEqualTo(colocated);
        await Assert.That(completion.Calls).IsEqualTo(1);
        await Assert.That(body.Writes).IsEqualTo(1);
        await Assert.That(body.Length).IsEqualTo(written);
        await Assert.That(http.Response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(http.Response.ContentLength).IsEqualTo(written);
        await AssertCorrectionExplanationAsync(body.ToArray(), pending.Request.Intent);
        await AssertCorrectionDisclosureBufferClearedAsync(body.Borrowed);
        await AssertCorrectionDisclosureReleasedAsync(reader, privacy, fixture.Database.ReviewerId);
        var replayWrites = 0;
        storageGuard.Guarded = true;
        try
        {
            await prepared.DiscloseAsync((_, _) =>
            {
                replayWrites++;
                return Task.CompletedTask;
            });
        }
        catch (InvalidOperationException) { }
        storageGuard.Guarded = false;
        await Assert.That(storageGuard.Observed).IsFalse();
        await Assert.That(replayWrites).IsEqualTo(0);
        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    [Test]
    [Arguments("replacement")]
    [Arguments("absentGrant")]
    [Arguments("expiredText")]
    [Arguments("otherOriginal")]
    public async Task Correction_http_pending_result_cannot_substitute_current_assignment_for_original_personal_authority(
        string loss)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture, issueGrant: loss != "absentGrant");
        var caller = fixture.Database.ReviewerId;
        if (loss == "replacement")
        {
            caller = fixture.Database.UrgencyAssessorId;
            grant = grant with { OperationId = Guid.CreateVersion7(), GranteeUserId = caller };
            await IssueCorrectionPreparationGrantAsync(fixture, grant);
        }
        else if (loss == "expiredText") fixture.Clock.UtcNow = pending.Request.Intent.ValidUntil;
        var coordinates = CorrectionPreparationRequest(pending, grant);
        if (loss == "otherOriginal") coordinates = coordinates with { OriginalNoticeId = pending.Original.SubjectNotice.Id };
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);
        await using var reader = fixture.Database.Context();
        var privacy = fixture.Privacy.Authority(reader, fixture.Clock);
        IModerationNoticeCorrectionDisclosure disclosure = CorrectionDisclosureReader(reader, fixture, caller, privacy);
        var handler = new ReadModerationNoticeCorrectionQueryHandler(disclosure, new UserContext(CorrectionHttpIdentity(caller)));
        var prepared = await handler.QueryAsync(CorrectionHttpQuery(coordinates));
        var writes = 0;

        await Assert.That(prepared.IsValid).IsTrue();
        await Assert.That(reader.Database.CurrentTransaction).IsNull();
        await Assert.That(await prepared.DiscloseAsync((_, _) =>
        {
            writes++;
            return Task.CompletedTask;
        })).IsFalse();

        await Assert.That(writes).IsEqualTo(0);
        await AssertCorrectionDisclosureReleasedAsync(reader, privacy, caller);
        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    private static ReadModerationNoticeCorrectionQuery CorrectionHttpQuery(
        ModerationNoticeCorrectionPreparationRequest request) => new()
        {
            PendingEffectId = request.PendingEffectId,
            ExpectedPendingSnapshotHash = request.ExpectedPendingSnapshotHash,
            ExpectedPendingEffectBindingHash = request.ExpectedPendingEffectBindingHash,
            ApprovalId = request.ApprovalId,
            GrantId = request.GrantId,
            CaseId = request.CaseId,
            ExpectedCaseRevision = request.ExpectedCaseRevision,
            OriginalNoticeId = request.OriginalNoticeId
        };

    private static HttpContextAccessor CorrectionHttpIdentity(Guid caller) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, caller.ToString())], "correction-http-native-witness"))
        }
    };

    private static async Task AssertCorrectionHttpHeadersAsync(HttpContext http)
    {
        await Assert.That(http.Response.ContentType).IsEqualTo("application/json; charset=utf-8");
        await Assert.That(http.Response.Headers.CacheControl.ToString()).IsEqualTo("private, no-store");
        await Assert.That(http.Response.Headers["Referrer-Policy"].ToString()).IsEqualTo("no-referrer");
        await Assert.That(http.Response.Headers["X-Content-Type-Options"].ToString()).IsEqualTo("nosniff");
        await Assert.That(http.Response.Headers.ContainsKey("ETag")).IsFalse();
    }

    private sealed class CorrectionHttpBody(ExploreDbContext context) : MemoryStream
    {
        public Guid? TransactionAtWrite { get; private set; }
        public ReadOnlyMemory<byte> Borrowed { get; private set; }
        public int Writes { get; private set; }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Writes++;
            Borrowed = buffer;
            TransactionAtWrite = context.Database.CurrentTransaction?.TransactionId;
            return base.WriteAsync(buffer, cancellationToken);
        }
    }

    private sealed class CorrectionHttpCompletion(ExploreDbContext context, Func<Task<bool>> observePrivacyGate)
        : IHttpResponseBodyFeature
    {
        public CorrectionHttpBody Body { get; } = new(context);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid? TransactionAtEntry { get; private set; }
        public Guid? TransactionAtExit { get; private set; }
        public Guid? TrustDomainAtExit { get; private set; }
        public bool IdentityFenceAtExit { get; private set; }
        public bool PrivacyGateObserved { get; private set; }
        public int Calls { get; private set; }
        private StreamResponseBodyFeature? _bodyFeature;
        private StreamResponseBodyFeature BodyFeature => _bodyFeature ??= new(Body);
        public Stream Stream => Body;
        public PipeWriter Writer => BodyFeature.Writer;
        public void DisableBuffering() => BodyFeature.DisableBuffering();
        public Task StartAsync(CancellationToken cancellationToken = default) => BodyFeature.StartAsync(cancellationToken);
        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default) =>
            BodyFeature.SendFileAsync(path, offset, count, cancellationToken);

        public async Task CompleteAsync()
        {
            Calls++;
            TransactionAtEntry = context.Database.CurrentTransaction?.TransactionId;
            PrivacyGateObserved = await observePrivacyGate();
            Entered.TrySetResult();
            await Release.Task;
            TransactionAtExit = context.Database.CurrentTransaction?.TransactionId;
            TrustDomainAtExit = context.ModerationTrustDomainId;
            IdentityFenceAtExit = context.IdentityFenceOwnsTransaction;
            await BodyFeature.CompleteAsync();
        }
    }

    private sealed class CorrectionHttpLifetime : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; }
        public bool Aborted { get; private set; }
        public void Abort() => Aborted = true;
    }

    private sealed class CorrectionHttpStorageGuard : DbCommandInterceptor
    {
        public bool Guarded { get; set; }
        public bool Observed { get; private set; }

        public override InterceptionResult<DbCommand> CommandCreating(
            CommandCorrelatedEventData eventData, InterceptionResult<DbCommand> result)
        {
            if (!Guarded) return result;
            Observed = true;
            throw new InvalidOperationException("Preparing or reusing a consumed correction result must not access native storage.");
        }
    }
}
