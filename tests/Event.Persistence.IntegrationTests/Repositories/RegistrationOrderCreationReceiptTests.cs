using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class RegistrationOrderCreationReceiptTests
{
    [Test]
    public async Task MatchingRetryUsesExactReceiptBeforeCurrentAllocationAndPublicationChecks()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.ExecuteAsync(fixture.Command);
        await Assert.That(first.IsSuccess).IsTrue();
        await using (var changed = fixture.Database.CreateScope())
        {
            var db = changed.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await db.EventTicketCatalogVersions.Where(value => value.Id == fixture.Command.TicketCatalogVersionId)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.IsDeleted, true));
        }
        var unavailable = await fixture.ExecuteAsync(fixture.Command);
        await Assert.That(unavailable.FailureCode).IsEqualTo("registration_order_not_found");
        await using (var changed = fixture.Database.CreateScope())
        {
            var db = changed.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await db.EventTicketCatalogVersions.IgnoreQueryFilters(["SoftDelete"])
                .Where(value => value.Id == fixture.Command.TicketCatalogVersionId)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.IsDeleted, false));
            await db.Events.Where(value => value.Id == fixture.Command.EventId)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.EventStatusId, (int)EventStatusEnum.Draft));
            var setting = await changed.ServiceProvider.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyInternalAsync(
                [new(null, GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
                    VisitorAccessSettingMutationKind.SetValue, "\"DirectoryListingOnly\"")],
                fixture.Database.UserId);
            await Assert.That(setting.Success).IsTrue();
        }
        var retry = await fixture.ExecuteAsync(fixture.Command);
        await Assert.That(retry.Id).IsEqualTo(first.Id);
        await Assert.That(retry.IsSuccess).IsTrue();
        await fixture.AssertSingleAllocationAsync(first.Id);
    }

    [Test]
    [Arguments("event")]
    [Arguments("catalog")]
    [Arguments("party")]
    [Arguments("quantity")]
    [Arguments("price")]
    [Arguments("null-contribution")]
    public async Task SameOperationChangedInputNeverAllocates(string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.ExecuteAsync(fixture.Command);
        StartAuthenticatedRegistrationOrderCommand changed = change switch
        {
            "event" => fixture.Command with { EventId = Guid.CreateVersion7() },
            "catalog" => fixture.Command with { TicketCatalogVersionId = Guid.CreateVersion7() },
            "party" => fixture.Command with { BookingPartyType = BookingPartyTypeEnum.Household },
            "quantity" => fixture.Command with { Lines = [fixture.Command.Lines[0] with { Quantity = 2 }] },
            "price" => fixture.Command with { Lines = [fixture.Command.Lines[0] with { ChosenUnitPriceMinor = 0 }] },
            _ => fixture.Command with { PlatformContributionBasisPoints = 0 }
        };
        var rejected = await fixture.ExecuteAsync(changed);
        await Assert.That(rejected.FailureCode).IsEqualTo("registration_order_creation_conflict");
        await fixture.AssertSingleAllocationAsync(first.Id);
    }

    [Test]
    public async Task SelectionOrderNormalizesButQuantityAndPriceRemainBound()
    {
        await using var fixture = await Fixture.CreateAsync(ticketCount: 2);
        var first = await fixture.ExecuteAsync(fixture.Command);
        var retry = await fixture.ExecuteAsync(fixture.Command with { Lines = fixture.Command.Lines.Reverse().ToArray() });
        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(retry.IsSuccess).IsTrue();
        await Assert.That(retry.Id).IsEqualTo(first.Id);
        await fixture.AssertSingleAllocationAsync(first.Id);
    }

    [Test]
    public async Task DifferentKeysForSameAccountRecoverTheirOwnOrdersNotAnArbitraryAccountOrder()
    {
        await using var fixture = await Fixture.CreateAsync();
        var secondCommand = fixture.Command with { OperationKey = Guid.CreateVersion7().ToString("N") };
        var first = await fixture.ExecuteAsync(fixture.Command);
        var second = await fixture.ExecuteAsync(secondCommand);
        await Assert.That(first.IsSuccess && second.IsSuccess).IsTrue();
        await Assert.That(first.Id == second.Id).IsFalse();
        await Assert.That((await fixture.ExecuteAsync(fixture.Command)).Id).IsEqualTo(first.Id);
        await Assert.That((await fixture.ExecuteAsync(secondCommand)).Id).IsEqualTo(second.Id);
        await using var observer = fixture.Database.CreateScope();
        var db = observer.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await db.Set<RegistrationOrderCreationReceipt>().CountAsync()).IsEqualTo(2);
        await Assert.That(await db.RegistrationOrders.CountAsync()).IsEqualTo(2);
        await Assert.That(await db.RegistrationInventoryHolds.CountAsync()).IsEqualTo(2);
    }

    [Test]
    [Arguments("")]
    [Arguments("not a key")]
    public async Task NativeBoundaryRequiresOperationKey(string key)
    {
        await using var fixture = await Fixture.CreateAsync();
        var rejected = await fixture.ExecuteAsync(fixture.Command with { OperationKey = key });
        await Assert.That(rejected.FailureCode).IsEqualTo("registration_order_operation_key_invalid");
        await using var observer = fixture.Database.CreateScope();
        var db = observer.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await db.RegistrationOrders.CountAsync()).IsEqualTo(0);
        await Assert.That(await db.Set<RegistrationOrderCreationReceipt>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("account")]
    [Arguments("actor")]
    [Arguments("contact")]
    public async Task DirectStarterCannotAcceptClientSuppliedAccountActorOrVerifiedContactAuthority(string supplied)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var request = fixture.Database.CreateScope();
        var command = new CreateRegistrationOrderWithHoldCommand
        {
            EventId = fixture.Command.EventId,
            TicketCatalogVersionId = fixture.Command.TicketCatalogVersionId,
            BookingPartyType = fixture.Command.BookingPartyType,
            Lines = fixture.Command.Lines,
            OperationKey = fixture.Command.OperationKey,
            AccountUserId = supplied == "account" ? Guid.CreateVersion7() : fixture.Database.UserId,
            PurchaserActorId = supplied == "actor" ? fixture.Database.ActorId : null,
            VerifiedContactNormalizedEmail = supplied == "contact" ? "unverified@example.test" : null
        };
        var denied = await request.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateRegistrationOrderWithHoldCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(command, CancellationToken.None);
        await Assert.That(denied.FailureCode).IsEqualTo("registration_order_authentication_required");
        await using var observer = fixture.Database.CreateScope();
        var db = observer.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await db.RegistrationOrders.CountAsync()).IsEqualTo(0);
        await Assert.That(await db.Set<RegistrationOrderCreationReceipt>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("principal")]
    [Arguments("purpose")]
    [Arguments("tenant")]
    public async Task DifferentAuthorityCannotRecoverAnotherCreation(string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.ExecuteAsync(fixture.Command);
        await using var other = fixture.Database.CreateScope();
        var principal = other.ServiceProvider.GetRequiredService<Principal>();
        if (change == "principal") principal.UserId = Guid.CreateVersion7();
        if (change == "purpose") principal.Admitted = false;
        if (change == "tenant")
            other.ServiceProvider.GetRequiredService<RequestTenant>().TenantId = Guid.CreateVersion7();
        var denied = await other.ServiceProvider
            .GetRequiredService<ICommandHandler<StartAuthenticatedRegistrationOrderCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(fixture.Command, CancellationToken.None);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Id == first.Id).IsFalse();
        await fixture.AssertSingleAllocationAsync(first.Id);
    }

    [Test]
    [Arguments("order")]
    [Arguments("event")]
    [Arguments("account")]
    [Arguments("owner")]
    [Arguments("expiry")]
    public async Task UnavailableParentDeniesDisclosureWithoutReopeningReceipt(string unavailable)
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.ExecuteAsync(fixture.Command);
        await using (var changed = fixture.Database.CreateScope())
        {
            var db = changed.ServiceProvider.GetRequiredService<ExploreDbContext>();
            switch (unavailable)
            {
                case "order":
                    await db.RegistrationOrders.Where(value => value.Id == first.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(value => value.IsDeleted, true));
                    break;
                case "event":
                    await db.Events.Where(value => value.Id == fixture.Command.EventId)
                        .ExecuteUpdateAsync(update => update.SetProperty(value => value.IsDeleted, true));
                    break;
                case "account":
                    await db.Users.Where(value => value.Id == fixture.Database.UserId)
                        .ExecuteUpdateAsync(update => update.SetProperty(value => value.IsDeleted, true));
                    break;
                case "owner":
                    Guid replacementOwner = Guid.CreateVersion7();
                    await db.RegistrationOrders.Where(value => value.Id == first.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(value => value.AccountUserId, replacementOwner));
                    break;
                default:
                    await db.RegistrationOrders.Where(value => value.Id == first.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(value => value.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
                    break;
            }
        }
        var denied = await fixture.ExecuteAsync(fixture.Command);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Id == first.Id).IsFalse();
        var changedWhileUnavailable = await fixture.ExecuteAsync(
            fixture.Command with { PlatformContributionBasisPoints = 0 });
        await Assert.That(changedWhileUnavailable.FailureCode).IsEqualTo(denied.FailureCode);
        await fixture.AssertSingleAllocationAsync(first.Id);
    }

    [Test]
    public async Task HardDeletedOrderRetainsNonReopeningCreationTombstone()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.ExecuteAsync(fixture.Command);
        await Assert.That(first.IsSuccess).IsTrue();
        await using (var deletion = fixture.Database.CreateScope())
        {
            var db = deletion.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await db.RegistrationInventoryHolds.Where(value => value.RegistrationOrderId == first.Id).ExecuteDeleteAsync();
            await db.RegistrationOrderLines.Where(value => value.RegistrationOrderId == first.Id).ExecuteDeleteAsync();
            await db.RegistrationOrders.Where(value => value.Id == first.Id).ExecuteDeleteAsync();
        }
        var retry = await fixture.ExecuteAsync(fixture.Command);
        await Assert.That(retry.FailureCode).IsEqualTo("registration_order_not_found");
        await using var observer = fixture.Database.CreateScope();
        var context = observer.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await context.Set<RegistrationOrderCreationReceipt>().CountAsync()).IsEqualTo(1);
        await Assert.That(await context.RegistrationOrders.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.RegistrationInventoryHolds.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task RetainedErasureFactDeniesRecoveryBeforePrimaryAccountDisposal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.ExecuteAsync(fixture.Command);
        await Assert.That(first.IsSuccess).IsTrue();
        await using (var erased = fixture.Database.CreateScope())
        {
            await erased.ServiceProvider.GetRequiredService<IPrivacyErasureAuthority>().AppendAsync(
                PrivacyErasureRequest.Create(Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
                    fixture.Database.UserId, PrivacyErasureReasonCode.SubjectErasureRequest, 1));
        }
        var retry = await fixture.ExecuteAsync(fixture.Command);
        await Assert.That(retry.FailureCode).IsEqualTo("registration_order_authentication_required");
        await fixture.AssertSingleAllocationAsync(first.Id);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommitBeforeLostAcknowledgementOrCancellationRecoversExactIdentity(bool cancellation)
    {
        var fault = new CommitObserver { LoseAcknowledgement = true, CancelAfterCommit = cancellation };
        await using var fixture = await Fixture.CreateAsync(fault);
        using var cancelled = new CancellationTokenSource();
        fault.Cancellation = cancelled;
        var recovered = await fixture.ExecuteAsync(fixture.Command, cancelled.Token);
        await Assert.That(recovered.IsSuccess).IsTrue();
        await Assert.That(recovered.Id).IsEqualTo(fault.CommittedOrderId);
        var retry = await fixture.ExecuteAsync(fixture.Command);
        await Assert.That(retry.Id).IsEqualTo(recovered.Id);
        await fixture.AssertSingleAllocationAsync(recovered.Id);
    }

    [Test]
    public async Task SerializableReplayKeepsReceiptOrderLineAndHoldIdentities()
    {
        var fault = new CommitObserver { ReplayBeforeCommit = true };
        await using var fixture = await Fixture.CreateAsync(fault);
        var result = await fixture.ExecuteAsync(fixture.Command);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(fault.Attempts.Count).IsEqualTo(2);
        await Assert.That(fault.Attempts[0].SequenceEqual(fault.Attempts[1])).IsTrue();
        await fixture.AssertSingleAllocationAsync(result.Id);
    }

    [Test]
    public async Task FailureBeforeCommitRollsBackReceiptOrderLinesAndHoldsTogether()
    {
        var fault = new CommitObserver { FailBeforeCommit = true };
        await using var fixture = await Fixture.CreateAsync(fault);
        bool failed = false;
        try { await fixture.ExecuteAsync(fixture.Command); }
        catch (IOException) { failed = true; }
        await Assert.That(failed).IsTrue();
        await using var observer = fixture.Database.CreateScope();
        var db = observer.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await db.Set<RegistrationOrderCreationReceipt>().CountAsync()).IsEqualTo(0);
        await Assert.That(await db.RegistrationOrders.CountAsync()).IsEqualTo(0);
        await Assert.That(await db.RegistrationOrderLines.CountAsync()).IsEqualTo(0);
        await Assert.That(await db.RegistrationInventoryHolds.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task IndependentContendingRequestsConvergeOnOneCommittedAllocation()
    {
        var barrier = new CommitObserver { HoldCommit = true };
        await using var fixture = await Fixture.CreateAsync(barrier);
        var first = fixture.ExecuteAsync(fixture.Command);
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var contender = fixture.Database.CreateScope();
        var principal = contender.ServiceProvider.GetRequiredService<Principal>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        principal.Observed = entered;
        var second = contender.ServiceProvider
            .GetRequiredService<ICommandHandler<StartAuthenticatedRegistrationOrderCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(fixture.Command, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            barrier.Release.TrySetResult();
        }
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(20));
        await Assert.That(results.All(result => result.IsSuccess)).IsTrue();
        await Assert.That(results[1].Id).IsEqualTo(results[0].Id);
        await fixture.AssertSingleAllocationAsync(results[0].Id);
    }

    private sealed class RequestTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; set; } = tenantId;
    }

    private sealed class Principal(ICurrentUserService current) : IRegistrationCreationPrincipal
    {
        internal Guid? UserId { get; set; }
        internal bool Admitted { get; set; } = true;
        internal TaskCompletionSource? Observed { get; set; }
        public RegistrationCreationPrincipal? GetCurrent()
        {
            Observed?.TrySetResult();
            return Admitted && current.UserId is { } userId
                ? new(UserId ?? userId, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("fixture-account"))))
                : null;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal EventVisitorCapabilitySqliteFixture Database { get; private set; } = null!;
        internal StartAuthenticatedRegistrationOrderCommand Command { get; private set; } = null!;

        internal static async Task<Fixture> CreateAsync(CommitObserver? observer = null, int ticketCount = 1)
        {
            var fixture = new Fixture();
            fixture.Database = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
            {
                var tenant = (ITenantContext)services.Last(value => value.ServiceType == typeof(ITenantContext))
                    .ImplementationInstance!;
                services.AddScoped(_ => new RequestTenant(tenant.TenantId));
                services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<RequestTenant>());
                services.AddScoped<Principal>();
                services.AddScoped<IRegistrationCreationPrincipal>(provider => provider.GetRequiredService<Principal>());
                if (observer is not null)
                    services.ConfigureDbContext<ExploreDbContext>(builder => builder.AddInterceptors(observer));
            });
            var entity = await fixture.Database.SeedEventAsync(published: true);
            var ticket = await fixture.Database.SeedTicketAsync(entity.Id, ticketCount);
            var lines = await fixture.Database.Context.EventTicketTypes.AsNoTracking()
                .Where(value => value.CatalogId == ticket.CatalogId)
                .OrderBy(value => value.Id)
                .Select(value => new RegistrationOrderLineSelection(value.Id, 1, null)).ToArrayAsync();
            fixture.Command = new(entity.Id, ticket.CatalogId, BookingPartyTypeEnum.Individual,
                lines, Guid.CreateVersion7().ToString("N"));
            return fixture;
        }

        internal async Task<BaseCommandResponse<Guid>> ExecuteAsync(
            StartAuthenticatedRegistrationOrderCommand command, CancellationToken token = default)
        {
            await using var scope = Database.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<ICommandHandler<StartAuthenticatedRegistrationOrderCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(command, token);
        }

        internal async Task AssertSingleAllocationAsync(Guid orderId)
        {
            await using var observer = Database.CreateScope();
            var db = observer.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var receipt = await db.Set<RegistrationOrderCreationReceipt>().AsNoTracking().SingleAsync();
            await Assert.That(receipt.RegistrationOrderId).IsEqualTo(orderId);
            await Assert.That(await db.RegistrationOrders.IgnoreQueryFilters(["SoftDelete"]).CountAsync()).IsEqualTo(1);
            await Assert.That(await db.RegistrationOrderLines.CountAsync()).IsEqualTo(Command.Lines.Count);
            await Assert.That(await db.RegistrationInventoryHolds.CountAsync()).IsEqualTo(Command.Lines.Count);
        }

        public async ValueTask DisposeAsync()
        {
            if (Database is not null) await Database.DisposeAsync();
        }
    }

    private sealed class CommitObserver : DbTransactionInterceptor
    {
        internal bool LoseAcknowledgement { get; set; }
        internal bool CancelAfterCommit { get; set; }
        internal bool ReplayBeforeCommit { get; set; }
        internal bool FailBeforeCommit { get; set; }
        internal bool HoldCommit { get; set; }
        internal CancellationTokenSource? Cancellation { get; set; }
        internal Guid CommittedOrderId { get; private set; }
        internal List<Guid[]> Attempts { get; } = [];
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not ExploreDbContext db ||
                db.ChangeTracker.Entries<RegistrationOrderCreationReceipt>().SingleOrDefault() is not { } receipt)
                return result;
            CommittedOrderId = receipt.Entity.RegistrationOrderId;
            Attempts.Add([receipt.Entity.Id, CommittedOrderId,
                db.ChangeTracker.Entries<RegistrationOrderLine>().Single().Entity.Id,
                db.ChangeTracker.Entries<RegistrationInventoryHold>().Single().Entity.Id]);
            if (FailBeforeCommit)
                throw new IOException("Creation transaction rejected before commit.");
            if (ReplayBeforeCommit)
            {
                ReplayBeforeCommit = false;
                throw new SqliteException("Creation serializable conflict.", 5);
            }
            if (HoldCommit)
            {
                HoldCommit = false;
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!LoseAcknowledgement || CommittedOrderId == Guid.Empty) return Task.CompletedTask;
            LoseAcknowledgement = false;
            if (CancelAfterCommit)
            {
                Cancellation!.Cancel();
                throw new OperationCanceledException(Cancellation.Token);
            }
            throw new IOException("Creation commit acknowledgement lost.");
        }
    }
}
