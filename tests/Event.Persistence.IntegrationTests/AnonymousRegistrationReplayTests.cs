
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

public sealed class AnonymousRegistrationReplayTests
{
    [Test]
    public async Task RawGuestHashWithoutProtectedAuthority_CannotAllocate()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var target = await fixture.SeedEventAsync();
        var ticket = await fixture.SeedTicketAsync(target.Id);
        var capability = fixture.Services.GetRequiredService<IGuestCapabilityTokenService>().Issue();
        var response = await fixture.Services.GetRequiredService<IRegistrationOrderStarter>().StartAsync(new()
        {
            EventId = target.Id,
            TicketCatalogVersionId = ticket.CatalogId,
            BookingPartyType = BookingPartyTypeEnum.Individual,
            GuestAccessTokenHash = capability.Hash,
            Lines = [new(ticket.TicketId, 1, null)]
        }, CancellationToken.None);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("registration_order_challenge_invalid");
        await Assert.That(await fixture.Context.RegistrationOrders.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.RegistrationInventoryHolds.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task CancellationAfterCommit_RecoversOriginalCapabilityAndHoldsDespiteExpiredProofAndChangedPolicy()
    {
        var clock = new Clock();
        var deadline = new CommitDeadlineBarrier();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IScheduledDeadlineDispatcher>(deadline);
        });
        var target = await fixture.SeedEventAsync(published: true);
        var ticket = await fixture.SeedTicketAsync(target.Id);
        var proof = await fixture.IssueGuestProofAsync(new(target.Id, ticket.CatalogId,
            BookingPartyTypeEnum.Individual, [new(ticket.TicketId, 1, null)]));
        var authority = proof.Request.ChallengeAuthority!;
        var claim = await fixture.Services.GetRequiredService<IIdempotencyRepository>().TryClaimAsync(new IdempotencyRecord
        {
            Id = Guid.CreateVersion7(), TenantId = fixture.TenantId, Key = proof.Binding.IdempotencyKey,
            RequestMethod = "POST", RequestTarget = $"/api/events/{target.Id}/registration-orders/guest",
            RequestBodyHash = proof.Binding.CanonicalRequestDigest, PrincipalFingerprint = Guid.CreateVersion7().ToString("N"),
            StatusCode = IdempotencyRecord.InProgressStatusCode,
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(24)
        });
        await Assert.That(claim.IsOwner).IsTrue();
        using var interruptedRequest = new CancellationTokenSource();
        Task<GuestRegistrationOrderStartDto> pending = StartAsync(fixture.Services, proof.Request, interruptedRequest.Token);
        await deadline.Committed.WaitAsync(TimeSpan.FromSeconds(20));
        await using var observer = fixture.CreateScope();
        var inventory = observer.ServiceProvider.GetRequiredService<IRegistrationInventoryRepository>();
        var original = await inventory.GetOrderWithLinesAsync(authority.OrderId, fixture.TenantId, CancellationToken.None);
        var originalHold = (await inventory.GetHoldsByOrderAsync(authority.OrderId, fixture.TenantId, CancellationToken.None)).Single();
        await Assert.That(original).IsNotNull();
        await Assert.That(pending.IsCompleted).IsFalse();
        interruptedRequest.Cancel();
        await Assert.That(async () => await pending).Throws<OperationCanceledException>();

