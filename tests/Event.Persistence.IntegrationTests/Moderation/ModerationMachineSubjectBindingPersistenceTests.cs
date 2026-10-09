using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationMachineSubjectBindingPersistenceTests
{
    [Test]
    public async Task Replacement_credentials_preserve_the_explicit_service_subject()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var repository = database.Repository(context);
        var first = database.Installation();
        await Assert.That((await repository.EstablishAsync(first)).Outcome)
            .IsEqualTo(ModerationAuthoritySourceOutcome.Established);
        await database.AddKeyAsync("replacement-key");
        var replacement = first with { Id = Guid.CreateVersion7(), KeyId = "replacement-key" };
        await Assert.That((await repository.EstablishAsync(replacement)).Outcome)
            .IsEqualTo(ModerationAuthoritySourceOutcome.Established);
        var original = await repository.ReadCurrentAsync(first.KeyId, first.CredentialSource);
        var replaced = await repository.ReadCurrentAsync(replacement.KeyId, replacement.CredentialSource);
        await Assert.That(original).IsNotNull();
        await Assert.That(replaced!.ServicePrincipalId).IsEqualTo(original!.ServicePrincipalId);
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
    }

    [Test]
    public async Task Repeated_binding_does_not_advance_qualification_or_change_service_identity()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var repository = database.Repository(context);
        var request = database.Installation();
        await Assert.That((await repository.EstablishAsync(request)).Outcome)
            .IsEqualTo(ModerationAuthoritySourceOutcome.Established);
        await Assert.That((await repository.EstablishAsync(request)).Outcome)
            .IsEqualTo(ModerationAuthoritySourceOutcome.Established);
        var otherService = new ServicePrincipal
        {
            Id = Guid.CreateVersion7(), Code = "another-explicit-service", DisplayName = "Other service"
        };
        context.ServicePrincipals.Add(otherService);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await Assert.That((await repository.EstablishAsync(request with { ServicePrincipalId = otherService.Id })).Outcome)
            .IsEqualTo(ModerationAuthoritySourceOutcome.Conflict);
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
        await Assert.That(await context.ModerationMachineSubjectBindings.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task Machine_cannot_install_a_binding_using_its_owners_real_admin_assignment()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var result = await database.Repository(context, machineCaller: true).EstablishAsync(database.Installation());
        await Assert.That(result.Outcome).IsEqualTo(ModerationAuthoritySourceOutcome.Denied);
        await Assert.That(await context.ModerationMachineSubjectBindings.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Revoked_native_administrator_cannot_repeat_an_existing_binding()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var repository = database.Repository(context);
        var request = database.Installation();
        await Assert.That((await repository.EstablishAsync(request)).Outcome)
            .IsEqualTo(ModerationAuthoritySourceOutcome.Established);
        await using (var writer = database.Context())
            await writer.PlatformUserRoles.Where(row => row.UserId == database.AdministratorId).ExecuteDeleteAsync();
        await Assert.That((await repository.EstablishAsync(request)).Outcome)
            .IsEqualTo(ModerationAuthoritySourceOutcome.Denied);
    }

    [Test]
    public async Task Deleted_selected_service_does_not_fall_back_to_the_maintenance_principal()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var repository = database.Repository(context);
        var request = database.Installation();
        await Assert.That((await repository.EstablishAsync(request)).Outcome)
            .IsEqualTo(ModerationAuthoritySourceOutcome.Established);
        await using (var writer = database.Context())
            await writer.ServicePrincipals.Where(row => row.Id == database.ServiceId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        await Assert.That(await repository.ReadCurrentAsync(request.KeyId, request.CredentialSource)).IsNull();
    }

    [Test]
    public async Task Changed_key_ownership_and_wrong_source_cannot_resolve_the_service()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var repository = database.Repository(context);
        var request = database.Installation();
        await Assert.That((await repository.EstablishAsync(request)).Outcome)
            .IsEqualTo(ModerationAuthoritySourceOutcome.Established);
        await Assert.That(await repository.ReadCurrentAsync(request.KeyId, ModerationMachineCredentialSource.Configured))
            .IsNull();
        await using (var writer = database.Context())
        {
            var replacementOwner = Guid.CreateVersion7();
            var changed = await writer.ExternalApiKeys.IgnoreQueryFilters(["Tenant"])
                .Where(row => row.KeyId == request.KeyId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.OwnerId, replacementOwner));
            await Assert.That(changed).IsEqualTo(1);
        }
        await Assert.That(await repository.ReadCurrentAsync(request.KeyId, request.CredentialSource)).IsNull();
    }

    [Test]
    public async Task Revoked_binding_cannot_be_reinstalled_or_resolved()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var repository = database.Repository(context);
        var request = database.Installation();
        await Assert.That((await repository.EstablishAsync(request)).Outcome)
            .IsEqualTo(ModerationAuthoritySourceOutcome.Established);
        await using (var writer = database.Context())
        {
            var binding = await writer.ModerationMachineSubjectBindings.SingleAsync();
            binding.Revoke(1, new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
            await writer.SaveChangesAsync();
        }
        await Assert.That(await repository.ReadCurrentAsync(request.KeyId, request.CredentialSource)).IsNull();
        await Assert.That((await repository.EstablishAsync(request)).Outcome)
            .IsEqualTo(ModerationAuthoritySourceOutcome.Conflict);
    }

    private sealed class SourceDatabase : IAsyncDisposable, ITenantContext
    {
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<ExploreDbContext> options;

        private SourceDatabase(SqliteConnection connection)
        {
            this.connection = connection;
            options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection)
                .UseSnakeCaseNamingConvention().AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance).Options;
        }

        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid AdministratorId { get; } = Guid.CreateVersion7();
        public Guid HostId { get; } = Guid.CreateVersion7();
        public Guid ServiceId { get; } = Guid.CreateVersion7();
        public ExploreDbContext Context() => new(options) { TenantContext = this };
        public ModerationMachineSubjectInstallation Installation() => new(
            Guid.CreateVersion7(), HostId, ModerationMachineCredentialSource.Persisted,
            "initial-service-key", null, ExternalApiKeyOwnerType.InstanceAdmin, AdministratorId, ServiceId);

        public ModerationMachineSubjectBindingRepository Repository(ExploreDbContext context, bool machineCaller = false)
        {
            var http = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, AdministratorId.ToString())], "source-witness"))
                }
            };
            var admin = new AdminContext(http, new PlatformUserRoleRepository(context),
                new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
            var machine = new MachinePrincipalAccessor(http);
            if (machineCaller)
                machine.SetPrincipal(new ApiKeyPrincipalContext("initial-service-key", null,
                    ExternalApiKeyOwnerType.InstanceAdmin, AdministratorId, []));
            return new(context, admin, machine, new ExternalApiKeyRepository(context),
                new UnconfiguredKeys(), TimeProvider.System);
        }

        public static async Task<SourceDatabase> CreateAsync()
        {
            var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
            var database = new SourceDatabase(connection);
            await using var context = database.Context();
            await context.Database.EnsureCreatedAsync();
            var status = new TenantStatus
            {
                Id = (int)TenantStatusEnum.Active, MasterCode = "ACTIVE",
                FullName = "Active", IsActiveState = true
            };
            var tenant = new Tenant
            {
                Id = database.TenantId, FullName = "Service binding tenant", Slug = $"bindings-{database.TenantId:N}",
                TenantStatusId = status.Id, TenantStatus = status
            };
            var user = new User
            {
                Id = database.AdministratorId,
                Pii = new UserPii
                {
                    UserId = database.AdministratorId, Email = $"{database.AdministratorId:N}@example.invalid",
                    FirstName = "Binding", LastName = "Owner"
                }
            };
            var role = new Role
            {
                Id = (int)RoleEnum.Admin, MasterCode = "platform.admin",
                FullName = "Admin", IsSystem = true, RoleScopeId = (int)RoleScopeEnum.Platform,
                RoleScope = new RoleScope
                {
                    Id = (int)RoleScopeEnum.Platform, MasterCode = "PLATFORM", FullName = "Platform"
                }
            };
            var maintenance = new ServicePrincipal
            {
                Id = Guid.CreateVersion7(), Code = $"maintenance-{database.TenantId:N}", DisplayName = "Maintenance"
            };
            var service = new ServicePrincipal
            {
                Id = database.ServiceId, Code = $"service-{database.TenantId:N}", DisplayName = "Explicit service"
            };
            var host = ModerationHostBinding.Establish(database.HostId, Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), maintenance.Id);
            context.AddRange(tenant, user, maintenance, service, host,
                new PlatformUserRole { Id = Guid.CreateVersion7(), UserId = user.Id, User = user, RoleId = role.Id, Role = role },
                ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), host, tenant),
                new ExternalApiKeyStatus
                {
                    Id = (int)ExternalApiKeyStatusEnum.Active, MasterCode = "ACTIVE",
                    FullName = "Active", IsUsable = true
                },
                new ExternalApiKeyCreditPeriod
                {
                    Id = (int)ExternalApiKeyCreditPeriodEnum.None, MasterCode = "NONE", FullName = "None"
                },
                new ExternalApiKeyOwnerTypeLookup
                {
                    Id = (int)ExternalApiKeyOwnerType.InstanceAdmin,
                    MasterCode = "INSTANCE_ADMIN", FullName = "Instance Admin"
                });
            await context.SaveChangesAsync();
            await database.AddKeyAsync("initial-service-key");
            return database;
        }

        public async Task AddKeyAsync(string keyId)
        {
            await using var context = Context();
            context.ExternalApiKeys.Add(new ExternalApiKey
            {
                Id = Guid.CreateVersion7(), KeyId = keyId, Name = "Explicit service credential",
                SecretHash = ApiKeyHashing.ComputeHash(ApiKeyHashing.CreateSecret()), Scopes = "events:read",
                OwnerType = ExternalApiKeyOwnerType.InstanceAdmin, OwnerId = AdministratorId,
                ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active, ExternalApiKeyStatus = null!,
                ExternalApiKeyCreditPeriodId = 1, ExternalApiKeyCreditPeriod = null!
            });
            await context.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class UnconfiguredKeys : IConfiguredApiKeyResourceAuthorityReader
    {
        public ValueTask<ApiKeyPrincipalContext?> ReadAsync(
            string keyId, DateTimeOffset evaluationUtc, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ApiKeyPrincipalContext?>(null);
    }
}
