using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Services.Registration;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Exercises the registered acceptance graph against persisted legal authority.
/// This is freshness admission, not an HTTP principal or payment-provider test.
/// </summary>
[NotInParallel]
public sealed class PaidOrderAcceptanceFreshnessNativeAuthenticationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task AcceptedPersistedFactsBecomeStaleWhenDirectoryIdentityLosesPaidCommerceReadiness()
    {
        await using var host = await LocalAdmissionWebApplicationFactory.CreateAsync();
        ConfigureCommerce(host.Services);
        Guid orderId = await SeedOrderAsync(host);
        Guid attemptId;
        Guid acceptedStamp;
        Guid acceptedDirectoryRevision;
        string acceptedRevision;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var orders = services.GetRequiredService<IRegistrationInventoryRepository>();
            var order = (await orders.GetOrderWithLinesAsync(orderId, PlatformDefaults.DefaultTenantId, default))!;
            var acceptances = services.GetRequiredService<IPaidOrderAcceptanceService>();
            var described = await acceptances.DescribeAsync(order, default);
            await Assert.That(described.FailureCode).IsNull();
            await Assert.That(described.Disclosure).IsNotNull();
            var disclosure = described.Disclosure!;
            await Assert.That(disclosure.CurrencyCode).IsEqualTo("EUR");
            await Assert.That(disclosure.OrganizerAmountMinor).IsEqualTo(1_000L);
            await Assert.That(disclosure.PlatformFeeMinor).IsEqualTo(75L);
            await Assert.That(disclosure.PlatformContributionMinor).IsEqualTo(125L);
            await Assert.That(disclosure.TotalMinor).IsEqualTo(1_125L);
            var accepted = await acceptances.AcceptAsync(order,
                new PaidOrderAcceptanceAcknowledgementDto { Acknowledged = true, DisclosureRevision = disclosure.DisclosureRevision },
                DateTime.UtcNow, default);
            await Assert.That(accepted.Snapshot).IsNotNull();
            var snapshot = accepted.Snapshot!;
            acceptedStamp = order.ConcurrencyStamp;
            acceptedDirectoryRevision = snapshot.TenantDirectoryOperatorRevisionId;
            acceptedRevision = snapshot.DisclosureRevision;
            await Assert.That(acceptedStamp).IsNotEqualTo(Guid.Empty);
            var recipient = OrganizerPaymentRecipientSnapshot.Create(order.TenantId, snapshot.OrganizerActorId,
                snapshot.OrganizerPaymentProviderConnectionId, "stripe", snapshot.ConnectPlatformId,
                snapshot.ExternalAccountId, "BE", "EUR", snapshot.InstancePolicyVersionId, snapshot.TenantPolicyVersionId,
                DateTime.UtcNow);
            var descriptor = services.GetRequiredService<IPaymentProviderDescriptor>().Describe();
            var attempt = PaymentAttempt.Create(Guid.CreateVersion7(), order.TenantId, order.Id, recipient,
                descriptor.ProfileCode, descriptor.ApiRevision, acceptedStamp.ToString("N"), Money.Create(1_000, "EUR"),
                Money.Create(75, "EUR"), Money.Create(125, "EUR"), $"checkout:freshness:{Guid.CreateVersion7():N}",
                DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30));
            attempt.AttachAcceptance(snapshot);
            var database = services.GetRequiredService<ExploreDbContext>();
            database.PaymentAttempts.Add(attempt);
            await database.SaveChangesAsync();
            attemptId = attempt.Id;
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var database = services.GetRequiredService<ExploreDbContext>();
            var attempt = await database.PaymentAttempts.Include(value => value.AcceptanceSnapshot)
                .ThenInclude(value => value!.Lines).SingleAsync(value => value.Id == attemptId);
            await Assert.That(attempt.AcceptanceSnapshot!.CompositionRevision).IsEqualTo(acceptedStamp.ToString("N"));
            await Assert.That(await services.GetRequiredService<IPaidOrderAcceptanceFreshnessService>()
                .IsCurrentAsync(attempt, default)).IsTrue();

            // A separate committed writer changes current authority, leaving the accepted evidence intact.
            await using (var writer = host.CreateDatabase())
            {
                writer.TenantContext = services.GetRequiredService<ITenantContext>();
                writer.BindTenantLease();
                var document = await writer.TenantSettingsDocuments.SingleAsync(value =>
                    value.TenantId == PlatformDefaults.DefaultTenantId &&
                    value.DocumentKey == SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity);
                var incomplete = DirectoryIdentity() with { TermsUrl = null };
                document.UpdatePayload(document.SchemaVersion, document.DefaultsVersion,
                    JsonSerializer.Serialize(incomplete, JsonOptions));
                await writer.SaveChangesAsync();
                await Assert.That(document.ConcurrencyStamp).IsNotEqualTo(acceptedDirectoryRevision);
            }

            var order = (await services.GetRequiredService<IRegistrationInventoryRepository>()
                .GetOrderWithLinesAsync(orderId, PlatformDefaults.DefaultTenantId, default))!;
            var refused = await services.GetRequiredService<IPaidOrderAcceptanceService>().DescribeAsync(order, attemptId, default);
            await Assert.That(refused.FailureCode).IsEqualTo("tenant_directory_operator_identity_unavailable");
            await Assert.That(refused.Disclosure).IsNull();
            await Assert.That(refused.Authority).IsNull();
            await Assert.That(await services.GetRequiredService<IPaidOrderAcceptanceFreshnessService>()
                .IsCurrentAsync(attempt, default)).IsFalse();
            await Assert.That(attempt.AcceptanceSnapshot.DisclosureRevision).IsEqualTo(acceptedRevision);
            await Assert.That(attempt.AcceptanceSnapshot.TenantDirectoryOperatorRevisionId).IsEqualTo(acceptedDirectoryRevision);
            await Assert.That(order.ConcurrencyStamp).IsEqualTo(acceptedStamp);
        }
    }

    private static void ConfigureCommerce(IServiceProvider services)
    {
        var governance = services.GetRequiredService<IOptions<PaidCheckoutGovernanceOptions>>().Value;
        governance.ComplaintOwner = "Trust and Safety";
        governance.RefundOwner = "Payments Operations";
        governance.DisputeOwner = "Dispute Operations";
        governance.ReconciliationOwner = "Payment Reconciliation";
        governance.ActivationStatus = "approved";
        governance.RefundPolicyLanguageTag = "en-GB";
        governance.StatementDescriptor = "EXAMPLE EVENT";
        governance.ChargeType = "direct-charge";
        var commerce = services.GetRequiredService<IOptions<OrganizerPaymentCommerceOptions>>().Value;
        commerce.ProviderCode = "stripe";
        commerce.ConnectPlatformId = "platform-test-eu";
    }

    private static async Task<Guid> SeedOrderAsync(LocalAdmissionWebApplicationFactory host)
    {
        await using var database = host.CreateDatabase();
        database.BindTenantLease();
        Guid tenant = PlatformDefaults.DefaultTenantId;
        DateTime now = DateTime.UtcNow;
        Guid userId = (await database.Users.FirstAsync()).Id;
        var actor = new ActorBuilder().WithActorType(ActorTypeEnum.Organization).Build();
        var target = new EventBuilder().WithTenantId(tenant).WithActorId(actor.Id).Build();
        target.OrganizerActorId = actor.Id;
        target.FirstSessionStartUtc = new DateTimeOffset(now.AddDays(10));
        target.LastSessionEndUtc = new DateTimeOffset(now.AddDays(10).AddHours(3));
        target.EventTimeZoneId = "Europe/Brussels";
        var catalog = EventTicketCatalogVersion.Create(tenant, target.Id, "EUR", 1);
        catalog.UpdateCommercialDisclosures("Example Organizer, legal merchant for this order", "Refund policy", "support@example.test");
        var ticket = EventTicketType.Create(Guid.CreateVersion7(), tenant, catalog.Id, "Admission", "EUR",
            TicketPricingModeEnum.Fixed, Money.Create(1_000, "EUR"), null, null, ParticipantDataCollectionModeEnum.None,
            null, null, null, false, false, null, null, null, null);
        catalog.AddTicketType(ticket, null);
        catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, tenant, target.Id, 1));
        catalog.Publish();
        var order = RegistrationOrder.Create(
            id: Guid.CreateVersion7(), tenantId: tenant, eventId: target.Id, accountUserId: userId,
            purchaserActorId: actor.Id, bookingPartyType: BookingPartyTypeEnum.Individual,
            ticketCatalogVersionId: catalog.Id,
            participationSnapshot: RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1,
                GuestRecoveryPolicyEnum.VerifiedEmailRequired), registrationWorkflowVersionId: null,
            guestAccessTokenHash: null, currencyCode: "EUR", createdAt: now, expiresAt: now.AddMinutes(30));
        order.AddLine(RegistrationOrderLine.Create(catalog, ticket, order.Id, 1, null, null));
        var previousContribution = await database.PlatformContributionSettings.SingleAsync(value => value.IsActive);
        var contribution = previousContribution.CreateRevision(true, "Support the platform", "Optional contribution",
            [PlatformContributionOption.Create(0, 0, true), PlatformContributionOption.Create(1_250, 1, false)]);
        order.SetPlatformContribution(RegistrationOrderPlatformContribution.CreateOrNull(order.Id, tenant, contribution,
            1_250, 1_000, "EUR"));
        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("EUR", 1_000, 75, 925, 125));
        var previousPolicy = await database.PaidEventPolicyVersions.SingleOrDefaultAsync(value => value.TenantId == null && value.IsActive)
            ?? PaidEventPolicyVersion.CreateDefaultInstance();
        var policy = previousPolicy.CreateRevision(true, previousPolicy.AllowedOrganizerKinds, false, ["EUR"], "EUR",
            previousPolicy.RefundProtections, [], false, null);
        var directory = TenantDirectoryOperatorIdentityDocumentDefaults.Create(tenant, DirectoryIdentity());
        var connection = OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), tenant, actor.Id, "stripe",
            "platform-test-eu", "acct_freshness", now);
        connection.ApplyReadiness(OrganizerPaymentProviderReadinessObservation.Create("BE", ChargeCapabilityState.Active,
            ProviderRequirementsState.Satisfied, ["EUR"], now, "freshness-ready"));
        var identity = new InstanceOperatorIdentitySettings
        {
            OperatorId = Guid.CreateVersion7(), Revision = Guid.CreateVersion7(), PublicName = "Independent Operator",
            LegalName = "Independent Operator ASBL", OperatorKindCode = "registered_organization",
            JurisdictionCountryCode = "BE", RegistrationIdentifier = "BE 0123.456.789",
            PublicContactEmail = "complaints@example.test", WebsiteUrl = "https://events.example.test",
            OfficialOrigin = "https://events.example.test", LegalNoticeUrl = "https://events.example.test/legal",
            TermsUrl = "https://events.example.test/terms", PrivacyUrl = "https://events.example.test/privacy"
        };
        database.AddRange(actor, target, catalog, contribution, order, policy, directory, connection,
            PaidCheckoutSaleControl.CreateActive(tenant, null, userId, now),
            new SystemSetting
            {
                Id = Guid.CreateVersion7(), SettingKey = InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                Value = JsonSerializer.Serialize(identity, JsonOptions), ValueType = SettingValueType.Json,
                Category = "OperatorIdentity", CreatedAt = now
            });
        await database.SaveChangesAsync();
        return order.Id;
    }

    private static TenantDirectoryOperatorIdentitySettings DirectoryIdentity() => new()
    {
        PublicName = "Community Events", LegalName = "Community Events ASBL", OperatorKindCode = "registered_organization",
        JurisdictionCountryCode = "BE", RegistrationIdentifier = "BE 0987.654.321", PublicContactEmail = "contact@example.test",
        LegalNoticeUrl = "https://example.test/legal", TermsUrl = "https://example.test/terms", PrivacyUrl = "https://example.test/privacy"
    };
}
