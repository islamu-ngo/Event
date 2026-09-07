using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class WebhookOwnershipContainmentTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(WebhookConsumerKind.Organization)]
    [Arguments(WebhookConsumerKind.Group)]
    [Arguments(WebhookConsumerKind.User)]
    public async Task ConsumerOwnerForeignKey_RejectsCrossTenantOwner(WebhookConsumerKind ownerKind)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var consumerTenant = CreateTenant("consumer-owner");
        var foreignTenant = CreateTenant("foreign-owner");
        context.Tenants.AddRange(consumerTenant, foreignTenant);
        await context.SaveChangesAsync();
        var foreignOwnerId = await SeedOwnerAsync(context, foreignTenant.Id, ownerKind);
        context.WebhookConsumers.Add(CreateOwnedConsumer(
            consumerTenant.Id,
            ownerKind,
            foreignOwnerId));

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task EndpointForeignKey_RejectsCrossTenantConsumerSubstitution()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var consumerTenant = CreateTenant("endpoint-consumer");
        var endpointTenant = CreateTenant("endpoint-foreign");
        var consumer = CreateTenantConsumer(consumerTenant.Id);
        context.Tenants.AddRange(consumerTenant, endpointTenant);
        context.WebhookConsumers.Add(consumer);
        await context.SaveChangesAsync();
        context.WebhookEndpoints.Add(CreateEndpoint(endpointTenant.Id, null, consumer.Id));

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task EndpointForeignKey_RejectsInstanceScopeForTenantConsumer()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = CreateTenant("instance-endpoint-consumer");
        var instance = CreateInstance();
        var consumer = CreateTenantConsumer(tenant.Id);
        context.Tenants.Add(tenant);
        context.InstanceBootstrapStates.Add(instance);
        context.WebhookConsumers.Add(consumer);
        await context.SaveChangesAsync();
        context.WebhookEndpoints.Add(CreateEndpoint(null, instance.Id, consumer.Id));

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task SubscriptionForeignKey_RejectsCrossTenantEndpointSubstitution()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var endpointTenant = CreateTenant("subscription-endpoint");
        var subscriptionTenant = CreateTenant("subscription-foreign");
        var consumer = CreateTenantConsumer(endpointTenant.Id);
        var endpoint = CreateEndpoint(endpointTenant.Id, null, consumer.Id);
        var eventType = CreateEventType();
        context.Tenants.AddRange(endpointTenant, subscriptionTenant);
        context.WebhookConsumers.Add(consumer);
        context.WebhookEndpoints.Add(endpoint);
        context.WebhookEventTypes.Add(eventType);
        await context.SaveChangesAsync();
        context.WebhookEndpointSubscriptions.Add(new WebhookEndpointSubscription
        {
            Id = Guid.CreateVersion7(),
            TenantId = subscriptionTenant.Id,
            EndpointId = endpoint.Id,
            EventTypeId = eventType.Id,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task ProviderBindingForeignKey_RejectsInstanceScopeForTenantConsumer()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = CreateTenant("instance-binding-consumer");
        var instance = CreateInstance();
        var consumer = CreateTenantConsumer(tenant.Id);
        context.Tenants.Add(tenant);
        context.InstanceBootstrapStates.Add(instance);
        context.WebhookConsumers.Add(consumer);
        await context.SaveChangesAsync();
        context.WebhookConsumerProviderBindings.Add(
            WebhookConsumerProviderBinding.CreatePending(
                null,
                consumer.Id,
                instance.Id,
                "self-hosted",
                CreateProviderProfile(),
                WebhookProviderCapability.AppPortal));

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    private static async Task<Guid> SeedOwnerAsync(
        Explore.Persistence.ExploreDbContext context,
        Guid tenantId,
        WebhookConsumerKind ownerKind)
    {
        Tenant tenant = await context.Tenants.SingleAsync(value => value.Id == tenantId);
        switch (ownerKind)
        {
            case WebhookConsumerKind.Organization:
                {
                    var organization = new Organization
                    {
                        Id = Guid.CreateVersion7(),
                        Pii = new OrganizationPii { FullName = "Foreign webhook owner" },
                        ConcurrencyStamp = Guid.CreateVersion7()
                    };
                    context.OrganizationTenants.Add(new OrganizationTenant
                    {
                        Id = Guid.CreateVersion7(),
                        TenantId = tenantId,
                        Tenant = tenant,
                        OrganizationId = organization.Id,
                        Organization = organization,
                        ApprovalStatusId = 1,
                        ApprovalStatus = null!,
                        ConcurrencyStamp = Guid.CreateVersion7()
                    });
                    await context.SaveChangesAsync();
                    return organization.Id;
                }
            case WebhookConsumerKind.Group:
                {
                    var group = new Group
                    {
                        Id = Guid.CreateVersion7(),
                        FullName = "Foreign webhook owner",
                        ConcurrencyStamp = Guid.CreateVersion7()
                    };
                    context.GroupTenants.Add(new GroupTenant
                    {
                        Id = Guid.CreateVersion7(),
                        TenantId = tenantId,
                        Tenant = tenant,
                        GroupId = group.Id,
                        Group = group,
                        ApprovalStatusId = 1,
                        ApprovalStatus = null!,
                        ConcurrencyStamp = Guid.CreateVersion7()
                    });
                    await context.SaveChangesAsync();
                    return group.Id;
                }
            case WebhookConsumerKind.User:
                {
                    var user = new User
                    {
                        Id = Guid.CreateVersion7(),
                        Pii = new UserPii
                        {
                            Email = $"webhook-owner-{Guid.NewGuid():N}@example.com",
                            FirstName = "Webhook",
                            LastName = "Owner"
                        },
                        EmailVerified = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    context.Users.Add(user);
                    await context.SaveChangesAsync();
                    context.TenantUsers.Add(new TenantUser
                    {
                        Id = Guid.CreateVersion7(),
                        TenantId = tenantId,
                        Tenant = tenant,
                        UserId = user.Id,
                        User = user,
                        StatusId = (int)TenantUserStatusEnum.Active,
                        JoinedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    });
                    await context.SaveChangesAsync();
                    return user.Id;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(ownerKind));
        }
    }

    private static WebhookConsumer CreateOwnedConsumer(
        Guid tenantId,
        WebhookConsumerKind ownerKind,
        Guid ownerId) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            OrganizationId = ownerKind == WebhookConsumerKind.Organization ? ownerId : null,
            GroupId = ownerKind == WebhookConsumerKind.Group ? ownerId : null,
            OwnerUserId = ownerKind == WebhookConsumerKind.User ? ownerId : null,
            ConsumerKind = ownerKind,
            Name = $"{ownerKind} webhook consumer",
            Status = WebhookConsumerStatus.Active,
            ProviderMode = WebhookProviderMode.Local,
            ConfigurationVersion = 1,
            CreatedAt = DateTime.UtcNow
        };

    private static WebhookConsumer CreateTenantConsumer(Guid tenantId) =>
        WebhookConsumer.Create(
            WebhookOwnershipScope.Create(
                WebhookConsumerKind.Tenant,
                tenantId,
                null,
                null,
                null,
                null),
            "Tenant webhook consumer",
            WebhookProviderMode.Local,
            DateTime.UtcNow);

    private static WebhookEndpoint CreateEndpoint(
        Guid? tenantId,
        Guid? instanceId,
        Guid consumerId) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            InstanceId = instanceId,
            ConsumerId = consumerId,
            Url = "https://integrator.example/webhook",
            Status = WebhookEndpointStatus.Active,
            SecretRef = "configuration:webhook:endpoint-secret",
            SecretVersion = 1,
            SecretActivatedAt = DateTime.UtcNow,
            ConfigurationVersion = 1,
            MaxAttempts = 8,
            TimeoutSeconds = 15,
            CreatedAt = DateTime.UtcNow
        };

    private static WebhookEventType CreateEventType() =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Name = $"event.containment.{Guid.NewGuid():N}",
            GroupName = "event",
            Description = "Containment test event type.",
            SchemaJson = "{}",
            SchemaVersion = 1,
            IsPublic = true,
            IsEnabled = true,
            PayloadRetentionDays = 14,
            CreatedAt = DateTime.UtcNow
        };

    private static InstanceBootstrapState CreateInstance() =>
        InstanceBootstrapState.CreateInteractivePending(
            Guid.CreateVersion7(),
            DeploymentMode.MultiTenant,
            DateTime.UtcNow);

    private static WebhookProviderCapabilityProfile CreateProviderProfile() =>
        WebhookProviderCapabilityProfile.Create(
            WebhookProviderKind.Svix,
            "1.96.1",
            WebhookProviderCapability.AppPortal,
            "selfhost-v1.96.1-v1",
            DateTimeOffset.UtcNow);

    private static Tenant CreateTenant(string slugPrefix) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            FullName = "Webhook containment tenant",
            Slug = $"{slugPrefix}-{Guid.NewGuid():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
}
