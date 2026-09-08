// ABOUTME: Exercises native ATProto visitor JIT through the signed private HTTP bridge and real SQLite transactions.
// ABOUTME: Keeps visitor signup policy separate from exact linked login and configured administrator authority.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Authentication;
using Explore.API.Models;
using Explore.Application.Constants;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.API.IntegrationTests.Authentication;

[NotInParallel("SecurityInfra")]
public sealed class AtprotoVisitorAdmissionTests
{
    [Test]
    [Arguments(AuthenticationProviderKind.Local)]
    [Arguments(AuthenticationProviderKind.Atproto)]
    public async Task EnabledUsableProviderCreatesOneVisitorAndLinkedRetryIgnoresVisitorMode(
        AuthenticationProviderKind primary)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: primary, enableAtproto: true);
        using HttpClient client = CreateClient(factory);
        AtprotoDid did = NewDid();
        await using ExploreDbContext before = factory.CreateDatabase();
        int usersBefore = await before.Users.CountAsync();
        using HttpResponseMessage response = await BootstrapAsync(factory, client, did);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var session = (await response.Content.ReadFromJsonAsync<BffAtprotoSessionBridgeResponse>())!;

        await SetSettingAsync(factory, GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
            JsonSerializer.Serialize(VisitorAccessMode.DirectoryListingOnly.ToString()));
        using HttpResponseMessage retry = await BootstrapAsync(factory, client, did);
        await Assert.That(retry.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var repeated = (await retry.Content.ReadFromJsonAsync<BffAtprotoSessionBridgeResponse>())!;
        await Assert.That(repeated.UserId).IsEqualTo(session.UserId);
        await Assert.That(repeated.ActorId).IsEqualTo(session.ActorId);
        await Assert.That(repeated.ParticipationId).IsEqualTo(session.ParticipationId);
        await using ExploreDbContext stored = factory.CreateDatabase();
        await Assert.That(await stored.Users.CountAsync()).IsEqualTo(usersBefore + 1);
        await Assert.That(await stored.Actors.CountAsync()).IsEqualTo(1);
        await Assert.That(await stored.UserExternalLogins.CountAsync()).IsEqualTo(1);
        await Assert.That(await stored.LocalIdentityUsers.CountAsync()).IsEqualTo(0);
        await Assert.That(await stored.PlatformUserRoles.CountAsync()).IsEqualTo(0);
        await Assert.That(await stored.TenantUserRoleGrants.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        UserExternalLogin binding = await stored.UserExternalLogins.Include(login => login.User)
            .SingleAsync(login => login.ProviderKey == did.Value);
        await Assert.That(binding.UserId).IsEqualTo(session.UserId);
        await Assert.That(binding.User.Email).IsEqualTo(string.Empty);
        await Assert.That(binding.User.EmailVerified).IsFalse();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var jwt = scope.ServiceProvider.GetRequiredService<AtprotoJwtService>();
        await Assert.That(await jwt.ValidateSessionAsync(session.AccessToken,
            PlatformDefaults.DefaultTenantId, CancellationToken.None)).IsNotNull();
    }

    [Test]
    [Arguments(AuthenticationProviderKind.Local, VisitorAccessMode.AnonymousOnly, true, true)]
    [Arguments(AuthenticationProviderKind.Local, VisitorAccessMode.DirectoryListingOnly, true, true)]
    [Arguments(AuthenticationProviderKind.Atproto, VisitorAccessMode.AnonymousOnly, true, true)]
    [Arguments(AuthenticationProviderKind.Atproto, VisitorAccessMode.DirectoryListingOnly, true, true)]
    [Arguments(AuthenticationProviderKind.Local, VisitorAccessMode.FullRegistrationAndAuth, false, true)]
    [Arguments(AuthenticationProviderKind.Local, VisitorAccessMode.FullRegistrationAndAuth, true, false)]
    public async Task DirectBridgeCannotCreateVisitorsWithoutUsableSignup(
        AuthenticationProviderKind primary, VisitorAccessMode mode, bool enabled, bool usable)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: primary, enableAtproto: true);
        using HttpClient client = CreateClient(factory);
        await SetSettingAsync(factory, GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
            JsonSerializer.Serialize(mode.ToString()));
        await SetSettingAsync(factory, GovernanceSettingKeys.Authentication.AtprotoLoginEnabled,
            enabled ? "true" : "false");
        await SetSettingAsync(factory, GovernanceSettingKeys.Authentication.AtprotoPublicUrl,
            JsonSerializer.Serialize(usable ? "https://localhost" : string.Empty));
        using HttpResponseMessage response = await BootstrapAsync(factory, client, NewDid());
        await Assert.That(response.StatusCode).IsEqualTo(enabled ? HttpStatusCode.Forbidden : HttpStatusCode.BadGateway);
        await AssertNoVisitorAsync(factory);
    }

    [Test]
    [Arguments("foreign-did")]
    [Arguments("foreign-tenant")]
    [Arguments("foreign-issuer")]
    [Arguments("unverified")]
    [Arguments("verified-did-mismatch")]
    public async Task VisitorCapabilityCannotReplaceVerifiedProviderAndBridgeProof(string attack)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(enableAtproto: true);
        using HttpClient client = CreateClient(factory);
        AtprotoDid did = NewDid();
        string assertion = factory.CreateAtprotoBootstrapAssertion(
            attack == "foreign-did" ? NewDid() : did, AtprotoSubjectClassification.Person,
            tenantId: attack == "foreign-tenant" ? Guid.CreateVersion7() : null,
            issuer: attack == "foreign-issuer" ? "https://foreign.example.test" : null);
        if (attack is "unverified" or "verified-did-mismatch")
        {
            var gateway = factory.Services.GetRequiredService<IAtprotoOAuthSecurityGateway>();
            gateway.VerifyAsync(Arg.Any<AtprotoOAuthVerificationInput>(), Arg.Any<CancellationToken>())
                .Returns(call => attack == "unverified"
                    ? AtprotoOAuthVerificationResult.Failed("invalid_session")
                    : AtprotoOAuthVerificationResult.Verified(new AtprotoVerifiedOAuthSession(
                        NewDid(), "foreign.example.test", new Uri("https://pds.example.test/"),
                        factory.AtprotoOAuthKeyId, call.ArgAt<AtprotoOAuthVerificationInput>(0).OAuthSessionPayload)));
        }
        using HttpResponseMessage response = await BootstrapAsync(factory, client, did, assertion);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertNoVisitorAsync(factory);
    }

    [Test]
    public async Task ConcurrentVerifiedSecondaryFirstLoginConvergesToOneExactDidBinding()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(enableAtproto: true);
        using HttpClient client = CreateClient(factory);
        AtprotoDid did = NewDid();
        var bothVerified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int arrivals = 0;
        var gateway = factory.Services.GetRequiredService<IAtprotoOAuthSecurityGateway>();
        gateway.VerifyAsync(Arg.Any<AtprotoOAuthVerificationInput>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var input = call.ArgAt<AtprotoOAuthVerificationInput>(0);
                if (Interlocked.Increment(ref arrivals) == 2) bothVerified.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(20));
                return AtprotoOAuthVerificationResult.Verified(new AtprotoVerifiedOAuthSession(
                    input.ExpectedDid, "verified.example.test", input.ExpectedPdsUri,
                    input.OAuthClientKeyId, input.OAuthSessionPayload));
            });
        Task<HttpResponseMessage> first = BootstrapAsync(factory, client, did);
        Task<HttpResponseMessage> second = BootstrapAsync(factory, client, did);
        try
        {
            await bothVerified.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            release.TrySetResult();
        }
        using HttpResponseMessage firstResponse = await first.WaitAsync(TimeSpan.FromSeconds(20));
        using HttpResponseMessage secondResponse = await second.WaitAsync(TimeSpan.FromSeconds(20));
        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var firstSession = (await firstResponse.Content.ReadFromJsonAsync<BffAtprotoSessionBridgeResponse>())!;
        var secondSession = (await secondResponse.Content.ReadFromJsonAsync<BffAtprotoSessionBridgeResponse>())!;
        await Assert.That(firstSession.UserId).IsEqualTo(secondSession.UserId);
        await Assert.That(firstSession.ActorId).IsEqualTo(secondSession.ActorId);
        await using ExploreDbContext stored = factory.CreateDatabase();
        await Assert.That(await stored.Users.CountAsync()).IsEqualTo(2);
        await Assert.That(await stored.Actors.CountAsync()).IsEqualTo(1);
        await Assert.That(await stored.UserExternalLogins.CountAsync()).IsEqualTo(1);
        await Assert.That((await stored.UserExternalLogins.SingleAsync()).ProviderKey).IsEqualTo(did.Value);
    }

    [Test]
    [Arguments(AuthenticationProviderKind.Local)]
    [Arguments(AuthenticationProviderKind.Atproto)]
    public async Task ConfiguredBootstrapProtectsClaimantWithoutCapturingCompletedInstanceVisitors(
        AuthenticationProviderKind primary)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: primary, enableAtproto: true);
        var configuredProvider = new TestConfiguredAdministratorBootstrapProvider();
        AtprotoDid administratorDid = NewDid();
        configuredProvider.Configure(administratorDid);
        await using (ExploreDbContext seed = factory.CreateDatabase())
        {
            seed.InstanceBootstrapStates.RemoveRange(await seed.InstanceBootstrapStates.ToListAsync());
            await seed.SaveChangesAsync();
            seed.InstanceBootstrapStates.Add(InstanceBootstrapState.CreateConfiguredAdministratorPending(
                Guid.CreateVersion7(), AuthenticationProviderKind.Atproto, DeploymentMode.MultiTenant,
                configuredProvider.Generation, configuredProvider.ConfigurationFingerprint,
                configuredProvider.IdentityFingerprint, DateTime.UtcNow));
            await seed.SaveChangesAsync();
        }
        await using var configuredHost = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Keycloak:Authority"] = null,
                    ["Keycloak:MetadataAddress"] = null
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConfiguredAdministratorBootstrapProvider>();
                services.AddSingleton<IConfiguredAdministratorBootstrapProvider>(configuredProvider);
            });
        });
        using HttpClient client = configuredHost.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost")
        });
        // Even with public signup enabled, incomplete configured bootstrap admits no unrelated claimant.
        using HttpResponseMessage unrelatedPending = await BootstrapAsync(factory, client, NewDid());
        await Assert.That(unrelatedPending.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await AssertNoVisitorAsync(factory);
        await SetSettingAsync(factory, GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
            JsonSerializer.Serialize(VisitorAccessMode.DirectoryListingOnly.ToString()));
        using HttpResponseMessage administrator = await BootstrapAsync(factory, client, administratorDid);
        await Assert.That(administrator.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var administratorSession = (await administrator.Content.ReadFromJsonAsync<BffAtprotoSessionBridgeResponse>())!;
        using HttpResponseMessage exactRetry = await BootstrapAsync(factory, client, administratorDid);
        await Assert.That(exactRetry.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var retrySession = (await exactRetry.Content.ReadFromJsonAsync<BffAtprotoSessionBridgeResponse>())!;
        await Assert.That(retrySession.UserId).IsEqualTo(administratorSession.UserId);
        AtprotoDid visitorDid = NewDid();
        using HttpResponseMessage restrictedVisitor = await BootstrapAsync(factory, client, visitorDid);
        await Assert.That(restrictedVisitor.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await SetSettingAsync(factory, GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
            JsonSerializer.Serialize(VisitorAccessMode.FullRegistrationAndAuth.ToString()));
        using HttpResponseMessage visitor = await BootstrapAsync(factory, client, visitorDid);
        await Assert.That(visitor.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var visitorSession = (await visitor.Content.ReadFromJsonAsync<BffAtprotoSessionBridgeResponse>())!;
        await Assert.That(visitorSession.UserId).IsNotEqualTo(administratorSession.UserId);
        // An unrelated linked visitor also retains login after configured completion, not just its first JIT.
        await SetSettingAsync(factory, GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
            JsonSerializer.Serialize(VisitorAccessMode.AnonymousOnly.ToString()));
        using HttpResponseMessage linkedVisitor = await BootstrapAsync(factory, client, visitorDid);
        await Assert.That(linkedVisitor.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using ExploreDbContext stored = factory.CreateDatabase();
        await Assert.That(await stored.Users.CountAsync()).IsEqualTo(3);
        await Assert.That(await stored.UserExternalLogins.CountAsync()).IsEqualTo(2);
        await Assert.That(await stored.LocalIdentityUsers.CountAsync()).IsEqualTo(0);
        await Assert.That((await stored.PlatformUserRoles.SingleAsync()).UserId).IsEqualTo(administratorSession.UserId);
        InstanceBootstrapState completed = await stored.InstanceBootstrapStates.SingleAsync();
        await Assert.That(completed.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
        await Assert.That(completed.CompletedByUserId).IsEqualTo(administratorSession.UserId);
        await Assert.That(completed.CompletedIdentityFingerprint).IsEqualTo(configuredProvider.IdentityFingerprint);
    }

    private static async Task AssertNoVisitorAsync(LocalAdmissionWebApplicationFactory factory)
    {
        await using ExploreDbContext stored = factory.CreateDatabase();
        await Assert.That(await stored.Users.CountAsync()).IsEqualTo(1);
        await Assert.That(await stored.Actors.CountAsync()).IsEqualTo(0);
        await Assert.That(await stored.UserExternalLogins.CountAsync()).IsEqualTo(0);
        await Assert.That(await stored.AtprotoIdentities.CountAsync()).IsEqualTo(0);
        await Assert.That(await stored.LocalIdentityUsers.CountAsync()).IsEqualTo(0);
        await Assert.That(await stored.PlatformUserRoles.CountAsync()).IsEqualTo(0);
    }

    private static HttpClient CreateClient(LocalAdmissionWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

    private static AtprotoDid NewDid() => AtprotoDid.Parse(
        $"did:plc:{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}");

    private static async Task<HttpResponseMessage> BootstrapAsync(
        LocalAdmissionWebApplicationFactory factory, HttpClient client, AtprotoDid did,
        string? assertion = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AtprotoJwtOptions.BridgePath)
        {
            Content = JsonContent.Create(new BffAtprotoSessionBridgeRequest(
                did.Value, "https://pds.example.test/", factory.AtprotoOAuthKeyId, "person",
                JsonSerializer.SerializeToElement(new { subject = did.Value }), null, null))
        };
        request.Headers.Add(TenantHeaderNames.TenantSlug, "local-admission");
        request.Headers.Add(AtprotoJwtOptions.BootstrapHeaderName, assertion
            ?? factory.CreateAtprotoBootstrapAssertion(did, AtprotoSubjectClassification.Person));
        return await client.SendAsync(request);
    }

    private static async Task SetSettingAsync(LocalAdmissionWebApplicationFactory factory, string key, string value)
    {
        await using ExploreDbContext database = factory.CreateDatabase();
        SystemSetting? setting = await database.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
        if (setting is null)
        {
            database.SystemSettings.Add(new SystemSetting
            {
                SettingKey = key, Value = value, ValueType = SettingValueType.String,
                Category = "Authentication", CreatedAt = DateTime.UtcNow
            });
        }
        else setting.Value = value;
        await database.SaveChangesAsync();
    }
}
