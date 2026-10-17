using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>Real Local login and persisted sources for settings/import consumption tests.</summary>
internal sealed class ResourceVisitorAuthenticatedFixture : IAsyncDisposable
{
    private readonly LocalIdentityLifecycleHttpFixture fixture;
    private readonly ResourceVisitorImportConsumptionTests.AuthenticatedRequest request;
    internal IServiceProvider Services => request.Services;
    internal ExploreDbContext Context => Services.GetRequiredService<ExploreDbContext>();
    internal Guid TenantId => PlatformDefaults.DefaultTenantId;
    internal Guid UserId => fixture.Binding.LocalSubjectId;
    internal Guid ActorId { get; private set; }
    internal HumanAuthenticationLifetime Lifetime { get; }
    internal LocalIdentityLifecycleHttpFixture.LifecycleClock Clock => fixture.Clock;

    private ResourceVisitorAuthenticatedFixture(LocalIdentityLifecycleHttpFixture fixture,
        ResourceVisitorImportConsumptionTests.AuthenticatedRequest request, HumanAuthenticationLifetime lifetime)
    {
        this.fixture = fixture;
        this.request = request;
        Lifetime = lifetime;
    }

    internal static async Task<ResourceVisitorAuthenticatedFixture> CreateAsync(IInterceptor? interceptor = null)
    {
        var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        var request = new ResourceVisitorImportConsumptionTests.AuthenticatedRequest(fixture.Host.Services);
        try
        {
            await request.AuthenticateAsync(await fixture.SignInAsync());
            var lifetime = await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                .ObserveAsync(request.Context.User, CancellationToken.None)
                ?? throw new InvalidOperationException("The real Local login did not produce validated credential custody.");
            var result = new ResourceVisitorAuthenticatedFixture(fixture, request, lifetime);
            result.ActorId = await result.Context.Actors.Where(row => row.UserId == result.UserId)
                .Select(row => row.Id).SingleAsync();
            if (!await result.Context.TenantUsers.AnyAsync(row => row.TenantId == result.TenantId && row.UserId == result.UserId))
            {
                result.Context.TenantUsers.Add(new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = result.TenantId,
                    Tenant = null!,
                    UserId = result.UserId,
                    User = null!,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = fixture.Clock.GetUtcNow().UtcDateTime
                });
                await result.Context.SaveChangesAsync();
            }
            await result.Context.Tenants.Where(row => row.Id == result.TenantId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.FullName, "Visitor authority"));
            return result;
        }
        catch
        {
            await request.DisposeAsync();
            await fixture.DisposeAsync();
            throw;
        }
    }

    internal AsyncServiceScope CreateScope() => fixture.Host.Services.CreateAsyncScope();
    internal ExploreDbContext CreateIndependentContext() => fixture.Native.CreateDatabase();

    internal async Task<Explore.Domain.Event> SeedEventAsync(bool accountRequired = false)
    {
        var entity = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = "Visitor gate event",
            TenantId = TenantId,
            Tenant = null!,
            ActorId = ActorId,
            Actor = null!,
            OrganizerActorId = ActorId,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            SessionCount = 1,
            FirstSessionStartUtc = fixture.Clock.GetUtcNow().AddDays(1),
            LastSessionEndUtc = fixture.Clock.GetUtcNow().AddDays(1).AddHours(1),
            CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
        };
        entity.ParticipationConfiguration = EventParticipationConfiguration.Create(entity.Id, TenantId,
            (int)ParticipationHandlingModeEnum.PlatformManaged, (int)AdvanceRegistrationObligationEnum.Required,
            (int)(accountRequired ? IdentityAccessModeEnum.AccountRequired : IdentityAccessModeEnum.GuestAllowed),
            accountRequired ? null : GuestRecoveryPolicyEnum.EmailOptional, fixture.Clock.GetUtcNow().UtcDateTime);
        Context.Events.Add(entity);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return entity;
    }

    public async ValueTask DisposeAsync()
    {
        await request.DisposeAsync();
        await fixture.DisposeAsync();
    }
}
