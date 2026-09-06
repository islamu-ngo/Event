// ABOUTME: EF model invariants for tenant-owned webhook authority and delivery evidence.
// ABOUTME: Verifies named isolation filters, composite relationships, Restrict behavior, indexes, and concurrency tokens.

using Explore.Domain;
using Explore.Domain.Interfaces;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class WebhookTenantModelInvariantTests
{
    private static readonly Type[] ConfigurationScopedWebhookPrincipalTypes =
    [
        typeof(WebhookConsumer),
        typeof(WebhookEndpoint)
    ];

    private static readonly Type[] ConfigurationScopedWebhookDependentTypes =
    [
        typeof(WebhookConsumerProviderBinding),
        typeof(WebhookEndpointSubscription)
    ];

    private static readonly Type[] TenantOwnedWebhookTypes =
    [
        typeof(WebhookConsumer),
        typeof(WebhookConsumerProviderBinding),
        typeof(WebhookEndpoint),
        typeof(WebhookEndpointSubscription),
        typeof(WebhookMessage),
        typeof(WebhookDeliveryPlanSnapshot),
        typeof(WebhookLocalTargetSnapshot),
        typeof(WebhookDeliveryAttempt),
        typeof(WebhookProviderPublication),
        typeof(WebhookProviderPublicationAttempt),
        typeof(IncomingWebhookMessage),
        typeof(IncomingWebhookEffectReceipt),
        typeof(IncomingWebhookProcessingAttempt),
        typeof(IncomingWebhookRedriveRecord)
    ];

    [Test]
    public async Task TenantOwnedWebhookEntities_HaveAlternateKeysAndNamedTenantFilters()
    {
        await using var context = CreateModelContext();
        var missingAlternateKeys = new List<string>();
        var missingTenantFilters = new List<string>();
        foreach (var clrType in TenantOwnedWebhookTypes)
        {
            var entity = context.Model.FindEntityType(clrType)!;
            var scopePropertyName = ConfigurationScopedWebhookPrincipalTypes.Contains(clrType)
                ? "ConfigurationScopeId"
                : "TenantId";
            if (!ConfigurationScopedWebhookDependentTypes.Contains(clrType) &&
                !entity.GetKeys().Any(key =>
                    !key.IsPrimaryKey() &&
                    key.Properties.Select(property => property.Name).SequenceEqual([scopePropertyName, "Id"])))
            {
                missingAlternateKeys.Add(clrType.Name);
            }

            if (entity.FindDeclaredQueryFilter(QueryFilterNames.Tenant) is null)
            {
                missingTenantFilters.Add(clrType.Name);
            }
        }

        await Assert.That(missingAlternateKeys).IsEmpty();
        await Assert.That(missingTenantFilters).IsEmpty();
    }

    [Test]
    public async Task TenantRelationships_AreCompositeIndexedAndRestrictDeletes()
    {
        await using var context = CreateModelContext();
        var nonCompositeRelationships = new List<string>();
        var nonRestrictRelationships = new List<string>();
        var unindexedRelationships = new List<string>();

        foreach (var clrType in TenantOwnedWebhookTypes)
        {
            var entity = context.Model.FindEntityType(clrType)!;
            var tenantRelationships = entity.GetForeignKeys().Where(foreignKey =>
                typeof(ITenantEntity).IsAssignableFrom(foreignKey.PrincipalEntityType.ClrType) &&
                foreignKey.PrincipalEntityType.FindProperty("TenantId") is not null);

            foreach (var foreignKey in tenantRelationships)
            {
                var foreignKeyProperties = foreignKey.Properties.Select(property => property.Name).ToArray();
                var principalKeyProperties = foreignKey.PrincipalKey.Properties
                    .Select(property => property.Name)
                    .ToArray();
                var relationship = $"{clrType.Name}->{foreignKey.PrincipalEntityType.ClrType.Name}";
                if (foreignKeyProperties[0] != "TenantId" ||
                    principalKeyProperties[0] != "TenantId" ||
                    foreignKeyProperties.Length != principalKeyProperties.Length)
                {
                    nonCompositeRelationships.Add(relationship);
                }

                if (foreignKey.DeleteBehavior != DeleteBehavior.Restrict)
                {
                    nonRestrictRelationships.Add(relationship);
                }

                if (!entity.GetIndexes().Any(index =>
                        index.Properties.Select(property => property.Name).Take(foreignKeyProperties.Length)
                            .SequenceEqual(foreignKeyProperties)))
                {
                    unindexedRelationships.Add(relationship);
                }
            }
        }

        await Assert.That(nonCompositeRelationships).IsEmpty();
        await Assert.That(nonRestrictRelationships).IsEmpty();
        await Assert.That(unindexedRelationships).IsEmpty();
    }

    [Test]
    public async Task MutableAuthority_UsesExplicitConcurrencyAndPublicationOwnsPlanIdentity()
    {
        await using var context = CreateModelContext();

        foreach (var clrType in new[]
                 {
                     typeof(WebhookConsumerProviderBinding),
                     typeof(WebhookLocalTargetSnapshot),
                     typeof(WebhookProviderPublication)
                 })
        {
            var concurrencyVersion = context.Model.FindEntityType(clrType)!
                .FindProperty("ConcurrencyVersion")!;
            await Assert.That(concurrencyVersion.IsConcurrencyToken).IsTrue();
            await Assert.That(concurrencyVersion.ClrType).IsEqualTo(typeof(long));
        }

        var publication = context.Model.FindEntityType(typeof(WebhookProviderPublication))!;
        await Assert.That(publication.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(WebhookDeliveryPlanSnapshot) &&
            foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual(["TenantId", "WebhookDeliveryPlanSnapshotId"]))).IsTrue();
        await Assert.That(publication.GetIndexes().Any(index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(
                ["TenantId", "WebhookMessageId", "ProviderKindId", "ProviderBindingId"]))).IsTrue();
        await Assert.That(context.Model.GetEntityTypes().Any(entity =>
            entity.ClrType.Name == "WebhookProviderTargetSnapshot" ||
            entity.GetTableName() == "webhook_provider_target_snapshots")).IsFalse();
    }

    private static ExploreDbContext CreateModelContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql("Host=localhost;Database=webhook_model;Username=unused;Password=unused")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ExploreDbContext(options);
    }

}
