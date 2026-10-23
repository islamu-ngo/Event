using Explore.Persistence.Database.ProviderPrimitives;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Services;
using RegistrationPaymentCheckoutDispatchService = Explore.Infrastructure.Services.RegistrationPaymentCheckoutDispatchService;
using RegistrationPaymentCheckoutDispatchRequest = Explore.Application.Contracts.Payments.RegistrationPaymentCheckoutDispatchRequest;
using RegistrationPaymentCheckoutDispatchResult = Explore.Application.Contracts.Payments.RegistrationPaymentCheckoutDispatchResult;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class CheckoutDispatchNativeAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task RequestlessProductionDispatcherRoutesUnknownSessionAndSettlesOnlyItsOriginalTenant()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var a = await SeedAsync(seed, knownUnknown: true);
        var b = await SeedAsync(seed, knownUnknown: true);
        await PostgresCheckoutDispatchContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        await GrantRuntimeAsync(seed);
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton<IGlobalExploreDbContextFactory>(
            new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), 1));
        services.AddSingleton<ITenantExploreDbContextFactory>(
            new TenantExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime), 2));
        services.AddCheckoutDispatchPersistence();
        services.AddScoped(provider => GenericOutboxExecutionScopeFactory.GetSelectedRuntimeContext(provider)
            ?? throw new InvalidOperationException("Checkout dependency constructed before exact tenant selection."));
        services.AddScoped<IRegistrationPaymentAttemptRepository, RegistrationPaymentAttemptRepository>();
        services.AddScoped<ICheckoutDispatchExecutionScopeFactory, CheckoutDispatchExecutionScopeFactory>();
        var calls = new List<(Guid Tenant, string Account, string Session, string Receipt)>();
        var contexts = new List<DbContextId>();
        services.AddScoped(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var tenant = context.TenantFilterTenantId ?? throw new InvalidOperationException("Provider has no Runtime tenant.");
            contexts.Add(context.ContextId);
            var attempt = new[] { a, b }.Single(value => value.TenantId == tenant);
            return new RecordingProvider(attempt, calls);
        });
        services.AddScoped<IHostedCheckoutSessionCreator>(provider => provider.GetRequiredService<RecordingProvider>());
        services.AddScoped<IHostedCheckoutSessionRetriever>(provider => provider.GetRequiredService<RecordingProvider>());
        services.AddScoped<IPaymentIntentRetriever>(provider => provider.GetRequiredService<RecordingProvider>());
        // These dependencies are not evaluated on known-session Unknown recovery.
        // Their construction still requires the selected real Runtime context.
        services.AddScoped(provider =>
        {
            _ = provider.GetRequiredService<ExploreDbContext>();
            return Substitute.For<IRegistrationOrderLifecycleService>();
        });
        services.AddScoped(provider =>
        {
            _ = provider.GetRequiredService<ExploreDbContext>();
            return Substitute.For<IPaidCheckoutActivationService>();
        });
        services.AddScoped(provider =>
        {
            _ = provider.GetRequiredService<ExploreDbContext>();
            return Substitute.For<IPaidOrderAcceptanceFreshnessService>();
        });
        services.AddScoped(provider =>
        {
            _ = provider.GetRequiredService<ExploreDbContext>();
            return Substitute.For<IPaymentProviderDescriptor>();
        });
        services.AddSingleton<TimeProvider>(new FixedTime(Now.AddSeconds(2)));
        services.AddScoped<RegistrationPaymentCheckoutDispatchService>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        { ValidateScopes = true, ValidateOnBuild = true });
        await using var parent = provider.CreateAsyncScope();
        var dispatcher = parent.ServiceProvider.GetRequiredService<RegistrationPaymentCheckoutDispatchService>();
        await Assert.That(contexts.Count).IsEqualTo(0);
        var request = new RegistrationPaymentCheckoutDispatchRequest("production-checkout", 1, TimeSpan.FromMinutes(2),
            new Uri("https://events.example.test"), new Uri("https://events.example.test/success"),
            new Uri("https://events.example.test/cancel"));
        RegistrationPaymentCheckoutDispatchResult first = await dispatcher.DispatchDueAsync(request, CancellationToken.None);
        await Assert.That(first.Completed).IsEqualTo(1);
        await Assert.That(calls.Count).IsEqualTo(1);
        var executed = new[] { a, b }.Single(value => value.TenantId == calls[0].Tenant);
        var foreign = new[] { a, b }.Single(value => value.TenantId != executed.TenantId);
        seed.ChangeTracker.Clear();
        var foreignEffect = await seed.CheckoutDispatchEffects.AsNoTracking()
            .SingleAsync(row => row.PaymentAttemptId == foreign.Id);
        await Assert.That(foreignEffect.Status).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(foreignEffect.ProcessingFence).IsEqualTo(0);
        await Assert.That((await seed.PaymentAttempts.AsNoTracking().SingleAsync(row => row.Id == executed.Id))
            .LastProviderRequestId).IsEqualTo(calls[0].Receipt);
        RegistrationPaymentCheckoutDispatchResult second = await dispatcher.DispatchDueAsync(request, CancellationToken.None);
        await Assert.That(second.Completed).IsEqualTo(1);
        await Assert.That(contexts.Count).IsEqualTo(2);
        await Assert.That(contexts[0]).IsNotEqualTo(contexts[1]);
        foreach (var call in calls)
        {
            var original = new[] { a, b }.Single(value => value.TenantId == call.Tenant);
            await Assert.That(call.Account).IsEqualTo(original.RecipientSnapshot.ExternalAccountId);
            await Assert.That(call.Session).IsEqualTo(original.ProviderCheckoutSessionId);
            seed.ChangeTracker.Clear();
            var settled = await seed.PaymentAttempts.AsNoTracking().SingleAsync(row => row.Id == original.Id);
            await Assert.That(settled.LastProviderRequestId).IsEqualTo(call.Receipt);
            await Assert.That(settled.ProviderIdempotencyKey).IsEqualTo(original.ProviderIdempotencyKey);
            var effect = await seed.CheckoutDispatchEffects.AsNoTracking().SingleAsync(row => row.PaymentAttemptId == original.Id);
            await Assert.That(effect.Status).IsEqualTo(OutboxMessageStatus.Completed);
            await Assert.That(effect.ProcessingFence).IsEqualTo(1);
            await Assert.That(effect.AttemptCount).IsEqualTo(1);
        }
        await Assert.That(parent.ServiceProvider.GetRequiredService<ITenantContextAccessor>().TenantId).IsNull();
    }

    [Test]
    public async Task RequestlessAdmissionHandsOriginalClaimsToSeparateImmutableRuntimeContexts()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var a = await SeedAsync(seed);
        var b = await SeedAsync(seed);
        await PostgresCheckoutDispatchContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        await GrantRuntimeAsync(seed);
        var global = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), 1);
        var runtime = new TenantExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime), 2);
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton<ITenantExploreDbContextFactory>(runtime);
        services.AddSingleton<IGlobalExploreDbContextFactory>(global);
        services.AddSingleton<TimeProvider>(new FixedTime(Now.AddSeconds(2)));
        services.AddCheckoutDispatchPersistence();
        services.AddScoped(provider => GenericOutboxExecutionScopeFactory.GetSelectedRuntimeContext(provider)
            ?? throw new InvalidOperationException("Checkout dependency constructed without tenant selection."));
        services.AddScoped<IRegistrationPaymentAttemptRepository, RegistrationPaymentAttemptRepository>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        { ValidateScopes = true, ValidateOnBuild = true });
        await using var parent = provider.CreateAsyncScope();
        await Assert.That(parent.ServiceProvider.GetRequiredService<ITenantContextAccessor>().TenantId).IsNull();
        var admission = parent.ServiceProvider.GetRequiredService<ICheckoutDispatchAdmission>();
        var factory = parent.ServiceProvider.GetRequiredService<ICheckoutDispatchRuntimeScopeFactory>();
        CheckoutDispatchClaim first = (await admission.ClaimDueDispatchEffectsAsync(
            "native-checkout", 1, Now.AddSeconds(1), TimeSpan.FromMinutes(2), CancellationToken.None)).Single();
        CheckoutDispatchClaim second = (await admission.ClaimDueDispatchEffectsAsync(
            "native-checkout", 1, Now.AddSeconds(1), TimeSpan.FromMinutes(2), CancellationToken.None)).Single();
        await Assert.That(first.TenantId != second.TenantId).IsTrue();
        await using var scopeA = await factory.CreateAsync(first, Now.AddSeconds(2), CancellationToken.None);
        await using var scopeB = await factory.CreateAsync(second, Now.AddSeconds(2), CancellationToken.None);
        await Assert.That(scopeA).IsNotNull();
        await Assert.That(scopeB).IsNotNull();
        var contextA = scopeA!.Services.GetRequiredService<ExploreDbContext>();
        var contextB = scopeB!.Services.GetRequiredService<ExploreDbContext>();
        await Assert.That(ReferenceEquals(contextA, contextB)).IsFalse();
        await Assert.That(contextA.TenantFilterTenantId).IsEqualTo(first.TenantId);
        await Assert.That(contextB.TenantFilterTenantId).IsEqualTo(second.TenantId);
        Assert.Throws<InvalidOperationException>(() =>
            scopeA.Services.GetRequiredService<ITenantContextAccessor>().SetTenant(second.TenantId));
        await Assert.That(await contextA.PaymentAttempts.AnyAsync(row => row.Id == second.PaymentAttemptId)).IsFalse();
        var repository = scopeA.Services.GetRequiredService<IRegistrationPaymentAttemptRepository>();
        var owned = await repository.GetClaimedAttemptAsync(first, Now.AddSeconds(2), CancellationToken.None);
        await Assert.That(owned!.Id).IsEqualTo(first.PaymentAttemptId);
        await Assert.That(owned.RegistrationOrderId).IsEqualTo(first.RegistrationOrderId);
        await Assert.That(await repository.CompleteCheckoutDispatchAsync(
            first, "cs_native_checkout", "req_native_checkout", Now.AddSeconds(3), CancellationToken.None)).IsTrue();
        await Assert.That(await repository.CompleteCheckoutDispatchAsync(
            first, "cs_duplicate", "req_duplicate", Now.AddSeconds(4), CancellationToken.None)).IsFalse();
        seed.ChangeTracker.Clear();
        var untouched = await seed.CheckoutDispatchEffects.AsNoTracking().SingleAsync(row => row.Id == second.EffectId);
        await Assert.That(untouched.Status).IsEqualTo(OutboxMessageStatus.Processing);
        await Assert.That(untouched.ProcessingLeaseToken).IsEqualTo(second.LeaseToken);
        await Assert.That(untouched.ProcessingFence).IsEqualTo(second.ProcessingFence);
        await Assert.That(untouched.ProcessingLeaseExpiresAt).IsEqualTo(second.LeaseExpiresAt);
        foreach (var value in new[] { a, b })
            await Assert.That((await seed.PaymentAttempts.AsNoTracking().SingleAsync(row => row.Id == value.Id))
                .ProviderIdempotencyKey).IsEqualTo(value.ProviderIdempotencyKey);
        await using var globalContext = await global.CreateDbContextAsync();
        foreach (string table in new[] { Table<PaymentAttempt>(seed), Table<CheckoutDispatchEffect>(seed) })
        {
            PostgresException? denial = await Assert.ThrowsAsync<PostgresException>(
                async () => await globalContext.Database.ExecuteSqlRawAsync($"SELECT * FROM {table}"));
            await Assert.That(denial?.SqlState).IsEqualTo("42501");
        }
    }

    [Test]
    [Arguments("revoked")]
    [Arguments("expired")]
    [Arguments("replaced")]
    [Arguments("tenant")]
    [Arguments("expiry")]
    [Arguments("attempt")]
    public async Task NativeHandoffDeniesStaleAndAlteredOriginalClaims(string invalidity)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        _ = await SeedAsync(seed);
        await PostgresCheckoutDispatchContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        await GrantRuntimeAsync(seed);
        var global = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), 1);
        var runtime = new TenantExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime), 1);
        var admission = new CheckoutDispatchAdmission(new CheckoutDispatchAdmissionDbContextFactory(global));
        CheckoutDispatchClaim claim = (await admission.ClaimDueDispatchEffectsAsync(
            "original", 1, Now.AddSeconds(1), TimeSpan.FromMinutes(2), CancellationToken.None)).Single();
        if (invalidity == "revoked")
            await new RegistrationPaymentAttemptRepository(seed).ParkDispatchAsync(
                claim, "revoked", Now.AddSeconds(2), CancellationToken.None);
        if (invalidity == "replaced")
            _ = await admission.ClaimDueDispatchEffectsAsync(
                "replacement", 1, Now.AddMinutes(3), TimeSpan.FromMinutes(2), CancellationToken.None);
        claim = invalidity switch
        {
            "tenant" => claim with { TenantId = Guid.CreateVersion7() },
            "expiry" => claim with { LeaseExpiresAt = claim.LeaseExpiresAt.AddTicks(1) },
            "attempt" => claim with { AttemptCount = claim.AttemptCount + 1 },
            _ => claim
        };
        await using var context = await runtime.CreateDbContextAsync(claim.TenantId);
        var repository = new RegistrationPaymentAttemptRepository(context);
        DateTime observation = invalidity == "expired" ? claim.LeaseExpiresAt : Now.AddSeconds(3);
        await Assert.That(await repository.GetClaimedAttemptAsync(claim, observation, CancellationToken.None)).IsNull();
        await Assert.That(await repository.CompleteCheckoutDispatchAsync(
            claim, "cs_denied", "req_denied", observation, CancellationToken.None)).IsFalse();
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton<ITenantExploreDbContextFactory>(runtime);
        services.AddSingleton<IGlobalExploreDbContextFactory>(global);
        services.AddSingleton<TimeProvider>(new FixedTime(Now.AddSeconds(3)));
        services.AddCheckoutDispatchPersistence();
        services.AddScoped<ExploreDbContext>(_ =>
            throw new InvalidOperationException("A denied claim constructed checkout dependencies."));
        await using var provider = services.BuildServiceProvider();
        await using var parent = provider.CreateAsyncScope();
        var factory = parent.ServiceProvider.GetRequiredService<ICheckoutDispatchRuntimeScopeFactory>();
        await using var denied = await factory.CreateAsync(claim, observation, CancellationToken.None);
        await Assert.That(denied).IsNull();
    }

    private static string Table<TEntity>(ExploreDbContext context) where TEntity : class
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))!;
        return $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
    }

    [Test]
    [Arguments("selected-tenant")]
    [Arguments("force-disabled")]
    [Arguments("policy-hidden")]
    [Arguments("restrictive-policy")]
    public async Task CheckoutAdmissionRejectsSelectedGlobalAndPolicyDriftBeforeClaiming(string drift)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var attempt = await SeedAsync(seed);
        await PostgresCheckoutDispatchContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        string table = Table<CheckoutDispatchEffect>(seed);
        const string policy = PostgresCheckoutDispatchContract.PolicyName;
        const string owner = PostgresCheckoutDispatchContract.OwnerRole;
        (string change, string restore) = drift switch
        {
            "force-disabled" => ($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY",
                $"ALTER TABLE {table} FORCE ROW LEVEL SECURITY"),
            "policy-hidden" => ($"ALTER POLICY {policy} ON {table} USING (false)",
                $"ALTER POLICY {policy} ON {table} USING (true)"),
            "restrictive-policy" => ($"CREATE POLICY checkout_deny ON {table} AS RESTRICTIVE FOR ALL TO {owner} USING (false)",
                $"DROP POLICY checkout_deny ON {table}"),
            _ => (string.Empty, string.Empty)
        };
        var global = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), 1);
        await using var observer = await global.CreateDbContextAsync();
        await observer.Database.OpenConnectionAsync();
        if (drift == "selected-tenant")
            await observer.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_tenant_id', {attempt.TenantId.ToString()}, false)");
        else
            await seed.Database.ExecuteSqlRawAsync(change);
        try
        {
            string function = PostgresCheckoutDispatchContract.ClaimFunctionSql(seed);
            await using var command = observer.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT * FROM {function}(@token, @owner, @claimed, @expiry)";
            command.Parameters.Add(new NpgsqlParameter("token", Guid.CreateVersion7()));
            command.Parameters.Add(new NpgsqlParameter("owner", "denied-claim"));
            command.Parameters.Add(new NpgsqlParameter("claimed", Now));
            command.Parameters.Add(new NpgsqlParameter("expiry", Now.AddMinutes(2)));
            // Observe the original SQLSTATE once, rather than EF retrying deliberate
            // prerequisite-state drift and wrapping the terminal provider exception.
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await using var reader = await command.ExecuteReaderAsync();
            });
            await Assert.That(denied!.SqlState).IsEqualTo(drift == "selected-tenant" ? "42501" : "55000");
            if (drift != "selected-tenant")
                await Assert.ThrowsAsync<PostgresException>(() =>
                    PostgresCheckoutDispatchContract.ApplyAsync(seed, fixture.NativeQueueEnrollment));
        }
        finally
        {
            if (drift == "selected-tenant")
                await observer.Database.ExecuteSqlRawAsync("SELECT set_config('app.current_tenant_id', '', false)");
            else
                await seed.Database.ExecuteSqlRawAsync(restore);
        }
        var retained = await seed.CheckoutDispatchEffects.AsNoTracking().SingleAsync(row => row.PaymentAttemptId == attempt.Id);
        await Assert.That(retained.Status).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(retained.ProcessingFence).IsEqualTo(0);
        var admission = new CheckoutDispatchAdmission(new CheckoutDispatchAdmissionDbContextFactory(global));
        var admitted = (await admission.ClaimDueDispatchEffectsAsync(
            "valid-control", 1, Now.AddSeconds(1), TimeSpan.FromMinutes(2), default)).Single();
        await Assert.That(admitted.PaymentAttemptId).IsEqualTo(attempt.Id);
    }

    private async Task GrantRuntimeAsync(ExploreDbContext context)
    {
        // Re-establish only this test's ordinary Runtime table privileges for each invocation.
        // Other native authority tests revoke the shared fixture login's grants after their own pass.
        // Native tenant policies remain unchanged, and Global receives no table privileges.
        string role = "\"" + fixture.NativeQueueEnrollment.RuntimeLogin.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        string schema = "\"" + context.Model.FindEntityType(typeof(PaymentAttempt))!.GetSchema()!
            .Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            GRANT USAGE ON SCHEMA {schema} TO {role};
            GRANT SELECT, UPDATE ON {Table<PaymentAttempt>(context)}, {Table<CheckoutDispatchEffect>(context)} TO {role};
            GRANT SELECT ON {Table<PaidOrderAcceptanceSnapshot>(context)}, {Table<PaidOrderAcceptanceLine>(context)},
                {Table<RegistrationMaterialChangeChoice>(context)} TO {role};
            GRANT SELECT, INSERT, UPDATE ON {Table<PaymentReconciliationEffect>(context)} TO {role};
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingProvider(
        PaymentAttempt original, List<(Guid Tenant, string Account, string Session, string Receipt)> calls)
        : IHostedCheckoutSessionCreator, IHostedCheckoutSessionRetriever, IPaymentIntentRetriever
    {
        public Task<HostedCheckoutCreateResult> CreateAsync(HostedCheckoutCreateRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Known-session recovery must not create another checkout.");

        public Task<HostedCheckoutRetrieveResult> RetrieveAsync(HostedCheckoutRetrieveRequest request, CancellationToken cancellationToken)
        {
            if (request.ExternalAccountId != original.RecipientSnapshot.ExternalAccountId ||
                request.ProviderCheckoutSessionId != original.ProviderCheckoutSessionId || request.ProviderCode != original.ProviderCode)
                throw new InvalidOperationException("Provider request does not belong to its selected Runtime tenant.");
            string receipt = $"req_{original.Id:N}";
            calls.Add((original.TenantId, request.ExternalAccountId, request.ProviderCheckoutSessionId, receipt));
            return Task.FromResult(HostedCheckoutRetrieveResult.Succeeded(new HostedCheckoutSession(
                request.ProviderCheckoutSessionId, null, HostedCheckoutSessionStatus.Complete, HostedCheckoutPaymentStatus.Paid,
                $"pi_{original.Id:N}", Now.AddMinutes(30), 1_000, "EUR"), "req_session"));
        }

        public Task<PaymentIntentRetrieveResult> RetrievePaymentIntentAsync(PaymentIntentRetrieveRequest request, CancellationToken cancellationToken)
        {
            if (request.ExternalAccountId != original.RecipientSnapshot.ExternalAccountId ||
                request.PaymentIntentId != $"pi_{original.Id:N}" || request.ProviderCode != original.ProviderCode)
                throw new InvalidOperationException("Payment retrieval crossed the admitted provider account.");
            return Task.FromResult(PaymentIntentRetrieveResult.Succeeded(
                new PaymentIntentObservation(request.PaymentIntentId, 1_000, "EUR", 75, PaymentIntentStatus.Succeeded),
                $"req_{original.Id:N}"));
        }
    }

    private static async Task<PaymentAttempt> SeedAsync(ExploreDbContext context, bool knownUnknown = false)
    {
        var tenant = new Tenant
        {
            FullName = "Checkout authority",
            Slug = $"checkout-{Guid.CreateVersion7():N}",
            TenantStatusId = 2,
            TenantStatus = null!
        };
        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"checkout-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Checkout",
                LastName = "Authority"
            }
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Checkout merchant" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id
        };
        context.Add(actor);
        await context.SaveChangesAsync();
        var target = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = "Checkout authority",
            Subtitle = "",
            Description = "",
            TenantId = tenant.Id,
            Tenant = null!,
            ActorId = actor.Id,
            Actor = null!,
            OrganizerActorId = actor.Id,
            EventTypeId = 1,
            AudienceGenderId = 1,
            AudienceAgeId = 1,
            EventFormatId = 1,
            EventFormat = null!,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventStatus = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var catalog = EventTicketCatalogVersion.Create(tenant.Id, target.Id, "EUR", 1);
        var ticket = EventTicketType.Create(Guid.CreateVersion7(), tenant.Id, catalog.Id, "Admission", "EUR",
            TicketPricingModeEnum.Fixed, Money.Create(1_000, "EUR"), null, null, ParticipantDataCollectionModeEnum.None,
            null, null, null, false, false, null, null, null, null);
        catalog.AddTicketType(ticket, null);
        catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, tenant.Id, target.Id, 1));
        catalog.UpdateCommercialDisclosures("Checkout merchant", "Refund policy", "support@example.test");
        catalog.Publish();
        var order = RegistrationOrder.Create(tenant.Id, target.Id, user.Id, actor.Id,
            BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, null),
            null, null, "EUR", Now, Now.AddMinutes(30));
        order.AddLine(RegistrationOrderLine.Create(catalog, ticket, order.Id, 1, null, null));
        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("EUR", 1_000, 75, 925, 0));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, Now);
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, Now);
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingPayment, Now);
        context.AddRange(target, catalog, order);
        await context.SaveChangesAsync();
        var recipient = OrganizerPaymentRecipientSnapshot.Create(tenant.Id, actor.Id, Guid.CreateVersion7(),
            "stripe", "payment-platform", $"acct_{actor.Id:N}", "BE", "EUR", Guid.CreateVersion7(), null, Now);
        string composition = $"checkout-{Guid.CreateVersion7():N}";
        var attempt = PaymentAttempt.Create(Guid.CreateVersion7(), tenant.Id, order.Id, recipient,
            "OrganizerDirect", "2026-08-20.acacia", composition, Money.Create(1_000, "EUR"),
            Money.Create(75, "EUR"), Money.Create(0, "EUR"), $"checkout:{tenant.Id:N}:{order.Id:N}", Now, Now.AddMinutes(30));
        attempt.AssignCampaignCursor(1);
        attempt.AttachAcceptance(PaidAcceptanceTestFacts.Create(tenant.Id, order.Id, target.Id, composition,
            recipient.InstancePolicyVersionId, 1_000, 75, 0, Now, recipient: recipient));
        if (knownUnknown)
        {
            attempt.MarkRequiresAction($"cs_{attempt.Id:N}", Now.AddSeconds(1), "req_original");
            attempt.MarkUnknown(Now.AddSeconds(2), "req_unknown");
        }
        context.AddRange(attempt, CheckoutDispatchEffect.Create(attempt, Now));
        await context.SaveChangesAsync();
        return attempt;
    }
}
