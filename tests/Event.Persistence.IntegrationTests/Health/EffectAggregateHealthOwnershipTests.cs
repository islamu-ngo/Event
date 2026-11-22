using System.Security.Cryptography;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Services.Webhooks;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Health;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class EffectAggregateHealthOwnershipTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime ObservedAt = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task GlobalWebhookHealth_CountsDueAndExpiredLeasesAcrossTwoTenants()
    {
        await fixture.ResetAsync();
        await SeedWebhookEffectsAsync();
        var reader = new IncomingWebhookEffectHealthReader(CreateGlobalFactory(), fixture.NativeQueueEnrollment);
        var snapshot = await reader.ReadAsync(ObservedAt, CancellationToken.None);

        // Tenant A contributes two due and one stale; tenant B contributes three due and two stale.
        await Assert.That(snapshot.DueEffects).IsEqualTo(5);
        await Assert.That(snapshot.StaleLeases).IsEqualTo(3);
    }

    [Test]
    public async Task GlobalFairReturnHealth_EmptyQueueRemainsObservableWithoutTenantSelection()
    {
        await fixture.ResetAsync();
        var reader = new FairReturnOrchestrationHealthReader(CreateGlobalFactory(), fixture.NativeQueueEnrollment);
        var health = await reader.ReadAsync(ObservedAt, CancellationToken.None);

        await Assert.That(health.Pending).IsEqualTo(0);
        await Assert.That(health.Processing).IsEqualTo(0);
        await Assert.That(health.Unknown).IsEqualTo(0);
        await Assert.That(health.DeadLettered).IsEqualTo(0);
        await Assert.That(health.OldestPendingAt).IsNull();
    }

    [Test]
    public async Task GlobalOperationalObserver_CannotReadRetainedWebhookContent()
    {
        await fixture.ResetAsync();
        await SeedWebhookEffectsAsync();
        await using var context = await CreateGlobalFactory().CreateDbContextAsync();

        // Execute against the actual Global connector without an EF tenant predicate masking the boundary.
        string table = context.Model.FindEntityType(typeof(IncomingWebhookMessage))!.GetSchemaQualifiedTableName()!;
        PostgresException? denial = await Assert.ThrowsAsync<PostgresException>(
            async () => await context.Database.ExecuteSqlRawAsync($"SELECT * FROM {table}"));

        await Assert.That(denial?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    private GlobalExploreDbContextFactory CreateGlobalFactory() =>
        new(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);

    [Test]
    public async Task GlobalOperationalObserver_CannotReadEffectOwnersOrMutateStatus()
    {
        await fixture.ResetAsync();
        await using var context = await CreateGlobalFactory().CreateDbContextAsync();
        string webhook = context.Model.FindEntityType(typeof(IncomingWebhookEffectOutbox))!.GetSchemaQualifiedTableName()!;
        string fairReturn = context.Model.FindEntityType(typeof(FairReturnOrchestrationEffect))!.GetSchemaQualifiedTableName()!;
        foreach (string command in new[]
        {
            $"SELECT tenant_id FROM {webhook}",
            $"SELECT waitlist_payment_intent_id FROM {fairReturn}",
            $"UPDATE {webhook} SET status = status WHERE FALSE",
            $"UPDATE {fairReturn} SET status_id = status_id WHERE FALSE"
        })
        {
            PostgresException? denial = await Assert.ThrowsAsync<PostgresException>(
                async () => await context.Database.ExecuteSqlRawAsync(command));
            await Assert.That(denial?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    [Test]
    public async Task GlobalOperationalObserver_RejectsTableWideGrantDrift()
    {
        await fixture.ResetAsync();
        await using var context = await CreateGlobalFactory().CreateDbContextAsync();
        await context.Database.OpenConnectionAsync();
        await PostgresEffectAggregateHealthReadPermissions.RequireReadyAsync(
            context, fixture.NativeQueueEnrollment, CancellationToken.None);
        await using var migration = fixture.CreateDbContext();
        string table = context.Model.FindEntityType(typeof(IncomingWebhookEffectOutbox))!.GetSchemaQualifiedTableName()!;
        string global = "\"" + fixture.NativeQueueEnrollment.GlobalLogin.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        await migration.Database.ExecuteSqlRawAsync($"GRANT SELECT ON {table} TO {global}");
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresEffectAggregateHealthReadPermissions.RequireReadyAsync(
                    context, fixture.NativeQueueEnrollment, CancellationToken.None));
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync($"REVOKE SELECT ON {table} FROM {global}");
            // Table SELECT revocation also removes existing column SELECT grants.
            await migration.Database.ExecuteSqlRawAsync(
                $"GRANT SELECT (status, next_attempt_at, processing_lease_expires_at) ON {table} TO {global}");
        }
        await PostgresEffectAggregateHealthReadPermissions.RequireReadyAsync(
            context, fixture.NativeQueueEnrollment, CancellationToken.None);
    }

    [Test]
    public async Task GlobalOperationalObserver_RejectsTenantBoundSession()
    {
        await fixture.ResetAsync();
        await using var context = await CreateGlobalFactory().CreateDbContextAsync();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_catalog.set_config('app.current_tenant_id', @tenant, false)";
        var tenant = command.CreateParameter();
        tenant.ParameterName = "tenant";
        tenant.Value = Guid.CreateVersion7().ToString("D");
        command.Parameters.Add(tenant);
        await command.ExecuteScalarAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await PostgresEffectAggregateHealthReadPermissions.RequireReadyAsync(
                context, fixture.NativeQueueEnrollment, CancellationToken.None));
    }

    [Test]
    public async Task GlobalFairReturnHealth_CombinesTenantStatusesAndFindsOldestPending()
    {
        await fixture.ResetAsync();
        await SeedFairReturnHealthAsync();
        var reader = new FairReturnOrchestrationHealthReader(CreateGlobalFactory(), fixture.NativeQueueEnrollment);
        var health = await reader.ReadAsync(ObservedAt, CancellationToken.None);

        // A: pending 1, processing 1, unknown-payment 1, dead-lettered 2.
        // B: pending 2, processing 3, unknown-payment 1, dead-lettered 3.
        // Unknown-payment failures are included in dead-lettered; completed rows are excluded.
        await Assert.That(health.Pending).IsEqualTo(3);
        await Assert.That(health.Processing).IsEqualTo(4);
        await Assert.That(health.Unknown).IsEqualTo(2);
        await Assert.That(health.DeadLettered).IsEqualTo(5);
        await Assert.That(health.OldestPendingAt).IsEqualTo((DateTime?)ObservedAt.AddHours(-3));
    }

    private async Task SeedFairReturnHealthAsync()
    {
        await using var context = fixture.CreateDbContext();
        context.BindTenantLease();
        for (int tenantIndex = 0; tenantIndex < 2; tenantIndex++)
        {
            var tenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                FullName = "Fair return health",
                Slug = $"fair-return-health-{Guid.CreateVersion7():N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            };
            var user = new User
            {
                Pii = new UserPii
                {
                    Email = $"health-{Guid.CreateVersion7():N}@example.test",
                    FirstName = "Health",
                    LastName = "Observer"
                }
            };
            context.AddRange(tenant, user);
            await context.SaveChangesAsync();
            var actor = new Actor
            {
                Pii = new ActorPii { DisplayName = "Health merchant" },
                ActorTypeId = 1,
                ActorType = null!,
                UserId = user.Id
            };
            context.Add(actor);
            await context.SaveChangesAsync();
            var target = new Explore.Domain.Event(EventStatusEnum.Draft)
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = null!,
                Title = "Fair return health",
                Subtitle = "",
                Description = "",
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
                TicketPricingModeEnum.Fixed, Money.Create(1_000, "EUR"), null, null,
                ParticipantDataCollectionModeEnum.None, null, null, null, false, false, null, null, null, null);
            catalog.AddTicketType(ticket, null);
            catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, tenant.Id, target.Id, 1));
            catalog.UpdateCommercialDisclosures("Health merchant", "Refund policy", "support@example.test");
            catalog.Publish();
            context.AddRange(target, catalog);
            await context.SaveChangesAsync();
            var recipient = OrganizerPaymentRecipientSnapshot.Create(tenant.Id, actor.Id, Guid.CreateVersion7(),
                "stripe", "payment-platform", $"acct_{actor.Id:N}", "BE", "EUR", Guid.CreateVersion7(), null, ObservedAt.AddDays(-1));

            foreach (var state in new[]
                     {
                         (Kind: "pending", Count: tenantIndex + 1),
                         (Kind: "processing", Count: tenantIndex == 0 ? 1 : 3),
                         (Kind: "unknown-payment", Count: 1),
                         (Kind: "dead-lettered", Count: tenantIndex + 1),
                         (Kind: "completed", Count: 1)
                     })
            {
                for (int index = 0; index < state.Count; index++)
                {
                    DateTime createdAt = state.Kind == "pending"
                        ? ObservedAt.AddHours(-2 - tenantIndex).AddMinutes(index)
                        : ObservedAt.AddHours(-6).AddMinutes(index);
                    var seller = CreateFairReturnPayment(context, catalog, ticket, user.Id, actor.Id, recipient);
                    var buyer = CreateFairReturnPayment(context, catalog, ticket, user.Id, actor.Id, recipient);
                    await Assert.That(seller.Payment.MarkSucceeded(
                        $"pi_{seller.Payment.Id:N}", ObservedAt.AddHours(-8), "health-settled")).IsTrue();
                    await context.SaveChangesAsync();
                    var refund = RefundAttempt.Create(Guid.CreateVersion7(), tenant.Id, seller.Payment.Id,
                        seller.Payment.AcceptanceSnapshot!, recipient.ExternalAccountId,
                        seller.Payment.ProviderPaymentId!, $"health-refund:{seller.Payment.Id:N}", 1_000, createdAt);
                    Guid purchasePolicySnapshotId = Guid.CreateVersion7();
                    string terms = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("health-terms")));
                    string entitlement = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("health-admission")));
                    var supply = FairReturnSupplyUnit.Create(Guid.CreateVersion7(), tenant.Id, target.Id, ticket.Id,
                        catalog.Id, purchasePolicySnapshotId, "EUR", terms, entitlement, 1_000, 1,
                        seller.Order.Lines.Single().Id, createdAt);
                    var entry = EventWaitlistEntry.Enqueue(Guid.CreateVersion7(), tenant.Id, target.Id, ticket.Id,
                        catalog.Id, purchasePolicySnapshotId, buyer.Order.Id, buyer.Order.Lines.Single().Id,
                        Guid.CreateVersion7(), user.Id, "EUR", terms, entitlement, 1_000, 1, 1, createdAt);
                    var binding = FairReturnSourceBinding.Create(Guid.CreateVersion7(), supply, entry, createdAt);
                    supply.Bind(createdAt);
                    var intent = WaitlistPaymentIntent.Create(Guid.CreateVersion7(), binding, refund, buyer.Payment.Id,
                        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), createdAt);
                    var effect = FairReturnOrchestrationEffect.Create(Guid.CreateVersion7(), intent, 5, createdAt);
                    if (state.Kind != "pending")
                    {
                        await Assert.That(effect.TryClaim("health-fair-return", ObservedAt.AddMinutes(-1),
                            TimeSpan.FromMinutes(10))).IsTrue();
                        if (state.Kind == "completed")
                            await Assert.That(effect.Complete(effect.ProcessingFence, ObservedAt)).IsTrue();
                        else if (state.Kind is "unknown-payment" or "dead-lettered")
                            await Assert.That(effect.Fail(effect.ProcessingFence,
                                state.Kind == "unknown-payment" ? "REPLACEMENT_PAYMENT_UNKNOWN" : "HEALTH_PERMANENT",
                                false, ObservedAt, ObservedAt.AddMinutes(1)))
                                .IsEqualTo(FairReturnOrchestrationEffectStatus.DeadLettered);
                    }
                    context.AddRange(refund, supply, entry, binding, intent, effect);
                    await context.SaveChangesAsync();
                }
            }
        }
    }

    private static (RegistrationOrder Order, PaymentAttempt Payment) CreateFairReturnPayment(
        ExploreDbContext context, EventTicketCatalogVersion catalog, EventTicketType ticket,
        Guid userId, Guid actorId, OrganizerPaymentRecipientSnapshot recipient)
    {
        DateTime createdAt = ObservedAt.AddDays(-1);
        var order = RegistrationOrder.Create(catalog.TenantId, catalog.EventId, userId, actorId,
            BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, null),
            null, null, "EUR", createdAt, ObservedAt.AddDays(1));
        order.AddLine(RegistrationOrderLine.Create(catalog, ticket, order.Id, 1, null, null));
        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("EUR", 1_000, 75, 925, 0));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, createdAt);
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, createdAt);
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingPayment, createdAt);
        var paymentRecipient = OrganizerPaymentRecipientSnapshot.Create(
            recipient.TenantId, recipient.OrganizerActorId, recipient.OrganizerPaymentProviderConnectionId,
            recipient.ProviderCode, recipient.ConnectPlatformId, recipient.ExternalAccountId,
            recipient.MerchantCountryCode, recipient.CurrencyCode, recipient.InstancePolicyVersionId,
            recipient.TenantPolicyVersionId, recipient.SnapshottedAt);
        string composition = $"health-{Guid.CreateVersion7():N}";
        var payment = PaymentAttempt.Create(Guid.CreateVersion7(), catalog.TenantId, order.Id, paymentRecipient,
            "OrganizerDirect", "2026-08-20.acacia", composition, Money.Create(1_000, "EUR"),
            Money.Create(75, "EUR"), Money.Create(0, "EUR"), $"health-payment:{order.Id:N}", createdAt, ObservedAt.AddDays(1));
        payment.AssignCampaignCursor(createdAt.Ticks);
        payment.AttachAcceptance(PaidAcceptanceTestFacts.Create(catalog.TenantId, order.Id, catalog.EventId,
            composition, recipient.InstancePolicyVersionId, 1_000, 75, 0, createdAt, recipient: paymentRecipient));
        context.AddRange(order, payment);
        return (order, payment);
    }

    private async Task SeedWebhookEffectsAsync()
    {
        await using var context = fixture.CreateDbContext();
        context.BindTenantLease();
        for (int tenantIndex = 0; tenantIndex < 2; tenantIndex++)
        {
            var tenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                FullName = $"Operational Health Tenant {tenantIndex}",
                Slug = $"effect-health-{Guid.CreateVersion7():N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            };
            context.Tenants.Add(tenant);
            for (int index = 0; index <= tenantIndex; index++)
            {
                AddWebhookEffect(context, tenant.Id);
                var stale = AddWebhookEffect(context, tenant.Id);
                stale.Claim("health-expired", Guid.CreateVersion7(), ObservedAt, ObservedAt.AddMinutes(-2));
            }

            var dueRetry = AddWebhookEffect(context, tenant.Id);
            ScheduleRetry(dueRetry, ObservedAt);
            var futureRetry = AddWebhookEffect(context, tenant.Id);
            ScheduleRetry(futureRetry, ObservedAt.AddMinutes(10));

            var active = AddWebhookEffect(context, tenant.Id);
            active.Claim("health-active", Guid.CreateVersion7(), ObservedAt.AddMinutes(2), ObservedAt.AddMinutes(-1));

            var completed = AddWebhookEffect(context, tenant.Id);
            Guid completedToken = Guid.CreateVersion7();
            completed.Claim("health-completed", completedToken, ObservedAt.AddMinutes(2), ObservedAt.AddMinutes(-1));
            completed.Complete(completedToken, completed.ProcessingFence, completed.ProcessingGeneration, ObservedAt);

            var deadLettered = AddWebhookEffect(context, tenant.Id);
            Guid deadLetterToken = Guid.CreateVersion7();
            deadLettered.Claim("health-dead-letter", deadLetterToken, ObservedAt.AddMinutes(2), ObservedAt.AddMinutes(-1));
            deadLettered.DeadLetter(deadLetterToken, deadLettered.ProcessingFence,
                deadLettered.ProcessingGeneration, "health_permanent", "Permanent test outcome.", ObservedAt);
        }
        await context.SaveChangesAsync();
    }

    private static void ScheduleRetry(IncomingWebhookEffectOutbox effect, DateTime nextAttemptAt)
    {
        Guid token = Guid.CreateVersion7();
        effect.Claim("health-retry", token, ObservedAt.AddMinutes(2), ObservedAt.AddMinutes(-2));
        effect.ScheduleRetry(token, effect.ProcessingFence, effect.ProcessingGeneration,
            "health_retry", "Retryable test outcome.", nextAttemptAt, ObservedAt.AddMinutes(-1));
    }

    private static IncomingWebhookEffectOutbox AddWebhookEffect(ExploreDbContext context, Guid tenantId)
    {
        string decisionId = $"health-{Guid.CreateVersion7():N}";
        byte[] payload = Encoding.UTF8.GetBytes($"{{\"decision\":\"{decisionId}\"}}");
        string hash = "sha256:" + Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        DateTime receivedAt = ObservedAt.AddHours(-1);
        const string effectKind = CoopDecisionIncomingWebhookHandler.StableEffectKind;
        var message = IncomingWebhookMessage.CreateVerified(
            tenantId, "coop", decisionId, decisionId, effectKind, payload, hash,
            "application/json", "utf-8", null, receivedAt, receivedAt,
            receivedAt.AddDays(14), "webhook-health-test-v1",
            receivedAt.AddDays(30), receivedAt.AddDays(90),
            receivedAt.AddDays(14), receivedAt.AddDays(30));
        var effect = IncomingWebhookEffectOutbox.CreatePending(
            tenantId, message.Id, "coop", decisionId, effectKind, hash, receivedAt);
        context.IncomingWebhookMessages.Add(message);
        context.IncomingWebhookEffectOutboxes.Add(effect);
        return effect;
    }
}
