// ABOUTME: Verifies external OIDC and ATProto identity evidence survives instance SMTP changes through native HTTP.
// ABOUTME: Keeps authentication and persisted account state real while substituting only external provider authorities.

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Authentication;
using Explore.API.Models;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class ExternalProviderEmailVerificationHttpTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    [Arguments(null)]
    public async Task InstanceEmailIntentCannotRewriteExternalVerificationEvidence(bool? verificationClaim)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: AuthenticationProviderKind.Keycloak);
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        Guid subject = Guid.CreateVersion7();
        string email = $"external-{subject:N}@example.test";
        string token = factory.CreateExternalProviderToken(
            subject: subject,
            email: email,
            emailVerified: verificationClaim);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        string providerKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            issuer: factory.ExternalIssuer,
            subject: subject.ToString("D")).Value;
        Guid? synchronizedUserId = null;

        await using ExploreDbContext before = factory.CreateDatabase();
        int usersBefore = await before.Users.CountAsync();
        await Assert.That(await before.LocalIdentityUsers.CountAsync()).IsEqualTo(0);

        foreach (bool enabled in new[] { false, true, false })
        {
            await SetInstanceEmailIntentAsync(factory, enabled);

            using HttpResponseMessage response = await client.PostAsync("/api/User/sync", content: null);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await using ExploreDbContext stored = factory.CreateDatabase();
            UserExternalLogin binding = await stored.UserExternalLogins
                .Include(login => login.User)
                .SingleAsync(login => login.AuthenticationProviderId == (int)AuthenticationProviderKind.Keycloak
                    && login.ProviderKey == providerKey);
            synchronizedUserId ??= binding.UserId;
            await Assert.That(binding.UserId).IsEqualTo(synchronizedUserId.Value);
            await Assert.That(binding.User.Email).IsEqualTo(email);
            await Assert.That(binding.User.EmailVerified).IsEqualTo(verificationClaim == true);
            await Assert.That(await stored.Users.CountAsync()).IsEqualTo(usersBefore + 1);
            await Assert.That(await stored.LocalIdentityUsers.CountAsync()).IsEqualTo(0);
            await Assert.That((await stored.SystemSettings.SingleAsync(
                setting => setting.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)).Value)
                .IsEqualTo(enabled ? "true" : "false");
        }
    }

    [Test]
    public async Task InstanceEmailIntentCannotGiveVerifiedEmailOrCredentialsToAtprotoDid()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: AuthenticationProviderKind.Atproto);
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        AtprotoDid did = AtprotoDid.Parse(
            $"did:plc:{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}");
        const AtprotoSubjectClassification classification = AtprotoSubjectClassification.Person;
        var body = new BffAtprotoSessionBridgeRequest(
            ExpectedDid: did.Value,
            ExpectedPdsUri: "https://pds.example.test/",
            OAuthClientKeyId: factory.AtprotoOAuthKeyId,
            Classification: classification.ToString().ToLowerInvariant(),
            OAuthSession: JsonSerializer.SerializeToElement(new { subject = did.Value }),
            CanonicalActorId: null,
            ExpectedCanonicalActorConcurrencyStamp: null);
        BffAtprotoSessionBridgeResponse? firstSession = null;
        await using ExploreDbContext before = factory.CreateDatabase();
        int usersBefore = await before.Users.CountAsync();

        foreach (bool enabled in new[] { false, true, false })
        {
            await SetInstanceEmailIntentAsync(factory, enabled);
            using var request = new HttpRequestMessage(HttpMethod.Post, AtprotoJwtOptions.BridgePath)
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Add(AtprotoJwtOptions.BootstrapHeaderName,
                factory.CreateAtprotoBootstrapAssertion(did: did, classification: classification));

            using HttpResponseMessage response = await client.SendAsync(request);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            BffAtprotoSessionBridgeResponse? session = await response.Content
                .ReadFromJsonAsync<BffAtprotoSessionBridgeResponse>();
            await Assert.That(session).IsNotNull();
            firstSession ??= session!;
            await Assert.That(session!.UserId).IsEqualTo(firstSession.UserId);
            await Assert.That(session.ActorId).IsEqualTo(firstSession.ActorId);
            await Assert.That(session.ParticipationId).IsEqualTo(firstSession.ParticipationId);
            await Assert.That(session.Did).IsEqualTo(did.Value);

            await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            {
                var jwtService = scope.ServiceProvider.GetRequiredService<AtprotoJwtService>();
                var principal = await jwtService.ValidateSessionAsync(
                    token: session.AccessToken,
                    tenantId: PlatformDefaults.DefaultTenantId,
                    cancellationToken: CancellationToken.None);
                await Assert.That(principal).IsNotNull();
                await Assert.That(principal!.FindFirst(JwtRegisteredClaimNames.Sub)?.Value)
                    .IsEqualTo(session.UserId.ToString("D"));
                await Assert.That(principal.FindFirst(AtprotoJwtOptions.DidClaim)?.Value).IsEqualTo(did.Value);
            }

            await using ExploreDbContext stored = factory.CreateDatabase();
            UserExternalLogin binding = await stored.UserExternalLogins.Include(login => login.User)
                .SingleAsync(login => login.AuthenticationProviderId == (int)AuthenticationProviderKind.Atproto
                    && login.ProviderKey == did.Value);
            await Assert.That(binding.UserId).IsEqualTo(session.UserId);
            await Assert.That(binding.User.Email).IsEqualTo(string.Empty);
            await Assert.That(binding.User.EmailVerified).IsEqualTo(false);
            await Assert.That(await stored.Users.CountAsync()).IsEqualTo(usersBefore + 1);
            await Assert.That(await stored.LocalIdentityUsers.CountAsync()).IsEqualTo(0);
            await Assert.That((await stored.Actors.SingleAsync(actor => actor.Id == session.ActorId)).UserId)
                .IsEqualTo(session.UserId);
            stored.EnableTenantFilterBypass("S04 verifies only the exact participation returned by this default-tenant bootstrap.");
            TenantUser participation = await stored.TenantUsers.SingleAsync(row =>
                row.Id == session.ParticipationId && row.TenantId == PlatformDefaults.DefaultTenantId);
            await Assert.That(participation.UserId).IsEqualTo(session.UserId);
            await Assert.That(participation.ActorId).IsEqualTo(session.ActorId);
            await Assert.That(participation.StatusId).IsEqualTo((int)TenantUserStatusEnum.Active);
            await Assert.That((await stored.SystemSettings.SingleAsync(
                setting => setting.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)).Value)
                .IsEqualTo(enabled ? "true" : "false");
        }
    }

    private static async Task SetInstanceEmailIntentAsync(LocalAdmissionWebApplicationFactory factory, bool enabled)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISystemSettingRepository>();
        await repository.UpsertAsync(new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = GovernanceSettingKeys.Email.DeliveryEnabled,
            Value = enabled ? "true" : "false",
            ValueType = SettingValueType.Boolean,
            Category = "Email",
            CreatedAt = DateTime.UtcNow
        });
    }
}