        var changed = await observer.ServiceProvider.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyAsync(
            [new(null, GovernanceSettingKeys.PublicExperience.VisitorAccessMode, VisitorAccessSettingMutationKind.SetValue,
                "\"DirectoryListingOnly\"")], fixture.UserId);
        await Assert.That(changed.Success).IsTrue();
        clock.Now = proof.Challenge.ExpiresAt.AddMinutes(1);
        var retryRequest = await EventVisitorCapabilitySqliteFixture.ValidateGuestProofAsync(observer.ServiceProvider, proof);
        var starter = observer.ServiceProvider.GetRequiredService<IRegistrationOrderStarter>();
        var recovered = await starter.TryRecoverCommittedGuestAsync(retryRequest, CancellationToken.None);
        await Assert.That(recovered).IsNotNull();
        await Assert.That(recovered!.IsSuccess).IsTrue();
        var replay = await StartAsync(observer.ServiceProvider, retryRequest);
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(original!.Id);
        await Assert.That(replay.GuestCapabilityToken).IsEqualTo(authority.GuestCapabilityToken);
        var stored = await inventory.GetOrderWithLinesAsync(replay.Id, fixture.TenantId, CancellationToken.None);
        var hold = (await inventory.GetHoldsByOrderAsync(replay.Id, fixture.TenantId, CancellationToken.None)).Single();
        await Assert.That(stored!.ExpiresAt).IsEqualTo(original.ExpiresAt);
        await Assert.That(stored.CreatedAt).IsEqualTo(original.CreatedAt);
        await Assert.That(stored.ConcurrencyStamp).IsEqualTo(original.ConcurrencyStamp);
        await Assert.That(stored.Lines.Single().Id).IsEqualTo(original.Lines.Single().Id);
        await Assert.That(hold.Id).IsEqualTo(originalHold.Id);
        await Assert.That(hold.ExpiresAt).IsEqualTo(originalHold.ExpiresAt);
        await Assert.That(hold.ConcurrencyStamp).IsEqualTo(originalHold.ConcurrencyStamp);
        await Assert.That(await inventory.GetAllocatedQuantityAsync(hold.CapacityPoolId, fixture.TenantId, CancellationToken.None)).IsEqualTo(1);
        await Assert.That(deadline.CallCount).IsEqualTo(1);
        var durableClaim = await observer.ServiceProvider.GetRequiredService<IIdempotencyRepository>()
            .FindAsync(proof.Binding.IdempotencyKey, fixture.TenantId);
        await Assert.That(durableClaim!.StatusCode).IsEqualTo(IdempotencyRecord.InProgressStatusCode);
        await Assert.That(durableClaim.ResponseBody).IsNull();
        var database = observer.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await database.RegistrationOrders.CountAsync()).IsEqualTo(1);
        await Assert.That(await database.RegistrationOrderLines.CountAsync()).IsEqualTo(1);
        await Assert.That(await database.RegistrationInventoryHolds.CountAsync()).IsEqualTo(1);
        await Assert.That(await database.RegistrationOrderPii.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task IndependentNativeReplicas_ConvergeOnOneDurableAllocation()
    {
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int contenders = 0;
        async Task BeforeLease(string key, CancellationToken token)
        {
            if (key != VisitorAccessCapabilityResolver.AuthoritySettingKeys[0]) return;
            if (Interlocked.Increment(ref contenders) == 2) arrived.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
        }
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
            services.AddScoped<ISettingMutationLock>(provider => new RelationalSettingMutationLock(
                provider.GetRequiredService<ExploreDbContext>(), provider.GetRequiredService<IUnitOfWork>(), BeforeLease)));
        var target = await fixture.SeedEventAsync(published: true);
        var ticket = await fixture.SeedTicketAsync(target.Id);
        var proof = await fixture.IssueGuestProofAsync(new(target.Id, ticket.CatalogId,
            BookingPartyTypeEnum.Individual, [new(ticket.TicketId, 1, null)]));
        await using var firstScope = fixture.CreateScope();
        await using var replica = fixture.CreateReplica();
        await using var secondScope = replica.CreateAsyncScope();
        var firstRequest = await EventVisitorCapabilitySqliteFixture.ValidateGuestProofAsync(firstScope.ServiceProvider, proof);
        var secondRequest = await EventVisitorCapabilitySqliteFixture.ValidateGuestProofAsync(secondScope.ServiceProvider, proof);
        Task<GuestRegistrationOrderStartDto> first = StartAsync(firstScope.ServiceProvider, firstRequest);
        Task<GuestRegistrationOrderStartDto> second = StartAsync(secondScope.ServiceProvider, secondRequest);
        try
        {
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Assert.That(first.IsCompleted).IsFalse();
            await Assert.That(second.IsCompleted).IsFalse();
        }
        finally { release.TrySetResult(); }
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(20));
        await Assert.That(results.All(result => result.IsSuccess)).IsTrue();
        await Assert.That(results[0].Id).IsEqualTo(proof.Request.ChallengeAuthority!.OrderId);
        await Assert.That(results[1].Id).IsEqualTo(results[0].Id);
        await Assert.That(results[1].GuestCapabilityToken).IsEqualTo(results[0].GuestCapabilityToken);
        await Assert.That(await fixture.Context.RegistrationOrders.CountAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.RegistrationOrderLines.CountAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.RegistrationInventoryHolds.CountAsync()).IsEqualTo(1);
        var inventory = fixture.Services.GetRequiredService<IRegistrationInventoryRepository>();
        var hold = (await inventory.GetHoldsByOrderAsync(results[0].Id, fixture.TenantId, CancellationToken.None)).Single();
        await Assert.That(await inventory.GetAllocatedQuantityAsync(hold.CapacityPoolId, fixture.TenantId, CancellationToken.None)).IsEqualTo(1);
    }

    [Test]
    public async Task ProofExpiringDuringOrderedLeaseWait_CannotAllocateAndReadOnlyRecoveryRemainsAbsent()
    {
        var clock = new Clock();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task BeforeLease(string key, CancellationToken token)
        {
            if (key != VisitorAccessCapabilityResolver.AuthoritySettingKeys[0]) return;
            arrived.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
        }
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddScoped<ISettingMutationLock>(provider => new RelationalSettingMutationLock(
                provider.GetRequiredService<ExploreDbContext>(), provider.GetRequiredService<IUnitOfWork>(), BeforeLease));
        });
        var target = await fixture.SeedEventAsync(published: true);
        var ticket = await fixture.SeedTicketAsync(target.Id);
        var proof = await fixture.IssueGuestProofAsync(new(target.Id, ticket.CatalogId,
            BookingPartyTypeEnum.Individual, [new(ticket.TicketId, 1, null)]));
        Task<GuestRegistrationOrderStartDto> pending = StartAsync(fixture.Services, proof.Request);
        try
        {
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
            clock.Now = proof.Challenge.ExpiresAt;
            await using var observer = fixture.CreateScope();
            await Assert.That(await observer.ServiceProvider.GetRequiredService<IRegistrationOrderStarter>()
                .TryRecoverCommittedGuestAsync(proof.Request, CancellationToken.None)).IsNull();
        }
        finally { release.TrySetResult(); }
        var response = await pending.WaitAsync(TimeSpan.FromSeconds(20));
        await Assert.That(response.FailureCode).IsEqualTo("registration_order_challenge_expired");
        await Assert.That(await fixture.Context.RegistrationOrders.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.RegistrationInventoryHolds.CountAsync()).IsEqualTo(0);
        var alreadyExpired = await StartAsync(fixture.Services, proof.Request);
        await Assert.That(alreadyExpired.FailureCode).IsEqualTo("registration_order_challenge_expired");
    }

    [Test]
    public async Task AlteredTypedRequestOrExactStoredScope_CannotDiscloseCommittedOrder()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var target = await fixture.SeedEventAsync(published: true);
        var ticket = await fixture.SeedTicketAsync(target.Id);
        var proof = await fixture.IssueGuestProofAsync(new(target.Id, ticket.CatalogId,
            BookingPartyTypeEnum.Individual, [new(ticket.TicketId, 1, null)]));
        var created = await StartAsync(fixture.Services, proof.Request);
        await Assert.That(created.IsSuccess).IsTrue();
        var starter = fixture.Services.GetRequiredService<IRegistrationOrderStarter>();
        StartGuestRegistrationOrderCommand[] altered =
        [
            proof.Request with { EventId = Guid.CreateVersion7() },
            proof.Request with { TicketCatalogVersionId = Guid.CreateVersion7() },
            proof.Request with { Lines = [new(ticket.TicketId, 2, null)] },
            proof.Request with { Lines = [new(ticket.TicketId, 1, 1)] },
            proof.Request with { PlatformContributionBasisPoints = 1 },
            proof.Request with { ChallengeAuthority = null }
        ];
        foreach (var request in altered)
        {
            var denied = await StartAsync(fixture.Services, request);
            var recovery = await starter.TryRecoverCommittedGuestAsync(request, CancellationToken.None);
            await Assert.That(denied.FailureCode).IsEqualTo("registration_order_challenge_invalid");
            await Assert.That(denied.GuestCapabilityToken).IsNull();
            await Assert.That(recovery!.IsSuccess).IsFalse();
        }
        var guestRegistrations = fixture.Services.GetRequiredService<IGuestRegistrationCapabilityRepository>();
        var authority = proof.Request.ChallengeAuthority!;
        var wrongHash = fixture.Services.GetRequiredService<IGuestCapabilityTokenService>().Issue().Hash;
        await Assert.That(await guestRegistrations.GetExactGuestOrderAsync(created.Id, fixture.TenantId, target.Id,
            wrongHash, CancellationToken.None)).IsNull();
        await Assert.That(await guestRegistrations.GetExactGuestOrderAsync(created.Id, Guid.CreateVersion7(), target.Id,
            authority.GuestAccessTokenHash, CancellationToken.None)).IsNull();
        await Assert.That(await guestRegistrations.GetExactGuestOrderAsync(created.Id, fixture.TenantId, Guid.CreateVersion7(),
            authority.GuestAccessTokenHash, CancellationToken.None)).IsNull();
        var newEnvelope = await fixture.IssueGuestProofAsync(proof.Request);
        await Assert.That(await starter.TryRecoverCommittedGuestAsync(newEnvelope.Request, CancellationToken.None)).IsNull();
        var cryptoOnly = fixture.Services.GetRequiredService<IAnonymousRegistrationChallengeService>()
            .Validate(proof.Binding, proof.Challenge.ProtectedChallenge, proof.Nonce);
        await Assert.That((await StartAsync(fixture.Services, proof.Request with { ChallengeAuthority = cryptoOnly })).IsSuccess).IsFalse();
        await Assert.That(await fixture.Context.RegistrationOrders.CountAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.RegistrationInventoryHolds.CountAsync()).IsEqualTo(1);
    }

    private static Task<GuestRegistrationOrderStartDto> StartAsync(IServiceProvider services,
        StartGuestRegistrationOrderCommand request, CancellationToken cancellationToken = default) =>
        services.GetRequiredService<IRequestHandler<StartGuestRegistrationOrderCommand, GuestRegistrationOrderStartDto>>()
            .Handle(request, cancellationToken);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CommitDeadlineBarrier : IScheduledDeadlineDispatcher
    {
        private readonly TaskCompletionSource _committed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _interrupted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Committed => _committed.Task;
        public int CallCount { get; private set; }

        public async Task<ScheduledDeadlineResult> ScheduleAsync(ScheduledDeadline deadline, CancellationToken cancellationToken)
        {
            CallCount++;
            _committed.TrySetResult();
            await _interrupted.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return ScheduledDeadlineResult.Success();
        }

        public Task<bool> CancelAsync(string jobName, string deadlineKey, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
