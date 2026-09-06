// ABOUTME: Specifies tenant-filtered digest-only admission recovery persistence metadata.
// ABOUTME: Requires portable lineage uniqueness, concurrency, and no plaintext capability or identity fields.

using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Event.Persistence.IntegrationTests;

public sealed class AdmissionRecoveryPersistenceContractTests
{
    [Test]
    public async Task ModelMapsTenantQualifiedDigestOnlyRecoveryState()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new ExploreDbContext(options);

        IEntityType? entity = context.Model.FindEntityType(typeof(AdmissionRecoveryCapability));
        IEntityType? delivery = context.Model.FindEntityType(typeof(AdmissionRecoveryDeliveryIntent));
        IEntityType? requestIntent = context.Model.FindEntityType(typeof(AdmissionRecoveryRequestIntent));
        string[] propertyNames = entity?.GetProperties().Select(property => property.Name).ToArray() ?? [];
        await Assert.That(entity).IsNotNull();
        await Assert.That(entity!.GetTableName()).IsEqualTo("ie_admission_recovery_capabilities");
        await Assert.That(entity.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(entity.FindProperty(nameof(AdmissionRecoveryCapability.ConcurrencyStamp))!
            .IsConcurrencyToken).IsTrue();
        await Assert.That(entity.FindProperty(nameof(AdmissionRecoveryCapability.LookupDigest))!
            .ClrType).IsEqualTo(typeof(string));
        await Assert.That(entity.FindProperty(nameof(AdmissionRecoveryCapability.LookupDigest))!
            .GetMaxLength()).IsEqualTo(44);
        await Assert.That(entity.FindProperty(nameof(AdmissionRecoveryCapability.LookupDigest))!
            .IsFixedLength()).IsTrue();
        await Assert.That(entity.FindProperty(nameof(AdmissionRecoveryCapability.LocatorDigest))!
            .ClrType).IsEqualTo(typeof(string));
        await Assert.That(entity.FindProperty(nameof(AdmissionRecoveryCapability.LocatorDigest))!
            .GetMaxLength()).IsEqualTo(44);
        await Assert.That(entity.FindProperty(nameof(AdmissionRecoveryCapability.LocatorDigest))!
            .IsFixedLength()).IsTrue();
        IIndex digest = FindIndex(
            entity,
            "TenantId",
            "LookupKeyVersion",
            "LookupDigest");
        IIndex generation = FindIndex(
            entity,
            "TenantId",
            "AdmissionTicketId",
            "Purpose",
            "CapabilityVersion");
        IIndex active = FindIndex(
            entity,
            "TenantId",
            "AdmissionTicketId",
            "Purpose",
            "ActiveUniquenessSlot");
        IIndex locator = FindIndex(
            entity,
            "TenantId",
            "LookupKeyVersion",
            "LocatorDigest");
        await Assert.That(digest.IsUnique).IsTrue();
        await Assert.That(generation.IsUnique).IsTrue();
        await Assert.That(active.IsUnique).IsTrue();
        await Assert.That(locator.IsUnique).IsTrue();
        await Assert.That(propertyNames.Intersect(
            new[] { "Capability", "Email", "NormalizedIdentity", "Recipient", "AdmissionCredential" },
            StringComparer.OrdinalIgnoreCase)).IsEmpty();
        string[] deliveryPropertyNames = delivery?.GetProperties()
            .Select(property => property.Name)
            .ToArray() ?? [];
        await Assert.That(delivery).IsNotNull();
        await Assert.That(delivery!.GetTableName())
            .IsEqualTo("ie_admission_recovery_delivery_intents");
        await Assert.That(delivery.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(delivery.FindProperty("ConcurrencyStamp")!.IsConcurrencyToken).IsTrue();
        IIndex deliveryGeneration = FindIndex(
            delivery,
            "TenantId",
            "RecoveryRequestId",
            "AdmissionTicketId",
            "Purpose",
            "CapabilityVersion");
        await Assert.That(deliveryGeneration.IsUnique).IsTrue();
        await Assert.That(deliveryPropertyNames.Intersect(
            new[] { "Capability", "Email", "NormalizedIdentity", "Recipient", "AdmissionCredential" },
            StringComparer.OrdinalIgnoreCase)).IsEmpty();
        await Assert.That(requestIntent).IsNotNull();
        await Assert.That(requestIntent!.GetTableName())
            .IsEqualTo("ie_admission_recovery_request_intents");
        await Assert.That(requestIntent.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(requestIntent.FindProperty("ConcurrencyStamp")!.IsConcurrencyToken).IsTrue();
        await Assert.That(requestIntent.GetProperties().Select(property => property.Name).Intersect(
            new[] { "Email", "NormalizedIdentity", "Recipient", "Capability", "Digest", "Credential" },
            StringComparer.OrdinalIgnoreCase)).IsEmpty();
    }

    private static IIndex FindIndex(IEntityType entityType, params string[] propertyNames) =>
        entityType.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
}
