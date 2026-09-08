// ABOUTME: Composes event visitor command gates with production services and a real SQLite database.
// ABOUTME: Shares the event/provider authority surface so race tests need no repository or unit-of-work substitutes.

using System.Security.Claims;
using Explore.Application;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.Secrets.Extensions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Event.Persistence.IntegrationTests.Fixtures;

internal sealed class EventVisitorCapabilitySqliteFixture : IAsyncDisposable, ITenantContext
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"event-visitor-{Guid.CreateVersion7():N}.db");
    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;
    public Guid TenantId { get; } = Guid.CreateVersion7();
    internal Guid UserId { get; } = Guid.CreateVersion7();
    internal Guid ActorId { get; } = Guid.CreateVersion7();
    internal IServiceProvider Services => _scope.ServiceProvider;
    internal ExploreDbContext Context => Services.GetRequiredService<ExploreDbContext>();
    internal string DatabasePath => _path;

    internal static async Task<EventVisitorCapabilitySqliteFixture> CreateAsync()
    {
        var fixture = new EventVisitorCapabilitySqliteFixture();
        await EmailDispatchSqliteFixture.CreateDatabaseAsync(fixture._path);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite", ["Database:Database"] = fixture._path,
            ["Authentication:Provider"] = "Local",
            ["SecretProvider:Provider"] = "Environment"
        }).Build();
        var services = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true, EnvironmentName = Environments.Production
        }).Services;
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHybridCache();
        services.AddMetrics();
        services.AddSingleton<ProjectionMetrics>();
        services.AddSingleton<BusinessMetrics>();
        services.Configure<InstanceOperatorIdentityOptions>(options =>
        {
            options.OperatorId = Guid.CreateVersion7();
            options.PublicName = "Fixture operator";
            options.LegalName = "Fixture operator ASBL";
            options.OfficialOrigin = "https://example.test";
            options.OperatorKindCode = "registered_organization";
            options.JurisdictionCountryCode = "BE";
            options.RegistrationIdentifier = "BE 0123.456.789";
            options.PublicContactEmail = "contact@example.test";
            options.WebsiteUrl = "https://example.test";
            options.LegalNoticeUrl = "https://example.test/legal";
            options.TermsUrl = "https://example.test/terms";
            options.PrivacyUrl = "https://example.test/privacy";
        });
        services.ConfigureApplicationServices(configuration);
        services.ConfigureInfrastructureServices(configuration);
        services.AddSecretProvider(configuration);
        services.AddSecretResolution();
        services.ConfigurePersistenceServices(configuration, skipLookupCacheInitializer: true);
        services.AddSingleton<ITenantContext>(fixture);
        services.AddSingleton<IHttpContextAccessor>(new FixtureHttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, fixture.UserId.ToString())], "fixture"))
            }
        });
        fixture._provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        fixture._scope = fixture._provider.CreateAsyncScope();
        var now = DateTime.UtcNow;
        fixture.Context.Tenants.Add(new Tenant
        {
            Id = fixture.TenantId, FullName = "Visitor authority", Slug = $"visitor-{fixture.TenantId:N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        });
        fixture.Context.Users.Add(new User
        {
            Id = fixture.UserId, Pii = new UserPii { Email = string.Empty, FirstName = "Visitor", LastName = "Operator" },
            CreatedAt = now
        });
        fixture.Context.Actors.Add(new Actor
        {
            Id = fixture.ActorId, ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
            UserId = fixture.UserId, Pii = new ActorPii { DisplayName = "Visitor operator" }, CreatedAt = now
        });
        fixture.Context.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(), TenantId = fixture.TenantId, Tenant = null!,
            UserId = fixture.UserId, User = null!, StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = now, CreatedAt = now
        });
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return fixture;
    }

    internal Task<TResponse> ExecuteAsync<TCommand, TResponse>(TCommand command)
        where TCommand : IRequest<TResponse> =>
        Services.GetRequiredService<IRequestHandler<TCommand, TResponse>>().Handle(command, CancellationToken.None);

    internal async Task<Explore.Domain.Event> SeedEventAsync(bool accountRequired = false)
    {
        var entity = new Explore.Domain.Event
        {
            Id = Guid.CreateVersion7(), Title = "Visitor gate event", TenantId = TenantId, Tenant = null!,
            ActorId = ActorId, Actor = null!, OrganizerActorId = ActorId,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, EventStatus = null!,
            SessionCount = 1, FirstSessionStartUtc = new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero),
            CreatedAt = DateTime.UtcNow
        };
        ConfigureEventParticipationDto participation = Participation(accountRequired);
        entity.ParticipationConfiguration = EventParticipationConfiguration.Create(entity.Id, TenantId,
            participation.ParticipationHandlingModeId, participation.AdvanceRegistrationObligationId,
            participation.IdentityAccessModeId, participation.GuestRecoveryPolicy, DateTime.UtcNow);
        Context.Events.Add(entity);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return entity;
    }

    internal async Task<(Guid CatalogId, Guid TicketId)> SeedTicketAsync(Guid eventId)
    {
        var catalog = EventTicketCatalogVersion.Create(TenantId, eventId, "USD", 1);
        var pool = EventCapacityPool.Create(TenantId, eventId, "Native allocation", 10, 900,
            CapacityHoldPolicyEnum.TimedHoldOnSelection, CapacityOversellPolicyEnum.Disallow, true);
        var ticket = EventTicketType.Create(Guid.CreateVersion7(), TenantId, catalog.Id, "Free admission", "USD",
            TicketPricingModeEnum.Free, null, null, null, ParticipantDataCollectionModeEnum.None, pool.Id,
            null, null, false, false, null, null, null, null);
        catalog.AddTicketType(ticket, pool);
        catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, TenantId, eventId, 1));
        catalog.Publish();
        Context.AddRange(catalog, pool);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return (catalog.Id, ticket.Id);
    }

    internal static ConfigureEventParticipationDto Participation(bool accountRequired = true) => new()
    {
        ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.PlatformManaged,
        AdvanceRegistrationObligationId = (int)AdvanceRegistrationObligationEnum.Required,
        IdentityAccessModeId = (int)(accountRequired ? IdentityAccessModeEnum.AccountRequired : IdentityAccessModeEnum.GuestAllowed),
        GuestRecoveryPolicy = accountRequired ? null : GuestRecoveryPolicyEnum.EmailOptional
    };

    private sealed class FixtureHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        using var connection = new SqliteConnection($"Data Source={_path}");
        SqliteConnection.ClearPool(connection);
        File.Delete(_path);
        File.Delete(_path + "-wal");
        File.Delete(_path + "-shm");
    }
}
