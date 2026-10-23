using Event.Api.IntegrationTests.Authentication;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class HumanActorContextReaderNativeTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CredentialExpiryAfterTheActualReadIsCheckedBeforeResultRelease(bool expired)
    {
        Guid tenant = await CreateTenantAsync();
        var clock = new ReadClock();
        DateTimeOffset boundary = default;
        void Decorate(IServiceCollection services)
        {
            var descriptor = services.Single(row => row.ServiceType == typeof(IAiActorContextRepository));
            services.Remove(descriptor);
            services.AddScoped<IAiActorContextRepository>(provider => new ExpireAfterRead(
                (IAiActorContextRepository)(descriptor.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)),
                () => clock.Now = boundary));
        }
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(
            postgres, tenant, clock, Decorate);
        boundary = request.Lifetime.ExpiresAt.Add(request.Lifetime.ClockSkew).AddTicks(expired ? 1 : 0);
        await request.InOriginalRequestAsync(async () =>
        {
            var reader = request.Services.GetRequiredService<IHumanActorContextReader>();
            if (expired)
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ReadAsync(tenant, request.Subject.PrincipalId));
            else
                await Assert.That((await reader.ReadAsync(tenant, request.Subject.PrincipalId)).Count).IsEqualTo(1);
            await Assert.That(request.Owner.Database.CurrentTransaction).IsNull();
            return true;
        });
    }

    [Test]
    public async Task OriginalAuthenticatedHumanCanReadAgainOnANewAttemptWithoutPrivateProfiles()
    {
        Guid tenant = await CreateTenantAsync();
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(postgres, tenant);
        await request.InOriginalRequestAsync(async () =>
        {
            var reader = request.Services.GetRequiredService<IHumanActorContextReader>();
            var first = await reader.ReadAsync(tenant, request.Subject.PrincipalId);
            var second = await reader.ReadAsync(tenant, request.Subject.PrincipalId);
            await Assert.That(first.Count).IsEqualTo(1);
            await Assert.That(second.Single().Id).IsEqualTo(first.Single().Id);
            await Assert.That(second.Single().UserId).IsEqualTo(request.Subject.PrincipalId);
            await Assert.That(second.Single().User).IsNull();
            await Assert.That(second.Single().Pii.ProfilePicture).IsNull();
            await Assert.That(second.Single().AtprotoIdentities.Count).IsEqualTo(0);
            await Assert.That(request.Owner.Database.CurrentTransaction).IsNull();
            return true;
        });
    }

    [Test]
    public async Task OriginalRequestCannotSelectAnotherHumanOrTenant()
    {
        Guid tenant = await CreateTenantAsync();
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(postgres, tenant);
        await request.InOriginalRequestAsync(async () =>
        {
            var reader = request.Services.GetRequiredService<IHumanActorContextReader>();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ReadAsync(tenant, Guid.CreateVersion7()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ReadAsync(Guid.CreateVersion7(), request.Subject.PrincipalId));
            await Assert.That(request.Owner.Database.CurrentTransaction).IsNull();
            return true;
        });
    }

    [Test]
    public async Task CommittedMembershipRevocationRemovesActorsOnTheNextAttempt()
    {
        Guid tenant = await CreateTenantAsync();
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(postgres, tenant);
        await request.InOriginalRequestAsync(async () =>
        {
            var reader = request.Services.GetRequiredService<IHumanActorContextReader>();
            await Assert.That((await reader.ReadAsync(tenant, request.Subject.PrincipalId)).Count).IsEqualTo(1);
            await using var writer = postgres.CreateDbContext();
            await writer.TenantUsers.Where(row => row.TenantId == tenant && row.UserId == request.Subject.PrincipalId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            await Assert.That((await reader.ReadAsync(tenant, request.Subject.PrincipalId)).Count).IsEqualTo(0);
            return true;
        });
    }

    private async Task<Guid> CreateTenantAsync()
    {
        await using var setup = postgres.CreateDbContext();
        var tenant = new Explore.Domain.Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Authenticated actor reader",
            Slug = Guid.CreateVersion7().ToString("N"),
            TenantStatusId = (int)Explore.Domain.Enums.TenantStatusEnum.Active,
            TenantStatus = null!
        };
        setup.Tenants.Add(tenant);
        await setup.SaveChangesAsync();
        return tenant.Id;
    }

    private sealed class ReadClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ExpireAfterRead(IAiActorContextRepository inner, Action readCompleted)
        : IAiActorContextRepository
    {
        public async Task<IReadOnlyList<Explore.Domain.Actor>> GetAvailableActorsAsync(CancellationToken cancellationToken = default)
        {
            var actors = await inner.GetAvailableActorsAsync(cancellationToken);
            readCompleted();
            return actors;
        }
    }
}
