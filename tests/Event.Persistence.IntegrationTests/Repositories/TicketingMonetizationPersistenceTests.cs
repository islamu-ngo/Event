// ABOUTME: Source-only EF metadata, runtime seed, and tenant-isolation tests for ticketing monetization persistence.
// ABOUTME: Uses design-time Npgsql metadata and InMemory repositories without Docker or Testcontainers.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests.Repositories;

[Category("TicketingMonetizationPersistence")]
public sealed class TicketingMonetizationPersistenceTests
{
    [Test]
    public async Task EfModel_MapsParticipantAssignmentsAndRequiredAdmissionLinkage()
    {
        await using var context = CreateModelContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IEntityType participant = model.FindEntityType(typeof(RegistrationParticipant))!;
        IEntityType pii = model.FindEntityType(typeof(RegistrationParticipantPii))!;
        IEntityType assignment = model.FindEntityType(typeof(RegistrationTicketAssignment))!;
        IEntityType admission = model.FindEntityType(typeof(EventRegistration))!;

        await Assert.That(participant.GetTableName()).IsEqualTo("registration_participants");
        await Assert.That(pii.GetTableName()).IsEqualTo("registration_participant_pii");
        await Assert.That(assignment.GetTableName()).IsEqualTo("registration_ticket_assignments");
        await Assert.That(participant.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(participant.FindDeclaredQueryFilter(QueryFilterNames.SoftDelete)).IsNotNull();
        await Assert.That(pii.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(assignment.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(admission.FindProperty(nameof(EventRegistration.RegistrationParticipantId))!.IsNullable).IsFalse();
        await Assert.That(admission.FindProperty("UserId")).IsNull();
        await Assert.That(admission.FindProperty(nameof(EventRegistration.LinkedUserId))!.IsNullable).IsTrue();
        await Assert.That(participant.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(RegistrationOrder)
            && foreignKey.Properties.Select(property => property.Name).SequenceEqual([
                nameof(RegistrationParticipant.TenantId), nameof(RegistrationParticipant.RegistrationOrderId)])
            && foreignKey.DeleteBehavior == DeleteBehavior.Restrict)).IsTrue();
        await Assert.That(participant.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(RegistrationParticipant)
            && foreignKey.Properties.Select(property => property.Name).SequenceEqual([
                nameof(RegistrationParticipant.TenantId),
                nameof(RegistrationParticipant.RegistrationOrderId),
                nameof(RegistrationParticipant.GuardianParticipantId)])
            && foreignKey.DeleteBehavior == DeleteBehavior.Restrict)).IsTrue();
        await Assert.That(participant.GetKeys().Any(key => key.Properties.Select(property => property.Name).SequenceEqual([
            nameof(RegistrationParticipant.TenantId),
            nameof(RegistrationParticipant.RegistrationOrderId),
            nameof(RegistrationParticipant.Id)]))).IsTrue();
        await Assert.That(assignment.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(RegistrationOrderLine)
            && foreignKey.Properties.Select(property => property.Name).SequenceEqual([
                nameof(RegistrationTicketAssignment.TenantId),
                nameof(RegistrationTicketAssignment.RegistrationOrderId),
                nameof(RegistrationTicketAssignment.RegistrationOrderLineId)])
            && foreignKey.DeleteBehavior == DeleteBehavior.Restrict)).IsTrue();
        await Assert.That(assignment.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(RegistrationParticipant)
            && foreignKey.Properties.Select(property => property.Name).SequenceEqual([
                nameof(RegistrationTicketAssignment.TenantId),
                nameof(RegistrationTicketAssignment.RegistrationOrderId),
                nameof(RegistrationTicketAssignment.ParticipantId)])
            && foreignKey.DeleteBehavior == DeleteBehavior.Restrict)).IsTrue();
        await Assert.That(admission.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(RegistrationParticipant)
            && foreignKey.Properties.Select(property => property.Name).SequenceEqual([
                nameof(EventRegistration.TenantId),
                nameof(EventRegistration.RegistrationOrderId),
                nameof(EventRegistration.RegistrationParticipantId)])
            && foreignKey.DeleteBehavior == DeleteBehavior.Restrict)).IsTrue();
        await Assert.That(admission.GetIndexes().Any(index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(EventRegistration.TenantId),
                nameof(EventRegistration.EventSessionId),
                nameof(EventRegistration.RegistrationParticipantId)])
            && index.GetFilter() == "is_deleted = false")).IsTrue();
        await Assert.That(assignment.GetIndexes().Any(index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(RegistrationTicketAssignment.TenantId),
                nameof(RegistrationTicketAssignment.RegistrationOrderLineId),
                nameof(RegistrationTicketAssignment.Ordinal)]))).IsTrue();
    }

    [Test]
    public async Task EfModel_MapsTicketingIsolationHistoryAndMinorUnitConstraints()
    {
        await using var context = CreateModelContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        foreach (Type type in new[] { typeof(EventTicketCatalogVersion), typeof(EventTicketType), typeof(EventCapacityPool) })
        {
            IEntityType entity = model.FindEntityType(type)!;
            await Assert.That(entity.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
            await Assert.That(entity.FindDeclaredQueryFilter(QueryFilterNames.SoftDelete)).IsNotNull();
            await Assert.That(entity.FindProperty(nameof(EventTicketCatalogVersion.ConcurrencyStamp))!.IsConcurrencyToken).IsTrue();
        }

        await Assert.That(model.FindEntityType(typeof(TicketTypeEntitlement))!.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        IEntityType ticketType = model.FindEntityType(typeof(EventTicketType))!;
        await Assert.That(ticketType.FindProperty(nameof(EventTicketType.FixedPriceMinor))!.GetColumnType()).IsEqualTo("bigint");
        await Assert.That(ticketType.FindProperty(nameof(EventTicketType.MinimumPriceMinor))!.GetColumnType()).IsEqualTo("bigint");
        await Assert.That(ticketType.FindProperty(nameof(EventTicketType.SuggestedPriceMinor))!.GetColumnType()).IsEqualTo("bigint");

        IEntityType feePolicy = model.FindEntityType(typeof(PlatformFeePolicy))!;
        IEntityType? capacityHoldPolicy = model.FindEntityType(typeof(CapacityHoldPolicy));
        await Assert.That(capacityHoldPolicy).IsNotNull();
        await Assert.That(model.FindEntityType(typeof(EventCapacityPool))!.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(CapacityHoldPolicy))).IsTrue();
        IEntityType eventEntity = model.FindEntityType(typeof(DomainEvent))!;
        await Assert.That(eventEntity.FindNavigation(nameof(DomainEvent.TicketCatalogVersions))!.GetFieldName()).IsEqualTo("_ticketCatalogVersions");
        await Assert.That(eventEntity.FindNavigation(nameof(DomainEvent.CapacityPools))!.GetFieldName()).IsEqualTo("_capacityPools");
        await Assert.That(model.FindEntityType(typeof(EventTicketCatalogVersion))!.GetTableName()).IsEqualTo("event_ticket_catalog_versions");
        await Assert.That(model.FindEntityType(typeof(EventTicketType))!.GetTableName()).IsEqualTo("event_ticket_types");
        await Assert.That(model.FindEntityType(typeof(TicketTypeEntitlement))!.GetTableName()).IsEqualTo("ticket_type_entitlements");
        await Assert.That(model.FindEntityType(typeof(EventCapacityPool))!.GetTableName()).IsEqualTo("event_capacity_pools");
        await Assert.That(feePolicy.FindProperty(nameof(PlatformFeePolicy.FeeBasisPoints))!.GetColumnType()).IsEqualTo("integer");
        await Assert.That(model.FindEntityType(typeof(PlatformFeeFixedCharge))!.FindProperty(nameof(PlatformFeeFixedCharge.AmountMinor))!.GetColumnType()).IsEqualTo("bigint");
        await Assert.That(model.FindEntityType(typeof(PlatformContributionOption))!.FindProperty(nameof(PlatformContributionOption.ContributionBasisPoints))!.GetColumnType()).IsEqualTo("integer");
        await Assert.That(feePolicy.GetIndexes().Any(index => index.IsUnique && index.GetFilter() == "is_active = true")).IsTrue();
        await Assert.That(model.FindEntityType(typeof(PlatformContributionSetting))!.GetIndexes().Any(index => index.IsUnique && index.GetFilter() == "is_active = true")).IsTrue();
        await Assert.That(model.FindEntityType(typeof(EventTicketCatalogVersion))!.GetIndexes().Any(index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["TenantId", "EventId", "VersionNumber"])
            && index.GetFilter() == "is_deleted = false")).IsTrue();
        await Assert.That(model.FindEntityType(typeof(EventTicketCatalogVersion))!.GetIndexes().Any(index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["TenantId", "EventId"])
            && index.GetFilter() == "ticket_catalog_status_id = 2 AND is_deleted = false")).IsTrue();
        await Assert.That(model.FindEntityType(typeof(EventTicketCatalogVersion))!.FindProperty(nameof(EventTicketCatalogVersion.MerchantDisclosureText))!.GetMaxLength())
            .IsEqualTo(EventTicketCatalogVersion.MaxCommercialDisclosureTextLength);
        await Assert.That(model.FindEntityType(typeof(EventTicketCatalogVersion))!.FindProperty(nameof(EventTicketCatalogVersion.RefundPolicyDisclosureText))!.GetMaxLength())
            .IsEqualTo(EventTicketCatalogVersion.MaxCommercialDisclosureTextLength);
        await Assert.That(model.FindEntityType(typeof(EventTicketCatalogVersion))!.FindProperty(nameof(EventTicketCatalogVersion.SupportContactDisclosureText))!.GetMaxLength())
            .IsEqualTo(EventTicketCatalogVersion.MaxCommercialDisclosureTextLength);
    }

    [Test]
    public async Task EfModel_MapsPaidEventPolicyAndOrganizerPaymentConnectionContractsWithoutSensitivePayloads()
    {
        await using var context = CreateModelContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IEntityType policy = model.FindEntityType(typeof(PaidEventPolicyVersion))!;
        IEntityType accountOperation = model.FindEntityType(typeof(OrganizerPaymentProviderAccountOperation))!;
        IEntityType connection = model.FindEntityType(typeof(OrganizerPaymentProviderConnection))!;
        IEntityType policyOrganizerKind = model.FindEntityType(typeof(PaidEventPolicyAllowedOrganizerKind))!;
        IEntityType policyCurrency = model.FindEntityType(typeof(PaidEventPolicyAllowedCurrency))!;
        IEntityType policyRefundProtection = model.FindEntityType(typeof(PaidEventPolicyRefundProtection))!;
        IEntityType policyRiskLimit = model.FindEntityType(typeof(PaidEventPolicyCurrencyRiskLimitRow))!;
        IEntityType connectionCurrency = model.FindEntityType(typeof(OrganizerPaymentProviderConnectionSupportedCurrency))!;

        await Assert.That(policy.GetTableName()).IsEqualTo("paid_event_policy_versions");
        await Assert.That(policy.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(policyOrganizerKind.GetTableName()).IsEqualTo("paid_event_policy_allowed_organizer_kinds");
        await Assert.That(policyCurrency.GetTableName()).IsEqualTo("paid_event_policy_allowed_currencies");
        await Assert.That(policyRefundProtection.GetTableName()).IsEqualTo("paid_event_policy_refund_protections");
        await Assert.That(policyRiskLimit.GetTableName()).IsEqualTo("paid_event_policy_currency_risk_limits");
        await Assert.That(policyOrganizerKind.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(policyCurrency.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(policyRefundProtection.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(policyRiskLimit.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(connection.GetTableName()).IsEqualTo("organizer_payment_provider_connections");
        await Assert.That(accountOperation.GetTableName()).IsEqualTo("organizer_payment_provider_account_operations");
        await Assert.That(accountOperation.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(connectionCurrency.GetTableName()).IsEqualTo("organizer_payment_provider_connection_supported_currencies");
        await Assert.That(connection.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(connection.FindDeclaredQueryFilter(QueryFilterNames.SoftDelete)).IsNotNull();
        await Assert.That(connectionCurrency.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(connectionCurrency.FindDeclaredQueryFilter(QueryFilterNames.SoftDelete)).IsNotNull();
        await Assert.That(policy.FindProperty(nameof(PaidEventPolicyVersion.PolicyScopeKey))!.GetMaxLength()).IsEqualTo(48);
        await Assert.That(policy.FindProperty("MerchantDisclosureText")).IsNull();
        await Assert.That(policy.FindProperty("RefundPolicyDisclosureText")).IsNull();
        await Assert.That(policy.FindProperty("SupportContactDisclosureText")).IsNull();
        await Assert.That(policy.FindProperty(nameof(PaidEventPolicyVersion.ActiveUniquenessSlot))!.IsNullable).IsFalse();
        await Assert.That(policy.FindProperty("_allowedOrganizerKinds")).IsNull();
        await Assert.That(policy.FindProperty("_allowedCurrencyCodes")).IsNull();
        await Assert.That(policy.FindProperty("_refundProtections")).IsNull();
        await Assert.That(policy.FindProperty("_currencyRiskLimits")).IsNull();
        await Assert.That(connection.FindProperty(nameof(OrganizerPaymentProviderConnection.MerchantCountryCode))!.GetMaxLength()).IsEqualTo(2);
        await Assert.That(connection.FindProperty(nameof(OrganizerPaymentProviderConnection.LastReadinessEvidenceRevision))!.GetMaxLength()).IsEqualTo(120);
        await Assert.That(connection.FindProperty(nameof(OrganizerPaymentProviderConnection.ProviderCode))!.GetMaxLength()).IsEqualTo(40);
        await Assert.That(connection.FindProperty(nameof(OrganizerPaymentProviderConnection.ConnectPlatformId))!.GetMaxLength()).IsEqualTo(120);
        await Assert.That(connection.FindProperty(nameof(OrganizerPaymentProviderConnection.ExternalAccountId))!.GetMaxLength()).IsEqualTo(200);
        await Assert.That(connection.FindProperty(nameof(OrganizerPaymentProviderConnection.ActiveScopeKey))!.GetMaxLength()).IsEqualTo(232);
        await Assert.That(connection.FindProperty(nameof(OrganizerPaymentProviderConnection.ActiveUniquenessSlot))!.GetMaxLength()).IsEqualTo(48);
        await Assert.That(connection.FindProperty("_supportedCurrencyCodes")).IsNull();
        await Assert.That(connection.FindProperty(nameof(OrganizerPaymentProviderConnection.DisabledReasonCode))!.GetMaxLength()).IsEqualTo(80);
        await Assert.That(accountOperation.FindProperty(nameof(OrganizerPaymentProviderAccountOperation.ProviderIdempotencyKey))!.GetMaxLength()).IsEqualTo(80);
        await Assert.That(accountOperation.FindProperty(nameof(OrganizerPaymentProviderAccountOperation.ActiveScopeKey))!.GetMaxLength()).IsEqualTo(232);
        await Assert.That(accountOperation.FindProperty(nameof(OrganizerPaymentProviderAccountOperation.ActiveUniquenessSlot))!.GetMaxLength()).IsEqualTo(80);
        await Assert.That(accountOperation.FindProperty(nameof(OrganizerPaymentProviderAccountOperation.ExternalAccountId))!.GetMaxLength()).IsEqualTo(200);
        await Assert.That(accountOperation.FindProperty(nameof(OrganizerPaymentProviderAccountOperation.FailureCode))!.GetMaxLength()).IsEqualTo(120);
        await Assert.That(accountOperation.FindProperty(nameof(OrganizerPaymentProviderAccountOperation.ProviderRequestId))!.GetMaxLength()).IsEqualTo(120);
        await Assert.That(policy.GetIndexes().Any(index =>
            index.IsUnique
            && HasProperties(index, nameof(PaidEventPolicyVersion.PolicyScopeKey), nameof(PaidEventPolicyVersion.ActiveUniquenessSlot))
            && string.IsNullOrWhiteSpace(index.GetFilter()))).IsTrue();
        await Assert.That(accountOperation.GetIndexes().Any(index =>
            index.IsUnique
            && HasProperties(index, nameof(OrganizerPaymentProviderAccountOperation.ActiveScopeKey), nameof(OrganizerPaymentProviderAccountOperation.ActiveUniquenessSlot))
            && string.IsNullOrWhiteSpace(index.GetFilter()))).IsTrue();
        await Assert.That(accountOperation.GetIndexes().Any(index =>
            index.IsUnique
            && HasProperties(index, nameof(OrganizerPaymentProviderAccountOperation.ProviderIdempotencyKey))
            && string.IsNullOrWhiteSpace(index.GetFilter()))).IsTrue();
        await Assert.That(connection.GetIndexes().Any(index =>
            index.IsUnique
            && HasProperties(index, nameof(OrganizerPaymentProviderConnection.ActiveScopeKey), nameof(OrganizerPaymentProviderConnection.ActiveUniquenessSlot))
            && string.IsNullOrWhiteSpace(index.GetFilter()))).IsTrue();
        await Assert.That(connection.GetIndexes().Any(index =>
            index.IsUnique
            && HasProperties(index,
                nameof(OrganizerPaymentProviderConnection.ProviderCode),
                nameof(OrganizerPaymentProviderConnection.ConnectPlatformId),
                nameof(OrganizerPaymentProviderConnection.ExternalAccountId))
            && string.IsNullOrWhiteSpace(index.GetFilter()))).IsTrue();
        await Assert.That(connection.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(OrganizerPaymentProviderConnection)
            && foreignKey.Properties.Select(property => property.Name).SequenceEqual([
                nameof(OrganizerPaymentProviderConnection.TenantId),
                nameof(OrganizerPaymentProviderConnection.ReplacesConnectionId)])
            && foreignKey.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual([
                nameof(OrganizerPaymentProviderConnection.TenantId),
                nameof(OrganizerPaymentProviderConnection.Id)]))).IsTrue();
        await Assert.That(connection.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(OrganizerPaymentProviderConnection)
            && foreignKey.Properties.Select(property => property.Name).SequenceEqual([
                nameof(OrganizerPaymentProviderConnection.TenantId),
                nameof(OrganizerPaymentProviderConnection.ReplacedByConnectionId)])
            && foreignKey.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual([
                nameof(OrganizerPaymentProviderConnection.TenantId),
                nameof(OrganizerPaymentProviderConnection.Id)]))).IsTrue();
        await Assert.That(accountOperation.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(OrganizerPaymentProviderConnection)
            && foreignKey.Properties.Select(property => property.Name).SequenceEqual([
                nameof(OrganizerPaymentProviderAccountOperation.TenantId),
                nameof(OrganizerPaymentProviderAccountOperation.ConnectionId)])
            && foreignKey.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual([
                nameof(OrganizerPaymentProviderConnection.TenantId),
                nameof(OrganizerPaymentProviderConnection.Id)]))).IsTrue();
        await Assert.That(connectionCurrency.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(OrganizerPaymentProviderConnection)
            && foreignKey.Properties.Select(property => property.Name).SequenceEqual([
                nameof(OrganizerPaymentProviderConnectionSupportedCurrency.TenantId),
                nameof(OrganizerPaymentProviderConnectionSupportedCurrency.OrganizerPaymentProviderConnectionId)])
            && foreignKey.DeleteBehavior == DeleteBehavior.Cascade)).IsTrue();
        await Assert.That(policyRiskLimit.FindProperty(nameof(PaidEventPolicyCurrencyRiskLimitRow.PerEventSalesCeilingMinor))!.GetColumnType()).IsEqualTo("bigint");
        await Assert.That(connectionCurrency.FindProperty(nameof(OrganizerPaymentProviderConnectionSupportedCurrency.CurrencyCode))!.GetMaxLength()).IsEqualTo(3);

        string[] sensitiveFragments = ["Secret", "Token", "Kyc", "Bank", "Payload", "Raw", "Json"];
        await Assert.That(new[] { policy, accountOperation, connection, policyOrganizerKind, policyCurrency, policyRefundProtection, policyRiskLimit, connectionCurrency }.SelectMany(entity => entity.GetProperties()).Any(property =>
            sensitiveFragments.Any(fragment => property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))).IsFalse();
    }

    [Test]
    public async Task PaidEventPolicyVersion_PersistsAndReloadsNormalizedPolicyChildren()
    {
        await using var context = CreateInMemoryContext("paid-event-policy-reload");
        Guid tenantId = Guid.CreateVersion7();
        PaidEventPolicyVersion policy = PaidEventPolicyVersion.CreateTenant(
            tenantId,
            isPaymentsEnabled: true,
            allowedOrganizerKinds: [ActorTypeEnum.Group, ActorTypeEnum.Organization],
            requiresLocalVerification: true,
            allowedCurrencyCodes: ["usd", "EUR"],
            defaultCurrencyCode: "eur",
            refundProtections: Enum.GetValues<PaidEventRefundProtection>(),
            currencyRiskLimits:
            [
                PaidEventPolicyCurrencyRiskLimit.Create("usd", 100_00, null, 500_00, null, 30, 75_00),
                PaidEventPolicyCurrencyRiskLimit.Create("eur", 90_00, null, 450_00, null, 30, 70_00)
            ],
            requiresFirstPaidEventReview: true,
            farFutureReviewThresholdDays: 180);

        context.Set<PaidEventPolicyVersion>().Add(policy);
        await context.SaveChangesAsync();
        context.TenantContext = new TestTenantContext(tenantId);
        await Assert.That(await context.Set<PaidEventPolicyAllowedOrganizerKind>().CountAsync()).IsEqualTo(2);
        await Assert.That(await context.Set<PaidEventPolicyAllowedCurrency>().CountAsync()).IsEqualTo(2);
        await Assert.That(await context.Set<PaidEventPolicyRefundProtection>().CountAsync()).IsEqualTo(Enum.GetValues<PaidEventRefundProtection>().Length);
        await Assert.That(await context.Set<PaidEventPolicyCurrencyRiskLimitRow>().CountAsync()).IsEqualTo(2);
        context.ChangeTracker.Clear();

        PaidEventPolicyVersion reloaded = await context.Set<PaidEventPolicyVersion>().SingleAsync(value => value.Id == policy.Id);

        await Assert.That(reloaded.TenantId).IsEqualTo(tenantId);
        await Assert.That(reloaded.PolicyScopeKey).IsEqualTo($"tenant:{tenantId:N}");
        await Assert.That(reloaded.ActiveUniquenessSlot).IsEqualTo(0);
        await Assert.That(reloaded.AllowedOrganizerKinds.Order().SequenceEqual([ActorTypeEnum.Organization, ActorTypeEnum.Group])).IsTrue();
        await Assert.That(reloaded.AllowedCurrencyCodes.Order(StringComparer.Ordinal).SequenceEqual(["EUR", "USD"])).IsTrue();
        await Assert.That(reloaded.DefaultCurrencyCode).IsEqualTo("EUR");
        await Assert.That(reloaded.RefundProtections.Order().SequenceEqual(Enum.GetValues<PaidEventRefundProtection>().Order())).IsTrue();
        await Assert.That(reloaded.CurrencyRiskLimits.OrderBy(limit => limit.CurrencyCode).Select(limit => limit.CurrencyCode).SequenceEqual(["EUR", "USD"])).IsTrue();
        await Assert.That(reloaded.CurrencyRiskLimits.Single(limit => limit.CurrencyCode == "USD").PerEventSalesCeilingMinor).IsEqualTo(100_00);
    }

    [Test]
    public async Task PaidEventPolicyRepository_ReadsActiveInstanceAndTenantVersionsWithChildrenAndPersistsRevisions()
    {
        string databaseName = $"paid-policy-repository-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        Guid tenantId = Guid.CreateVersion7();
        PaidEventPolicyVersion instance = PaidEventPolicyVersion.CreateDefaultInstance().CreateRevision(
            true,
            [ActorTypeEnum.Organization, ActorTypeEnum.Group],
            true,
            ["EUR", "USD"],
            "EUR",
            Enum.GetValues<PaidEventRefundProtection>(),
            [PaidEventPolicyCurrencyRiskLimit.Create("EUR", 500_000, null, 1_000_000, null, 30, 250_000)],
            true,
            180);
        PaidEventPolicyVersion tenant = PaidEventPolicyVersion.CreateTenant(
            tenantId,
            true,
            [ActorTypeEnum.Organization],
            true,
            ["EUR"],
            "EUR",
            Enum.GetValues<PaidEventRefundProtection>(),
            [PaidEventPolicyCurrencyRiskLimit.Create("EUR", 250_000, null, 750_000, null, 30, 100_000)],
            true,
            90);

        IModel model;
        await using (ExploreDbContext seed = CreateNamedInMemoryContext(databaseName, root))
        {
            seed.EnableTenantFilterBypass("Seeds paid policy repository rows.");
            seed.Set<PaidEventPolicyVersion>().AddRange(instance, tenant);
            await seed.SaveChangesAsync();
            model = seed.Model;
        }

        // The shared InMemory store must use the same key metadata across uncached providers.
        await using ExploreDbContext context = new TicketingTestDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase(databaseName, root)
            .UseModel(model)
            .Options)
        {
            TenantContext = new TestTenantContext(tenantId)
        };
        var repository = new PaidEventPolicyRepository(context);

        PaidEventPolicyVersion? activeInstance = await repository.GetActiveInstanceAsync(CancellationToken.None);
        PaidEventPolicyVersion? activeTenant = await repository.GetActiveTenantAsync(tenantId, CancellationToken.None);
        PaidEventPolicyVersion tenantRevision = activeTenant!.CreateRevision(
            true,
            activeTenant.AllowedOrganizerKinds,
            true,
            activeTenant.AllowedCurrencyCodes,
            activeTenant.DefaultCurrencyCode,
            activeTenant.RefundProtections,
            activeTenant.CurrencyRiskLimits,
            activeTenant.RequiresFirstPaidEventReview,
            activeTenant.FarFutureReviewThresholdDays);
        await repository.AddAsync(tenantRevision, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();

        PaidEventPolicyVersion[] history = await repository.ListTenantHistoryAsync(tenantId, CancellationToken.None);

        await Assert.That(activeInstance?.TenantId).IsNull();
        await Assert.That(activeInstance?.AllowedCurrencyCodes.SequenceEqual(["EUR", "USD"])).IsTrue();
        await Assert.That(activeInstance?.CurrencyRiskLimits.Single().CurrencyCode).IsEqualTo("EUR");
        await Assert.That(history.Select(policy => policy.VersionNumber).SequenceEqual([1, 2])).IsTrue();
        await Assert.That(history.Single(policy => policy.VersionNumber == 1).IsActive).IsFalse();
        await Assert.That(history.Single(policy => policy.VersionNumber == 2).IsActive).IsTrue();
    }

    [Test]
    public async Task PaidEventPolicyRepository_UsesExactTenantPredicateAcrossAmbientTenantContext()
    {
        string databaseName = $"paid-policy-tenant-isolation-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();

        await using (ExploreDbContext seed = CreateNamedInMemoryContext(databaseName, root))
        {
            seed.EnableTenantFilterBypass("Seeds paid policy tenant isolation rows.");
            seed.Set<PaidEventPolicyVersion>().AddRange(CreateTenantPaidPolicy(tenantA), CreateTenantPaidPolicy(tenantB));
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext context = CreateNamedInMemoryContext(databaseName, root, tenantA);
        var repository = new PaidEventPolicyRepository(context);

        PaidEventPolicyVersion? policyA = await repository.GetActiveTenantAsync(
            tenantA,
            CancellationToken.None);
        PaidEventPolicyVersion? policyB = await repository.GetActiveTenantAsync(
            tenantB,
            CancellationToken.None);

        await Assert.That(policyA?.TenantId).IsEqualTo(tenantA);
        await Assert.That(policyB?.TenantId).IsEqualTo(tenantB);
    }

    [Test]
    public async Task PaidEventPolicyRepository_ListsBoundedActiveTenantPagesWithoutTracking()
    {
        string databaseName = $"paid-policy-active-pages-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        Guid tenantA = Guid.Parse("0199464e-e388-7f56-9281-cefabd6a5671");
        Guid tenantB = Guid.Parse("0199464e-e388-7f56-9281-cefabd6a5672");
        PaidEventPolicyVersion retiredTenantPolicy = CreateTenantPaidPolicy(tenantA);
        PaidEventPolicyVersion activeTenantRevision = retiredTenantPolicy.CreateRevision(
            retiredTenantPolicy.IsPaymentsEnabled,
            retiredTenantPolicy.AllowedOrganizerKinds,
            retiredTenantPolicy.RequiresLocalVerification,
            retiredTenantPolicy.AllowedCurrencyCodes,
            retiredTenantPolicy.DefaultCurrencyCode,
            retiredTenantPolicy.RefundProtections,
            retiredTenantPolicy.CurrencyRiskLimits,
            retiredTenantPolicy.RequiresFirstPaidEventReview,
            retiredTenantPolicy.FarFutureReviewThresholdDays);

        await using (ExploreDbContext seed = CreateNamedInMemoryContext(databaseName, root))
        {
            seed.EnableTenantFilterBypass("Seeds active paid-policy page rows.");
            seed.Set<PaidEventPolicyVersion>().AddRange(
                PaidEventPolicyVersion.CreateDefaultInstance(),
                retiredTenantPolicy,
                activeTenantRevision,
                CreateTenantPaidPolicy(tenantB));
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext context =
            CreateNamedInMemoryContext(databaseName, root, tenantA);
        var repository = new PaidEventPolicyRepository(context);

        PaidEventPolicyVersion[] firstPage = await repository.ListActiveTenantsAsync(
            offset: 0,
            limit: 1,
            CancellationToken.None);
        PaidEventPolicyVersion[] secondPage = await repository.ListActiveTenantsAsync(
            offset: 1,
            limit: 1,
            CancellationToken.None);

        await Assert.That(firstPage.Length).IsEqualTo(1);
        await Assert.That(secondPage.Length).IsEqualTo(1);
        await Assert.That(firstPage.Concat(secondPage).Select(policy => policy.TenantId))
            .IsEquivalentTo(new Guid?[] { tenantA, tenantB });
        await Assert.That(firstPage.Concat(secondPage).All(policy => policy.IsActive))
            .IsTrue();
        await Assert.That(firstPage.Concat(secondPage).All(policy => policy.AllowedCurrencyCodes.Count > 0))
            .IsTrue();
        await Assert.That(context.ChangeTracker.Entries()).IsEmpty();
        await Assert.That(() => (Task)repository.ListActiveTenantsAsync(
                offset: 0,
                limit: IPaidEventPolicyRepository.MaximumActiveTenantPolicyPageSize + 1,
                CancellationToken.None))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task OrganizerPaymentProviderConnection_PersistsReadinessMetadataAndNamedFilters()
    {
        string databaseName = $"organizer-payment-filter-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        OrganizerPaymentProviderConnection visible = ReadyConnection(tenantA, Guid.CreateVersion7(), "acct_visible", 0);
        OrganizerPaymentProviderConnection deleted = ReadyConnection(tenantA, Guid.CreateVersion7(), "acct_deleted", 1);
        OrganizerPaymentProviderConnection otherTenant = ReadyConnection(tenantB, Guid.CreateVersion7(), "acct_other", 2);
        deleted.IsDeleted = true;

        await using (ExploreDbContext seed = CreateNamedInMemoryContext(databaseName, root))
        {
            seed.EnableTenantFilterBypass("Seeds organizer payment provider filter test rows.");
            seed.Set<OrganizerPaymentProviderConnection>().AddRange(visible, deleted, otherTenant);
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext tenant = CreateNamedInMemoryContext(databaseName, root, tenantA);
        OrganizerPaymentProviderConnection reloaded = await tenant.Set<OrganizerPaymentProviderConnection>().SingleAsync();

        await Assert.That(await tenant.Set<OrganizerPaymentProviderConnectionSupportedCurrency>().CountAsync()).IsEqualTo(2);
        await Assert.That(reloaded.SupportedCurrencyCodes.SequenceEqual(["EUR", "USD"])).IsTrue();
        await Assert.That(reloaded.MerchantCountryCode).IsEqualTo("BE");
        await Assert.That(reloaded.ChargeCapabilityStateId).IsEqualTo((int)ChargeCapabilityState.Active);
        await Assert.That(reloaded.RequirementsStateId).IsEqualTo((int)ProviderRequirementsState.Satisfied);
        await Assert.That(reloaded.LastReadinessObservedAt).IsEqualTo(new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc));
        await Assert.That(reloaded.LastReadinessEvidenceRevision).IsEqualTo("stripe-readiness-0");
        await Assert.That(await tenant.Set<OrganizerPaymentProviderConnection>().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task OrganizerPaymentProviderConnectionRepository_IsRegisteredAndHonorsActiveAndHistoricalIdentitySemantics()
    {
        string databaseName = $"organizer-payment-repository-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        Guid tenantId = Guid.CreateVersion7();
        Guid organizerActorId = Guid.CreateVersion7();
        OrganizerPaymentProviderConnection active = ReadyConnection(tenantId, organizerActorId, "acct_active", 0);
        OrganizerPaymentProviderConnection disabled = ReadyConnection(tenantId, organizerActorId, "acct_disabled", 1);
        OrganizerPaymentProviderConnection replaced = ReadyConnection(tenantId, organizerActorId, "acct_replaced", 2);
        OrganizerPaymentProviderConnection softDeleted = ReadyConnection(tenantId, organizerActorId, "acct_soft_deleted", 3);
        disabled.Disable("operator_disabled", new DateTime(2026, 8, 13, 13, 0, 0, DateTimeKind.Utc));
        OrganizerPaymentProviderConnection replacement = replaced.ReplaceWith(Guid.CreateVersion7(), "acct_replacement", new DateTime(2026, 8, 13, 14, 0, 0, DateTimeKind.Utc));
        softDeleted.IsDeleted = true;

        await using (ExploreDbContext seed = CreateNamedInMemoryContext(databaseName, root))
        {
            seed.EnableTenantFilterBypass("Seeds organizer payment provider repository test rows.");
            seed.Set<OrganizerPaymentProviderConnection>().AddRange(active, disabled, replaced, replacement, softDeleted);
            await seed.SaveChangesAsync();
        }

        using ServiceProvider serviceProvider = CreateRepositoryServiceProvider(databaseName, root, tenantId);
        using IServiceScope scope = serviceProvider.CreateScope();
        IOrganizerPaymentProviderConnectionRepository repository = scope.ServiceProvider.GetRequiredService<IOrganizerPaymentProviderConnectionRepository>();

        OrganizerPaymentProviderConnection? activeByScope = await repository.GetActiveByScopeAsync(tenantId, organizerActorId, "stripe", "platform-main", CancellationToken.None);
        OrganizerPaymentProviderConnection? disabledHistorical = await repository.GetHistoricalByExternalAccountAsync("stripe", "platform-main", "acct_disabled", CancellationToken.None);
        OrganizerPaymentProviderConnection? replacedHistorical = await repository.GetHistoricalByExternalAccountAsync("stripe", "platform-main", "acct_replaced", CancellationToken.None);
        OrganizerPaymentProviderConnection? softDeletedHistorical = await repository.GetHistoricalByExternalAccountAsync("stripe", "platform-main", "acct_soft_deleted", CancellationToken.None);
        IReadOnlyList<OrganizerPaymentProviderConnection> listed = await repository.ListByTenantAndActorAsync(tenantId, organizerActorId, CancellationToken.None);

        await Assert.That(activeByScope?.Id).IsEqualTo(active.Id);
        await Assert.That(disabledHistorical?.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Disabled);
        await Assert.That(replacedHistorical?.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Replaced);
        await Assert.That(softDeletedHistorical?.Id).IsEqualTo(softDeleted.Id);
        await Assert.That(listed.Select(connection => connection.Id).Order().SequenceEqual(new[] { active.Id, disabled.Id, replaced.Id, replacement.Id }.Order())).IsTrue();
    }

    [Test]
    public async Task OrganizerPaymentProviderConnectionRepository_ListDueReadinessChecksBoundsStatusOrderAndNoTracking()
    {
        string databaseName = $"organizer-payment-readiness-due-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        Guid tenantId = Guid.CreateVersion7();
        Guid organizerActorId = Guid.CreateVersion7();
        DateTime cutoff = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);
        OrganizerPaymentProviderConnection neverObserved = OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), tenantId, organizerActorId, "stripe", "platform-main", "acct_never", cutoff.AddMinutes(-30));
        OrganizerPaymentProviderConnection olderRestricted = OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), tenantId, organizerActorId, "stripe", "platform-main-2", "acct_old", cutoff.AddMinutes(-20));
        olderRestricted.ApplyReadiness(OrganizerPaymentProviderReadinessObservation.Create("BE", ChargeCapabilityState.Pending, ProviderRequirementsState.CurrentlyDue, ["EUR"], cutoff.AddMinutes(-10), "old"));
        OrganizerPaymentProviderConnection newerRestricted = OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), tenantId, organizerActorId, "stripe", "platform-main-3", "acct_new", cutoff.AddMinutes(-10));
        newerRestricted.ApplyReadiness(OrganizerPaymentProviderReadinessObservation.Create("BE", ChargeCapabilityState.Pending, ProviderRequirementsState.CurrentlyDue, ["EUR"], cutoff.AddMinutes(-1), "new"));
        OrganizerPaymentProviderConnection ready = ReadyConnection(tenantId, organizerActorId, "acct_ready", 1);
        OrganizerPaymentProviderConnection disabled = OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), tenantId, organizerActorId, "stripe", "platform-main-4", "acct_disabled", cutoff.AddMinutes(-40));
        disabled.Disable("operator_disabled", cutoff.AddMinutes(-30));
        OrganizerPaymentProviderConnection deleted = OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), tenantId, organizerActorId, "stripe", "platform-main-5", "acct_deleted", cutoff.AddMinutes(-50));
        deleted.IsDeleted = true;

        await using (ExploreDbContext seed = CreateNamedInMemoryContext(databaseName, root))
        {
            seed.EnableTenantFilterBypass("Seeds organizer payment readiness due rows.");
            seed.Set<OrganizerPaymentProviderConnection>().AddRange(olderRestricted, ready, neverObserved, newerRestricted, disabled, deleted);
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext context = CreateNamedInMemoryContext(databaseName, root, tenantId);
        var repository = new OrganizerPaymentProviderConnectionRepository(context);

        IReadOnlyList<OrganizerPaymentProviderConnection> due = await repository.ListDueReadinessChecksAsync(cutoff, 2, CancellationToken.None);

        await Assert.That(due.Select(connection => connection.Id).SequenceEqual([neverObserved.Id, olderRestricted.Id])).IsTrue();
        await Assert.That(due.All(connection => connection.StatusId is (int)OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding or (int)OrganizerPaymentProviderConnectionStatusEnum.Restricted)).IsTrue();
        await Assert.That(due.Any(connection => connection.Id == ready.Id || connection.Id == disabled.Id || connection.Id == deleted.Id || connection.Id == newerRestricted.Id)).IsFalse();
        await Assert.That(context.ChangeTracker.Entries<OrganizerPaymentProviderConnection>()).IsEmpty();
    }

    [Test]
    public async Task OrganizerPaymentProviderConnectionRepositoryListDueReadinessChecksCrossesTenantsWithoutAmbientTenantButPreservesSoftDelete()
    {
        string databaseName = $"organizer-payment-readiness-cross-tenant-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        Guid unrelatedTenant = Guid.CreateVersion7();
        Guid organizerActorId = Guid.CreateVersion7();
        DateTime cutoff = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);
        OrganizerPaymentProviderConnection tenantADue = OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), tenantA, organizerActorId, "stripe", "platform-main", "acct_due_a", cutoff.AddMinutes(-30));
        OrganizerPaymentProviderConnection tenantBDue = OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), tenantB, organizerActorId, "stripe", "platform-main", "acct_due_b", cutoff.AddMinutes(-20));
        OrganizerPaymentProviderConnection deletedDue = OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), tenantA, organizerActorId, "stripe", "platform-main", "acct_deleted_due", cutoff.AddMinutes(-40));
        deletedDue.IsDeleted = true;

        await using (ExploreDbContext seed = CreateNamedInMemoryContext(databaseName, root))
        {
            seed.EnableTenantFilterBypass("Seeds organizer payment readiness cross-tenant queue rows.");
            seed.Set<OrganizerPaymentProviderConnection>().AddRange(tenantADue, tenantBDue, deletedDue);
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext noTenantContext = CreateNamedInMemoryContext(databaseName, root);
        var noTenantRepository = new OrganizerPaymentProviderConnectionRepository(noTenantContext);
        await using ExploreDbContext unrelatedTenantContext = CreateNamedInMemoryContext(databaseName, root, unrelatedTenant);
        var unrelatedTenantRepository = new OrganizerPaymentProviderConnectionRepository(unrelatedTenantContext);

        IReadOnlyList<OrganizerPaymentProviderConnection> withoutAmbientTenant = await noTenantRepository.ListDueReadinessChecksAsync(cutoff, 10, CancellationToken.None);
        IReadOnlyList<OrganizerPaymentProviderConnection> withDifferentAmbientTenant = await unrelatedTenantRepository.ListDueReadinessChecksAsync(cutoff, 10, CancellationToken.None);

        Guid[] expectedIds = [tenantADue.Id, tenantBDue.Id];
        await Assert.That(withoutAmbientTenant.Select(connection => connection.Id).SequenceEqual(expectedIds)).IsTrue();
        await Assert.That(withDifferentAmbientTenant.Select(connection => connection.Id).SequenceEqual(expectedIds)).IsTrue();
        await Assert.That(withoutAmbientTenant.Any(connection => connection.Id == deletedDue.Id)).IsFalse();
        await Assert.That(withDifferentAmbientTenant.Any(connection => connection.Id == deletedDue.Id)).IsFalse();
    }

    [Test]
    public async Task OrganizerPaymentProviderConnectionRepository_IsScopedInPersistenceComposition()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder().Build();

        services.ConfigurePersistenceServices(configuration, skipDbContextRegistration: true, skipLookupCacheInitializer: true);

        ServiceDescriptor descriptor = services.Single(value =>
            value.ServiceType == typeof(IOrganizerPaymentProviderConnectionRepository));
        await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(descriptor.ImplementationType).IsNotNull();
        await Assert.That(services.Single(value => value.ServiceType == typeof(IOrganizerPaymentProviderAccountOperationRepository)).Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(services.Single(value => value.ServiceType == typeof(IPaidEventPolicyRepository)).Lifetime).IsEqualTo(ServiceLifetime.Scoped);
    }

    [Test]
    public async Task OrganizerPaymentProviderAccountOperationSqliteUniqueActiveSlotBlocksDuplicateAndReleasesAfterTerminal()
    {
        await using SqliteConnection connection = await OpenSqliteConnectionAsync();
        await using TicketingTestDbContext context = CreateSqliteContext(connection);
        await context.Database.EnsureCreatedAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid organizerActorId = Guid.CreateVersion7();
        context.AddRange(CreateActiveTenantStatus(), CreateOrganizationActorType(), CreateTenant(tenantId), CreateActor(organizerActorId));
        OrganizerPaymentProviderAccountOperation active = CreateAccountOperation(tenantId, organizerActorId, 0);
        OrganizerPaymentProviderAccountOperation duplicate = CreateAccountOperation(tenantId, organizerActorId, 1);

        context.Set<OrganizerPaymentProviderAccountOperation>().Add(active);
        await context.SaveChangesAsync();
        context.Set<OrganizerPaymentProviderAccountOperation>().Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        context.TenantContext = new TestTenantContext(tenantId);

        active = await context.Set<OrganizerPaymentProviderAccountOperation>().SingleAsync(operation => operation.Id == active.Id);
        active.RejectByProvider("account_invalid", "req_bad", new DateTime(2026, 8, 14, 12, 30, 0, DateTimeKind.Utc));
        OrganizerPaymentProviderAccountOperation successor = CreateAccountOperation(tenantId, organizerActorId, 2);
        context.Set<OrganizerPaymentProviderAccountOperation>().Add(successor);
        await context.SaveChangesAsync();

        await Assert.That(active.ActiveUniquenessSlot).IsEqualTo($"providerrejected:{active.Id:N}");
        await Assert.That(successor.ActiveUniquenessSlot).IsEqualTo("active");
    }

    [Test]
    public async Task PaidEventPolicyVersionSqliteUniqueActiveSlotBlocksDuplicateAndReleasesAfterRevision()
    {
        await using SqliteConnection connection = await OpenSqliteConnectionAsync();
        await using TicketingTestDbContext context = CreateSqliteContext(connection);
        await context.Database.EnsureCreatedAsync();
        Guid tenantId = Guid.CreateVersion7();
        context.AddRange(CreateActiveTenantStatus(), CreateOrganizationActorType(), CreateTenant(tenantId));
        PaidEventPolicyVersion active = CreateTenantPaidPolicy(tenantId);
        PaidEventPolicyVersion duplicate = CreateTenantPaidPolicy(tenantId);

        context.Set<PaidEventPolicyVersion>().Add(active);
        await context.SaveChangesAsync();
        context.Set<PaidEventPolicyVersion>().Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        context.TenantContext = new TestTenantContext(tenantId);

        active = await context.Set<PaidEventPolicyVersion>().SingleAsync(policy => policy.Id == active.Id);
        PaidEventPolicyVersion successor = active.CreateRevision(
            isPaymentsEnabled: true,
            allowedOrganizerKinds: [ActorTypeEnum.Organization],
            requiresLocalVerification: true,
            allowedCurrencyCodes: ["EUR"],
            defaultCurrencyCode: "EUR",
            refundProtections: Enum.GetValues<PaidEventRefundProtection>(),
            currencyRiskLimits: [],
            requiresFirstPaidEventReview: true,
            farFutureReviewThresholdDays: 180);
        context.Set<PaidEventPolicyVersion>().Add(successor);
        await context.SaveChangesAsync();

        await Assert.That(active.ActiveUniquenessSlot).IsEqualTo(1);
        await Assert.That(successor.ActiveUniquenessSlot).IsEqualTo(0);
    }

    [Test]
    public async Task OrganizerPaymentProviderConnectionSqliteUniqueActiveSlotBlocksDuplicateAndReleasesAfterDisable()
    {
        await using SqliteConnection connection = await OpenSqliteConnectionAsync();
        await using TicketingTestDbContext context = CreateSqliteContext(connection);
        await context.Database.EnsureCreatedAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid organizerActorId = Guid.CreateVersion7();
        context.AddRange(CreateActiveTenantStatus(), CreateOrganizationActorType(), CreateTenant(tenantId), CreateActor(organizerActorId));
        OrganizerPaymentProviderConnection active = ReadyConnection(tenantId, organizerActorId, "acct_active", 0);
        OrganizerPaymentProviderConnection duplicate = ReadyConnection(tenantId, organizerActorId, "acct_duplicate", 1);

        context.Set<OrganizerPaymentProviderConnection>().Add(active);
        await context.SaveChangesAsync();
        context.Set<OrganizerPaymentProviderConnection>().Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        context.TenantContext = new TestTenantContext(tenantId);

        active = await context.Set<OrganizerPaymentProviderConnection>().SingleAsync(connection => connection.Id == active.Id);
        active.Disable("operator_disabled", new DateTime(2026, 8, 13, 13, 0, 0, DateTimeKind.Utc));
        OrganizerPaymentProviderConnection successor = ReadyConnection(tenantId, organizerActorId, "acct_successor", 2);
        context.Set<OrganizerPaymentProviderConnection>().Add(successor);
        await context.SaveChangesAsync();

        await Assert.That(active.ActiveUniquenessSlot).IsEqualTo($"disabled:{active.Id:N}");
        await Assert.That(successor.ActiveUniquenessSlot).IsEqualTo("active");
    }

    [Test]
    public async Task OrganizerPaymentProviderConnectionSqliteReplacementPersistsActiveSlotAndSelfReferencingLineageWithStagedSaves()
    {
        await using SqliteConnection connection = await OpenSqliteConnectionAsync();
        await using TicketingTestDbContext context = CreateSqliteContext(connection);
        await context.Database.EnsureCreatedAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid organizerActorId = Guid.CreateVersion7();
        context.AddRange(CreateActiveTenantStatus(), CreateOrganizationActorType(), CreateTenant(tenantId), CreateActor(organizerActorId));
        OrganizerPaymentProviderConnection current = ReadyConnection(tenantId, organizerActorId, "acct_current", 0);
        context.Set<OrganizerPaymentProviderConnection>().Add(current);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        context.TenantContext = new TestTenantContext(tenantId);
        var repository = new OrganizerPaymentProviderConnectionRepository(context);

        OrganizerPaymentProviderConnection managed = (await repository.GetByTenantAndIdForUpdateAsync(tenantId, current.Id, CancellationToken.None))!;
        OrganizerPaymentProviderConnection replacement = managed.ReplaceWith(
            Guid.CreateVersion7(),
            "acct_replacement",
            new DateTime(2026, 8, 13, 14, 0, 0, DateTimeKind.Utc));
        await repository.SaveChangesAsync(CancellationToken.None);

        await repository.CreateAsync(replacement, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        managed.MarkReplacedBy(replacement.Id);
        await repository.SaveChangesAsync(CancellationToken.None);

        context.ChangeTracker.Clear();
        OrganizerPaymentProviderConnection[] persisted = await context.Set<OrganizerPaymentProviderConnection>()
            .OrderBy(row => row.CreatedAt)
            .ToArrayAsync();
        OrganizerPaymentProviderConnection oldConnection = persisted.Single(row => row.Id == current.Id);
        OrganizerPaymentProviderConnection newConnection = persisted.Single(row => row.Id == replacement.Id);

        await Assert.That(oldConnection.ReplacedByConnectionId).IsEqualTo(newConnection.Id);
        await Assert.That(newConnection.ReplacesConnectionId).IsEqualTo(oldConnection.Id);
        await Assert.That(oldConnection.ActiveUniquenessSlot).IsEqualTo($"replaced:{oldConnection.Id:N}");
        await Assert.That(newConnection.ActiveUniquenessSlot).IsEqualTo("active");
        await Assert.That(oldConnection.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Replaced);
        await Assert.That(newConnection.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding);
        await Assert.That((await repository.GetActiveByScopeAsync(tenantId, organizerActorId, "stripe", "platform-main", CancellationToken.None))?.Id).IsEqualTo(newConnection.Id);
    }

    [Test]
    public async Task RuntimeSeeder_RepairsTicketingLookupsAndCreatesDisabledMonetizationDefaults()
    {
        await using var context = CreateInMemoryContext("ticketing-seeder");

        await LookupTableSeeder.SeedTicketingLookupsAsync(context, CancellationToken.None);
        await LookupTableSeeder.SeedPlatformMonetizationDefaultsAsync(context, CancellationToken.None);
        context.TicketPricingModes.Remove(await context.TicketPricingModes.SingleAsync(mode => mode.Id == (int)TicketPricingModeEnum.SlidingScale));
        await context.SaveChangesAsync();

        await LookupTableSeeder.SeedTicketingLookupsAsync(context, CancellationToken.None);
        await LookupTableSeeder.SeedPlatformMonetizationDefaultsAsync(context, CancellationToken.None);

        await Assert.That((await context.TicketCatalogStatuses.OrderBy(row => row.Id).Select(row => row.MasterCode).ToArrayAsync()).SequenceEqual(["DRAFT", "PUBLISHED", "RETIRED"])).IsTrue();
        await Assert.That((await context.TicketPricingModes.OrderBy(row => row.Id).Select(row => row.MasterCode).ToArrayAsync()).SequenceEqual(["FIXED", "FREE", "DONATION", "PAY_WHAT_YOU_CAN", "SLIDING_SCALE"])).IsTrue();
        await Assert.That((await context.ParticipantDataCollectionModes.OrderBy(row => row.Id).Select(row => row.MasterCode).ToArrayAsync()).SequenceEqual(["NONE", "LEAD_BOOKER_ONLY", "PER_TICKET_OPTIONAL", "PER_TICKET_REQUIRED", "DEFERRED_ASSIGNMENT"])).IsTrue();
        await Assert.That((await context.EntitlementScopeTypes.OrderBy(row => row.Id).Select(row => row.MasterCode).ToArrayAsync()).SequenceEqual(["EVENT", "EVENT_DAY", "EVENT_SESSION"])).IsTrue();
        await Assert.That((await context.EntitlementSelectionRules.OrderBy(row => row.Id).Select(row => row.MasterCode).ToArrayAsync()).SequenceEqual(["ALL_INCLUDED", "FIXED_SELECTION", "CHOOSE_ONE", "CHOOSE_UP_TO_N"])).IsTrue();
        await Assert.That((await context.CapacityOversellPolicies.OrderBy(row => row.Id).Select(row => row.MasterCode).ToArrayAsync()).SequenceEqual(["DISALLOW", "ALLOW"])).IsTrue();
        await Assert.That((await context.Set<CapacityHoldPolicy>().OrderBy(row => row.Id).Select(row => row.MasterCode).ToArrayAsync()).SequenceEqual([
            "NO_HOLD_UNTIL_READY",
            "TIMED_HOLD_ON_SELECTION",
            "APPROVAL_NO_HOLD",
            "WAITLIST_WHEN_FULL"
        ])).IsTrue();
        PlatformFeePolicy policy = await context.PlatformFeePolicies.SingleAsync();
        PlatformContributionSetting setting = await context.PlatformContributionSettings.Include(row => row.Options).SingleAsync();
        await Assert.That(policy.IsEnabled).IsFalse();
        await Assert.That(policy.FeeBasisPoints).IsEqualTo(0);
        await Assert.That(setting.IsEnabled).IsFalse();
        await Assert.That(setting.Options.OrderBy(option => option.SortOrder).Select(option => option.ContributionBasisPoints).SequenceEqual([0, 500, 1_000, 1_500, 2_000])).IsTrue();
        await Assert.That(setting.Options.Single(option => option.IsDefault).ContributionBasisPoints).IsEqualTo(0);
    }

    [Test]
    public async Task RuntimeSeeder_RepairsParticipantLookupParity()
    {
        await using var context = CreateInMemoryContext("participant-seeder");

        await LookupTableSeeder.SeedParticipantLookupsAsync(context, CancellationToken.None);
        context.ParticipantTypes.Remove(await context.ParticipantTypes.SingleAsync(type => type.Id == (int)ParticipantTypeEnum.Unnamed));
        await context.SaveChangesAsync();
        await LookupTableSeeder.SeedParticipantLookupsAsync(context, CancellationToken.None);
        await context.SaveChangesAsync();

        await Assert.That((await context.ParticipantTypes.OrderBy(row => row.Id).Select(row => row.MasterCode).ToArrayAsync())
            .SequenceEqual(["ADULT", "CHILD", "DEPENDENT", "EMPLOYEE", "GUEST", "UNNAMED"])).IsTrue();
        await Assert.That((await context.AssignmentStatuses.OrderBy(row => row.Id).Select(row => row.MasterCode).ToArrayAsync())
            .SequenceEqual(["UNASSIGNED", "ASSIGNED", "DEFERRED"])).IsTrue();
    }

    [Test]
    public async Task TicketRepository_RequiresMatchingTenantAndEventForTicketAndCapacityLookups()
    {
        await using var context = CreateInMemoryContext("ticketing-repository");
        await LookupTableSeeder.SeedTicketingLookupsAsync(context, CancellationToken.None);
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        Guid eventA = Guid.CreateVersion7();
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenantA, eventA, "USD", 1);
        EventCapacityPool pool = EventCapacityPool.Create(tenantA, eventA, "Hall", 100, 900, CapacityHoldPolicyEnum.TimedHoldOnSelection, CapacityOversellPolicyEnum.Disallow, true);
        EventTicketType ticket = EventTicketType.Create(Guid.CreateVersion7(), tenantA, catalog.Id, "General", "USD", TicketPricingModeEnum.Free, null, null, null, ParticipantDataCollectionModeEnum.None, pool.Id, null, null, false, false, null, null, null, null);
        catalog.AddTicketType(ticket, pool);

        context.EnableTenantFilterBypass("Seeds ticketing repository isolation test rows.");
        context.AddRange(catalog, pool, ticket);
        await context.SaveChangesAsync();
        context.ClearTenantFilterBypass();
        context.TenantContext = new TestTenantContext(tenantA);
        var repository = new EventTicketCatalogRepository(context);

        await Assert.That(await repository.GetTicketTypeByIdEventAndTenantAsync(ticket.Id, eventA, tenantA, CancellationToken.None)).IsNotNull();
        await Assert.That(await repository.GetTicketTypeByIdEventAndTenantAsync(ticket.Id, Guid.CreateVersion7(), tenantA, CancellationToken.None)).IsNull();
        await Assert.That(await repository.GetTicketTypeByIdEventAndTenantAsync(ticket.Id, eventA, tenantB, CancellationToken.None)).IsNull();
        EventCapacityPool? loadedPool = await repository.GetCapacityPoolByIdEventAndTenantAsync(pool.Id, eventA, tenantA, CancellationToken.None);
        await Assert.That(loadedPool).IsNotNull();
        await Assert.That(loadedPool!.CapacityHoldPolicy?.MasterCode).IsEqualTo("TIMED_HOLD_ON_SELECTION");
        await Assert.That(await repository.GetCapacityPoolByIdEventAndTenantAsync(pool.Id, eventA, tenantB, CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task ManagementCatalog_TracksAndPersistsAddedTicketTypesAndEntitlements()
    {
        await using var context = CreateInMemoryContext("ticketing-management");
        await LookupTableSeeder.SeedTicketingLookupsAsync(context, CancellationToken.None);
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenantId, eventId, "USD", 1);
        EventTicketType initialTicket = CreateFreeTicket(catalog, "Initial");
        catalog.AddTicketType(initialTicket, null);
        context.EnableTenantFilterBypass("Seeds tracked catalog management test rows.");
        context.Add(catalog);
        await context.SaveChangesAsync();
        context.ClearTenantFilterBypass();
        context.TenantContext = new TestTenantContext(tenantId);
        var repository = new EventTicketCatalogRepository(context);

        EventTicketCatalogVersion managed = (await repository.GetManagementCatalogAsync(eventId, tenantId, CancellationToken.None))!;
        EventTicketType addedTicket = CreateFreeTicket(managed, "Added");
        managed.AddTicketType(addedTicket, null);
        managed.AddEntitlement(addedTicket, TicketTypeEntitlement.CreateForEvent(addedTicket.Id, tenantId, eventId, 1));
        await repository.UpdateAsync(managed, CancellationToken.None);

        context.ChangeTracker.Clear();
        EventTicketCatalogVersion persisted = (await repository.GetManagementCatalogAsync(eventId, tenantId, CancellationToken.None))!;
        await Assert.That(persisted.TicketTypes.Count).IsEqualTo(2);
        await Assert.That(persisted.TicketTypes.Single(ticket => ticket.Name == "Added").Entitlements.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ManagementCatalog_RemovesOldEntitlementsBeforePersistingReplacement()
    {
        await using var context = CreateInMemoryContext("ticketing-entitlement-replacement");
        await LookupTableSeeder.SeedTicketingLookupsAsync(context, CancellationToken.None);
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenantId, eventId, "USD", 1);
        EventTicketType ticket = CreateFreeTicket(catalog, "General");
        catalog.AddTicketType(ticket, null);
        catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, tenantId, eventId, 1));
        context.EnableTenantFilterBypass("Seeds entitlement replacement test rows.");
        context.Add(catalog);
        await context.SaveChangesAsync();
        context.ClearTenantFilterBypass();
        context.ChangeTracker.Clear();
        context.TenantContext = new TestTenantContext(tenantId);
        var repository = new EventTicketCatalogRepository(context);

        EventTicketCatalogVersion managed = (await repository.GetManagementCatalogAsync(eventId, tenantId, CancellationToken.None))!;
        EventTicketType managedTicket = managed.TicketTypes.Single();
        TicketTypeEntitlement[] previousEntitlements = managedTicket.Entitlements.ToArray();
        TicketTypeEntitlement replacement = TicketTypeEntitlement.CreateForEvent(managedTicket.Id, tenantId, eventId, 2);

        await repository.RemoveEntitlementsAsync(previousEntitlements, CancellationToken.None);
        managed.UpdateTicketType(
            managedTicket,
            name: "General",
            pricingMode: TicketPricingModeEnum.Free,
            fixedPrice: null,
            minimumPrice: null,
            suggestedPrice: null,
            participantDataCollectionMode: ParticipantDataCollectionModeEnum.None,
            capacityPool: null,
            minimumAge: null,
            maximumAge: null,
            requiresGuardian: false,
            requiresApproval: false,
            perOrderLimit: null,
            perAccountLimit: null,
            perVerifiedContactLimit: null,
            perBookingPartyLimit: null,
            entitlements: [replacement]);
        await repository.UpdateAsync(managed, CancellationToken.None);

        context.ChangeTracker.Clear();
        TicketTypeEntitlement[] persisted = await context.TicketTypeEntitlements
            .Where(entitlement => entitlement.TicketTypeId == managedTicket.Id)
            .ToArrayAsync();
        await Assert.That(persisted.Any(entitlement => entitlement.Id == previousEntitlements[0].Id)).IsFalse();
        await Assert.That(persisted).HasSingleItem();
        await Assert.That(persisted[0].Id).IsEqualTo(replacement.Id);
    }

    [Test]
    public async Task ManagementCatalog_LoadsNormalizedTicketingLookupNavigations()
    {
        await using var context = CreateInMemoryContext("ticketing-management-lookups");
        await LookupTableSeeder.SeedTicketingLookupsAsync(context, CancellationToken.None);
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenantId, eventId, "USD", 1);
        EventTicketType ticket = CreateFreeTicket(catalog, "General");
        catalog.AddTicketType(ticket, null);
        catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, tenantId, eventId, 1));
        context.EnableTenantFilterBypass("Seeds ticketing lookup-navigation test rows.");
        context.Add(catalog);
        await context.SaveChangesAsync();
        context.ClearTenantFilterBypass();
        context.ChangeTracker.Clear();
        context.TenantContext = new TestTenantContext(tenantId);

        EventTicketCatalogVersion loaded = (await new EventTicketCatalogRepository(context)
            .GetManagementCatalogAsync(eventId, tenantId, CancellationToken.None))!;
        EventTicketType loadedTicket = loaded.TicketTypes.Single();
        TicketTypeEntitlement loadedEntitlement = loadedTicket.Entitlements.Single();

        await Assert.That(loaded.TicketCatalogStatus?.MasterCode).IsEqualTo("DRAFT");
        await Assert.That(loadedTicket.TicketPricingMode?.MasterCode).IsEqualTo("FREE");
        await Assert.That(loadedTicket.ParticipantDataCollectionMode?.MasterCode).IsEqualTo("NONE");
        await Assert.That(loadedEntitlement.EntitlementScopeType?.MasterCode).IsEqualTo("EVENT");
        await Assert.That(loadedEntitlement.EntitlementSelectionRule?.MasterCode).IsEqualTo("ALL_INCLUDED");
    }

    [Test]
    public async Task CatalogReads_SelectLatestNonRetiredManagementGraphAndTrackStatusSpecificUpdateGraphs()
    {
        await using var context = CreateInMemoryContext("ticketing-catalog-reads");
        await LookupTableSeeder.SeedTicketingLookupsAsync(context, CancellationToken.None);
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        EventTicketCatalogVersion draft = EventTicketCatalogVersion.Create(tenantId, eventId, "USD", 1);
        EventTicketCatalogVersion published = CreatePublishedCatalog(tenantId, eventId, 2);
        EventTicketCatalogVersion retired = CreatePublishedCatalog(tenantId, eventId, 3);
        retired.Retire();
        context.EnableTenantFilterBypass("Seeds catalog read semantics test rows.");
        context.AddRange(draft, published, retired);
        await context.SaveChangesAsync();
        context.ClearTenantFilterBypass();
        context.TenantContext = new TestTenantContext(tenantId);
        var repository = new EventTicketCatalogRepository(context);

        EventTicketCatalogVersion management = (await repository.GetManagementCatalogAsync(eventId, tenantId, CancellationToken.None))!;
        await Assert.That(management.VersionNumber).IsEqualTo(2);
        await Assert.That(context.Entry(management).State).IsEqualTo(EntityState.Unchanged);

        context.ChangeTracker.Clear();
        EventTicketCatalogVersion draftForUpdate = (await repository.GetDraftCatalogForUpdateAsync(eventId, tenantId, CancellationToken.None))!;
        await Assert.That(draftForUpdate.VersionNumber).IsEqualTo(1);
        await Assert.That(context.Entry(draftForUpdate).State).IsEqualTo(EntityState.Unchanged);

        EventTicketCatalogVersion publishedForUpdate = (await repository.GetPublishedForUpdateAsync(eventId, tenantId, CancellationToken.None))!;
        await Assert.That(publishedForUpdate.VersionNumber).IsEqualTo(2);
        await Assert.That(context.Entry(publishedForUpdate).State).IsEqualTo(EntityState.Unchanged);

        context.ChangeTracker.Clear();
        EventTicketCatalogVersion publishedRead = (await repository.GetPublishedCatalogAsync(eventId, tenantId, CancellationToken.None))!;
        await Assert.That(publishedRead.VersionNumber).IsEqualTo(2);
        await Assert.That(context.Entry(publishedRead).State).IsEqualTo(EntityState.Detached);
        await Assert.That(publishedRead.TicketTypes.Single().Entitlements.Single().TicketTypeId).IsEqualTo(publishedRead.TicketTypes.Single().Id);
    }

    [Test]
    public async Task TicketRepository_DoesNotOwnTransactionCreation()
    {
        string repositoryPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/Explore.Persistence/Repositories/EventTicketCatalogRepository.cs"));
        string source = await File.ReadAllTextAsync(repositoryPath);

        await Assert.That(source.Contains("BeginTransaction", StringComparison.Ordinal)).IsFalse();
        await Assert.That(source.Contains("PublishDraftReplacingCurrentAsync", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task TicketRepository_TranslatesOptimisticConcurrencyFailures()
    {
        await using var context = CreateInMemoryContext(
            "ticketing-concurrency-translation",
            new ThrowingSaveChangesInterceptor(
                () => new DbUpdateConcurrencyException("Simulated optimistic concurrency failure.")));
        var repository = new EventTicketCatalogRepository(context);

        ConcurrencyConflictException exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => repository.SaveChangesAsync(CancellationToken.None));

        await Assert.That(exception.Code).IsEqualTo(ConcurrencyConflictException.ConcurrentUpdate);
    }

    [Test]
    public async Task TicketRepository_TranslatesMetadataResolvedUniqueRaces()
    {
        await using ExploreDbContext modelContext = CreateSqliteContext();
        string[] constraintNames =
        [
            RelationalConstraintDescriptorResolver.UniqueIndex<EventTicketCatalogVersion>(
                modelContext,
                nameof(EventTicketCatalogVersion.TenantId),
                nameof(EventTicketCatalogVersion.EventId)).Name,
            RelationalConstraintDescriptorResolver.UniqueIndex<EventTicketCatalogVersion>(
                modelContext,
                nameof(EventTicketCatalogVersion.TenantId),
                nameof(EventTicketCatalogVersion.EventId),
                nameof(EventTicketCatalogVersion.VersionNumber)).Name,
            RelationalConstraintDescriptorResolver.UniqueIndex<EventCapacityPool>(
                modelContext,
                nameof(EventCapacityPool.TenantId),
                nameof(EventCapacityPool.EventId),
                nameof(EventCapacityPool.Name)).Name
        ];

        foreach (string constraintName in constraintNames)
        {
            await using ExploreDbContext context = CreateSqliteContext(
                new ThrowingSaveChangesInterceptor(() => CreateUniqueViolation(constraintName)));
            var repository = new EventTicketCatalogRepository(context);

            ConcurrencyConflictException exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => repository.SaveChangesAsync(CancellationToken.None));

            await Assert.That(exception.Code).IsEqualTo(ConcurrencyConflictException.ConcurrentUpdate);
        }
    }

    [Test]
    public async Task TicketRepository_LeavesUnrecognizedUniqueFailuresUntranslated()
    {
        DbUpdateException expected = CreateUniqueViolation("ix_unrelated_constraint");
        await using ExploreDbContext context = CreateSqliteContext(
            new ThrowingSaveChangesInterceptor(() => expected));
        var repository = new EventTicketCatalogRepository(context);

        DbUpdateException exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => repository.SaveChangesAsync(CancellationToken.None));

        await Assert.That(exception).IsSameReferenceAs(expected);
    }

    private static DbUpdateException CreateUniqueViolation(string constraintName) => new(
        $"Simulated unique violation for {constraintName}.",
        new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            constraintName: constraintName));

    private static EventTicketCatalogVersion CreatePublishedCatalog(Guid tenantId, Guid eventId, int versionNumber)
    {
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenantId, eventId, "USD", versionNumber);
        EventTicketType ticket = CreateFreeTicket(catalog, $"Ticket {versionNumber}");
        catalog.AddTicketType(ticket, null);
        catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, tenantId, eventId, 1));
        catalog.Publish();
        return catalog;
    }

    private static EventTicketType CreateFreeTicket(EventTicketCatalogVersion catalog, string name) => EventTicketType.Create(
        Guid.CreateVersion7(),
        catalog.TenantId, catalog.Id, name, "USD", TicketPricingModeEnum.Free, null, null, null,
        ParticipantDataCollectionModeEnum.None, null, null, null, false, false, null, null, null, null);

    private static TicketingTestDbContext CreateModelContext() => new(TestDbContextOptions.Create<ExploreDbContext>()
        .UseNpgsql("Host=localhost;Database=ticketing_model;Username=unused;Password=unused")
        .UseSnakeCaseNamingConvention().Options);

    private static async Task<SqliteConnection> OpenSqliteConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static TicketingTestDbContext CreateSqliteContext(SqliteConnection connection) => new(TestDbContextOptions.Create<ExploreDbContext>()
        .UseSqlite(connection)
        .UseSnakeCaseNamingConvention()
        .Options);

    private static TicketingTestDbContext CreateInMemoryContext(string name, SaveChangesInterceptor? interceptor = null)
    {
        var optionsBuilder = TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase($"{name}-{Guid.NewGuid():N}");
        if (interceptor is not null)
        {
            optionsBuilder.AddInterceptors(interceptor);
        }

        return new(optionsBuilder.Options);
    }

    private static ExploreDbContext CreateSqliteContext(SaveChangesInterceptor? interceptor = null)
    {
        var optionsBuilder = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite("Data Source=:memory:")
            .UseSnakeCaseNamingConvention();
        if (interceptor is not null)
        {
            optionsBuilder.AddInterceptors(interceptor);
        }

        return new ExploreDbContext(optionsBuilder.Options);
    }

    private static TicketingTestDbContext CreateNamedInMemoryContext(string databaseName, InMemoryDatabaseRoot databaseRoot, Guid? tenantId = null)
    {
        var context = new TicketingTestDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase(databaseName, databaseRoot)
            .Options);
        if (tenantId is { } value)
        {
            context.TenantContext = new TestTenantContext(value);
        }

        return context;
    }

    private static ServiceProvider CreateRepositoryServiceProvider(string databaseName, InMemoryDatabaseRoot databaseRoot, Guid tenantId)
    {
        var services = new ServiceCollection();
        services.AddScoped<ExploreDbContext>(_ => CreateNamedInMemoryContext(databaseName, databaseRoot, tenantId));
        services.ConfigurePersistenceServices(new ConfigurationBuilder().Build(), skipDbContextRegistration: true, skipLookupCacheInitializer: true);
        return services.BuildIsolatedServiceProvider(validateScopes: true);
    }

    private static OrganizerPaymentProviderConnection ReadyConnection(Guid tenantId, Guid organizerActorId, string externalAccountId, int revision)
    {
        OrganizerPaymentProviderConnection connection = OrganizerPaymentProviderConnection.Create(
            Guid.CreateVersion7(),
            tenantId,
            organizerActorId,
            "stripe",
            "platform-main",
            externalAccountId,
            new DateTime(2026, 8, 13, 11, 0, 0, DateTimeKind.Utc).AddMinutes(revision));
        connection.ApplyReadiness(OrganizerPaymentProviderReadinessObservation.Create(
            "be",
            ChargeCapabilityState.Active,
            ProviderRequirementsState.Satisfied,
            ["usd", "EUR", "usd"],
            new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc).AddMinutes(revision),
            $"stripe-readiness-{revision}"));
        return connection;
    }

    private static OrganizerPaymentProviderAccountOperation CreateAccountOperation(Guid tenantId, Guid organizerActorId, int revision) =>
        OrganizerPaymentProviderAccountOperation.CreateRequested(
            Guid.CreateVersion7(),
            tenantId,
            organizerActorId,
            "stripe",
            "platform-main",
            new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc).AddMinutes(revision));

    private static PaidEventPolicyVersion CreateTenantPaidPolicy(Guid tenantId) => PaidEventPolicyVersion.CreateTenant(
        tenantId,
        isPaymentsEnabled: true,
        allowedOrganizerKinds: [ActorTypeEnum.Organization],
        requiresLocalVerification: true,
        allowedCurrencyCodes: ["EUR"],
        defaultCurrencyCode: "EUR",
        refundProtections: Enum.GetValues<PaidEventRefundProtection>(),
        currencyRiskLimits: [],
        requiresFirstPaidEventReview: true,
        farFutureReviewThresholdDays: 180);

    private static Tenant CreateTenant(Guid tenantId) => new()
    {
        Id = tenantId,
        FullName = "SQLite tenant",
        Slug = $"sqlite-{tenantId:N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!,
        CreatedAt = new DateTime(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc)
    };

    private static TenantStatus CreateActiveTenantStatus() => new()
    {
        Id = (int)TenantStatusEnum.Active,
        MasterCode = "ACTIVE",
        FullName = "Active",
        IsActiveState = true
    };

    private static ActorType CreateOrganizationActorType() => new()
    {
        Id = (int)ActorTypeEnum.Organization,
        MasterCode = "ORGANIZATION",
        FullName = "Organization"
    };

    private static Actor CreateActor(Guid actorId) => new()
    {
        Id = actorId,
        ActorTypeId = (int)ActorTypeEnum.Organization,
        ActorType = null!,
        Pii = new ActorPii
        {
            DisplayName = "SQLite organizer"
        },
        CreatedAt = new DateTime(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc),
        ConcurrencyStamp = Guid.CreateVersion7()
    };

    private static bool HasProperties(IIndex index, params string[] properties) =>
        index.Properties.Select(property => property.Name).SequenceEqual(properties);

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed class ThrowingSaveChangesInterceptor(Func<Exception> exceptionFactory) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw exceptionFactory();
    }

    private sealed class TicketingTestDbContext(DbContextOptions<ExploreDbContext> options) : ExploreDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Actor>().Ignore(actor => actor.MergesFrom).Ignore(actor => actor.MergesInto);
            modelBuilder.Ignore<ActorMerge>();
        }
    }
}
