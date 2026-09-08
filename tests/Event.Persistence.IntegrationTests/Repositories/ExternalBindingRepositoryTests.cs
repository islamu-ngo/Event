using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ExternalBindingRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task GetByExternalKeyAsync_WhenGlobalAndTenantScopedBindingsExist_ReturnsExactScope()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "bindings-scope");
        var globalBinding = NewBinding(
            ExternalBindingTypes.External.ProviderCustomer,
            "customer-42",
            ExternalBindingTypes.Internal.Tenant,
            tenant.Id,
            scopeTenantId: null);
        var tenantBinding = NewBinding(
            ExternalBindingTypes.External.ExternalAdminUser,
            "subject-42",
            ExternalBindingTypes.Internal.User,
            Guid.NewGuid(),
            tenant.Id);
        context.ExternalBindings.AddRange(globalBinding, tenantBinding);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new ExternalBindingRepository(context);

        var globalResult = await repository.GetByExternalKeyAsync(
            "erp",
            "crmworx",
            ExternalBindingTypes.External.ProviderCustomer,
            "customer-42",
            scopeTenantId: null,
            CancellationToken.None);
        var tenantResult = await repository.GetByExternalKeyAsync(
            "erp",
            "crmworx-idp",
            ExternalBindingTypes.External.ExternalAdminUser,
            "subject-42",
            tenant.Id,
            CancellationToken.None);

        await Assert.That(globalResult?.Id).IsEqualTo(globalBinding.Id);
        await Assert.That(globalResult?.ScopeTenantId).IsNull();
        await Assert.That(tenantResult?.Id).IsEqualTo(tenantBinding.Id);
        await Assert.That(tenantResult?.ScopeTenantId).IsEqualTo(tenant.Id);
        await Assert.That(context.ChangeTracker.Entries<ExternalBinding>()).IsEmpty();
    }

    [Test]
    public async Task ExternalBinding_WhenDuplicateGlobalExternalKey_IsRejectedByUniqueIndex()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "bindings-unique");
        context.ExternalBindings.Add(NewBinding(
            ExternalBindingTypes.External.ProviderCustomer,
            "customer-duplicate",
            ExternalBindingTypes.Internal.Tenant,
            tenant.Id,
            scopeTenantId: null));
        await context.SaveChangesAsync();

        context.ExternalBindings.Add(NewBinding(
            ExternalBindingTypes.External.ProviderCustomer,
            "customer-duplicate",
            ExternalBindingTypes.Internal.Tenant,
            Guid.NewGuid(),
            scopeTenantId: null));

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task Create_WhenExternalInternalPairIsNotRegistered_ThrowsPredictableValidationError()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var repository = new ExternalBindingRepository(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.Create(NewBinding(
                ExternalBindingTypes.External.ProviderCustomer,
                "customer-invalid-pair",
                ExternalBindingTypes.Internal.User,
                Guid.NewGuid(),
                scopeTenantId: null)));

        await Assert.That(exception.Message).Contains("is not registered");
    }

    [Test]
    public async Task Create_WhenTenantScopedBindingHasNoScopeTenant_ThrowsPredictableValidationError()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var repository = new ExternalBindingRepository(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.Create(NewBinding(
                ExternalBindingTypes.External.ExternalAdminUser,
                "subject-no-scope",
                ExternalBindingTypes.Internal.User,
                Guid.NewGuid(),
                scopeTenantId: null)));

        await Assert.That(exception.Message).Contains("requires ScopeTenantId");
    }

    [Test]
    public async Task Create_WhenManagedProviderCustomerBindingIsRegistered_PersistsBinding()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "managed-provider");
        var repository = new ExternalBindingRepository(context);

        var binding = await repository.Create(NewBinding(
            ExternalBindingTypes.External.ProviderCustomer,
            "customer-managed-provider",
            ExternalBindingTypes.Internal.Tenant,
            tenant.Id,
            scopeTenantId: null));

        await Assert.That(binding.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(binding.ScopeTenantId).IsNull();
    }

    [Test]
    public async Task ActorIndexes_ShouldRejectMultipleGlobalActorsForSameUser()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var user = NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.Actors.Add(NewUserActor(user.Id, "global-admin"));
        await context.SaveChangesAsync();

        context.Actors.Add(NewUserActor(user.Id, "duplicate-admin"));

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    private static async Task<Tenant> SeedTenantAsync(ExploreDbContext context, string slugPrefix)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            FullName = $"External Binding {slugPrefix}",
            Slug = $"external-binding-{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };

        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant;
    }

    private static ExternalBinding NewBinding(
        string externalType,
        string externalId,
        string internalType,
        Guid internalId,
        Guid? scopeTenantId) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProviderKey = "erp",
            ExternalSystem = externalType == ExternalBindingTypes.External.ExternalAdminUser ? "crmworx-idp" : "crmworx",
            ExternalType = externalType,
            ExternalId = externalId,
            InternalType = internalType,
            InternalId = internalId,
            ScopeTenantId = scopeTenantId,
            ExternalBindingStatusId = (int)ExternalBindingStatusEnum.Active,
            CreatedAt = DateTime.UtcNow,
        };

    private static User NewUser() =>
        new()
        {
            Id = Guid.NewGuid(),
            Pii = new UserPii
            {
                Email = $"admin-{Guid.NewGuid():N}@example.com",
                FirstName = "Amina",
                LastName = "Admin",
            },
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
        };

    private static Actor NewUserActor(Guid userId, string handle)
    {
        var actor = new Actor
        {
            Id = Guid.NewGuid(),
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = userId,
            Pii = new ActorPii { DisplayName = handle },
            CreatedAt = DateTime.UtcNow,
        };
        actor.AtprotoIdentities.Add(new AtprotoIdentity(Explore.Domain.ValueObjects.AtprotoDid.Parse($"did:plc:{Guid.NewGuid():N}"))
        {
            Id = Guid.NewGuid(),
            ActorId = actor.Id,
            Actor = actor,

            Handle = handle,
            PdsHost = "https://pds.example.invalid",
            IsActive = true,
            LastResolvedAt = DateTime.UtcNow,
        });
        return actor;
    }
}
