using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Event.API.IntegrationTests.Authentication;

public sealed partial class VerifiedTenantJoinHttpTests
{
    private sealed class JoinFixture : IAsyncDisposable
    {
        internal const string TargetSlug = "join-selected";
        internal LocalAdmissionWebApplicationFactory Native { get; private set; } = null!;
        private LocalIdentityLifecycleHttpFixture? _local;
        private WebApplicationFactory<Program>? _keycloakHost;
        internal HttpClient Client { get; private set; } = null!;
        internal string Token { get; private set; } = null!;
        internal Guid UserId { get; private set; }
        internal Guid OtherUserId { get; private set; }
        internal Guid TargetId { get; private set; }
        internal Guid DefaultTenantId { get; private set; }

        internal static async Task<JoinFixture> CreateAsync(bool keycloak,
            bool registrationOpen = true, TenantUserStatusEnum? membershipStatus = null,
            bool initialMembership = false)
        {
            var fixture = new JoinFixture();
            try
            {
                if (keycloak)
                {
                    fixture.Native = await LocalAdmissionWebApplicationFactory.CreateAsync(
                        primaryProvider: AuthenticationProviderKind.Keycloak);
                    fixture._keycloakHost = fixture.Native.WithWebHostBuilder(builder =>
                        builder.ConfigureAppConfiguration((_, configuration) =>
                            configuration.AddInMemoryCollection(new Dictionary<string, string?>
                            {
                                ["Keycloak:ClientId"] = "islamu-event-api"
                            })));
                    fixture.Client = fixture._keycloakHost.CreateClient(new WebApplicationFactoryClientOptions
                    {
                        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
                    });
                }
                else
                {
                    fixture._local = await LocalIdentityLifecycleHttpFixture.CreateAsync();
                    fixture.Native = fixture._local.Native;
                    fixture.Client = fixture._local.Client;
                    fixture.Token = await fixture._local.SignInAsync();
                }

                await using var database = fixture.Native.CreateDatabase();
                database.BindTenantLease();
                await using var humanGate = new HumanSecuritySourceGate(database);
                await using var human = await humanGate.AcquireAsync();
                await using var discoveryGate = new EventDiscoveryEnrollmentGate(database);
                await using var discovery = await discoveryGate.AcquireAsync();
                await using var transaction = await database.Database.BeginTransactionAsync();
                var user = keycloak
                    ? await database.Users.Include(row => row.Pii).SingleAsync()
                    : await database.Users.Include(row => row.Pii)
                        .SingleAsync(row => row.Id == fixture._local!.Binding.LocalSubjectId);
                fixture.UserId = user.Id;
                fixture.OtherUserId = keycloak ? Guid.CreateVersion7()
                    : await database.Users.Where(row => row.Id != user.Id).Select(row => row.Id).SingleAsync();
                fixture.DefaultTenantId = (await database.Tenants.SingleAsync()).Id;
                var target = NewTenant(TargetSlug);
                fixture.TargetId = target.Id;
                var inactive = NewTenant("join-inactive");
                inactive.TenantStatusId = (int)TenantStatusEnum.Suspended;
                database.Tenants.AddRange(target, inactive);
                database.TenantSettingOverrides.Add(new TenantSetting
                {
                    Id = Guid.CreateVersion7(), TenantId = target.Id, Tenant = target,
                    SettingKey = GovernanceSettingKeys.Tenants.UserRegistrationEnabled,
                    Value = registrationOpen ? "true" : "false", CreatedAt = DateTime.UtcNow
                });
                if (keycloak)
                {
                    database.Actors.Add(new Actor
                    {
                        Id = Guid.CreateVersion7(), UserId = user.Id, User = user,
                        ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                        Pii = new ActorPii { DisplayName = "Verified join account" },
                        CreatedAt = DateTime.UtcNow
                    });
                    database.Users.Add(new User
                    {
                        Id = fixture.OtherUserId,
                        Pii = new UserPii
                        {
                            Email = $"other-{fixture.OtherUserId:N}@example.test",
                            FirstName = "Other", LastName = "Account"
                        },
                        EmailVerified = true, CreatedAt = DateTime.UtcNow
                    });
                    string subject = "join-account-" + Guid.CreateVersion7().ToString("N");
                    database.UserExternalLogins.Add(new UserExternalLogin
                    {
                        Id = Guid.CreateVersion7(), UserId = user.Id, User = user,
                        AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
                        AuthenticationProvider = null!,
                        ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
                            fixture.Native.ExternalIssuer, subject).Value,
                        CreatedAt = DateTime.UtcNow
                    });
                    fixture.Token = fixture.Native.CreateExternalProviderToken(
                    [
                        new Claim("sub", subject), new Claim("typ", "Bearer"),
                        new Claim("auth_provider", "keycloak"), new Claim("email", user.Email),
                        new Claim("email_verified", "true")
                    ], fixture.Native.ExternalIssuer);
                }
                if (membershipStatus is not null || initialMembership)
                {
                    var actor = keycloak
                        ? database.Actors.Local.Single(row => row.UserId == user.Id)
                        : await database.Actors.SingleAsync(row => row.UserId == user.Id);
                    var membershipTenant = initialMembership
                        ? await database.Tenants.SingleAsync(row => row.Id == fixture.DefaultTenantId)
                        : target;
                    database.TenantUsers.Add(new TenantUser
                    {
                        Id = Guid.CreateVersion7(), TenantId = membershipTenant.Id, Tenant = membershipTenant,
                        UserId = user.Id, User = user, ActorId = actor.Id, Actor = actor,
                        StatusId = (int)(membershipStatus ?? TenantUserStatusEnum.Active),
                        JoinedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
                    });
                }
                await database.SaveChangesAsync();
                await transaction.CommitAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        private static Tenant NewTenant(string slug) => new()
        {
            Id = Guid.CreateVersion7(), Slug = slug, FullName = slug,
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!,
            CreatedAt = DateTime.UtcNow
        };

        internal async Task<HttpResponseMessage> JoinAsync(string? slug = TargetSlug, object? body = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Route);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            if (slug is not null)
                request.Headers.TryAddWithoutValidation("X-Tenant-Slug", slug);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            return await Client.SendAsync(request);
        }

