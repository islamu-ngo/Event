// ABOUTME: Integration tests for verified authentication flows that resolve internal external-login links.
// ABOUTME: Proves provider identity resolution remains intact after public external-login CRUD removal.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.Authentication.Atproto.Handlers.Commands;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Services;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("AuthenticatedApiFixture")]
[ClassDataSource<AuthenticatedApiTestFixture>(Shared = SharedType.PerAssembly)]
public class UserExternalLoginIntegrationTests
{
    private readonly AuthenticatedApiTestFixture _fixture;

    public UserExternalLoginIntegrationTests(AuthenticatedApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task SyncUser_GoogleVerifiedEmail_ShouldAutoMatchExistingUserAndCreateGoogleLink()
    {
        var existingUserId = Guid.NewGuid();
        await EnsureUserExistsAsync(existingUserId, "shared@example.com");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/user/sync");
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            CreateCustomAuthHeader(
                ("sub", "google-sub-123"),
                ("iss", "https://accounts.google.com"),
                ("name", "Shared User"),
                ("email", "shared@example.com"),
                ("given_name", "Shared"),
                ("family_name", "User"),
                ("preferred_username", "shared.user"),
                ("email_verified", "true"),
                ("idp", "google")));

        var response = await _fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
        await Assert.That(body.Id).IsEqualTo(existingUserId);

        using var scope = _fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        string providerKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            "https://accounts.google.com",
            "google-sub-123").Value;
        var googleLink = await dbContext.UserExternalLogins
            .SingleOrDefaultAsync(x =>
                x.AuthenticationProviderId == (int)AuthenticationProviderKind.Google
                && x.ProviderKey == providerKey);
        await Assert.That(googleLink).IsNotNull();
        await Assert.That(googleLink!.UserId).IsEqualTo(existingUserId);
    }

    [Test]
    public async Task SyncUser_AmbientAtprotoClaimsWithoutExplicitLink_ShouldReturnUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/user/sync");
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            CreateCustomAuthHeader(
                ("sub", "did:plc:abc123"),
                ("name", "ATProto User"),
                ("preferred_username", "did:plc:abc123"),
                ("idp", "atproto")));

        var response = await _fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task SyncUser_AmbientAtprotoClaimsWithExistingLink_ShouldReturnUnauthorized()
    {
        var existingUserId = Guid.NewGuid();
        await EnsureUserExistsAsync(existingUserId, "linked@example.com");
        await SeedExternalLoginAsync(existingUserId, "atproto", "did:plc:linked123");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/user/sync");
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            CreateCustomAuthHeader(
                ("sub", "did:plc:linked123"),
                ("name", "AT Linked"),
                ("preferred_username", "did:plc:linked123"),
                ("idp", "atproto")));

        var response = await _fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task BootstrapAtprotoSession_WhenGatewayReturnsDifferentDid_FailsBeforeClaimOrWrites()
    {
        AtprotoDid expectedDid = AtprotoDid.Parse($"did:plc:{Guid.NewGuid():N}");
        AtprotoDid substitutedDid = AtprotoDid.Parse($"did:plc:{Guid.NewGuid():N}");
        var gateway = Substitute.For<IAtprotoOAuthSecurityGateway>();
        gateway.VerifyAsync(Arg.Any<AtprotoOAuthVerificationInput>(), Arg.Any<CancellationToken>())
            .Returns(AtprotoOAuthVerificationResult.Verified(new AtprotoVerifiedOAuthSession(
                substitutedDid,
                "substituted.example.test",
                new Uri("https://pds.example.test"),
                "oauth-key",
                new byte[] { 1 })));
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<ClaimConfiguredInstanceAdministratorCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Failure<Guid>("claim_rejected", "Claim rejected."));
        var logins = Substitute.For<IUserExternalLoginRepository>();
        var tokenIssuer = Substitute.For<IAtprotoSessionTokenIssuer>();
        BootstrapAtprotoSessionCommandHandler handler = CreateAtprotoHandler(
            gateway,
            tokenIssuer,
            sender,
            logins,
            out _);

        AtprotoSessionBootstrapResult result = await handler.Handle(
            CreateBootstrapCommand(expectedDid),
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("pds_identity_mismatch");
        await sender.DidNotReceiveWithAnyArgs().Send(default(ClaimConfiguredInstanceAdministratorCommand)!, default);
        await logins.DidNotReceiveWithAnyArgs().GetByProviderAndKey(default!);
        await gateway.DidNotReceiveWithAnyArgs().PreparePersistenceAsync(default!, default, default, default);
        await gateway.DidNotReceiveWithAnyArgs().PersistPreparedAsync(default!, default);
        await tokenIssuer.DidNotReceiveWithAnyArgs().IssueAsync(default, default, default!, default);
    }

    [Test]
    public async Task BootstrapAtprotoSession_ConcurrentFirstClaim_RereadsAfterClaimAndPreservesOrdering()
    {
        AtprotoDid did = AtprotoDid.Parse($"did:plc:{Guid.NewGuid():N}");
        Guid userId = Guid.CreateVersion7();
        var events = new List<string>();
        var currentAttempt = new AsyncLocal<string>();
        var readsByAttempt = new ConcurrentDictionary<string, int>();
        object eventLock = new();
        void Record(string value)
        {
            lock (eventLock)
            {
                events.Add($"{currentAttempt.Value}:{value}");
            }
        }

        var gateway = Substitute.For<IAtprotoOAuthSecurityGateway>();
        gateway.VerifyAsync(Arg.Any<AtprotoOAuthVerificationInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Record("verify");
                return AtprotoOAuthVerificationResult.Verified(new AtprotoVerifiedOAuthSession(
                    did,
                    "canonical.example.test",
                    new Uri("https://pds.example.test"),
                    "oauth-key",
                    new byte[] { 1 }));
            });
        gateway.PreparePersistenceAsync(
                Arg.Any<AtprotoVerifiedOAuthSession>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Record("prepare");
                return new AtprotoPreparedOAuthSession(
                    new byte[] { 2 },
                    "encryption-key",
                    1,
                    call.ArgAt<Guid>(1),
                    call.ArgAt<Guid>(2),
                    did,
                    "https://pds.example.test/",
                    "oauth-key",
                    null);
            });
        gateway.PersistPreparedAsync(Arg.Any<AtprotoPreparedOAuthSession>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Record("persist-session");
                return Task.CompletedTask;
            });

        var login = new UserExternalLogin { Id = Guid.CreateVersion7(),
        UserId = userId,
        User = null!,
        AuthenticationProviderId = (int)"atproto".ParseAuthenticationProviderKind(), AuthenticationProvider = null!, ProviderKey = did.Value,
        ProviderDisplayName = "AT Protocol" };
        var logins = Substitute.For<IUserExternalLoginRepository>();
        var bothInitialReads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int loginReads = 0;
        logins.GetByProviderAndKey(Arg.Any<ProviderAccountKey>())
            .Returns(async _ =>
            {
                int read = Interlocked.Increment(ref loginReads);
                int attemptRead = readsByAttempt.AddOrUpdate(currentAttempt.Value!, 1, (_, count) => count + 1);
                if (attemptRead == 1)
                {
                    Record("initial-read");
                    if (read == 2)
                    {
                        bothInitialReads.TrySetResult();
                    }
                    await bothInitialReads.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    return null;
                }

                Record(attemptRead == 2 ? "post-claim-reread" : "transaction-reread");
                return login;
            });

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<ClaimConfiguredInstanceAdministratorCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Record("claim");
                return BaseCommandResponse.Success(Guid.CreateVersion7(), "claimed");
            });
        var tokenIssuer = Substitute.For<IAtprotoSessionTokenIssuer>();
        tokenIssuer.IssueAsync(userId, Arg.Any<Guid>(), did, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Record("issue-token");
                return new AtprotoIssuedSessionToken("platform-token", DateTimeOffset.UtcNow.AddMinutes(5));
            });

        var bootstrapRepository = Substitute.For<IInstanceBootstrapStateRepository>();
        bootstrapRepository.GetCurrent(Arg.Any<CancellationToken>()).Returns(
            InstanceBootstrapState.CreateConfiguredAdministratorPending(
                Guid.CreateVersion7(),
                AuthenticationProviderKind.Atproto,
                DeploymentMode.MultiTenant,
                1,
                new string('a', 64),
                new string('b', 64),
                DateTime.UtcNow));

        BootstrapAtprotoSessionCommandHandler first = CreateAtprotoHandler(
            gateway,
            tokenIssuer,
            sender,
            logins,
            out _,
            bootstrapRepository);
        BootstrapAtprotoSessionCommandHandler second = CreateAtprotoHandler(
            gateway,
            tokenIssuer,
            sender,
            logins,
            out _,
            bootstrapRepository);

        async Task<AtprotoSessionBootstrapResult> RunAttemptAsync(
            string name, BootstrapAtprotoSessionCommandHandler handler)
        {
            currentAttempt.Value = name;
            return await handler.Handle(CreateBootstrapCommand(did), CancellationToken.None);
        }

        AtprotoSessionBootstrapResult[] results = await Task.WhenAll(
            RunAttemptAsync("first", first),
            RunAttemptAsync("second", second));

        await Assert.That(results.All(result => result.Success)).IsTrue();
        await Assert.That(loginReads).IsEqualTo(8);
        await sender.Received(2).Send(
            Arg.Any<ClaimConfiguredInstanceAdministratorCommand>(),
            Arg.Any<CancellationToken>());
        List<string> snapshot;
        lock (eventLock)
        {
            snapshot = [.. events];
        }
        foreach (string attempt in new[] { "first", "second" })
        {
            string[] expectedOrder =
            [
                "verify", "initial-read", "claim", "post-claim-reread",
                "transaction-reread", "prepare", "persist-session", "issue-token"
            ];
            int previous = -1;
            foreach (string stage in expectedOrder)
            {
                int position = snapshot.IndexOf($"{attempt}:{stage}");
                await Assert.That(position).IsGreaterThan(previous);
                previous = position;
            }
        }
    }

    [Test]
    public async Task BootstrapAtprotoSession_ExactConfiguredRetryWithLinkedLogin_ReplaysClaimBeforeSessionEffects()
    {
        AtprotoDid did = AtprotoDid.Parse($"did:plc:{Guid.NewGuid():N}");
        Guid userId = Guid.CreateVersion7();
        var login = new UserExternalLogin { Id = Guid.CreateVersion7(),
        UserId = userId,
        User = null!,
        AuthenticationProviderId = (int)"atproto".ParseAuthenticationProviderKind(), AuthenticationProvider = null!, ProviderKey = did.Value,
        ProviderDisplayName = "AT Protocol" };
        var gateway = Substitute.For<IAtprotoOAuthSecurityGateway>();
        gateway.VerifyAsync(Arg.Any<AtprotoOAuthVerificationInput>(), Arg.Any<CancellationToken>())
            .Returns(AtprotoOAuthVerificationResult.Verified(new AtprotoVerifiedOAuthSession(
                did,
                "canonical.example.test",
                new Uri("https://pds.example.test"),
                "oauth-key",
                new byte[] { 1 })));
        var logins = Substitute.For<IUserExternalLoginRepository>();
        logins.GetByProviderAndKey(Arg.Any<ProviderAccountKey>()).Returns(login);
        var sender = Substitute.For<ISender>();
        gateway.PreparePersistenceAsync(
                Arg.Any<AtprotoVerifiedOAuthSession>(),
                Arg.Any<Guid>(),
                userId,
                Arg.Any<CancellationToken>())
            .Returns(call => new AtprotoPreparedOAuthSession(
                new byte[] { 2 },
                "encryption-key",
                1,
                call.ArgAt<Guid>(1),
                userId,
                did,
                "https://pds.example.test/",
                "oauth-key",
                null));
        int claimAttempts = 0;
        ClaimConfiguredInstanceAdministratorCommand? lastClaim = null;
        sender.Send(Arg.Any<ClaimConfiguredInstanceAdministratorCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lastClaim = call.Arg<ClaimConfiguredInstanceAdministratorCommand>();
                return Interlocked.Increment(ref claimAttempts) == 1
                    ? throw new InvalidOperationException("Injected post-commit effect failure.")
                    : BaseCommandResponse.Success(Guid.CreateVersion7(), "reconciled");
            });
        var bootstrapRepository = Substitute.For<IInstanceBootstrapStateRepository>();
        bootstrapRepository.GetCurrent(Arg.Any<CancellationToken>()).Returns(
            InstanceBootstrapState.CreateConfiguredAdministratorPending(
                Guid.CreateVersion7(),
                AuthenticationProviderKind.Atproto,
                DeploymentMode.MultiTenant,
                1,
                new string('a', 64),
                new string('b', 64),
                DateTime.UtcNow));
        var tokenIssuer = Substitute.For<IAtprotoSessionTokenIssuer>();
        tokenIssuer.IssueAsync(userId, Arg.Any<Guid>(), did, Arg.Any<CancellationToken>())
            .Returns(new AtprotoIssuedSessionToken("platform-token", DateTimeOffset.UtcNow.AddMinutes(5)));
        BootstrapAtprotoSessionCommandHandler handler = CreateAtprotoHandler(
            gateway,
            tokenIssuer,
            sender,
            logins,
            out _,
            bootstrapRepository);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(CreateBootstrapCommand(did), CancellationToken.None));
        AtprotoSessionBootstrapResult retry = await handler.Handle(
            CreateBootstrapCommand(did),
            CancellationToken.None);

        await Assert.That(retry.Success).IsTrue();
        await Assert.That(claimAttempts).IsEqualTo(2);
        await Assert.That(lastClaim).IsNotNull();
        await Assert.That(lastClaim!.AuthenticatedAccount)
            .IsEqualTo(PlatformIdentityPrincipalExtensions.CreateAtprotoAccountKey(did));
        await Assert.That(lastClaim.UserId).IsEqualTo(userId);
    }

    [Test]
    public async Task BootstrapAtprotoSession_NormalLinkedLogin_DoesNotInvokeConfiguredClaim()
    {
        AtprotoDid did = AtprotoDid.Parse($"did:plc:{Guid.NewGuid():N}");
        Guid userId = Guid.CreateVersion7();
        var verified = new AtprotoVerifiedOAuthSession(
            did,
            "canonical.example.test",
            new Uri("https://pds.example.test"),
            "oauth-key",
            new byte[] { 1 });
        var gateway = Substitute.For<IAtprotoOAuthSecurityGateway>();
        gateway.VerifyAsync(Arg.Any<AtprotoOAuthVerificationInput>(), Arg.Any<CancellationToken>())
            .Returns(AtprotoOAuthVerificationResult.Verified(verified));
        gateway.PreparePersistenceAsync(
                verified,
                Arg.Any<Guid>(),
                userId,
                Arg.Any<CancellationToken>())
            .Returns(call => new AtprotoPreparedOAuthSession(
                new byte[] { 2 },
                "encryption-key",
                1,
                call.ArgAt<Guid>(1),
                userId,
                did,
                "https://pds.example.test/",
                "oauth-key",
                null));
        var login = new UserExternalLogin { Id = Guid.CreateVersion7(),
        UserId = userId,
        User = null!,
        AuthenticationProviderId = (int)"atproto".ParseAuthenticationProviderKind(), AuthenticationProvider = null!, ProviderKey = did.Value,
        ProviderDisplayName = "AT Protocol" };
        var logins = Substitute.For<IUserExternalLoginRepository>();
        logins.GetByProviderAndKey(Arg.Any<ProviderAccountKey>()).Returns(login);
        var sender = Substitute.For<ISender>();
        var tokenIssuer = Substitute.For<IAtprotoSessionTokenIssuer>();
        tokenIssuer.IssueAsync(userId, Arg.Any<Guid>(), did, Arg.Any<CancellationToken>())
            .Returns(new AtprotoIssuedSessionToken("platform-token", DateTimeOffset.UtcNow.AddMinutes(5)));
        BootstrapAtprotoSessionCommandHandler handler = CreateAtprotoHandler(
            gateway,
            tokenIssuer,
            sender,
            logins,
            out _);

        AtprotoSessionBootstrapResult result = await handler.Handle(
            CreateBootstrapCommand(did),
            CancellationToken.None);

        await Assert.That(result.Success).IsTrue();
        await sender.DidNotReceiveWithAnyArgs().Send(
            default(ClaimConfiguredInstanceAdministratorCommand)!,
            default);
    }

    [Test]
    public async Task BootstrapAtprotoSession_LinkedLoginIsRejectedWhenAtprotoIsDisabled()
    {
        AtprotoDid did = AtprotoDid.Parse($"did:plc:{Guid.NewGuid():N}");
        Guid userId = Guid.CreateVersion7();
        var verified = new AtprotoVerifiedOAuthSession(
            did,
            "canonical.example.test",
            new Uri("https://pds.example.test"),
            "oauth-key",
            new byte[] { 1 });
        var gateway = Substitute.For<IAtprotoOAuthSecurityGateway>();
        gateway.VerifyAsync(
                Arg.Any<AtprotoOAuthVerificationInput>(),
                Arg.Any<CancellationToken>())
            .Returns(AtprotoOAuthVerificationResult.Verified(verified));
        var logins = Substitute.For<IUserExternalLoginRepository>();
        logins.GetByProviderAndKey(Arg.Any<ProviderAccountKey>())
            .Returns(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                User = null!,
                AuthenticationProviderId =
                    (int)AuthenticationProviderKind.Atproto,
                AuthenticationProvider = null!,
                ProviderKey = did.Value,
                ProviderDisplayName = "AT Protocol"
            });
        var dispatcher = Substitute.For<IAuthenticationProviderDispatcher>();
        dispatcher.GetActivePrimaryProviderAsync(
                Arg.Any<CancellationToken>())
            .Returns(AuthenticationProviderKind.Local);
        var configuration =
            Substitute.For<IAuthProviderConfigurationService>();
        configuration.ReadConfigurationAsync()
            .Returns(new AuthProviderConfigurationDto
            {
                AtprotoLoginEnabled = false
            });
        BootstrapAtprotoSessionCommandHandler handler = CreateAtprotoHandler(
            gateway,
            Substitute.For<IAtprotoSessionTokenIssuer>(),
            Substitute.For<ISender>(),
            logins,
            out _,
            authenticationProviderDispatcher: dispatcher,
            authProviderConfiguration: configuration);

        AtprotoSessionBootstrapResult result = await handler.Handle(
            CreateBootstrapCommand(did),
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("provider_inactive");
        await gateway.DidNotReceiveWithAnyArgs()
            .PreparePersistenceAsync(default!, default, default, default);
    }

    private static BootstrapAtprotoSessionCommand CreateBootstrapCommand(AtprotoDid did) =>
        new(
            did,
            "https://pds.example.test/",
            "oauth-key",
            AtprotoSubjectClassification.Person,
            new byte[] { 1 });

    private static BootstrapAtprotoSessionCommandHandler CreateAtprotoHandler(
        IAtprotoOAuthSecurityGateway gateway,
        IAtprotoSessionTokenIssuer tokenIssuer,
        ISender sender,
        IUserExternalLoginRepository logins,
        out IUnitOfWork unitOfWork,
        IInstanceBootstrapStateRepository? bootstrapRepository = null,
        IAuthenticationProviderDispatcher?
            authenticationProviderDispatcher = null,
        IAuthProviderConfigurationService?
            authProviderConfiguration = null)
    {
        var users = Substitute.For<IUserRepository>();
        users.GetById(Arg.Any<Guid>()).Returns(call => new User { Id = call.Arg<Guid>(), Pii = new UserPii
        {
            UserId = call.Arg<Guid>(),
            Email = "atproto@example.test",
            FirstName = "ATProto",
            LastName = "User"
        } });
        var actors = Substitute.For<IActorRepository>();
        actors.GetTrackedActorByUserId(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => new Actor
            {
                Id = Guid.CreateVersion7(),
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                UserId = call.ArgAt<Guid>(0),
                Pii = new ActorPii { DisplayName = "ATProto User" }
            });
        var identities = Substitute.For<IAtprotoIdentityRepository>();
        identities.GetByDid(Arg.Any<AtprotoDid>(), Arg.Any<CancellationToken>())
            .Returns((AtprotoIdentity?)null);
        identities.Create(Arg.Any<AtprotoIdentity>()).Returns(call => call.Arg<AtprotoIdentity>());
        var tenantUsers = Substitute.For<ITenantUserRepository>();
        tenantUsers.GetByTenantAndUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TenantUser?)null);
        tenantUsers.Create(Arg.Any<TenantUser>()).Returns(call =>
        {
            TenantUser value = call.Arg<TenantUser>();
            value.Id = Guid.CreateVersion7();
            return value;
        });
        var tenantRoles = Substitute.For<ITenantUserRoleGrantRepository>();
        tenantRoles.IsTenantAdminInCurrentTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetById(PlatformDefaults.DefaultTenantId).Returns(new Tenant
        {
            Id = PlatformDefaults.DefaultTenantId,
            FullName = "ATProto tenant",
            Slug = "atproto",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        });
        var onboarding = new AtprotoSubjectOnboardingOperation(
            logins,
            identities,
            actors,
            Substitute.For<IActorTypeRepository>(),
            tenants,
            tenantUsers,
            tenantRoles,
            Substitute.For<IOrganizationRepository>(),
            Substitute.For<IOrganizationTenantRepository>(),
            Substitute.For<IOrganizationMemberRepository>(),
            Substitute.For<IGroupRepository>(),
            Substitute.For<IGroupTenantRepository>(),
            Substitute.For<IGroupMemberRepository>(),
            Substitute.For<IActorReferenceConsolidationRepository>(),
            Substitute.For<IGenericRepository<ActorMerge, Guid>>());
        unitOfWork = new InlineUnitOfWork();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(PlatformDefaults.DefaultTenantId);
        var configuration = new ConfigurationBuilder().Build();
        if (authProviderConfiguration is null)
        {
            authProviderConfiguration =
                Substitute.For<IAuthProviderConfigurationService>();
            authProviderConfiguration.ReadConfigurationAsync()
                .Returns(new AuthProviderConfigurationDto
            {
                AtprotoLoginEnabled = true
            });
        }

        return new BootstrapAtprotoSessionCommandHandler(
            gateway,
            tokenIssuer,
            sender,
            logins,
            bootstrapRepository ?? Substitute.For<IInstanceBootstrapStateRepository>(),
            authenticationProviderDispatcher
            ?? Substitute.For<IAuthenticationProviderDispatcher>(),
            authProviderConfiguration,
            new AtprotoJitAccountProvisioningOperation(
                users,
                actors,
                logins),
            onboarding,
            unitOfWork,
            Substitute.For<IAdminCacheInvalidator>(),
            tenantContext,
            configuration,
            TimeProvider.System);
    }

    private async Task EnsureUserExistsAsync(Guid userId, string? email = null)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

        if (await dbContext.Users.AnyAsync(x => x.Id == userId))
        {
            return;
        }

        dbContext.Users.Add(new User { Id = userId, CreatedAt = DateTime.UtcNow,
        CreatedBy = userId,
        Pii = new UserPii
        {
            UserId = userId,
            Email = email ?? $"{userId:N}@integration.test",
            FirstName = "Integration",
            LastName = "User"
        } });

        await dbContext.SaveChangesAsync();
    }

    private async Task SeedExternalLoginAsync(Guid userId, string provider, string providerKey)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

        dbContext.UserExternalLogins.Add(new UserExternalLogin { Id = Guid.NewGuid(),
        UserId = userId,
        User = null!,
        AuthenticationProviderId = (int)provider.ParseAuthenticationProviderKind(), AuthenticationProvider = null!, ProviderKey = providerKey,
        ProviderDisplayName = provider,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = userId });
        await dbContext.SaveChangesAsync();
    }

    private static string CreateCustomAuthHeader(params (string Type, string Value)[] claims)
    {
        var payload = claims.Select(claim => new TestClaimPayload { Type = claim.Type, Value = claim.Value }).ToList();
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
    }

    private sealed class InlineUnitOfWork : IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation, CancellationToken ct = default) => operation(ct);

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);

        public Task<T> ExecuteSerializableAsync<T>(
            Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
    }

    private sealed class TestClaimPayload
    {
        public string Type { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
