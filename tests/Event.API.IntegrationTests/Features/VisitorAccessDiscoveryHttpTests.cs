
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Application.DTOs.Instance;
using Explore.Domain.Settings;
using Microsoft.AspNetCore.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.Models.Common;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class VisitorAccessDiscoveryHttpTests
{
    private const string ProviderPath = "/api/instanceonboarding/auth-provider-configuration";
    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    [Test]
    [Arguments(VisitorAccessMode.FullRegistrationAndAuth, true)]
    [Arguments(VisitorAccessMode.AnonymousOnly, true)]
    [Arguments(VisitorAccessMode.DirectoryListingOnly, false)]
    public async Task LocalOnlyPublicSurfacesShareVisitorFactsWithoutAdvertisingSignup(VisitorAccessMode mode, bool allocation)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await SeedIdentityAsync(factory);
        await SetSettingAsync(factory, GovernanceSettingKeys.PublicExperience.VisitorAccessMode, mode.ToString());
        using var client = CreateClient(factory);
        foreach (string path in new[] { ProviderPath, "/api/publicexperience/settings", "/api/publicexperience/shell" })
        {
            using var response = await client.GetAsync(path, Token);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            JsonElement visitor = document.RootElement.GetProperty("visitorAccess");
            await Assert.That(visitor.GetProperty("mode").GetString()).IsEqualTo(mode.ToString());
            await Assert.That(visitor.GetProperty("allowsNewNativeAllocation").GetBoolean()).IsEqualTo(allocation);
            await Assert.That(visitor.GetProperty("allowsAnonymousParticipation").GetBoolean()).IsEqualTo(allocation);
            await Assert.That(visitor.GetProperty("allowsAccountRequiredParticipation").GetBoolean()).IsFalse();
            await Assert.That(visitor.GetProperty("allowsExistingAccountLogin").GetBoolean()).IsFalse();
            await Assert.That(visitor.GetProperty("signupDestinations").GetArrayLength()).IsEqualTo(0);
            if (path == ProviderPath)
            {
                await Assert.That(document.RootElement.GetProperty("primaryProviderCode").GetString()).IsEqualTo("local");
                await Assert.That(document.RootElement.GetProperty("_links").EnumerateObject().Any(link => link.Name.StartsWith("signup:", StringComparison.Ordinal))).IsFalse();
                await Assert.That(response.Headers.CacheControl?.Private).IsEqualTo(true);
            }
        }
    }

    [Test]
    [Arguments(PublicOnboardingPolicy.Unknown, false)]
    [Arguments(PublicOnboardingPolicy.Denied, false)]
    [Arguments(PublicOnboardingPolicy.Allowed, true)]
    public async Task SecondaryConfigurableProviderRequiresDeclaredSignupButRetainsExistingLogin(PublicOnboardingPolicy policy, bool signup)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await SetSettingAsync(factory, GovernanceSettingKeys.Authentication.GoogleSsoEnabled, true);
        await SetSettingAsync(factory, GovernanceSettingKeys.Authentication.GoogleClientId, "native-public-client");
        await SetSettingAsync(factory, GovernanceSettingKeys.Authentication.GooglePublicOnboardingPolicy, policy.ToString());
        await SetSettingAsync(factory, GovernanceSettingKeys.Authentication.GooglePublicSignupUrl, "https://accounts.example.test/create-account");
        using var client = CreateClient(factory);
        using var response = await client.GetAsync(ProviderPath, Token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
        JsonElement visitor = body.RootElement.GetProperty("visitorAccess");
        await Assert.That(visitor.GetProperty("allowsExistingAccountLogin").GetBoolean()).IsTrue();
        await Assert.That(visitor.GetProperty("allowsAccountRequiredParticipation").GetBoolean()).IsEqualTo(signup);
        await Assert.That(visitor.GetProperty("signupDestinations").GetArrayLength()).IsEqualTo(signup ? 1 : 0);
        JsonElement links = body.RootElement.GetProperty("_links");
        await Assert.That(links.TryGetProperty("signup:google", out var signupLink)).IsEqualTo(signup);
        if (signup)
        {
            await Assert.That(signupLink.GetProperty("href").GetString()).IsEqualTo("https://accounts.example.test/create-account");
            await Assert.That(signupLink.TryGetProperty("method", out var method) ? method.GetString() : "GET").IsEqualTo("GET");
            await Assert.That(visitor.GetProperty("signupDestinations")[0].GetProperty("provider").GetString()).IsEqualTo("Google");
        }
    }

    [Test]
    public async Task CachedEventDetailsUseFreshPolicyAndPreserveExternalAndWalkInDiscovery()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        Guid guestEvent = await SeedEventAsync(factory, ParticipationHandlingModeEnum.PlatformManaged, IdentityAccessModeEnum.GuestAllowed);
        Guid accountEvent = await SeedEventAsync(factory, ParticipationHandlingModeEnum.PlatformManaged, IdentityAccessModeEnum.AccountRequired);
        Guid externalEvent = await SeedEventAsync(factory, ParticipationHandlingModeEnum.ExternalManaged, IdentityAccessModeEnum.GuestAllowed);
        Guid walkInEvent = await SeedEventAsync(factory, ParticipationHandlingModeEnum.WalkIn, IdentityAccessModeEnum.GuestAllowed);
        using var client = CreateClient(factory);
        using (var guest = await GetEventAsync(client, guestEvent))
            await Assert.That(guest.RootElement.GetProperty("_links").TryGetProperty("start-guest-registration", out _)).IsTrue();
        using (var account = await GetEventAsync(client, accountEvent))
            await Assert.That(account.RootElement.GetProperty("_links").TryGetProperty("sign-in-to-register", out _)).IsFalse();

        await SetSettingAsync(factory, GovernanceSettingKeys.Authentication.GoogleSsoEnabled, true);
        await SetSettingAsync(factory, GovernanceSettingKeys.Authentication.GoogleClientId, "native-public-client");
        await SetSettingAsync(factory, GovernanceSettingKeys.Authentication.GooglePublicOnboardingPolicy, "Allowed");
        await SetSettingAsync(factory, GovernanceSettingKeys.Authentication.GooglePublicSignupUrl, "https://accounts.example.test/create-account");
        using (var account = await GetEventAsync(client, accountEvent))
            await Assert.That(account.RootElement.GetProperty("_links").TryGetProperty("sign-in-to-register", out _)).IsTrue();

        await SetSettingAsync(factory, GovernanceSettingKeys.PublicExperience.VisitorAccessMode, "DirectoryListingOnly");
        using (var guest = await GetEventAsync(client, guestEvent))
        {
            await Assert.That(guest.RootElement.GetProperty("visitorAccess").GetProperty("allowsNewNativeAllocation").GetBoolean()).IsFalse();
            await Assert.That(guest.RootElement.GetProperty("_links").TryGetProperty("start-guest-registration", out _)).IsFalse();
            await Assert.That(guest.RootElement.GetProperty("_links").TryGetProperty("collection", out _)).IsTrue();
        }
        using (var external = await GetEventAsync(client, externalEvent))
            await Assert.That(external.RootElement.GetProperty("_links").TryGetProperty("external-registration", out _)).IsTrue();
        using (var walkIn = await GetEventAsync(client, walkInEvent))
            await Assert.That(walkIn.RootElement.GetProperty("participationConfiguration").GetProperty("participationHandlingModeId").GetInt32()).IsEqualTo((int)ParticipationHandlingModeEnum.WalkIn);
    }

    [Test]
    public async Task RealLocalLoginRetainsOperatorSessionWhileDirectoryModeRemovesAuthenticatedStart()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        Guid eventId = await SeedEventAsync(factory, ParticipationHandlingModeEnum.PlatformManaged, IdentityAccessModeEnum.GuestAllowed);
        using var client = CreateClient(factory);
        using var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var session = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.RootElement.GetProperty("token").GetString());
        using (var current = await client.GetAsync("/api/user", Token))
            await Assert.That(current.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using (var available = await GetEventAsync(client, eventId))
            await Assert.That(available.RootElement.GetProperty("_links").TryGetProperty("start-registration", out _)).IsTrue();
        await SetSettingAsync(factory, GovernanceSettingKeys.PublicExperience.VisitorAccessMode, "DirectoryListingOnly");
        using (var blocked = await GetEventAsync(client, eventId))
            await Assert.That(blocked.RootElement.GetProperty("_links").TryGetProperty("start-registration", out _)).IsFalse();
        using var retainedSession = await client.GetAsync("/api/user", Token);
        await Assert.That(retainedSession.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    [Arguments("tenant-scalar")]
    [Arguments("tenant-batch")]
    [Arguments("control-plane")]
    [Arguments("provider")]
    public async Task VisitorMutationsReturnExplicitConflictWithoutChangingAccountRequiredEvents(string surface)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        await GrantAdministratorAsync(factory, credentials.Identifier);
        await SetSettingAsync(factory, GovernanceSettingKeys.Authentication.GoogleSsoEnabled, true);
        await SetSettingAsync(factory, GovernanceSettingKeys.Authentication.GoogleClientId, "native-public-client");
        await SetSettingAsync(factory, GovernanceSettingKeys.Authentication.GooglePublicOnboardingPolicy, "Allowed");
        await SetSettingAsync(factory, GovernanceSettingKeys.Authentication.GooglePublicSignupUrl, "https://accounts.example.test/create-account");
        Guid eventId = await SeedEventAsync(factory, ParticipationHandlingModeEnum.PlatformManaged, IdentityAccessModeEnum.AccountRequired);
        if (surface == "control-plane")
        {
            await using var deploymentScope = factory.Services.CreateAsyncScope();
            var deploymentDatabase = deploymentScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var bootstrap = await deploymentDatabase.InstanceBootstrapStates.SingleAsync(Token);
            bootstrap.TransitionDeploymentMode(DeploymentMode.MultiTenant);
            await deploymentDatabase.SaveChangesAsync(Token);
            await deploymentScope.ServiceProvider.GetRequiredService<IDeploymentModeProvider>().InvalidateCacheAsync();
        }
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", "local-admission");
        using var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var session = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.RootElement.GetProperty("token").GetString());
        string key = GovernanceSettingKeys.PublicExperience.VisitorAccessMode;
        using var response = surface switch
        {
            "tenant-scalar" => await client.PutAsJsonAsync($"/api/settings/tenant/keys/{key}", new { value = "AnonymousOnly" }, Token),
            "tenant-batch" => await client.PutAsJsonAsync($"/api/settings/tenant/{SettingRegistry.Get(key)!.Category}", new
            {
                values = new Dictionary<string, string> { [key] = "AnonymousOnly", [GovernanceSettingKeys.PublicExperience.EventCatalogLabel] = "must-not-commit" }
            }, Token),
            "control-plane" => await client.PutAsJsonAsync($"/api/admin/control-plane/tenants/{PlatformDefaults.DefaultTenantId}/settings/{key}", new { value = "AnonymousOnly" }, Token),
            "provider" => await client.PatchAsJsonAsync("/api/instance/settings/auth-provider", new PatchAuthProviderConfigurationDto
            {
                Configuration = OptionalUpdate<AuthProviderConfigurationWriteDto>.Set(new()
                {
                    PrimaryProviderId = (int)AuthenticationProviderKind.Local,
                    GoogleSsoEnabled = false,
                    GoogleClientId = "native-public-client",
                    GooglePublicOnboardingPolicy = PublicOnboardingPolicy.Allowed,
                    GooglePublicSignupUrl = "https://accounts.example.test/create-account"
                })
            }, Token),
            _ => throw new ArgumentOutOfRangeException(nameof(surface))
        };
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict)
            .Because(await response.Content.ReadAsStringAsync(Token));
        // Control-plane's existing class-level Produces contract uses ordinary JSON for ProblemDetails too.
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo(surface == "control-plane" ? "application/json" : "application/problem+json");
        using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
        await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo(409);
        await Assert.That(problem.RootElement.GetProperty("code").GetString()).IsEqualTo("visitor_access_account_required_conflict");
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var participation = await database.Set<EventParticipationConfiguration>().SingleAsync(row => row.Id == eventId, Token);
        await Assert.That(participation.IdentityAccessModeId).IsEqualTo((int)IdentityAccessModeEnum.AccountRequired);
        await Assert.That(await database.Set<TenantSetting>().AnyAsync(row => row.SettingKey == key || row.SettingKey == GovernanceSettingKeys.PublicExperience.EventCatalogLabel, Token)).IsFalse();
        await Assert.That((await database.SystemSettings.SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Authentication.GoogleSsoEnabled, Token)).Value).IsEqualTo("true");
    }

    private static async Task GrantAdministratorAsync(LocalAdmissionWebApplicationFactory factory, string identifier)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var identity = await database.LocalIdentityUsers.SingleAsync(user => user.Email == identifier, Token);
        var user = await database.Users.SingleAsync(user => user.Id == identity.Id, Token);
        var tenant = await database.Tenants.SingleAsync(Token);
        var actor = await database.Actors.SingleAsync(actor => actor.UserId == user.Id, Token);
        var platformRole = await database.Set<Role>().SingleAsync(role => role.MasterCode == "platform.admin", Token);
        database.PlatformUserRoles.Add(new PlatformUserRole { Id = Guid.CreateVersion7(), UserId = user.Id, User = user, RoleId = platformRole.Id, Role = platformRole, GrantedAt = DateTime.UtcNow });
        var membership = new TenantUser { Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant, UserId = user.Id, User = user, ActorId = actor.Id, Actor = actor, StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = DateTime.UtcNow };
        database.TenantUsers.Add(membership);
        database.Set<TenantUserRoleGrant>().Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            TenantUserId = membership.Id,
            TenantUser = membership,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = DateTime.UtcNow
        });
        await database.SaveChangesAsync(Token);
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<LocalIdentityRole>>();
        await Assert.That((await roles.CreateAsync(new LocalIdentityRole("platform.admin"))).Succeeded).IsTrue();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        await Assert.That((await manager.AddToRoleAsync(identity, "platform.admin")).Succeeded).IsTrue();
    }

    private static HttpClient CreateClient(LocalAdmissionWebApplicationFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    private static async Task<JsonDocument> GetEventAsync(HttpClient client, Guid eventId)
    {
        using var response = await client.GetAsync($"/api/event/{eventId}", Token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
    }

    private static async Task SetSettingAsync<T>(LocalAdmissionWebApplicationFactory factory, string key, T value)
    {
        await using var database = factory.CreateDatabase();
        var setting = await database.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key, Token);
        if (setting is null)
        {
            setting = new SystemSetting { Id = Guid.CreateVersion7(), SettingKey = key, Value = JsonSerializer.Serialize(value), Category = "VisitorTest", CreatedAt = DateTime.UtcNow };
            database.SystemSettings.Add(setting);
        }
        setting.Value = JsonSerializer.Serialize(value);
        setting.ValueType = value is bool ? SettingValueType.Boolean : SettingValueType.String;
        await database.SaveChangesAsync(Token);
    }

    private static async Task SeedIdentityAsync(LocalAdmissionWebApplicationFactory factory)
    {
        await using var database = factory.CreateDatabase();
        database.Add(TenantDirectoryOperatorIdentityDocumentDefaults.Create(PlatformDefaults.DefaultTenantId, new TenantDirectoryOperatorIdentitySettings
        {
            PublicName = "Native public operator",
            LegalName = "Native public operator ASBL",
            OperatorKindCode = "registered_organization",
            JurisdictionCountryCode = "BE",
            RegistrationIdentifier = "BE 0123.456.789",
            PublicContactEmail = "operator@example.test",
            LegalNoticeUrl = "https://example.test/legal",
            TermsUrl = "https://example.test/terms",
            PrivacyUrl = "https://example.test/privacy"
        }));
        await database.SaveChangesAsync(Token);
    }

    private static async Task<Guid> SeedEventAsync(LocalAdmissionWebApplicationFactory factory, ParticipationHandlingModeEnum mode, IdentityAccessModeEnum identity)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid organizerId = (await database.InstanceBootstrapStates.SingleAsync(Token)).CompletedByUserId!.Value;
        var user = await database.Users.SingleAsync(row => row.Id == organizerId, Token);
        var tenant = await database.Tenants.SingleAsync(Token);
        var actor = await database.Actors.SingleOrDefaultAsync(row => row.UserId == user.Id, Token) ?? new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Visitor event organizer" },
            CreatedAt = DateTime.UtcNow
        };
        // A distinct organizer membership keeps public eligibility in its real tenant-owned query.
        var existing = await database.TenantUsers.SingleOrDefaultAsync(row => row.UserId == user.Id && row.TenantId == tenant.Id, Token);
        if (existing is not null)
            actor = await database.Actors.SingleAsync(row => row.Id == existing.ActorId, Token);
        else
            database.TenantUsers.Add(new TenantUser { Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant, UserId = user.Id, User = user, Actor = actor, ActorId = actor.Id, StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = DateTime.UtcNow });
        var entity = new Explore.Domain.Event
        {
            Id = Guid.CreateVersion7(),
            Title = "Native visitor discovery",
            TenantId = tenant.Id,
            Tenant = tenant,
            ActorId = actor.Id,
            Actor = actor,
            OrganizerActorId = actor.Id,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            CreatedAt = DateTime.UtcNow
        };
        entity.ParticipationConfiguration = EventParticipationConfiguration.Create(entity.Id, tenant.Id, (int)mode,
            (int)(mode == ParticipationHandlingModeEnum.WalkIn ? AdvanceRegistrationObligationEnum.NotApplicable : AdvanceRegistrationObligationEnum.Required),
            mode == ParticipationHandlingModeEnum.PlatformManaged ? (int)identity : null,
            mode == ParticipationHandlingModeEnum.PlatformManaged && identity != IdentityAccessModeEnum.AccountRequired ? GuestRecoveryPolicyEnum.EmailOptional : null, DateTime.UtcNow);
        entity.Publish(DateTime.UtcNow);
        database.Events.Add(entity);
        if (mode == ParticipationHandlingModeEnum.ExternalManaged)
        {
            var action = new EventPublicAction { Id = Guid.CreateVersion7(), TenantId = tenant.Id, EventId = entity.Id, EventPublicActionKindId = (int)EventPublicActionKindEnum.ExternalRegistration, HealthStateId = (int)EventPublicActionHealthStateEnum.Active, IsPrimary = true, CreatedAt = DateTime.UtcNow };
            action.SetDestination(ExternalActionUrl.Create("https://organizer.example.test/register"));
            database.Add(action);
        }
        await database.SaveChangesAsync(Token);
        return entity.Id;
    }
}
