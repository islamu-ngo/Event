using Event.Api.IntegrationTests.Authentication;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class HumanEventCreationPublisherReaderNativeTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    public async Task OriginalLocalHumanRetainsUnavailableMembershipsWithoutActorsOrPrivateProfiles()
    {
        Guid tenant = await CreateTenantAsync();
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(postgres, tenant);
        var roles = await SeedMembershipsAsync(tenant, request.Subject.PrincipalId);
        await request.InOriginalRequestAsync(async () =>
        {
            var reader = request.Services.GetRequiredService<IHumanEventCreationPublisherReader>();
            var result = await reader.ReadAsync(tenant, request.Subject.PrincipalId);
            await Assert.That(result.Organizations.Count).IsEqualTo(2);
            await Assert.That(result.Groups.Count).IsEqualTo(2);
            await Assert.That(result.Organizations.Select(row => row.OrganizationTenant.Organization.FullName).Order())
                .IsEquivalentTo(new[] { "Allowed organization", "Unavailable organization" });
            await Assert.That(result.Groups.Select(row => row.GroupTenant.Group.FullName).Order())
                .IsEquivalentTo(new[] { "Allowed group", "Unavailable group" });
            await Assert.That(result.Permissions.Select(row => row.RoleId).Distinct())
                .IsEquivalentTo(new[] { roles.Organization, roles.Group });
            await Assert.That(result.Organizations.All(row => row.User is null && row.Role is null
                && row.OrganizationTenant.Organization.Actor is null
                && row.OrganizationTenant.Organization.Email is null)).IsTrue();
            await Assert.That(result.Groups.All(row => row.User is null && row.Role is null
                && row.GroupTenant.Group.Actor is null)).IsTrue();
            await Assert.That(request.Owner.ChangeTracker.Entries().Count()).IsEqualTo(0);
            await Assert.That(request.Owner.Database.CurrentTransaction).IsNull();
            return true;
        });
    }

    [Test]
    public async Task RevokedPermissionChangesCapabilityWithoutRemovingMembershipOptions()
    {
        Guid tenant = await CreateTenantAsync();
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(postgres, tenant);
        var roles = await SeedMembershipsAsync(tenant, request.Subject.PrincipalId);
        await request.InOriginalRequestAsync(async () =>
        {
            var reader = request.Services.GetRequiredService<IHumanEventCreationPublisherReader>();
            await Assert.That((await reader.ReadAsync(tenant, request.Subject.PrincipalId)).Permissions.Count).IsEqualTo(2);
            await using var writer = postgres.CreateDbContext();
            await writer.Set<RolePermission>().Where(row => row.RoleId == roles.Organization || row.RoleId == roles.Group)
                .ExecuteDeleteAsync();
            var next = await reader.ReadAsync(tenant, request.Subject.PrincipalId);
            await Assert.That(next.Organizations.Count).IsEqualTo(2);
            await Assert.That(next.Groups.Count).IsEqualTo(2);
            await Assert.That(next.Permissions.Count).IsEqualTo(0);
            return true;
        });
    }

    [Test]
    public async Task OriginalRequestCannotReadAnotherHumanOrTenant()
    {
        Guid tenant = await CreateTenantAsync();
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(postgres, tenant);
        await request.InOriginalRequestAsync(async () =>
        {
            var reader = request.Services.GetRequiredService<IHumanEventCreationPublisherReader>();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ReadAsync(tenant, Guid.CreateVersion7()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ReadAsync(Guid.CreateVersion7(), request.Subject.PrincipalId));
            await Assert.That(request.Owner.Database.CurrentTransaction).IsNull();
            return true;
        });
    }

    [Test]
    public async Task ReplacingPrincipalAfterNativeReadCannotReleaseOptions()
    {
        Guid tenant = await CreateTenantAsync();
        void Decorate(IServiceCollection services)
        {
            var descriptor = services.Single(row => row.ServiceType == typeof(IEventCreationPublisherRepository));
            services.Remove(descriptor);
            services.AddScoped<IEventCreationPublisherRepository>(provider => new AfterRead(
                (IEventCreationPublisherRepository)(descriptor.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)),
                () =>
                {
                    var request = provider.GetRequiredService<IHttpContextAccessor>().HttpContext!;
                    request.User = new ClaimsPrincipal(request.User);
                }));
        }
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(
            postgres, tenant, configureServices: Decorate);
        await SeedMembershipsAsync(tenant, request.Subject.PrincipalId);
        await request.InOriginalRequestAsync(async () =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                request.Services.GetRequiredService<IHumanEventCreationPublisherReader>().ReadAsync(tenant, request.Subject.PrincipalId));
            await Assert.That(request.Owner.Database.CurrentTransaction).IsNull();
            return true;
        });
    }

    [Test]
    public async Task MembershipRevokedAfterObservationCannotReuseOriginalAuthority()
    {
        Guid tenant = await CreateTenantAsync();
        Guid principal = Guid.Empty;
        bool armed = false;
        void Decorate(IServiceCollection services)
        {
            var descriptor = services.Single(row => row.ServiceType == typeof(IHumanPrincipalSourceFence));
            services.Remove(descriptor);
            services.AddScoped<IHumanPrincipalSourceFence>(provider => new AfterObservation(
                (IHumanPrincipalSourceFence)(descriptor.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)),
                async () =>
                {
                    if (!armed) return;
                    armed = false;
                    await using var writer = postgres.CreateDbContext();
                    await writer.OrganizationMembers.Where(row => row.TenantId == tenant && row.UserId == principal)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
                }));
        }
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(
            postgres, tenant, configureServices: Decorate);
        principal = request.Subject.PrincipalId;
        await SeedMembershipsAsync(tenant, principal);
        armed = true;
        await request.InOriginalRequestAsync(async () =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                request.Services.GetRequiredService<IHumanEventCreationPublisherReader>().ReadAsync(tenant, principal));
            await Assert.That(request.Owner.Database.CurrentTransaction).IsNull();
            return true;
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CredentialExpiryAfterNativeReadIsCheckedBeforeReleasingOptions(bool expired)
    {
        Guid tenant = await CreateTenantAsync();
        var clock = new ReadClock();
        DateTimeOffset boundary = default;
        void Decorate(IServiceCollection services)
        {
            var descriptor = services.Single(row => row.ServiceType == typeof(IEventCreationPublisherRepository));
            services.Remove(descriptor);
            services.AddScoped<IEventCreationPublisherRepository>(provider => new AfterRead(
                (IEventCreationPublisherRepository)(descriptor.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)),
                () => clock.Now = boundary));
        }
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(
            postgres, tenant, clock, Decorate);
        await SeedMembershipsAsync(tenant, request.Subject.PrincipalId);
        boundary = request.Lifetime.ExpiresAt.Add(request.Lifetime.ClockSkew).AddTicks(expired ? 1 : 0);
        await request.InOriginalRequestAsync(async () =>
        {
            var reader = request.Services.GetRequiredService<IHumanEventCreationPublisherReader>();
            if (expired)
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ReadAsync(tenant, request.Subject.PrincipalId));
            else
                await Assert.That((await reader.ReadAsync(tenant, request.Subject.PrincipalId)).Organizations.Count).IsEqualTo(2);
            await Assert.That(request.Owner.Database.CurrentTransaction).IsNull();
            return true;
        });
    }

    private async Task<Guid> CreateTenantAsync()
    {
        await using var setup = postgres.CreateDbContext();
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Creation publisher reader",
            Slug = Guid.CreateVersion7().ToString("N"),
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        setup.Tenants.Add(tenant);
        await setup.SaveChangesAsync();
        return tenant.Id;
    }

    private async Task<(int Organization, int Group)> SeedMembershipsAsync(Guid tenant, Guid user)
    {
        await using var setup = postgres.CreateDbContext();
        int nextRole = await setup.Set<Role>().MaxAsync(row => row.Id) + 1;
        var permission = await setup.Set<Permission>().SingleAsync(row => row.MasterCode == PermissionCodes.EventCreate);
        for (int i = 0; i < 4; i++)
        {
            bool organization = i < 2;
            bool allowed = i % 2 == 0;
            var role = new Role
            {
                Id = nextRole + i,
                MasterCode = $"publisher-{Guid.CreateVersion7():N}",
                FullName = "Publisher role",
                RoleScopeId = (int)(organization ? RoleScopeEnum.Organization : RoleScopeEnum.Group)
            };
            setup.Add(role);
            if (allowed)
                setup.Add(new RolePermission { Role = role, RoleId = role.Id, Permission = permission, PermissionId = permission.Id });
            if (organization)
            {
                var owner = new Organization
                {
                    Id = Guid.CreateVersion7(),
                    Pii = new OrganizationPii
                    {
                        FullName = allowed ? "Allowed organization" : "Unavailable organization",
                        Email = "private@example.invalid"
                    }
                };
                var placement = new OrganizationTenant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant,
                    Tenant = null!,
                    OrganizationId = owner.Id,
                    Organization = owner,
                    ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
                    ApprovalStatus = null!,
                    IsSuspended = true
                };
                setup.Add(new OrganizationMember
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant,
                    Tenant = null!,
                    UserId = user,
                    User = null!,
                    RoleId = role.Id,
                    Role = role,
                    OrganizationTenantId = placement.Id,
                    OrganizationTenant = placement
                });
            }
            else
            {
                var owner = new Group { Id = Guid.CreateVersion7(), FullName = allowed ? "Allowed group" : "Unavailable group" };
                var placement = new GroupTenant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant,
                    Tenant = null!,
                    GroupId = owner.Id,
                    Group = owner,
                    ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
                    ApprovalStatus = null!,
                    IsSuspended = true
                };
                setup.Add(new GroupMember
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant,
                    Tenant = null!,
                    UserId = user,
                    User = null!,
                    RoleId = role.Id,
                    Role = role,
                    GroupTenantId = placement.Id,
                    GroupTenant = placement
                });
            }
        }
        await setup.SaveChangesAsync();
        return (nextRole, nextRole + 2);
    }

    private sealed class ReadClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AfterRead(IEventCreationPublisherRepository inner, Action completed)
        : IEventCreationPublisherRepository
    {
        public async Task<(IReadOnlyList<OrganizationMember> Organizations, IReadOnlyList<GroupMember> Groups,
            IReadOnlyList<RolePermission> Permissions)> ReadAsync(CancellationToken cancellationToken = default)
        {
            var result = await inner.ReadAsync(cancellationToken);
            completed();
            return result;
        }
    }

    private sealed class AfterObservation(IHumanPrincipalSourceFence inner, Func<Task> observed) : IHumanPrincipalSourceFence
    {
        public async Task<HumanPrincipalSourceFacts> ObserveAsync(CancellationToken cancellationToken = default)
        {
            var facts = await inner.ObserveAsync(cancellationToken);
            await observed();
            return facts;
        }

        public Task<IHumanPrincipalSourceLease> AcquireAsync(
            IPolicySourceSetLease sourceSet, CancellationToken cancellationToken = default) =>
            inner.AcquireAsync(sourceSet, cancellationToken);
    }
}
