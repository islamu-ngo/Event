using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Npgsql;
using TUnit.Core.Interfaces;
using Testcontainers.PostgreSql;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests;

[NotInParallel("PersistenceDb")]
[ClassDataSource<RefundPostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
public sealed class RefundReservationPostgreSqlConcurrencyTests(RefundPostgreSqlContainerFixture fixture)
{
    private static readonly DateTime UtcNow = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task TwoRacingReservationsCannotExceedCapturedCapacity()
    {
        string connectionString = fixture.ConnectionString;
        Guid tenantId = Guid.CreateVersion7();
        Guid paymentId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        PaymentAttempt payment;
        await using (ExploreDbContext seed = await CreateContextAsync(connectionString))
        {
            payment = await SeedCapturedPaymentAsync(seed, tenantId, paymentId, orderId);
        }

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        RefundReservationResult[] results = await Task.WhenAll(
            ReserveAsync(connectionString, Refund(payment, 750, "refund:race:a"), timeout.Token),
            ReserveAsync(connectionString, Refund(payment, 750, "refund:race:b"), timeout.Token));

        await Assert.That(results.Count(result => result.Disposition == RefundReservationDisposition.Reserved)).IsEqualTo(1);
        await Assert.That(results.Count(result => result.Disposition == RefundReservationDisposition.CapacityExceeded)).IsEqualTo(1);
        await using ExploreDbContext verification = await CreateContextAsync(connectionString, ensureCreated: false);
        long reserved = await verification.RefundAttempts
            .Where(attempt => attempt.TenantId == tenantId && attempt.PaymentAttemptId == paymentId)
            .SumAsync(attempt => attempt.Allocation.TotalMinor, timeout.Token);
        await Assert.That(reserved).IsEqualTo(750L);
    }

    [Test]
    public async Task SequentialRoundedFeeReservationsRespectEveryPersistedLineConstraint()
    {
        string connectionString = fixture.ConnectionString;
        Guid tenantId = Guid.CreateVersion7();
        Guid paymentId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        long[] lineTotals = [1, 1, 1];
        PaymentAttempt payment;
        await using (ExploreDbContext seed = await CreateContextAsync(connectionString))
        {
            payment = await SeedCapturedPaymentAsync(seed, tenantId, paymentId, orderId, 3, 1, lineTotals);
        }

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        RefundReservationResult first = await ReserveAsync(
            connectionString,
            Refund(payment, 1, "refund:line-fee:first"),
            timeout.Token);
        RefundReservationResult second = await ReserveAsync(
            connectionString,
            Refund(payment, 1, "refund:line-fee:second"),
            timeout.Token);

        await using ExploreDbContext verification = await CreateContextAsync(connectionString, ensureCreated: false);
        RefundLineAllocation[] persisted = await verification.RefundLineAllocations
            .Where(line => line.TenantId == tenantId)
            .ToArrayAsync(timeout.Token);
        await Assert.That(first.Disposition).IsEqualTo(RefundReservationDisposition.Reserved);
        await Assert.That(second.Disposition).IsEqualTo(RefundReservationDisposition.Reserved);
        await Assert.That(persisted.All(line => line.PlatformFeeMinor <= line.OrganizerAmountMinor)).IsTrue();
        await Assert.That(persisted.Sum(line => line.OrganizerAmountMinor)).IsEqualTo(2L);
        await Assert.That(persisted.Sum(line => line.PlatformFeeMinor)).IsEqualTo(1L);
    }

    private static async Task<RefundReservationResult> ReserveAsync(
        string connectionString,
        RefundAttempt attempt,
        CancellationToken cancellationToken)
    {
        await using ExploreDbContext context = await CreateContextAsync(connectionString, ensureCreated: false);
        return await new RefundAttemptRepository(context).ReserveAsync(attempt, cancellationToken);
    }

    [Test]
    public async Task PostgreSqlNamedLockLifecycleCoversContentionCommitRollbackCancellationAndDisposal()
    {
        string resource = $"named-lock-lifecycle:postgres:{Guid.CreateVersion7():N}";
        await using ExploreDbContext owner =
            await CreateContextAsync(fixture.ConnectionString, ensureCreated: false);
        await using var ownerTransaction = await owner.Database.BeginTransactionAsync();
        await using IAsyncDisposable transactionLease =
            await RelationalNamedLock.AcquireTransactionAsync(
                owner,
                resource,
                CancellationToken.None);

        await using (ExploreDbContext contender =
                     await CreateContextAsync(fixture.ConnectionString, ensureCreated: false))
        await using (var contenderTransaction = await contender.Database.BeginTransactionAsync())
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
            Exception? blocked = await CaptureNamedLockFailureAsync(
                contender,
                resource,
                cancellation.Token);
            await Assert.That(blocked).IsNotNull();
        }

        await ownerTransaction.CommitAsync();

        await using (ExploreDbContext rollbackOwner =
                     await CreateContextAsync(fixture.ConnectionString, ensureCreated: false))
        await using (var rollbackTransaction = await rollbackOwner.Database.BeginTransactionAsync())
        {
            await using IAsyncDisposable lease =
                await RelationalNamedLock.AcquireTransactionAsync(
                    rollbackOwner,
                    resource,
                    CancellationToken.None);
            await rollbackTransaction.RollbackAsync();
        }

        await using ExploreDbContext sessionOwner =
            await CreateContextAsync(fixture.ConnectionString, ensureCreated: false);
        IAsyncDisposable sessionLease = await RelationalNamedLock.AcquireSessionAsync(
            sessionOwner,
            resource,
            CancellationToken.None);
        await sessionLease.DisposeAsync();

        await using ExploreDbContext verifier =
            await CreateContextAsync(fixture.ConnectionString, ensureCreated: false);
        await using var verificationTransaction = await verifier.Database.BeginTransactionAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using IAsyncDisposable verificationLease =
            await RelationalNamedLock.AcquireTransactionAsync(
                verifier,
                resource,
                timeout.Token);
        await verificationTransaction.CommitAsync(timeout.Token);
    }

    private static async Task<Exception?> CaptureNamedLockFailureAsync(
        ExploreDbContext context,
        string resource,
        CancellationToken cancellationToken)
    {
        try
        {
            await using IAsyncDisposable lease =
                await RelationalNamedLock.AcquireTransactionAsync(
                    context,
                    resource,
                    cancellationToken);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<ExploreDbContext> CreateContextAsync(string connectionString, bool ensureCreated = true)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(connectionString, provider => provider.EnableRetryOnFailure(3))
            .UseSnakeCaseNamingConvention()
            .Options;
        var context = new ExploreDbContext(options);
        context.EnableTenantFilterBypass("Phase 19 PostgreSQL refund race test setup.");
        if (ensureCreated)
        {
            await context.Database.EnsureCreatedAsync();
        }
        return context;
    }

    private static async Task<PaymentAttempt> SeedCapturedPaymentAsync(
        ExploreDbContext context,
        Guid tenantId,
        Guid paymentId,
        Guid orderId,
        long organizerMinor = 1_000,
        long platformFeeMinor = 75,
        IReadOnlyList<long>? lineTotals = null)
    {
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET session_replication_role = replica;");
        var tenant = new Tenant
        {
            Id = tenantId,
            FullName = "Refund race tenant",
            Slug = $"refund-{tenantId:N}",
            TenantStatusId = 1,
            TenantStatus = null!,
            CreatedAt = UtcNow
        };
        RegistrationOrder order = RegistrationOrder.Create(
            orderId,
            tenantId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            BookingPartyTypeEnum.Individual,
            Guid.CreateVersion7(),
            RegistrationParticipationSnapshot.Create(
                Guid.CreateVersion7(), 1, 1, 1, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            null,
            null,
            "EUR",
            UtcNow,
            UtcNow.AddMinutes(30));
        OrganizerPaymentRecipientSnapshot recipient = OrganizerPaymentRecipientSnapshot.Create(
            tenantId, Guid.CreateVersion7(), Guid.CreateVersion7(), "stripe", "platform-live-eu", "acct_original",
            "BE", "EUR", Guid.CreateVersion7(), null, UtcNow);
        PaymentAttempt payment = PaymentAttempt.Create(
            paymentId, tenantId, orderId, recipient, "OrganizerDirect", "2026-08-20.acacia", "refund-race",
            Money.Create(organizerMinor, recipient.CurrencyCode), Money.Create(platformFeeMinor, recipient.CurrencyCode), Money.Create(0, recipient.CurrencyCode), $"payment:{tenantId:N}:{paymentId:N}", UtcNow, UtcNow.AddMinutes(30));
        payment.AttachAcceptance(Acceptance(
            tenantId, paymentId, orderId, order.EventId, recipient, organizerMinor, platformFeeMinor, lineTotals));
        payment.MarkSucceeded(PaymentProviderId(paymentId), UtcNow.AddSeconds(1), "req_payment");
        context.Tenants.Add(tenant);
        context.RegistrationOrders.Add(order);
        context.PaymentAttempts.Add(payment);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync("SET session_replication_role = origin;");
        return payment;
    }

    private static RefundAttempt Refund(
        PaymentAttempt payment,
        long totalMinor,
        string idempotencyKey) =>
        RefundAttempt.Create(
            Guid.CreateVersion7(), payment.TenantId, payment.Id,
            payment.AcceptanceSnapshot!, payment.RecipientSnapshot.ExternalAccountId,
            payment.ProviderPaymentId!, idempotencyKey, totalMinor, UtcNow.AddMinutes(1));

    private static string PaymentProviderId(Guid paymentId) => $"pi_{paymentId:N}";

    private static PaidOrderAcceptanceSnapshot Acceptance(
        Guid tenantId,
        Guid paymentId,
        Guid orderId,
        Guid eventId,
        OrganizerPaymentRecipientSnapshot recipient,
        long organizerMinor = 1_000,
        long platformFeeMinor = 75,
        IReadOnlyList<long>? lineTotals = null) =>
        PaidOrderAcceptanceSnapshot.Create(
            paymentId, tenantId, tenantId, orderId, eventId, "refund-race", "disclosure-1",
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier,
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText,
            recipient.OrganizerActorId,
            "Example Organizer",
            PaidCheckoutTenantDirectoryOperatorDisclosure.Create(
                Guid.CreateVersion7(), Guid.CreateVersion7(), "Community Events", "Community Events ASBL",
                "registered_organization", "BE", null, "contact@example.test", "https://example.test/legal",
                "https://example.test/terms", "https://example.test/privacy"),
            PaidCheckoutOperatorDisclosure.Create(
                Guid.CreateVersion7(), "Example Operator", false, "https://events.example.test", "BE",
                "https://events.example.test", "https://events.example.test/legal", "https://events.example.test/terms",
                "https://events.example.test/privacy", "complaints@example.test", "Trust and Safety", "Payments Operations",
                "Dispute Operations", "Payment Reconciliation", "approved"),
            PaidOrderDeliverySnapshot.Create(
                DateTimeOffset.Parse("2026-09-10T17:00:00Z"), DateTimeOffset.Parse("2026-09-10T20:00:00Z"), "Europe/Brussels"),
            recipient.CurrencyCode, organizerMinor, platformFeeMinor, 0, organizerMinor, recipient.InstancePolicyVersionId, 7,
            "Refunds follow accepted policy v7.", "en-GB", "support@example.test",
            PaidCheckoutProviderDisclosure.Create(
                "stripe", "OrganizerDirect", "direct-charge", "EXAMPLE EVENT", "test", "instance-operator"),
            (lineTotals ?? [organizerMinor]).Select((total, index) => PaidOrderAcceptanceLineFact.Create(
                Guid.CreateVersion7(), $"Line {index + 1}", 1, total, 0, total)).ToArray(), UtcNow,
            tenantPolicyVersionId: recipient.TenantPolicyVersionId,
            organizerPaymentProviderConnectionId: recipient.OrganizerPaymentProviderConnectionId,
            connectPlatformId: recipient.ConnectPlatformId,
            externalAccountId: recipient.ExternalAccountId,
            merchantCountryCode: recipient.MerchantCountryCode);
}

public sealed class RefundPostgreSqlContainerFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("refund_test")
        .WithUsername("postgres")
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)))
        .Build();

    public string ConnectionString
    {
        get
        {
            var builder = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
            {
                SearchPath = "islamu_event"
            };
            return builder.ConnectionString;
        }
    }

    public Task InitializeAsync() => container.StartAsync();

    public async ValueTask DisposeAsync()
    {
        await container.StopAsync();
        await container.DisposeAsync();
    }
}
