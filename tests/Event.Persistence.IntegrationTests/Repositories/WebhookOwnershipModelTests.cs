// ABOUTME: EF Core model tests for typed webhook ownership and instance/tenant scope isolation.
// ABOUTME: Verifies typed owner FKs, computed configuration scopes, composite containment, and checks.

using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class WebhookOwnershipModelTests
{
    [Test]
    public async Task ConsumerModel_EnforcesTypedOwnerAndOptionalTenantScope()
    {
        await using var context = CreateModelContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var consumer = model.FindEntityType(typeof(WebhookConsumer))!;

        await Assert.That(consumer.FindProperty(nameof(WebhookConsumer.TenantId))!.IsNullable).IsTrue();
        await Assert.That(consumer.FindProperty(nameof(WebhookConsumer.InstanceId))!.IsNullable).IsTrue();
        await Assert.That(consumer.FindProperty(nameof(WebhookConsumer.ConfigurationScopeId))!.IsNullable)
            .IsFalse();
        await AssertComputedConfigurationScopeAsync(
            consumer.FindProperty(nameof(WebhookConsumer.ConfigurationScopeId))!);
        await Assert.That(consumer.GetCheckConstraints().Select(constraint => constraint.Name))
            .Contains("ck_webhook_consumers_typed_owner");
        await Assert.That(consumer.GetCheckConstraints().Select(constraint => constraint.Name))
            .Contains("ck_webhook_consumers_configuration_scope");

        var principals = consumer.GetForeignKeys()
            .Select(foreignKey => foreignKey.PrincipalEntityType.ClrType)
            .ToArray();
        await Assert.That(principals).Contains(typeof(InstanceBootstrapState));
        await Assert.That(principals).Contains(typeof(OrganizationTenant));
        await Assert.That(principals).Contains(typeof(GroupTenant));
        await Assert.That(principals).Contains(typeof(TenantUser));

        var organizationOwner = consumer.GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(OrganizationTenant));
        var groupOwner = consumer.GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(GroupTenant));
        await Assert.That(organizationOwner.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(WebhookConsumer.TenantId), nameof(WebhookConsumer.OrganizationId)]);
        await Assert.That(organizationOwner.PrincipalKey.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(OrganizationTenant.TenantId), nameof(OrganizationTenant.OrganizationId)]);
        await Assert.That(groupOwner.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(WebhookConsumer.TenantId), nameof(WebhookConsumer.GroupId)]);
        await Assert.That(groupOwner.PrincipalKey.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(GroupTenant.TenantId), nameof(GroupTenant.GroupId)]);
    }

    [Test]
    public async Task ChildModels_EnforceTheSameConfigurationScopeAsTheirParents()
    {
        await using var context = CreateModelContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var endpoint = model.FindEntityType(typeof(WebhookEndpoint))!;
        var subscription = model.FindEntityType(typeof(WebhookEndpointSubscription))!;
        var binding = model.FindEntityType(typeof(WebhookConsumerProviderBinding))!;

        await Assert.That(endpoint.FindProperty(nameof(WebhookEndpoint.TenantId))!.IsNullable).IsTrue();
        await Assert.That(endpoint.FindProperty(nameof(WebhookEndpoint.InstanceId))!.IsNullable).IsTrue();
        await Assert.That(subscription.FindProperty(nameof(WebhookEndpointSubscription.TenantId))!.IsNullable).IsTrue();
        await Assert.That(subscription.FindProperty(nameof(WebhookEndpointSubscription.InstanceId))!.IsNullable).IsTrue();
        await Assert.That(endpoint.FindProperty(nameof(WebhookEndpoint.ConfigurationScopeId))!.IsNullable)
            .IsFalse();
        await Assert.That(subscription.FindProperty(nameof(WebhookEndpointSubscription.ConfigurationScopeId))!.IsNullable)
            .IsFalse();
        await Assert.That(binding.FindProperty(nameof(WebhookConsumerProviderBinding.ConfigurationScopeId))!.IsNullable)
            .IsFalse();
        await AssertComputedConfigurationScopeAsync(
            endpoint.FindProperty(nameof(WebhookEndpoint.ConfigurationScopeId))!);
        await AssertComputedConfigurationScopeAsync(
            subscription.FindProperty(nameof(WebhookEndpointSubscription.ConfigurationScopeId))!);
        await AssertComputedConfigurationScopeAsync(
            binding.FindProperty(nameof(WebhookConsumerProviderBinding.ConfigurationScopeId))!);
        await Assert.That(endpoint.GetCheckConstraints().Select(constraint => constraint.Name))
            .Contains("ck_webhook_endpoints_configuration_scope");
        await Assert.That(subscription.GetCheckConstraints().Select(constraint => constraint.Name))
            .Contains("ck_webhook_endpoint_subscriptions_configuration_scope");
        await Assert.That(endpoint.GetCheckConstraints().Select(constraint => constraint.Name))
            .Contains("ck_webhook_endpoints_configuration_scope_key");
        await Assert.That(subscription.GetCheckConstraints().Select(constraint => constraint.Name))
            .Contains("ck_webhook_endpoint_subscriptions_configuration_scope_key");
        await Assert.That(binding.GetCheckConstraints().Select(constraint => constraint.Name))
            .Contains("ck_webhook_consumer_provider_bindings_configuration_scope");
        await Assert.That(binding.GetCheckConstraints()
            .Single(constraint => constraint.Name == "ck_webhook_consumer_provider_bindings_verified_scope")
            .Sql).Contains("verification_state_id <> 3");

        await AssertCompositeScopeForeignKeyAsync<WebhookEndpoint, WebhookConsumer>(
            endpoint,
            nameof(WebhookEndpoint.ConfigurationScopeId),
            nameof(WebhookEndpoint.ConsumerId));
        await AssertCompositeScopeForeignKeyAsync<WebhookEndpointSubscription, WebhookEndpoint>(
            subscription,
            nameof(WebhookEndpointSubscription.ConfigurationScopeId),
            nameof(WebhookEndpointSubscription.EndpointId));
        await AssertCompositeScopeForeignKeyAsync<WebhookConsumerProviderBinding, WebhookConsumer>(
            binding,
            nameof(WebhookConsumerProviderBinding.ConfigurationScopeId),
            nameof(WebhookConsumerProviderBinding.WebhookConsumerId));
    }

    private static async Task AssertCompositeScopeForeignKeyAsync<TDependent, TPrincipal>(
        IEntityType dependent,
        string scopePropertyName,
        string resourcePropertyName)
    {
        var foreignKey = dependent.GetForeignKeys().Single(candidate =>
            candidate.PrincipalEntityType.ClrType == typeof(TPrincipal) &&
            candidate.Properties.Select(property => property.Name)
                .SequenceEqual([scopePropertyName, resourcePropertyName]));

        await Assert.That(foreignKey.Properties.Count).IsEqualTo(2);
    }

    private static async Task AssertComputedConfigurationScopeAsync(IProperty property)
    {
        await Assert.That(property.GetComputedColumnSql())
            .IsEqualTo("COALESCE(tenant_id, instance_id)");
        await Assert.That(property.ValueGenerated).IsEqualTo(ValueGenerated.OnAdd);
        await Assert.That(property.GetBeforeSaveBehavior()).IsEqualTo(PropertySaveBehavior.Ignore);
    }

    private static ExploreDbContext CreateModelContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql("Host=localhost;Database=webhook_ownership_model;Username=unused;Password=unused")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ExploreDbContext(options);
    }
}
