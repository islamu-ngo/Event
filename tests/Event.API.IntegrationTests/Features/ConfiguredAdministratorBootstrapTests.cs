// ABOUTME: API integration tests for exact configured-administrator provider claims and bounded status.
// ABOUTME: Proves indirect identity claims cannot take over bootstrap authority or produce partial writes.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Models;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MediatR;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class ConfiguredAdministratorBootstrapTests
{
    private const string StatusRoute = "/api/instanceOnboarding/status";
    private const string SyncRoute = "/api/user/sync";
    private const string ExpectedIssuer = "https://auth.example.test/realms/ISLAMU";
    private const string ExpectedSubject = "configured-admin-subject";
    private const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    [Arguments("keycloak")]
    [Arguments("google")]
    public async Task SyncUser_ExactNormalizedIssuerAndSubject_CompletesConfiguredClaim(string providerHint)
    {
        ProviderAccountKey expected = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            ExpectedIssuer,
            ExpectedSubject);
        await using var factory = new ConfiguredClaimFactory(expected);
        using var client = factory.CreateClient();
        await SeedPendingAsync(factory);

        using var request = CreateSyncRequest(
            ("sub", ExpectedSubject),
            ("iss", "HTTPS://AUTH.EXAMPLE.TEST/realms/ISLAMU/"),
            ("idp", providerHint),
            ("email", "configured-admin@example.test"),
            ("given_name", "Configured"),
            ("family_name", "Administrator"));

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        InstanceBootstrapState state = await db.InstanceBootstrapStates.SingleAsync();
        UserExternalLogin login = await db.UserExternalLogins.SingleAsync();
        await Assert.That(state.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
        await Assert.That(login.ProviderKey).IsEqualTo(expected.Value);
        await Assert.That(await db.PlatformUserRoles.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task SyncUser_ExactConfiguredRetryAfterPostCommitFailure_ReplaysClaimEffects()
    {
        ProviderAccountKey expected = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            ExpectedIssuer,
            ExpectedSubject);
        var notifier = new FailFirstJwtAuthorityRefreshNotifier();
        await using var factory = new ConfiguredClaimFactory(expected, notifier);
        using var client = factory.CreateClient();
        await SeedPendingAsync(factory);

        using HttpResponseMessage first = await client.SendAsync(CreateSyncRequest(
            ("sub", ExpectedSubject),
            ("iss", ExpectedIssuer),
            ("idp", "keycloak"),
            ("email", "configured-admin@example.test")));
        await using var scope = factory.Services.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var retry = await mediator.Send(new SyncUserCommand
        {
            AccountKey = expected,
            UserDto = new UserDto
            {
                Id = Guid.Empty,
                Email = "configured-admin@example.test",
                FirstName = "Configured",
                LastName = "Administrator",
                AuthProvider = "keycloak",
                AuthProviderId = expected.Value,
                EmailVerified = true
            }
        });

        await Assert.That(first.IsSuccessStatusCode).IsFalse();
        await Assert.That(retry.IsSuccess).IsTrue();
        await Assert.That(notifier.CallCount).IsEqualTo(2);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await db.InstanceBootstrapStates.CountAsync(
            state => state.Status == InstanceBootstrapStatus.Completed)).IsEqualTo(1);
        await Assert.That(await db.UserExternalLogins.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task SyncUser_CompletedExactLoginRemainsUsableWhenSelectorAuthorityIsRemoved()
    {
        ProviderAccountKey expected = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            ExpectedIssuer,
            ExpectedSubject);
        var provider = new OneShotConfiguredProvider(expected);
        await using var factory = new ConfiguredClaimFactory(expected, configuredProvider: provider);
        using var client = factory.CreateClient();
        await SeedPendingAsync(factory);

        using HttpRequestMessage firstRequest = CreateSyncRequest(
            ("sub", ExpectedSubject),
            ("iss", ExpectedIssuer),
            ("idp", "keycloak"),
            ("email", "configured-admin@example.test"));
        using HttpResponseMessage first = await client.SendAsync(firstRequest);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        BaseCommandResponse<Guid> second = await mediator.Send(new SyncUserCommand
        {
            AccountKey = expected,
            UserDto = new UserDto
            {
                Id = Guid.Empty,
                Email = "configured-admin@example.test",
                FirstName = "Configured",
                LastName = "Administrator",
                AuthProvider = "keycloak",
                AuthProviderId = expected.Value,
                EmailVerified = true
            }
        });

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(second.IsSuccess).IsTrue();
        await Assert.That(provider.CallCount).IsEqualTo(2);
        DatabaseCounts counts = await ReadCountsAsync(factory);
        await Assert.That(counts).IsEqualTo(new DatabaseCounts(1, 1, 1, 1));
    }

    [Test]
    [Arguments("wrong-issuer")]
    [Arguments("session-id")]
    [Arguments("email")]
    [Arguments("username")]
    [Arguments("provider-role")]
    [Arguments("nonmatching-provider")]
    [Arguments("realm-only-issuer")]
    public async Task SyncUser_IndirectOrWrongAuthority_RejectsTakeoverWithZeroWrites(string attack)
    {
        ProviderAccountKey expected = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            ExpectedIssuer,
            ExpectedSubject);
        await using var factory = new ConfiguredClaimFactory(expected);
        using var client = factory.CreateClient();
        await SeedPendingAsync(factory);
        DatabaseCounts before = await ReadCountsAsync(factory);

        (string Type, string Value)[] claims = CreateAttackClaims(attack);
        using var request = CreateSyncRequest(claims);
        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        DatabaseCounts after = await ReadCountsAsync(factory);
        await Assert.That(after).IsEqualTo(before);
    }

    [Test]
    public async Task GetStatus_ConfiguredPending_IsStableGetOnlyAndValueFree()
    {
        ProviderAccountKey expected = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            ExpectedIssuer,
            ExpectedSubject);
        await using var factory = new ConfiguredClaimFactory(expected);
        using var client = factory.CreateClient();
        await SeedPendingAsync(factory);

        using HttpResponseMessage response = await client.GetAsync(StatusRoute);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        JsonElement root = body.RootElement;

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(root.GetProperty("state").GetString()).IsEqualTo("ConfiguredAdministratorPending");
        await Assert.That(root.GetProperty("mode").GetString()).IsEqualTo("ConfiguredAdministrator");
        await Assert.That(root.GetProperty("provider").GetString()).IsEqualTo("Keycloak");
        await Assert.That(root.GetProperty("generation").GetInt64()).IsEqualTo(7L);
        string[] relations = root.GetProperty("_links")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        await Assert.That(relations).IsEquivalentTo(["self"]);

        string serialized = root.GetRawText();
        await Assert.That(serialized).DoesNotContain(ExpectedIssuer);
        await Assert.That(serialized).DoesNotContain(ExpectedSubject);
        await Assert.That(serialized).DoesNotContain("configured-admin@example.test");
        await Assert.That(serialized).DoesNotContain(Fingerprint);

        using HttpResponseMessage post = await client.PostAsJsonAsync(StatusRoute, new
        {
            issuer = ExpectedIssuer,
            subject = ExpectedSubject
        });
        await Assert.That(post.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
        await Assert.That((await ReadCountsAsync(factory)).Users).IsEqualTo(0);
    }

    private static (string Type, string Value)[] CreateAttackClaims(string attack)
    {
        var claims = new List<(string Type, string Value)>
        {
            ("sub", attack is "wrong-issuer" or "nonmatching-provider" ? ExpectedSubject : "attacker-subject"),
            ("iss", attack == "wrong-issuer"
                ? "https://other.example.test/realms/ISLAMU"
                : attack == "nonmatching-provider" ? "https://accounts.google.com"
                : attack == "realm-only-issuer" ? "ISLAMU" : ExpectedIssuer),
            ("idp", attack == "nonmatching-provider" ? "google" : "keycloak"),
            ("email", "configured-admin@example.test"),
            ("preferred_username", ExpectedSubject),
            ("roles", "instance-admin"),
            ("sid", ExpectedSubject),
            ("handle", ExpectedSubject)
        };

        if (attack == "session-id")
        {
            claims.RemoveAll(claim => claim.Type == "sub");
            claims.Add(("name", "attacker"));
        }

        return [.. claims];
    }

    private static HttpRequestMessage CreateSyncRequest(params (string Type, string Value)[] claims)
    {
        string payload = JsonSerializer.Serialize(claims.Select(claim => new
        {
            claim.Type,
            claim.Value
        }));
        var request = new HttpRequestMessage(HttpMethod.Post, SyncRoute);
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)));
        return request;
    }

    private static async Task SeedPendingAsync(ConfiguredClaimFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        db.InstanceBootstrapStates.Add(InstanceBootstrapState.CreateConfiguredAdministratorPending(
            Guid.CreateVersion7(),
            AuthenticationProviderKind.Keycloak,
            DeploymentMode.MultiTenant,
            7,
            new string('b', 64),
            Fingerprint,
            DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    private static async Task<DatabaseCounts> ReadCountsAsync(ConfiguredClaimFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        return new DatabaseCounts(
            await db.Users.CountAsync(),
            await db.UserExternalLogins.CountAsync(),
            await db.PlatformUserRoles.CountAsync(),
            await db.InstanceBootstrapStates.CountAsync(state => state.Status == InstanceBootstrapStatus.Completed));
    }

    private sealed record DatabaseCounts(int Users, int ExternalLogins, int PlatformRoles, int CompletedStates);

    private sealed class ConfiguredClaimFactory(
        ProviderAccountKey expectedAccount,
        IJwtAuthorityRefreshNotifier? notifier = null,
        IConfiguredAdministratorBootstrapProvider? configuredProvider = null)
        : AuthenticatedWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConfiguredAdministratorBootstrapProvider>();
                services.AddSingleton<IConfiguredAdministratorBootstrapProvider>(
                    configuredProvider ?? new ExactConfiguredProvider(expectedAccount));
                if (notifier is not null)
                {
                    services.RemoveAll<IJwtAuthorityRefreshNotifier>();
                    services.AddSingleton(notifier);
                }
            });
        }
    }

    private sealed class FailFirstJwtAuthorityRefreshNotifier : IJwtAuthorityRefreshNotifier
    {
        public int CallCount { get; private set; }

        public Task ReloadAsync(CancellationToken ct = default)
        {
            CallCount++;
            return CallCount == 1
                ? Task.FromException(new InvalidOperationException("Injected post-commit effect failure."))
                : Task.CompletedTask;
        }
    }

    private sealed class ExactConfiguredProvider(ProviderAccountKey expectedAccount)
        : IConfiguredAdministratorBootstrapProvider
    {
        public Task<ConfiguredAdministratorBootstrapBinding?> GetVerifiedBindingAsync(
            ProviderAccountKey authenticatedAccount,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConfiguredAdministratorBootstrapBinding? binding = authenticatedAccount == expectedAccount
                ? new ConfiguredAdministratorBootstrapBinding(
                    expectedAccount,
                    7,
                    Fingerprint,
                    new CompleteInstanceOnboardingRequest
                    {
                        DeploymentMode = DeploymentMode.MultiTenant,
                        SiteProfile = new SelfHostOnboardingProfileDto { SiteName = "Integration Instance" },
                        AdministrationAccessMode = CompleteInstanceOnboardingRequest.EmbeddedAdministrationAccess
                    },
                    new ConfiguredAdministratorProfile(
                        "configured-admin@example.test",
                        "Configured",
                        "Administrator"))
                : null;
            return Task.FromResult(binding);
        }
    }

    private sealed class OneShotConfiguredProvider(ProviderAccountKey expectedAccount)
        : IConfiguredAdministratorBootstrapProvider
    {
        private readonly ExactConfiguredProvider _inner = new(expectedAccount);

        public int CallCount { get; private set; }

        public Task<ConfiguredAdministratorBootstrapBinding?> GetVerifiedBindingAsync(
            ProviderAccountKey authenticatedAccount,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return CallCount == 1
                ? _inner.GetVerifiedBindingAsync(authenticatedAccount, cancellationToken)
                : Task.FromResult<ConfiguredAdministratorBootstrapBinding?>(null);
        }
    }
}
