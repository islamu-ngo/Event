using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Authentication.Local.Handlers.Queries;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class NativeLocalIdentityLifecycleDiscoveryTests
{
    private const string DiscoveryPath = "/api/InstanceOnboarding/auth-provider-configuration";
    private static CancellationToken CancellationToken => TestContext.Current!.Execution.CancellationToken;

    [Test]
    public async Task ControllerOwnsDiscoveryPortWithoutExposingAnActionDependency()
    {
        var controller = typeof(Explore.API.Controllers.InstanceOnboardingController);
        var port = typeof(IQueryHandler<GetLocalIdentityLifecycleCapabilitiesQuery, LocalIdentityLifecycleCapabilities>);
        await Assert.That(controller.GetConstructors().Single().GetParameters()
            .Any(parameter => parameter.ParameterType == port)).IsTrue();
        var action = controller.GetMethod(nameof(Explore.API.Controllers.InstanceOnboardingController.GetAuthProviderConfiguration))!;
        await Assert.That(action.GetParameters().Select(parameter => parameter.ParameterType).ToArray())
            .IsEquivalentTo(new[] { typeof(CancellationToken) });
    }

    [Test]
    public async Task DecoratedQueryRequiresExactPersistedSessionAndDoesNotMutateIdentity()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        string bearer = await fixture.SignInAsync();
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        ClaimsPrincipal principal = await AuthenticateAsync(scope.ServiceProvider, bearer);
        LocalSessionAuthority authority = principal.TryGetLocalSessionAuthority()!;
        var identity = await fixture.ReadIdentityAsync();
        var mirror = await fixture.ReadMirrorAsync();

        await Assert.That(await QueryAsync(scope.ServiceProvider, new())).IsEqualTo(new(false, false, false));
        await Assert.That(await QueryAsync(scope.ServiceProvider, new(authority))).IsEqualTo(new(true, true, true));
        await Assert.That(await QueryAsync(scope.ServiceProvider, new(new LocalSessionAuthority(
            authority.LocalSubjectId, Guid.NewGuid().ToString("N"), authority.EmailVerified)))).IsEqualTo(new(false, false, false));
        await Assert.That(await QueryAsync(scope.ServiceProvider, new(new LocalSessionAuthority(
            Guid.CreateVersion7(), authority.SecurityStamp, authority.EmailVerified)))).IsEqualTo(new(false, false, false));
        await Assert.That(await QueryAsync(scope.ServiceProvider, new(new LocalSessionAuthority(
            authority.LocalSubjectId, authority.SecurityStamp, false)))).IsEqualTo(new(false, false, false));
        await Assert.That(await fixture.ReadIdentityAsync()).IsEqualTo(identity);
        await Assert.That(await fixture.ReadMirrorAsync()).IsEqualTo(mirror);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SerializedAssemblerNeverOffersLocalActionsForForeignDtoAndDropsStaleAuthorityAfterReset()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        string bearer = await fixture.SignInAsync();
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        ClaimsPrincipal principal = await AuthenticateAsync(scope.ServiceProvider, bearer);
        LocalSessionAuthority authority = principal.TryGetLocalSessionAuthority()!;
        var dto = new UserDto { Id = authority.LocalSubjectId, Email = fixture.Login.Identifier, FirstName = "Local", LastName = "Member" };
        using (JsonDocument owned = await AssembleAsync(scope.ServiceProvider, dto, principal))
            await AssertLinksAsync(owned, true, true, true);
        using (JsonDocument foreign = await AssembleAsync(scope.ServiceProvider, dto with { Id = Guid.CreateVersion7() }, principal))
            await AssertLinksAsync(foreign, false, false, false);
        using (JsonDocument anonymous = await AssembleAsync(scope.ServiceProvider, dto, new ClaimsPrincipal()))
            await AssertLinksAsync(anonymous, false, false, false);

        var credentials = scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>();
        var current = (await credentials.ListAsync(new(1, 100), CancellationToken)).Items.Single(item => item.LocalSubjectId == dto.Id);
        var operation = (await credentials.ReadOperationAsync(current.CurrentOperationId!.Value, CancellationToken))!;
        // The fixture freezes its clock before seeding with the native host's system clock.
        // Reset must not precede the persisted verification provenance.
        fixture.Clock.Advance(operation.VerifiedAt - fixture.Clock.GetUtcNow().UtcDateTime);
        var reset = await credentials.ResetAsync(new(Guid.CreateVersion7(), operation.Receipt.InitiatingApplicationUserId,
            dto.Id, current.CurrentOperationId.Value, current.CurrentOperationConcurrencyStamp!.Value, "Discovery revocation regression"), CancellationToken);
        await Assert.That(reset.Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);
        await Assert.That((await credentials.ReadLinkedIdentityAsync(dto.Id, CancellationToken))!.CredentialState)
            .IsEqualTo(LocalCredentialState.ChangeRequired);
        await Assert.That(await QueryAsync(scope.ServiceProvider, new(authority))).IsEqualTo(new(false, false, false));
        var changedIdentity = await fixture.ReadIdentityAsync();
        await Assert.That(await QueryAsync(scope.ServiceProvider, new(new LocalSessionAuthority(
            dto.Id, changedIdentity.SecurityStamp, changedIdentity.Verified)))).IsEqualTo(new(false, false, false));
        using (JsonDocument stale = await AssembleAsync(scope.ServiceProvider, dto, principal))
            await AssertLinksAsync(stale, false, false, false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/User");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var rejected = await fixture.Client.SendAsync(request, CancellationToken);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task PublicAndCurrentUserHttpKeepEmailAvailabilitySeparateFromPasswordAuthority(bool emailEnabled)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(emailEnabled: emailEnabled);
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        await Assert.That(await QueryAsync(scope.ServiceProvider, new(PublicDiscovery: true)))
            .IsEqualTo(new(emailEnabled, emailEnabled, false));
        using (var response = await fixture.Client.GetAsync(DiscoveryPath, CancellationToken))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            await AssertLinksAsync(json, emailEnabled, emailEnabled, false);
            await Assert.That(json.RootElement.TryGetProperty("primaryProviderId", out _)).IsTrue();
        }
        string bearer = await fixture.SignInAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/User");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var current = await fixture.Client.SendAsync(request, CancellationToken);
        await Assert.That(current.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var currentJson = JsonDocument.Parse(await current.Content.ReadAsStringAsync(CancellationToken));
        await AssertLinksAsync(currentJson, emailEnabled, emailEnabled, true);

        await using (var database = fixture.Native.CreateDatabase())
        {
            var host = await database.SystemSettings.SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Email.SmtpHost, CancellationToken);
            host.Value = "\"\"";
            await database.SaveChangesAsync(CancellationToken);
        }
        await Assert.That(await QueryAsync(scope.ServiceProvider, new(PublicDiscovery: true))).IsEqualTo(new(false, false, false));
        var principal = await AuthenticateAsync(scope.ServiceProvider, bearer);
        await Assert.That(await QueryAsync(scope.ServiceProvider, new(principal.TryGetLocalSessionAuthority()))).IsEqualTo(new(false, false, true));
        using var missing = await fixture.Client.GetAsync(DiscoveryPath, CancellationToken);
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var missingJson = JsonDocument.Parse(await missing.Content.ReadAsStringAsync(CancellationToken));
        await AssertLinksAsync(missingJson, false, false, false);
    }

    [Test]
    public async Task ExternalPrimaryProviderHidesPublicLocalDiscoveryEvenWithAvailableEmail()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(AuthenticationProviderKind.Keycloak);
        await using (var database = factory.CreateDatabase())
        {
            var settings = await database.SystemSettings.ToDictionaryAsync(row => row.SettingKey, CancellationToken);
            settings[GovernanceSettingKeys.Email.DeliveryEnabled].Value = "true";
            settings[GovernanceSettingKeys.Email.SmtpHost].Value = "\"smtp.lifecycle.test\"";
            settings[GovernanceSettingKeys.Email.FromAddress].Value = "\"events@lifecycle.test\"";
            await database.SaveChangesAsync(CancellationToken);
        }
        var login = await factory.SeedLocalUserAsync(emailConfirmed: true);
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        await using var identityDatabase = factory.CreateIdentityDatabase();
        var localIdentity = await identityDatabase.Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(user => user.Email == login.Identifier, CancellationToken);
        await Assert.That(await QueryAsync(scope.ServiceProvider, new(new LocalSessionAuthority(
            localIdentity.Id, localIdentity.SecurityStamp!, localIdentity.EmailConfirmed)))).IsEqualTo(new(true, true, true));
        await Assert.That((await scope.ServiceProvider.GetRequiredService<IEmailDeliveryCapabilityResolver>()
            .ResolveAsync(null, CancellationToken)).State).IsEqualTo(EmailDeliveryState.Available);
        await Assert.That(await QueryAsync(scope.ServiceProvider, new(PublicDiscovery: true))).IsEqualTo(new(false, false, false));
        using var response = await client.GetAsync(DiscoveryPath, CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        await AssertLinksAsync(json, false, false, false);
    }

    [Test]
    public async Task UnverifiedSessionLosesPasswordAuthorityWhenEmailVerificationBecomesRequired()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(verified: false, emailEnabled: false);
        string bearer = await fixture.SignInAsync();
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var principal = await AuthenticateAsync(scope.ServiceProvider, bearer);
        var authority = principal.TryGetLocalSessionAuthority()!;
        await Assert.That(await QueryAsync(scope.ServiceProvider, new(authority))).IsEqualTo(new(false, false, true));
        await using (var database = fixture.Native.CreateDatabase())
        {
            var intent = await database.SystemSettings.SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled, CancellationToken);
            intent.Value = "true";
            await database.SaveChangesAsync(CancellationToken);
        }
        await Assert.That(await QueryAsync(scope.ServiceProvider, new(authority))).IsEqualTo(new(false, false, false));
        await Assert.That(await QueryAsync(scope.ServiceProvider, new(PublicDiscovery: true))).IsEqualTo(new(true, true, false));
    }

    [Test]
    public async Task CancellationAndReadFailuresCannotBecomeSuccessfulCapabilitiesOrPublicLinks()
    {
        var failure = new ReadFailure();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: failure);
        string bearer = await fixture.SignInAsync();
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var principal = await AuthenticateAsync(scope.ServiceProvider, bearer);
        var authority = principal.TryGetLocalSessionAuthority()!;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.That(async () => await QueryAsync(scope.ServiceProvider, new(authority), cancelled.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(async () => await QueryAsync(scope.ServiceProvider, new(PublicDiscovery: true), cancelled.Token))
            .Throws<OperationCanceledException>();

        failure.Armed = true;
        await Assert.That(await QueryAsync(scope.ServiceProvider, new(authority))).IsEqualTo(new(false, false, false));
        await Assert.That(async () => await QueryAsync(scope.ServiceProvider, new(PublicDiscovery: true)))
            .Throws<IOException>();
        using var response = await fixture.Client.GetAsync(DiscoveryPath, CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        await Assert.That(json.RootElement.GetProperty("status").GetInt32()).IsEqualTo(500);
        await Assert.That(json.RootElement.TryGetProperty("_links", out _)).IsFalse();
    }

    private static async Task<ClaimsPrincipal> AuthenticateAsync(IServiceProvider services, string bearer)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers.Authorization = $"Bearer {bearer}";
        var result = await services.GetRequiredService<IAuthenticationService>().AuthenticateAsync(context, ApiAuthenticationSchemeNames.LocalIdentity);
        await Assert.That(result.Succeeded).IsTrue();
        return result.Principal!;
    }

    private static async Task<JsonDocument> AssembleAsync(IServiceProvider services, UserDto dto, ClaimsPrincipal principal)
    {
        var context = new DefaultHttpContext { RequestServices = services, User = principal, RequestAborted = CancellationToken };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        var assembler = services.GetRequiredService<IResourceAssembler<UserDto, UserDto>>();
        return JsonDocument.Parse(JsonSerializer.Serialize(await assembler.ToResource(dto, context), JsonSerializerOptions.Web));
    }

    private static async Task AssertLinksAsync(JsonDocument json, bool verify, bool recover, bool change)
    {
        bool hasLinks = json.RootElement.TryGetProperty("_links", out var links);
        await Assert.That(hasLinks || !(verify || recover || change)).IsTrue();
        if (!hasLinks) return;
        await AssertLinkAsync("verify-email", "/api/auth/local/email-verifications", verify);
        await AssertLinkAsync("recover-password", "/api/auth/local/password-recoveries", recover);
        await AssertLinkAsync("change-password", "/api/auth/local/password", change);

        async Task AssertLinkAsync(string relation, string path, bool expected)
        {
            bool present = links.TryGetProperty(relation, out var link);
            await Assert.That(present).IsEqualTo(expected);
            if (!expected) return;
            await Assert.That(link.GetProperty("href").GetString()!.EndsWith(path, StringComparison.OrdinalIgnoreCase)).IsTrue();
            await Assert.That(link.GetProperty("method").GetString()).IsEqualTo("POST");
        }
    }

    private static async Task<LocalIdentityLifecycleCapabilities> QueryAsync(IServiceProvider services,
        GetLocalIdentityLifecycleCapabilitiesQuery request, CancellationToken? cancellationToken = null)
    {
        var query = services.GetRequiredService<IQueryHandler<GetLocalIdentityLifecycleCapabilitiesQuery, LocalIdentityLifecycleCapabilities>>();
        await Assert.That(query.GetType()).IsEqualTo(typeof(Explore.Application.Operations.Decorators.AuthorizationQueryHandlerDecorator<
            GetLocalIdentityLifecycleCapabilitiesQuery, LocalIdentityLifecycleCapabilities>));
        return await query.QueryAsync(request, cancellationToken ?? CancellationToken);
    }

    private sealed class ReadFailure : DbCommandInterceptor
    {
        internal bool Armed { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed) throw new IOException("Injected discovery read failure.");
            return ValueTask.FromResult(result);
        }
    }
}
