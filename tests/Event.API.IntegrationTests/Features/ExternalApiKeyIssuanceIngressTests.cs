extern alias standalone;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Builders;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Authentication;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StandaloneMarker = standalone::Event.Standalone.Hosting.StandaloneHostMarker;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class ExternalApiKeyIssuanceIngressTests
{
    private static CancellationToken CancellationToken => TestContext.Current!.Execution.CancellationToken;

    /// <summary>
    /// Verifies signed issuance for tenant and instance owners in Dedicated and Combined hosting
    /// discloses the credential on first creation without storing it in generic idempotency responses
    /// or allowing response caching.
    /// </summary>
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SignedIngress_FirstCreationDisclosesOnlyOnce(bool combined, bool tenant)
    {
        await using var host = await IngressHost.StartAsync(combined, tenant);
        using var response = await host.CreateAsync();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = await ReadAsync(response);
        await Assert.That(body.RootElement.GetProperty("disclosureStatus").GetString()).IsEqualTo("Issued");
        string raw = body.RootElement.GetProperty("apiKey").GetString()!;
        await Assert.That(!string.IsNullOrWhiteSpace(raw)).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.Contains("X-Idempotency-Replay")).IsFalse();
        await using var database = host.Native.CreateDatabase();
        var records = await database.IdempotencyRecords.AsNoTracking().ToListAsync(CancellationToken);
        await Assert.That(records.Any(record =>
            record.ResponseBody?.Contains(raw, StringComparison.Ordinal) == true)).IsFalse();
    }

    /// <summary>
    /// Verifies reusing the signed issuance operation in Dedicated and Combined hosting preserves
    /// tenant or instance key identity while returning PreviouslyIssued metadata without the credential,
    /// shared caching, or a generic idempotency replay marker.
    /// </summary>
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SignedIngress_ReplayReturnsStableMetadataWithoutRawCredential(bool combined, bool tenant)
    {
        await using var host = await IngressHost.StartAsync(combined, tenant);
        using var first = await host.CreateAsync();
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var issued = await ReadAsync(first);
        using var replay = await host.CreateAsync();
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var recovered = await ReadAsync(replay);
        await Assert.That(recovered.RootElement.GetProperty("id").GetGuid())
            .IsEqualTo(issued.RootElement.GetProperty("id").GetGuid());
        await Assert.That(recovered.RootElement.GetProperty("keyId").GetString())
            .IsEqualTo(issued.RootElement.GetProperty("keyId").GetString());
        await Assert.That(recovered.RootElement.GetProperty("disclosureStatus").GetString()).IsEqualTo("PreviouslyIssued");
        await Assert.That(!recovered.RootElement.TryGetProperty("apiKey", out var raw)
            || raw.ValueKind == JsonValueKind.Null).IsTrue();
        await Assert.That((await replay.Content.ReadAsStringAsync(CancellationToken))
            .Contains(issued.RootElement.GetProperty("apiKey").GetString()!, StringComparison.Ordinal)).IsFalse();
        await Assert.That(replay.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(replay.Headers.Contains("X-Idempotency-Replay")).IsFalse();
    }

    /// <summary>
    /// Verifies committed tenant-grant revocation or platform-role removal is checked afresh
    /// before issuance recovery in either hosting mode, so the unchanged signed token receives
    /// a non-cacheable forbidden problem response with no credential or key identity disclosure.
    /// </summary>
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SignedIngress_CommittedAuthorityRevocationDeniesSameTokenReplay(bool combined, bool tenant)
    {
        await using var host = await IngressHost.StartAsync(combined, tenant);
        using var first = await host.CreateAsync();
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var issued = await ReadAsync(first);
        await using (var database = host.Native.CreateDatabase())
        {
            if (tenant)
            {
                var grant = await database.TenantUserRoleGrants
                    .IgnoreQueryFilters()
                    .SingleAsync(row => row.Id == host.GrantId
                        && row.TenantId == PlatformDefaults.DefaultTenantId, CancellationToken);
                grant.RevokedAt = DateTime.UtcNow;
            }
            else
            {
                var grant = await database.PlatformUserRoles
                    .SingleAsync(row => row.Id == host.GrantId, CancellationToken);
                database.PlatformUserRoles.Remove(grant);
            }
            await database.SaveChangesAsync(CancellationToken);
        }

        using var replay = await host.CreateAsync();
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(replay.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        string denied = await replay.Content.ReadAsStringAsync(CancellationToken);
        await Assert.That(denied.Contains(issued.RootElement.GetProperty("apiKey").GetString()!, StringComparison.Ordinal)).IsFalse();
        await Assert.That(denied.Contains(issued.RootElement.GetProperty("id").GetString()!, StringComparison.Ordinal)).IsFalse();
        await Assert.That(denied.Contains(issued.RootElement.GetProperty("keyId").GetString()!, StringComparison.Ordinal)).IsFalse();
        await Assert.That(replay.Headers.CacheControl?.NoStore).IsTrue();
    }

    /// <summary>
    /// Parses the actual ingress response stream for disclosure and identity assertions
    /// using the current test's cancellation token.
    /// </summary>
    private static Task<JsonDocument> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.ParseAsync(response.Content.ReadAsStream(CancellationToken), cancellationToken: CancellationToken);

    /// <summary>
    /// Verifies a signed Keycloak subject maps to the persisted local user rather than becoming
    /// the resource owner: that user can list and revoke the issued key, and recovery of the
    /// revoked key returns not found.
    /// </summary>
    [Test]
    public async Task SignedProviderGuidDoesNotReplacePersistedUserIdentityDuringRecovery()
    {
        await using var host = await IngressHost.StartAsync(false, true, provider: AuthenticationProviderKind.Keycloak);
        using var first = await host.CreateAsync(ExternalApiKeyOwnerType.User);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var issued = await ReadAsync(first);
        string id = issued.RootElement.GetProperty("id").GetString()!;
        using var list = await host.ListAsync();
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var listed = await ReadAsync(list);
        await Assert.That(listed.RootElement.GetRawText().Contains(id, StringComparison.Ordinal)).IsTrue();
        using var revoked = await host.RevokeAsync(id);
        await Assert.That(revoked.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await using var database = host.Native.CreateDatabase();
        var key = await database.ExternalApiKeys.IgnoreQueryFilters().SingleAsync(CancellationToken);
        await Assert.That(key.OwnerId).IsEqualTo(host.PrincipalId);
        await Assert.That(key.ExternalApiKeyStatusId).IsEqualTo((int)ExternalApiKeyStatusEnum.Revoked);
        using var unavailable = await host.CreateAsync(ExternalApiKeyOwnerType.User);
        await Assert.That(unavailable.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies the MultiTenant global host's nullable tenant context permits instance-owned issuance,
    /// metadata-only replay, listing, and revocation in either hosting mode but denies tenantless
    /// user ownership. Revocation retains the receipt and blocks recovery; a new operation can
    /// deliberately issue a replacement resource.
    /// </summary>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MultiTenantGlobalHostIssuesOnceAndRejectsTenantlessUserOwnership(bool combined)
    {
        await using var host = await IngressHost.StartAsync(combined, false, multiTenant: true);
        using var first = await host.CreateAsync();
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var issued = await ReadAsync(first);
        await Assert.That(issued.RootElement.GetProperty("disclosureStatus").GetString()).IsEqualTo("Issued");
        using var replay = await host.CreateAsync();
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var recovered = await ReadAsync(replay);
        await Assert.That(recovered.RootElement.GetProperty("apiKey").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(recovered.RootElement.GetProperty("id").GetString())
            .IsEqualTo(issued.RootElement.GetProperty("id").GetString());
        using var tenantless = await host.CreateAsync(ExternalApiKeyOwnerType.User);
        await Assert.That(tenantless.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await using var database = host.Native.CreateDatabase();
        await Assert.That(await database.ExternalApiKeys.IgnoreQueryFilters().CountAsync(CancellationToken)).IsEqualTo(1);
        string originalId = issued.RootElement.GetProperty("id").GetString()!;
        using var list = await host.ListAsync();
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var listed = await ReadAsync(list);
        await Assert.That(listed.RootElement.GetRawText().Contains(originalId, StringComparison.Ordinal)).IsTrue();
        await Assert.That(listed.RootElement.GetRawText().Contains(
            issued.RootElement.GetProperty("apiKey").GetString()!, StringComparison.Ordinal)).IsFalse();
        using var revoked = await host.RevokeAsync(originalId);
        await Assert.That(revoked.IsSuccessStatusCode).IsTrue();
        using var unavailable = await host.CreateAsync();
        await Assert.That(unavailable.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await database.ExternalApiKeyIssuanceReceipts.IgnoreQueryFilters()
            .CountAsync(CancellationToken)).IsEqualTo(1);
        using var replacement = await host.CreateAsync(
            operationKey: Guid.CreateVersion7().ToString("N"), name: "Deliberate replacement");
        await Assert.That(replacement.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var replaced = await ReadAsync(replacement);
        await Assert.That(replaced.RootElement.GetProperty("id").GetString()).IsNotEqualTo(originalId);
    }

    /// <summary>
    /// Owns a signed HTTP client and its hosting resources, with persisted tenant or platform
    /// authority and a stable issuance operation for first-disclosure and recovery assertions.
    /// </summary>
    private sealed class IngressHost : IAsyncDisposable
    {
        public LocalAdmissionWebApplicationFactory Native { get; private set; } = null!;
        public Guid GrantId { get; } = Guid.CreateVersion7();
        private WebApplicationFactory<StandaloneMarker>? _combined;
        private WebApplicationFactory<Program>? _dedicated;
        private HttpClient? _client;
        private bool _tenant;
        public Guid PrincipalId { get; private set; }
        private readonly string _operationKey = Guid.CreateVersion7().ToString("N");

        /// <summary>
        /// Seeds persisted tenant-admin or platform-admin authority and signs requests through
        /// Dedicated or Combined hosting against the native fixture's configuration.
        /// External-provider subjects are linked to a distinct persisted user; MultiTenant global
        /// hosting is selected without inventing a tenant for instance-owned operations.
        /// </summary>
        public static async Task<IngressHost> StartAsync(
            bool combined, bool tenant, bool multiTenant = false,
            AuthenticationProviderKind provider = AuthenticationProviderKind.Local)
        {
            var host = new IngressHost { _tenant = tenant };
            try
            {
                host.Native = await LocalAdmissionWebApplicationFactory.CreateAsync(primaryProvider: provider);
                LocalAuthRequestDto? login = provider == AuthenticationProviderKind.Local
                    ? await host.Native.SeedLocalUserAsync(emailConfirmed: true) : null;
                string? token = null;
                await using (var database = host.Native.CreateDatabase())
                {
                    Guid actor;
                    if (login is not null)
                    {
                        actor = await database.Users.Where(user => user.Pii.Email == login.Identifier)
                            .Select(user => user.Id).SingleAsync(CancellationToken);
                    }
                    else
                    {
                        var user = new UserBuilder().WithId(Guid.CreateVersion7()).Build();
                        actor = user.Id;
                        Guid subject = Guid.CreateVersion7();
                        database.Users.Add(user);
                        database.UserExternalLogins.Add(new UserExternalLogin
                        {
                            Id = Guid.CreateVersion7(),
                            UserId = actor,
                            User = user,
                            AuthenticationProviderId = (int)provider,
                            AuthenticationProvider = null!,
                            ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
                                host.Native.ExternalIssuer, subject.ToString("D")).Value,
                            CreatedAt = DateTime.UtcNow
                        });
                        token = host.Native.CreateExternalProviderToken(subject, user.Pii.Email!, false);
                    }
                    host.PrincipalId = actor;
                    if (tenant)
                    {
                        var member = new TenantUser
                        {
                            Id = Guid.CreateVersion7(),
                            TenantId = PlatformDefaults.DefaultTenantId,
                            Tenant = null!,
                            UserId = actor,
                            User = null!,
                            StatusId = (int)TenantUserStatusEnum.Active,
                            JoinedAt = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow
                        };
                        database.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                        {
                            Id = host.GrantId,
                            TenantId = member.TenantId,
                            Tenant = null!,
                            TenantUserId = member.Id,
                            TenantUser = member,
                            RoleId = (int)RoleEnum.TenantAdmin,
                            Role = null!,
                            RoleScopeId = (int)RoleScopeEnum.Tenant,
                            GrantedAt = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        database.PlatformUserRoles.Add(new PlatformUserRole
                        {
                            Id = host.GrantId,
                            UserId = actor,
                            User = null!,
                            RoleId = (int)RoleEnum.Admin,
                            Role = null!,
                            GrantedAt = DateTime.UtcNow
                        });
                    }
                    await database.SaveChangesAsync(CancellationToken);
                }

                using var dedicated = host.Native.CreateClient(new WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://localhost"),
                    AllowAutoRedirect = false,
                    HandleCookies = false
                });
                if (token is null)
                {
                    using var signedIn = await dedicated.PostAsJsonAsync("/api/auth/local/login", login, CancellationToken);
                    await Assert.That(signedIn.StatusCode).IsEqualTo(HttpStatusCode.OK);
                    using var session = await ReadAsync(signedIn);
                    token = session.RootElement.GetProperty("token").GetString()!;
                }
                if (combined)
                {
                    host._combined = new CombinedIngressFactory(
                        host.Native.Services.GetRequiredService<IConfiguration>(), multiTenant);
                    host._client = host._combined.CreateClient(new WebApplicationFactoryClientOptions
                    {
                        BaseAddress = new Uri("https://localhost"),
                        AllowAutoRedirect = false,
                        HandleCookies = false
                    });
                }
                else
                {
                    host._dedicated = multiTenant
                        ? host.Native.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
                            configuration.AddInMemoryCollection(new Dictionary<string, string?>
                            {
                                ["Deployment:Mode"] = "MultiTenant"
                            })))
                        : null;
                    host._client = (host._dedicated ?? host.Native).CreateClient(new WebApplicationFactoryClientOptions
                    {
                        BaseAddress = new Uri("https://localhost"),
                        AllowAutoRedirect = false,
                        HandleCookies = false
                    });
                }
                host._client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return host;
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }
        }

        /// <summary>
        /// Sends signed issuance with the fixture's stable idempotency key and tenant or instance
        /// ownership default; overrides exercise user ownership or a deliberately new issuance operation.
        /// </summary>
        public async Task<HttpResponseMessage> CreateAsync(
            ExternalApiKeyOwnerType? owner = null, string? operationKey = null, string? name = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ExternalApiKey")
            {
                Content = JsonContent.Create(new CreateExternalApiKeyDto
                {
                    Name = name ?? "Signed ingress issuance",
                    ExternalApiKeyOwnerTypeId = (int)(owner ?? (_tenant
                        ? ExternalApiKeyOwnerType.Tenant : ExternalApiKeyOwnerType.InstanceAdmin)),
                    Scopes = ["events:read"]
                })
            };
            request.Headers.Add("Idempotency-Key", operationKey ?? _operationKey);
            return await _client!.SendAsync(request, CancellationToken);
        }

        /// <summary>
        /// Lists key metadata through the same signed principal used for issuance, allowing
        /// assertions about persisted ownership and absence of credential disclosure.
        /// </summary>
        public Task<HttpResponseMessage> ListAsync() =>
            _client!.GetAsync("/api/ExternalApiKey", CancellationToken);

        /// <summary>
        /// Requests revocation of an issued resource through its signed owner context so subsequent
        /// recovery assertions exercise the revoked key rather than a changed principal.
        /// </summary>
        public Task<HttpResponseMessage> RevokeAsync(string id) =>
            _client!.DeleteAsync($"/api/ExternalApiKey/{id}", CancellationToken);

        /// <summary>
        /// Releases the signed client and every factory owned by this fixture, including
        /// partially initialized hosting resources when startup fails.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_combined is not null) await _combined.DisposeAsync();
            if (_dedicated is not null) await _dedicated.DisposeAsync();
            if (Native is not null) await Native.DisposeAsync();
        }
    }

    /// <summary>
    /// Runs the Standalone Combined host with the native fixture's database and identity configuration
    /// so signed ingress is exercised through the alternate hosting composition.
    /// </summary>
    private sealed class CombinedIngressFactory(IConfiguration nativeConfiguration, bool multiTenant)
        : WebApplicationFactory<StandaloneMarker>
    {
        /// <summary>
        /// Reuses native database, authentication, secret-provider, and operator-identity settings
        /// in Testing, optionally selects MultiTenant mode, and substitutes an in-memory cache
        /// while disabling unrelated hosted services, authority warmup, and rate limiting.
        /// </summary>
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestInstanceOperatorIdentityConfiguration.Apply(builder);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var settings = new Dictionary<string, string?>();
                foreach (string section in new[]
                {
                    "Database", "Deployment", "IdentityDatabase", "Authentication",
                    "PrivacyErasure", "SecretProvider", "Instance:OperatorIdentity"
                })
                    foreach (var item in nativeConfiguration.GetSection(section).AsEnumerable())
                        settings[item.Key] = item.Value;
                settings["Testing:SkipJwtAuthorityWarmup"] = "true";
                settings["Database:Host"] = null;
                settings["Database:Port"] = null;
                if (multiTenant)
                    settings["Deployment:Mode"] = "MultiTenant";
                settings["Scheduler:Quartz:Enabled"] = "false";
                settings["RateLimiting:DisableInTesting"] = "true";
                settings["HttpsRedirection:Enabled"] = "false";
                settings["Mcp:Enabled"] = "false";
                settings["ConnectionStrings:cache"] = "";
                configuration.AddInMemoryCollection(settings);
            });
            builder.ConfigureTestServices(services =>
            {
                TestHostServicePruner.RemoveNoisyHostedServices(services);
                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();
            });
        }
    }
}
