using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Authentication;
using Explore.API.Configuration;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.API.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class VerifiedAccountContinuationHttpTests
{
    private const string Route = "/api/auth/account-continuation";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UntargetedLocalContinuationUsesLinkedAccountPreferenceWithoutEnrollment(bool multiTenant)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid preferred = await ArrangeMembershipsAsync(fixture, multiTenant);
        string token = await fixture.SignInAsync();
        string before = await ReadRowsAsync(fixture.Native);

        using var response = await ContinueAsync(fixture.Client, token);

        await AssertDestinationAsync(response, preferred, "continuation-preferred", multiple: true);
        await Assert.That(await ReadRowsAsync(fixture.Native)).IsEqualTo(before);
    }

    [Test]
    [Arguments(false, "local-admission", HttpStatusCode.OK, "Destination")]
    [Arguments(false, "continuation-unjoined", HttpStatusCode.OK, "NoMembership")]
    [Arguments(true, "continuation-inactive", HttpStatusCode.NotFound, null)]
    [Arguments(false, "missing-tenant", HttpStatusCode.NotFound, null)]
    [Arguments(true, " ", HttpStatusCode.NotFound, null)]
    public async Task ExplicitTargetNeverFallsBackToPreferredMembership(
        bool multiTenant, string slug, HttpStatusCode status, string? outcome)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await ArrangeMembershipsAsync(fixture, multiTenant);
        string token = await fixture.SignInAsync();
        string before = await ReadRowsAsync(fixture.Native);

        using var response = await ContinueAsync(fixture.Client, token, slug);

        await Assert.That(response.StatusCode).IsEqualTo(status);
        await AssertPrivateAsync(response);
        if (outcome is not null)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await Assert.That(body.RootElement.GetProperty("outcome").GetString()).IsEqualTo(outcome);
            if (outcome == "Destination")
            {
                await Assert.That(body.RootElement.GetProperty("tenantSlug").GetString()).IsEqualTo("local-admission");
            }
            else
            {
                await Assert.That(body.RootElement.TryGetProperty("tenantId", out _)).IsFalse();
            }
        }
        await Assert.That(await ReadRowsAsync(fixture.Native)).IsEqualTo(before);
    }

    [Test]
    public async Task LinkedLocalAccountWithoutMembershipRemainsUnchanged()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        string token = await fixture.SignInAsync();
        string before = await ReadRowsAsync(fixture.Native);

        using var response = await ContinueAsync(fixture.Client, token);

        await AssertOutcomeAsync(response, "NoMembership");
        await Assert.That(await ReadRowsAsync(fixture.Native)).IsEqualTo(before);
    }

    [Test]
    public async Task ClosedRegistrationStillAllowsNativeLoginAndExactAccountContinuation()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid preferred = await ArrangeMembershipsAsync(fixture, multiTenant: false);
        await using (var database = fixture.Native.CreateDatabase())
        {
            database.BindTenantLease();
            var tenant = await database.Tenants.SingleAsync(row => row.Id == preferred);
            database.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
                SettingKey = GovernanceSettingKeys.Tenants.UserRegistrationEnabled,
                Value = "false", CreatedAt = DateTime.UtcNow
            });
            await database.SaveChangesAsync();
        }
        string token = await fixture.SignInAsync();
        string before = await ReadRowsAsync(fixture.Native);

        using var response = await ContinueAsync(fixture.Client, token, "continuation-preferred");

        await AssertDestinationAsync(response, preferred, "continuation-preferred", multiple: true);
        await Assert.That(await ReadRowsAsync(fixture.Native)).IsEqualTo(before);
    }

    [Test]
    public async Task BrowserIdentityAndTargetBodyCannotReplaceVerifiedLocalAccount()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid preferred = await ArrangeMembershipsAsync(fixture, multiTenant: true);
        string token = await fixture.SignInAsync();
        string before = await ReadRowsAsync(fixture.Native);
        using var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = JsonContent.Create(new
            {
                issuer = "https://untrusted.example.test",
                subject = Guid.CreateVersion7(),
                userId = Guid.CreateVersion7(),
                accountKey = Guid.CreateVersion7().ToString("D"),
                targetTenantId = Guid.CreateVersion7(),
                email = "another-account@example.test"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await fixture.Client.SendAsync(request);

        await AssertDestinationAsync(response, preferred, "continuation-preferred", multiple: true);
        await Assert.That(await ReadRowsAsync(fixture.Native)).IsEqualTo(before);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task VerifiedHostTargetsItsActiveTenantButCannotMaskInvalidExplicitSlug(bool invalidSlug)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => services.PostConfigure<EffectiveHostAuthorityOptions>(options =>
                options.TenantDomains =
                [
                    new TenantDomainOwnershipAttestation
                    {
                        Host = "continuation.example.test", TenantId = PlatformDefaults.DefaultTenantId,
                        VerifiedAt = now.AddMinutes(-1), ExpiresAt = now.AddMinutes(30),
                        EvidenceReference = "Native continuation test ownership"
                    }
                ]));
        await ArrangeMembershipsAsync(fixture, multiTenant: true);
        await using (var database = fixture.Native.CreateDatabase())
        {
            database.BindTenantLease();
            await using var transaction = await database.Database.BeginTransactionAsync();
            var tenant = await database.Tenants.SingleAsync(row => row.Id == PlatformDefaults.DefaultTenantId);
            database.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
                SettingKey = GovernanceSettingKeys.Domains.TenantCustomDomain,
                Value = JsonSerializer.Serialize("continuation.example.test"), CreatedAt = DateTime.UtcNow
            });
            var customDomain = await database.SystemSettings.SingleOrDefaultAsync(
                row => row.SettingKey == GovernanceSettingKeys.Routing.ResolverCustomDomainEnabled);
            if (customDomain is null)
                database.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = GovernanceSettingKeys.Routing.ResolverCustomDomainEnabled,
                    Value = "true", ValueType = SettingValueType.Boolean, Category = "Routing",
                    CreatedAt = DateTime.UtcNow
                });
            else
                customDomain.Value = "true";
            await database.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        string token = await fixture.SignInAsync();
        string before = await ReadRowsAsync(fixture.Native);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://continuation.example.test" + Route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (invalidSlug)
            request.Headers.Add("X-Tenant-Slug", "missing-tenant");

        using var response = await fixture.Client.SendAsync(request);

        if (invalidSlug)
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await AssertPrivateAsync(response);
        }
        else
            await AssertDestinationAsync(response, PlatformDefaults.DefaultTenantId, "local-admission", multiple: true);
        await Assert.That(await ReadRowsAsync(fixture.Native)).IsEqualTo(before);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task KeycloakContinuationRequiresExactIssuerSubjectLinkWithoutEmailEnrollment(bool linked)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: AuthenticationProviderKind.Keycloak);
        string subject = "opaque-account-" + Guid.CreateVersion7().ToString("N");
        Guid tenant;
        string email;
        await using (var database = factory.CreateDatabase())
        {
            database.BindTenantLease();
            var user = await database.Users.Include(row => row.Pii).SingleAsync();
            var destination = await database.Tenants.SingleAsync();
            tenant = destination.Id;
            email = user.Email;
            if (linked)
            {
                database.UserExternalLogins.Add(new UserExternalLogin
                {
                    Id = Guid.CreateVersion7(), UserId = user.Id, User = user,
                    AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
                    AuthenticationProvider = null!,
                    ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
                        factory.ExternalIssuer, subject).Value,
                    CreatedAt = DateTime.UtcNow
                });
                database.TenantUsers.Add(new TenantUser
                {
                    Id = Guid.CreateVersion7(), TenantId = tenant, Tenant = destination,
                    UserId = user.Id, User = user, StatusId = (int)TenantUserStatusEnum.Active,
                    CreatedAt = DateTime.UtcNow
                });
                await database.SaveChangesAsync();
            }
        }
        using var client = CreateClient(factory);
        string token = factory.CreateExternalProviderToken(
            [
                new Claim("sub", subject), new Claim("typ", "Bearer"),
                new Claim("auth_provider", "keycloak"), new Claim("email", email),
                new Claim("email_verified", "true"),
                new Claim("internal_user_id", Guid.CreateVersion7().ToString("D"))
            ], factory.ExternalIssuer);
        string before = await ReadRowsAsync(factory);

        using var response = await ContinueAsync(client, token);

        if (linked)
            await AssertDestinationAsync(response, tenant, "local-admission", multiple: false);
        else
            await AssertOutcomeAsync(response, "Unlinked");
        await Assert.That(await ReadRowsAsync(factory)).IsEqualTo(before);
    }

    [Test]
    [Arguments("atproto")]
    [Arguments("google")]
    public async Task AmbientAtprotoAndRetiredGoogleClaimsCannotContinue(string provider)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: AuthenticationProviderKind.Keycloak);
        using var client = CreateClient(factory);
        string token = factory.CreateExternalProviderToken(
            [
                new Claim("sub", "did:plc:abcdefghijklmnopqrstuvwx"),
                new Claim("typ", "Bearer"), new Claim("auth_provider", provider)
            ], factory.ExternalIssuer);
        string before = await ReadRowsAsync(factory);

        using var response = await ContinueAsync(client, token);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await AssertPrivateAsync(response);
        await Assert.That(await ReadRowsAsync(factory)).IsEqualTo(before);
    }

    [Test]
    public async Task SignedTenantBoundAtprotoSessionCannotBecomeGlobalAccountEvidence()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(enableAtproto: true);
        using var client = CreateClient(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var issued = await scope.ServiceProvider.GetRequiredService<IAtprotoSessionTokenIssuer>().IssueAsync(
            Guid.CreateVersion7(), PlatformDefaults.DefaultTenantId,
            AtprotoDid.Parse("did:plc:abcdefghijklmnopqrstuvwx"), default);
        string before = await ReadRowsAsync(factory);

        using var response = await ContinueAsync(client, issued.Token, "local-admission");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await AssertPrivateAsync(response);
        await Assert.That(await ReadRowsAsync(factory)).IsEqualTo(before);
    }

    [Test]
    public async Task SignedAtprotoBootstrapMachineCannotAuthenticateAccountContinuation()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: AuthenticationProviderKind.Keycloak, enableAtproto: true);
        using var client = CreateClient(factory);
        string assertion = factory.CreateAtprotoBootstrapAssertion(
            AtprotoDid.Parse("did:plc:abcdefghijklmnopqrstuvwx"), AtprotoSubjectClassification.Person);
        using var request = new HttpRequestMessage(HttpMethod.Post, Route);
        request.Headers.Add(AtprotoJwtOptions.BootstrapHeaderName, assertion);
        string before = await ReadRowsAsync(factory);

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertPrivateAsync(response);
        await Assert.That(await ReadRowsAsync(factory)).IsEqualTo(before);
    }

    [Test]
    public async Task ActualUserApiKeyCannotActAsVerifiedAccount()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await ArrangeMembershipsAsync(fixture, multiTenant: false);
        string token = await fixture.SignInAsync();
        using var issuance = new HttpRequestMessage(HttpMethod.Post, "/api/externalapikey")
        {
            Content = JsonContent.Create(new CreateExternalApiKeyDto
            {
                Name = "Continuation rejection witness",
                Scopes = ["events:read"],
                ExternalApiKeyOwnerTypeId = (int)ExternalApiKeyOwnerType.User
            })
        };
        issuance.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        issuance.Headers.Add("X-Tenant-Slug", "local-admission");
        issuance.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
        using var issued = await fixture.Client.SendAsync(issuance);
        await Assert.That(issued.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        string key = body.RootElement.GetProperty("apiKey").GetString()!;
        string before = await ReadRowsAsync(fixture.Native);
        using var request = new HttpRequestMessage(HttpMethod.Post, Route);
        request.Headers.Add("X-API-Key", key);

        using var response = await fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await AssertPrivateAsync(response);
        await Assert.That(await ReadRowsAsync(fixture.Native)).IsEqualTo(before);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnauthenticatedContinuationCannotReadAccountFacts(bool tamperedToken)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        string? token = tamperedToken ? await fixture.SignInAsync() : null;
        if (token is not null)
        {
            string[] parts = token.Split('.');
            parts[2] = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
            token = string.Join('.', parts);
        }
        await SetMultiTenantAsync(fixture);
        using var request = new HttpRequestMessage(HttpMethod.Post, Route);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        string before = await ReadRowsAsync(fixture.Native);

        using var response = await fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertPrivateAsync(response);
        await Assert.That(await ReadRowsAsync(fixture.Native)).IsEqualTo(before);
    }

    [Test]
    public async Task UntargetedContinuationDoesNotExemptUserSynchronizationFromTenantResolution()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await ArrangeMembershipsAsync(fixture, multiTenant: true);
        string token = await fixture.SignInAsync();
        string before = await ReadRowsAsync(fixture.Native);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/user/sync");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await ReadRowsAsync(fixture.Native)).IsEqualTo(before);
    }

}
