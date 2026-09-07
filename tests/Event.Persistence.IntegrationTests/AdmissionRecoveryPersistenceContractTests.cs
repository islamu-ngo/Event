using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Event.Persistence.IntegrationTests;

public sealed class AdmissionRecoveryPersistenceContractTests
{
    [Test]
    public async Task ModelMapsTenantQualifiedDigestOnlyRecoveryState()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new ExploreDbContext(options);

        IModel model = context.GetService<IDesignTimeModel>().Model;
        IEntityType? entity = model.FindEntityType(typeof(AdmissionRecoveryCapability));
        IEntityType? delivery = model.FindEntityType(typeof(AdmissionRecoveryDeliveryIntent));
        IEntityType? requestIntent = model.FindEntityType(typeof(AdmissionRecoveryRequestIntent));
        string[] propertyNames = entity?.GetProperties().Select(property => property.Name).ToArray() ?? [];
        await Assert.That(entity).IsNotNull();
        await Assert.That(entity!.GetTableName()).IsEqualTo("ie_admission_recovery_capabilities");
        await Assert.That(entity.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(entity.FindProperty(nameof(AdmissionRecoveryCapability.ConcurrencyStamp))!
            .IsConcurrencyToken).IsTrue();
        foreach (string digestProperty in new[]
                 {
                     nameof(AdmissionRecoveryCapability.LookupDigest),
                     nameof(AdmissionRecoveryCapability.LocatorDigest)
                 })
        {
            IProperty property = entity.FindProperty(digestProperty)!;
            await Assert.That(property.ClrType).IsEqualTo(typeof(string));
            await Assert.That(property.GetMaxLength()).IsEqualTo(44);
             await Assert.That(property.IsFixedLength()).IsTrue();
             await Assert.That(property.IsNullable).IsFalse();
         }
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
