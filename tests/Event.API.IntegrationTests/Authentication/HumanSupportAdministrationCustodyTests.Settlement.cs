using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.Exceptions;
using Explore.Application.Features.SupportAccess.Handlers.Commands;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSupportAdministrationCustodyTests
{
    [Test]
    [Arguments("list", false)]
    [Arguments("list", true)]
    [Arguments("audit", false)]
    [Arguments("audit", true)]
    public async Task DisclosureCannotBorrowOwnStopExceptionOrAnExtendedHorizon(string family, bool extend)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid sessionId = await Seed(fixture, session: true);
        var original = await ReadSession(fixture, sessionId);
        await using var request = new Request(fixture.Host.Services, sessionId);
        await request.Authenticate(await fixture.SignInAsync());
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new AfterBody(request.Services.GetRequiredService<ISerializableAttemptOwner>(), async token =>
        {
            if (!extend)
            {
                var session = await context.SupportAccessSessions.SingleAsync(row => row.Id == sessionId, token);
                session.Stop(fixture.Clock.GetUtcNow(), "Outside the original query");
                await context.SaveChangesAsync(token);
                entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            }
            else
            {
                request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    // A deliberately later finalizer attempts horizon substitution after
                    // this query's source reader. The original bound must still reject it.
                    entered.TrySetResult();
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(30), finalToken);
                    await context.SupportAccessSessions.Where(row => row.Id == sessionId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc,
                            original.ExpiresAtUtc.AddMinutes(20)), finalToken);
                    await context.SupportAccessSessions.AsNoTracking().SingleAsync(row => row.Id == sessionId, finalToken);
                    fixture.Clock.Advance(original.ExpiresAtUtc.AddTicks(1) - fixture.Clock.GetUtcNow());
                }, _ => { });
            }
        });
        Task operation = Invoke(request.Services, owner, family, sessionId);
        try { await Reach(entered.Task, operation); }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await operation);
        var stored = await ReadSession(fixture, sessionId);
        await Assert.That(stored.StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
        await Assert.That(stored.ExpiresAtUtc).IsEqualTo(original.ExpiresAtUtc);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SupportAccessAuditEvents.AsNoTracking().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LostPhysicalSettlementAcknowledgementIsNeverReportedAsRollbackKnown(bool commit)
    {
        var fault = new LostAcknowledgement(commit);
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: fault);
        Guid sessionId = await Seed(fixture, session: true);
        await using var request = new Request(fixture.Host.Services, sessionId);
        await request.Authenticate(await fixture.SignInAsync());
        fault.Owner = request.Services.GetRequiredService<ExploreDbContext>();
        var realOwner = request.Services.GetRequiredService<ISerializableAttemptOwner>();
        ISerializableAttemptOwner owner = commit ? realOwner : new AfterBody(realOwner,
            _ => throw new InvalidOperationException("Fail after both original support saves."));
        Task operation = ActivatorUtilities.CreateInstance<StopSupportAccessSessionCommandHandler>(request.Services, owner)
            .ExecuteAsync(new() { SessionId = sessionId }, default);
        await Reach(fault.Settled.Task, operation);
        await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(async () => await operation);
        var stored = await ReadSession(fixture, sessionId);
        await Assert.That(stored.StatusId).IsEqualTo((int)(commit
            ? SupportAccessSessionStatusEnum.Stopped : SupportAccessSessionStatusEnum.Active));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SupportAccessAuditEvents.AsNoTracking()
            .CountAsync(row => row.SupportAccessSessionId == sessionId)).IsEqualTo(commit ? 1 : 0);
    }

    [Test]
    public async Task AuthenticatedHttpOwnStopReachesTheTransactionalLifecycleOwner()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid sessionId = await Seed(fixture, session: true);
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/support-access/sessions/{sessionId:D}/stop")
        {
            Content = JsonContent.Create(new StopSupportAccessSessionRequestDto { EndReasonText = "HTTP finished" })
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await fixture.SignInAsync());
        message.Headers.Add(SupportAccessHeaderNames.SessionId, sessionId.ToString("D"));
        using var response = await fixture.Client.SendAsync(message);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var stored = await ReadSession(fixture, sessionId);
        await Assert.That(stored.StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Stopped);
        await Assert.That(stored.EndReasonText).IsEqualTo("HTTP finished");
        await using var observer = fixture.Native.CreateDatabase();
        var lifecycle = await observer.SupportAccessAuditEvents.AsNoTracking().SingleAsync(row =>
            row.SupportAccessSessionId == sessionId && row.EventTypeId == (int)SupportAccessAuditEventTypeEnum.Stopped);
        await Assert.That(lifecycle.ActorUserId).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(lifecycle.OccurredAtUtc).IsEqualTo(stored.EndedAtUtc!.Value);
    }

    private sealed class LostAcknowledgement(bool commit) : DbTransactionInterceptor
    {
        internal ExploreDbContext? Owner { get; set; }
        internal TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!commit || Owner is null || !ReferenceEquals(eventData.Context, Owner)) return Task.CompletedTask;
            Owner = null;
            Settled.TrySetResult();
            throw new InvalidOperationException("Lost support commit acknowledgement.");
        }

        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (commit || Owner is null || !ReferenceEquals(eventData.Context, Owner)) return Task.CompletedTask;
            Owner = null;
            Settled.TrySetResult();
            throw new InvalidOperationException("Lost support rollback acknowledgement.");
        }
    }
}