        internal async Task<Guid> AssertJoinedAsync()
        {
            await using var database = Native.CreateDatabase();
            database.BindTenantLease();
            var memberships = await database.TenantUsers.IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.TenantId == TargetId).ToArrayAsync();
            await Assert.That(memberships.Length).IsEqualTo(1);
            await Assert.That(memberships[0].UserId).IsEqualTo(UserId);
            await Assert.That(memberships[0].StatusId).IsEqualTo((int)TenantUserStatusEnum.Active);
            await Assert.That(memberships[0].IsDeleted).IsFalse();
            await Assert.That(await database.TenantUsers.IgnoreQueryFilters()
                .AnyAsync(row => row.UserId == UserId && row.TenantId != TargetId)).IsFalse();
            return memberships[0].Id;
        }

        internal async Task<string> ReadIdentitiesAsync()
        {
            await using var database = Native.CreateDatabase();
            database.BindTenantLease();
            return JsonSerializer.Serialize(new
            {
                Users = await database.Users.IgnoreQueryFilters().AsNoTracking().OrderBy(row => row.Id)
                    .Select(row => new { row.Id, row.IsDeleted }).ToArrayAsync(),
                Logins = await database.UserExternalLogins.AsNoTracking().OrderBy(row => row.Id)
                    .Select(row => new { row.Id, row.UserId, row.ProviderKey, row.AuthenticationProviderId }).ToArrayAsync(),
                Actors = await database.Actors.IgnoreQueryFilters().AsNoTracking().OrderBy(row => row.Id)
                    .Select(row => new { row.Id, row.UserId, row.IsDeleted }).ToArrayAsync()
            });
        }

        internal async Task<string> ReadGraphAsync()
        {
            await using var database = Native.CreateDatabase();
            database.BindTenantLease();
            return JsonSerializer.Serialize(new
            {
                Identities = await ReadIdentitiesAsync(),
                Users = await database.Users.IgnoreQueryFilters().AsNoTracking().OrderBy(row => row.Id)
                    .Select(row => new { row.Id, row.LastActiveTenantId, row.UpdatedAt }).ToArrayAsync(),
                Memberships = await database.TenantUsers.IgnoreQueryFilters().AsNoTracking().OrderBy(row => row.Id)
                    .Select(row => new
                    {
                        row.Id, row.UserId, row.TenantId, row.ActorId, row.StatusId,
                        row.IsDeleted, row.JoinedAt, row.CreatedAt, row.UpdatedAt
                    }).ToArrayAsync()
            });
        }

        public async ValueTask DisposeAsync()
        {
            if (_local is not null)
                await _local.DisposeAsync();
            else
            {
                Client?.Dispose();
                if (_keycloakHost is not null)
                    await _keycloakHost.DisposeAsync();
                if (Native is not null)
                    await Native.DisposeAsync();
            }
        }
    }
}
