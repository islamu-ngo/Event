using System.Security.Cryptography;
using System.Security.Claims;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Event.API.IntegrationTests.Authentication;

[NotInParallel]
public sealed class FederatedEnrollmentAdmissionTests
{
    [Test]
    public async Task ExactAtprotoAccountWithActiveMembershipCanLoginAfterRegistrationCloses()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(enableAtproto: true);
        AtprotoDid did = NewDid();
        AtprotoSessionBootstrapResult enrolled = await BootstrapAsync(factory, did);
        await Assert.That(enrolled.Success).IsTrue().Because(enrolled.FailureCode);
        await Assert.That(enrolled.ParticipationId).IsNotNull();
        await CloseRegistrationAsync(factory);
        GraphCounts before = await ReadGraphAsync(factory);

        AtprotoSessionBootstrapResult login = await BootstrapAsync(factory, did);

        await Assert.That(login.Success).IsTrue().Because(login.FailureCode);
        await Assert.That(login.UserId).IsEqualTo(enrolled.UserId);
        await Assert.That(login.ActorId).IsEqualTo(enrolled.ActorId);
        await Assert.That(login.ParticipationId).IsEqualTo(enrolled.ParticipationId);
        await Assert.That(login.Token is null).IsFalse();
        await Assert.That(await ReadGraphAsync(factory)).IsEqualTo(before);
        await using ExploreDbContext stored = factory.CreateDatabase();
        stored.BindTenantLease();
        UserExternalLogin binding = await stored.UserExternalLogins.AsNoTracking().SingleAsync(
            row => row.AuthenticationProviderId == (int)AuthenticationProviderKind.Atproto
                && row.ProviderKey == did.Value);
        await Assert.That(binding.UserId).IsEqualTo(enrolled.UserId!.Value);
        TenantUser member = await stored.TenantUsers.IgnoreQueryFilters().AsNoTracking().SingleAsync(
            row => row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == binding.UserId);
        await Assert.That(member.Id).IsEqualTo(enrolled.ParticipationId!.Value);
        await Assert.That(member.StatusId).IsEqualTo((int)TenantUserStatusEnum.Active);
        await Assert.That(member.IsDeleted).IsFalse();
    }

    [Test]
    public async Task ExactAtprotoAccountWithoutTargetMembershipCannotJoinClosedTenant()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(enableAtproto: true);
        AtprotoDid did = NewDid();
        AtprotoSessionBootstrapResult enrolled = await BootstrapAsync(factory, did);
        await Assert.That(enrolled.Success).IsTrue().Because(enrolled.FailureCode);
        await using (ExploreDbContext seed = factory.CreateDatabase())
        {
            seed.BindTenantLease();
            // Keep the exact global account, actor and DID binding; only target membership is absent.
            int removed = await seed.TenantUsers.IgnoreQueryFilters().Where(
                row => row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == enrolled.UserId)
                .ExecuteDeleteAsync();
            await Assert.That(removed).IsEqualTo(1);
        }
        await CloseRegistrationAsync(factory);
        GraphCounts before = await ReadGraphAsync(factory);
        await using (ExploreDbContext seed = factory.CreateDatabase())
        {
            seed.BindTenantLease();
            await Assert.That(await seed.TenantUsers.IgnoreQueryFilters().AnyAsync(
                row => row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == enrolled.UserId)).IsFalse();
            await Assert.That(await seed.UserExternalLogins.AnyAsync(
                row => row.AuthenticationProviderId == (int)AuthenticationProviderKind.Atproto
                    && row.ProviderKey == did.Value && row.UserId == enrolled.UserId)).IsTrue();
        }

        AtprotoSessionBootstrapResult result = await BootstrapAsync(factory, did);

        // Observe committed rows before the result: an issued session must not hide an illicit join.
        await using ExploreDbContext stored = factory.CreateDatabase();
        stored.BindTenantLease();
        await Assert.That(await stored.TenantUsers.IgnoreQueryFilters().AnyAsync(
            row => row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == enrolled.UserId)).IsFalse();
        await Assert.That(await ReadGraphAsync(factory)).IsEqualTo(before);
        await Assert.That(await stored.UserExternalLogins.AnyAsync(
            row => row.AuthenticationProviderId == (int)AuthenticationProviderKind.Atproto
                && row.ProviderKey == did.Value && row.UserId == enrolled.UserId)).IsTrue();
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Token is null).IsTrue();
        await Assert.That(result.ParticipationId).IsNull();
    }

    [Test]
    public async Task NewAtprotoDidCannotAllocateAnyAccountRowsWhenTargetRegistrationIsClosed()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(enableAtproto: true);
        // A successful real enrollment proves this host's provider, fences and transaction are usable.
        AtprotoSessionBootstrapResult control = await BootstrapAsync(factory, NewDid());
        await Assert.That(control.Success).IsTrue().Because(control.FailureCode);
        await CloseRegistrationAsync(factory);
        AtprotoDid did = NewDid();
        GraphCounts before = await ReadGraphAsync(factory);

        AtprotoSessionBootstrapResult result = await BootstrapAsync(factory, did);

        await Assert.That(await ReadGraphAsync(factory)).IsEqualTo(before);
        await using ExploreDbContext stored = factory.CreateDatabase();
        stored.BindTenantLease();
        await Assert.That(await stored.UserExternalLogins.AnyAsync(
            row => row.AuthenticationProviderId == (int)AuthenticationProviderKind.Atproto
                && row.ProviderKey == did.Value)).IsFalse();
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("account_not_linked");
        await Assert.That(result.UserId).IsNull();
        await Assert.That(result.ActorId).IsNull();
        await Assert.That(result.ParticipationId).IsNull();
        await Assert.That(result.Token is null).IsTrue();
    }

    [Test]
    public async Task KeycloakSynchronizationCannotCreateAccountUnderClosedTargetRegistration()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: AuthenticationProviderKind.Keycloak);
        factory.Services.GetRequiredService<IConfiguration>()["Keycloak:ClientId"] = "native-browser-client";
        await using (var readiness = factory.Services.CreateAsyncScope())
        {
            readiness.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
            var capability = await readiness.ServiceProvider.GetRequiredService<IVisitorAccessCapabilityResolver>()
                .ResolveAsync(PlatformDefaults.DefaultTenantId);
            await Assert.That(capability.UserRegistrationEnabled).IsTrue();
            await Assert.That(capability.AllowsAccountRequiredParticipation).IsTrue();
        }
        BaseCommandResponse<Guid> control = await SynchronizeAsync(factory, Guid.CreateVersion7());
        await Assert.That(control.IsSuccess).IsTrue().Because(control.Message);
        await using (ExploreDbContext stored = factory.CreateDatabase())
        {
            stored.BindTenantLease();
            await Assert.That(await stored.UserExternalLogins.AnyAsync(
                row => row.AuthenticationProviderId == (int)AuthenticationProviderKind.Keycloak
                    && row.UserId == control.Id)).IsTrue();
        }
        await CloseRegistrationAsync(factory);
        Guid subject = Guid.CreateVersion7();
        ProviderAccountKey key = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            factory.ExternalIssuer, subject.ToString("D"));
        GraphCounts before = await ReadGraphAsync(factory);

        BaseCommandResponse<Guid> result = await SynchronizeAsync(factory, subject);

        await using ExploreDbContext verification = factory.CreateDatabase();
        verification.BindTenantLease();
        await Assert.That(await verification.UserExternalLogins.AnyAsync(
            row => row.AuthenticationProviderId == (int)AuthenticationProviderKind.Keycloak
                && row.ProviderKey == key.Value)).IsFalse();
        await Assert.That(await ReadGraphAsync(factory)).IsEqualTo(before);
        await Assert.That(result.IsSuccess).IsFalse();
    }

    private static AtprotoDid NewDid() =>
        AtprotoDid.Parse($"did:plc:{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}");

    private static async Task<AtprotoSessionBootstrapResult> BootstrapAsync(
        LocalAdmissionWebApplicationFactory factory, AtprotoDid did)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        await using var human = await scope.ServiceProvider.GetRequiredService<IHumanSecuritySourceGate>()
            .AcquireAsync(CancellationToken.None);
        await using var enrollment = await scope.ServiceProvider.GetRequiredService<IEventDiscoveryEnrollmentGate>()
            .AcquireAsync(CancellationToken.None);
        return await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<BootstrapAtprotoSessionCommand, AtprotoSessionBootstrapResult>>()
            .ExecuteAsync(new BootstrapAtprotoSessionCommand(
                ExpectedDid: did,
                ExpectedPdsUri: "https://pds.example.test/",
                OAuthClientKeyId: factory.AtprotoOAuthKeyId,
                Classification: AtprotoSubjectClassification.Person,
                OAuthSessionPayload: JsonSerializer.SerializeToUtf8Bytes(new
                {
                    subject = did.Value,
                    nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
                })));
    }

    private static async Task<BaseCommandResponse<Guid>> SynchronizeAsync(
        LocalAdmissionWebApplicationFactory factory, Guid subject)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = "Bearer " + factory.CreateExternalProviderToken(
            [
                new Claim("sub", subject.ToString("D")), new Claim("typ", "Bearer"),
                new Claim("auth_provider", "keycloak"),
                new Claim("email", $"federated-{subject:N}@example.test"),
                new Claim("email_verified", "false", ClaimValueTypes.Boolean),
                new Claim("given_name", "Federated"), new Claim("family_name", "Applicant")
            ], factory.ExternalIssuer);
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        // Install in this execution context before awaiting authentication; AsyncLocal does not flow back.
        accessor.HttpContext = context;
        try
        {
            AuthenticateResult authentication = await context.AuthenticateAsync();
            await Assert.That(authentication.Succeeded).IsTrue().Because(
                authentication.Failure?.Message ?? "The fixture-issued Keycloak token must pass native validation.");
            context.User = authentication.Principal!;
            var identity = context.User.GetProviderIdentity()!;
            await Assert.That(identity.AccountKey.ProviderKind).IsEqualTo(AuthenticationProviderKind.Keycloak);
            await Assert.That(identity.AccountKey).IsEqualTo(
                PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(factory.ExternalIssuer, subject.ToString("D")));
            return await scope.ServiceProvider
                .GetRequiredService<ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new SyncUserCommand
                {
                    AccountKey = identity.AccountKey,
                    AuthorityEvidence = identity.AuthorityEvidence,
                    UserDto = new UserDto
                    {
                        Id = Guid.Empty,
                        AuthProvider = identity.Provider,
                        AuthProviderId = identity.AccountKey.Value,
                        Email = identity.Email,
                        EmailVerified = identity.EmailVerified,
                        FirstName = "Federated",
                        LastName = "Applicant"
                    }
                });
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static async Task CloseRegistrationAsync(LocalAdmissionWebApplicationFactory factory)
    {
        await using (ExploreDbContext seed = factory.CreateDatabase())
        {
            seed.BindTenantLease();
            seed.Set<TenantSetting>().Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = await seed.Tenants.SingleAsync(row => row.Id == PlatformDefaults.DefaultTenantId),
                SettingKey = GovernanceSettingKeys.Tenants.UserRegistrationEnabled,
                Value = "false",
                CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var capability = await scope.ServiceProvider.GetRequiredService<IVisitorAccessCapabilityResolver>()
            .ResolveAsync(PlatformDefaults.DefaultTenantId);
        await Assert.That(capability.Mode).IsEqualTo(VisitorAccessMode.FullRegistrationAndAuth);
        await Assert.That(capability.UserRegistrationEnabled).IsFalse();
        await Assert.That(capability.SignupDestinations.IsEmpty).IsTrue();
    }

    private static async Task<GraphCounts> ReadGraphAsync(LocalAdmissionWebApplicationFactory factory)
    {
        await using ExploreDbContext stored = factory.CreateDatabase();
        stored.BindTenantLease();
        return new GraphCounts(
            await stored.Users.IgnoreQueryFilters().CountAsync(),
            await stored.Actors.IgnoreQueryFilters().CountAsync(),
            await stored.UserExternalLogins.IgnoreQueryFilters().CountAsync(),
            await stored.TenantUsers.IgnoreQueryFilters().CountAsync(),
            await stored.AtprotoIdentities.IgnoreQueryFilters().CountAsync(),
            await stored.LocalIdentityUsers.CountAsync(),
            await stored.PlatformUserRoles.IgnoreQueryFilters().CountAsync(),
            await stored.TenantUserRoleGrants.IgnoreQueryFilters().CountAsync());
    }

    private sealed record GraphCounts(
        int Users, int Actors, int ExternalLogins, int Memberships, int AtprotoIdentities,
        int LocalCredentials, int PlatformRoles, int TenantRoles);
}
