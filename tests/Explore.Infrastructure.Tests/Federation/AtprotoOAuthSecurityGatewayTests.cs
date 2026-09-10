using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Storage;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.Authentication.Atproto.Handlers.Commands;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Services;
using Explore.Application.Services;
using Explore.Atproto.Transport;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Services.Federation;
using Explore.Persistence.Repositories;
using Explore.Tests.Shared.Settings;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Federation;

public sealed class AtprotoOAuthSecurityGatewayTests
{
    private const string Did = "did:plc:gateway-user";
    private static readonly AtprotoDid ParsedDid = AtprotoDid.Parse(Did);
    private const string Pds = "https://pds.example/";
    private const string Issuer = "https://issuer.example/";
    private const string OAuthKeyId = "oauth-active";
    private const string AccessToken = "gateway-access-canary";

    [Test]
    public async Task VerifiedSessionPersistsEncryptedAndRestoresThroughRealCarpaFactoryAndPdsXrpc()
    {
        var fixture = CreateFixture(Did);
        var session = CreateSession();

        var verified = await fixture.Gateway.VerifyAsync(new AtprotoOAuthVerificationInput(
            ParsedDid,
            new Uri(Pds),
            OAuthKeyId,
            JsonSerializer.SerializeToUtf8Bytes(session)), CancellationToken.None);

        await Assert.That(fixture.Transport.MetadataRequests).IsEqualTo(1);
        await Assert.That(fixture.Transport.PdsRequests).IsEqualTo(1);
        await Assert.That(verified.FailureCode).IsNull();
        await Assert.That(verified.Session).IsNotNull();
        await Assert.That(verified.Session!.Did.Value).IsEqualTo(Did);
        await Assert.That(verified.Session.PdsUri.AbsoluteUri).IsEqualTo(Pds);
        await Assert.That(fixture.Transport.LastPdsPath)
            .IsEqualTo("/xrpc/com.atproto.server.getSession");
        await Assert.That(fixture.Transport.LastAuthorizationScheme).IsEqualTo("DPoP");
        await Assert.That(fixture.Transport.SawDpopProof).IsTrue();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var prepared = await fixture.Gateway.PreparePersistenceAsync(
            verified.Session,
            tenantId,
            userId,
            CancellationToken.None);
        await fixture.Gateway.PersistPreparedAsync(prepared, CancellationToken.None);
        var row = fixture.GetPersistedRow() ?? throw new InvalidOperationException("Encrypted session row was not stored.");
        await Assert.That(Encoding.UTF8.GetString(row.SessionCiphertext)).DoesNotContain(AccessToken);

        var repositoryStore = new RepositoryBackedOAuthSessionStore(
            fixture.TokenRepository,
            fixture.Protector,
            new AtprotoOAuthSessionStoreContext(
                tenantId,
                userId,
                Explore.Domain.ValueObjects.AtprotoDid.Parse(Did),
                new Uri(Pds),
                OAuthKeyId));
        using var lease = await fixture.CoreFactory.CreateAsync(
            Did,
            OAuthKeyId,
            repositoryStore,
            CancellationToken.None);
        var restoredPdsSession = await lease.Client.GetAsync<InfrastructureAtprotoGetSessionResponse>(
            "com.atproto.server.getSession",
            cancellationToken: CancellationToken.None);

        await Assert.That(lease.Client.AuthenticatedDid).IsEqualTo(Did);
        await Assert.That(lease.Client.BaseUrl.AbsoluteUri).IsEqualTo(Pds);
        await Assert.That(restoredPdsSession.Did).IsEqualTo(Did);
        await Assert.That(fixture.Transport.MetadataRequests).IsEqualTo(2);
        await Assert.That(fixture.Transport.PdsRequests).IsEqualTo(2);
    }

    [Test]
    public async Task PdsDidMismatchReturnsTypedFailureBeforeIdentityOrSessionWrites()
    {
        var fixture = CreateFixture("did:plc:substituted-user");
        await using var visitorSettings = await SmtpSettingsDatabase.CreateAsync();
        var configuration = new ConfigurationBuilder().Build();
        var systemSettings = new SystemSettingRepository(visitorSettings.Context, visitorSettings.MutationLock);
        var externalLogins = Substitute.For<IUserExternalLoginRepository>();
        var users = Substitute.For<IUserRepository>();
        var actors = Substitute.For<IActorRepository>();
        var atprotoIdentities = Substitute.For<IAtprotoIdentityRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var tokenIssuer = Substitute.For<IAtprotoSessionTokenIssuer>();
        var sender = Substitute.For<ISender>();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(Guid.NewGuid());
        var handler = new BootstrapAtprotoSessionCommandHandler(
            fixture.Gateway,
            tokenIssuer,
            sender,
            externalLogins,
            Substitute.For<IInstanceBootstrapStateRepository>(),
            Substitute.For<IAuthenticationProviderDispatcher>(),
            Substitute.For<IAuthProviderConfigurationService>(),
            new AtprotoJitAccountProvisioningOperation(
                users,
                actors,
                externalLogins),
            new AtprotoSubjectOnboardingOperation(
                externalLogins,
                atprotoIdentities,
                actors,
                Substitute.For<IActorTypeRepository>(),
                Substitute.For<ITenantRepository>(),
                Substitute.For<ITenantUserRepository>(),
                Substitute.For<ITenantUserRoleGrantRepository>(),
                Substitute.For<IOrganizationRepository>(),
                Substitute.For<IOrganizationTenantRepository>(),
                Substitute.For<IOrganizationMemberRepository>(),
                Substitute.For<IGroupRepository>(),
                Substitute.For<IGroupTenantRepository>(),
                Substitute.For<IGroupMemberRepository>(),
                Substitute.For<IActorReferenceConsolidationRepository>(),
                Substitute.For<IGenericRepository<ActorMerge, Guid>>()),
            unitOfWork,
            visitorSettings.MutationLock,
            new VisitorAccessCapabilityResolver(
                systemSettings,
                new TenantSettingRepository(visitorSettings.Context, visitorSettings.MutationLock),
                new VisitorAccessProviderReader(systemSettings, configuration)),
            Substitute.For<IAdminCacheInvalidator>(),
            tenantContext,
            configuration,
            TimeProvider.System);
        var payload = JsonSerializer.SerializeToUtf8Bytes(CreateSession());

        var result = await handler.Handle(new BootstrapAtprotoSessionCommand(
            ParsedDid,
            Pds,
            OAuthKeyId,
            AtprotoSubjectClassification.Person,
            payload), CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("pds_identity_mismatch");
        await Assert.That(fixture.Transport.PdsRequests).IsEqualTo(1);
        await externalLogins.DidNotReceiveWithAnyArgs().GetByProviderAndKey(default!);
        await actors.DidNotReceiveWithAnyArgs().Update(default!);
        await atprotoIdentities.DidNotReceiveWithAnyArgs().Create(default!);
        await fixture.TokenRepository.DidNotReceiveWithAnyArgs()
            .CreateAtprotoSessionAsync(default!, default);
        await tokenIssuer.DidNotReceiveWithAnyArgs().IssueAsync(default, default, default!, default);
    }

    [Test]
    public async Task RefreshPersistsCarpaTokenRotationBeforeReturningSuccess()
    {
        var fixture = CreateFixture(Did);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var expired = CreateSession();
        expired.TokenSet.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await PersistCurrentSessionAsync(fixture, tenantId, userId, expired);

        var result = await fixture.Gateway.RefreshAsync(
            new AtprotoCurrentSessionIdentity(tenantId, userId, ParsedDid),
            CancellationToken.None);

        await Assert.That(fixture.Transport.TokenRequests).IsEqualTo(1);
        await Assert.That(fixture.Transport.MetadataRequests).IsEqualTo(1);
        await Assert.That(fixture.Transport.PdsRequests).IsEqualTo(1);
        await Assert.That(result.Success).IsTrue();
        var restored = await new RepositoryBackedOAuthSessionStore(
                fixture.TokenRepository,
                fixture.Protector,
                new AtprotoOAuthSessionStoreContext(
                    tenantId,
                    userId,
                    Explore.Domain.ValueObjects.AtprotoDid.Parse(Did),
                    new Uri(Pds),
                    OAuthKeyId))
            .GetAsync(Did, CancellationToken.None);
        await Assert.That(restored!.TokenSet.AccessToken).IsEqualTo("rotated-access-canary");
        await Assert.That(restored.TokenSet.RefreshToken).IsEqualTo("rotated-refresh-canary");
    }

    [Test]
    public async Task DeliveryRestoresBoundSessionAndWritesRecord()
    {
        var fixture = CreateFixture(Did);
        fixture.Transport.DeliverRecord = true;
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await PersistCurrentSessionAsync(fixture, tenantId, userId);

        AtprotoPdsDeliveryResult result = await CreateDeliveryGateway(
                fixture,
                new BlockingRefreshLock(released: true))
            .DeliverAsync(CreateDeliveryRequest(tenantId, userId), CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Uri).IsEqualTo($"at://{Did}/app.bsky.feed.post/3kdeliverytest");
        await Assert.That(result.Cid).IsEqualTo("bafydeliverycid");
        await Assert.That(fixture.Transport.PdsRequests).IsEqualTo(2);
        await Assert.That(fixture.Transport.TokenRequests).IsEqualTo(0);
    }

    [Test]
    public async Task DeliveryExpiredSessionAndChallengeSerializeRefreshAndRereadDurableRotation()
    {
        var fixture = CreateFixture(Did);
        fixture.Transport.DeliverRecord = true;
        fixture.Transport.ChallengeFirstPdsRequest = true;
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        OAuthSessionData expired = CreateSession();
        expired.TokenSet.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await PersistCurrentSessionAsync(fixture, tenantId, userId, expired);
        var refreshLock = new BlockingRefreshLock(released: false);
        fixture.Transport.RefreshLeaseHeld = () => refreshLock.IsHeld;
        AtprotoPdsDeliveryGateway gateway = CreateDeliveryGateway(fixture, refreshLock);

        Task<AtprotoPdsDeliveryResult> delivery = gateway.DeliverAsync(
            CreateDeliveryRequest(tenantId, userId),
            CancellationToken.None);
        await refreshLock.Entered.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.That(fixture.Transport.PdsRequests).IsEqualTo(0);
        refreshLock.Release();
        AtprotoPdsDeliveryResult result = await delivery.WaitAsync(TimeSpan.FromSeconds(5));
        OAuthSessionData? restored = await new RepositoryBackedOAuthSessionStore(
                fixture.TokenRepository,
                fixture.Protector,
                new AtprotoOAuthSessionStoreContext(
                    tenantId,
                    userId,
                    Explore.Domain.ValueObjects.AtprotoDid.Parse(Did),
                    new Uri(Pds),
                    OAuthKeyId))
            .GetAsync(Did, CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(fixture.Transport.PdsRequests).IsEqualTo(3);
        await Assert.That(fixture.Transport.TokenRequests).IsEqualTo(1);
        await Assert.That(fixture.Transport.TokenRequestOutsideRefreshLease).IsFalse();
        await Assert.That(restored!.TokenSet.AccessToken).IsEqualTo("rotated-access-canary");
        await Assert.That(restored.TokenSet.RefreshToken).IsEqualTo("rotated-refresh-canary");
        await Assert.That(refreshLock.IsHeld).IsFalse();
    }

    [Test]
    public async Task DeliveryRefreshPersistenceFailureReturnsNoSuccessOrPdsWrite()
    {
        var fixture = CreateFixture(Did);
        fixture.Transport.DeliverRecord = true;
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        OAuthSessionData expired = CreateSession();
        expired.TokenSet.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await PersistCurrentSessionAsync(fixture, tenantId, userId, expired);
        fixture.TokenRepository.UpdateAtprotoSessionAsync(
                Arg.Any<UserAuthenticationToken>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("bounded persistence failure"));

        AtprotoPdsDeliveryResult result = await CreateDeliveryGateway(
                fixture,
                new BlockingRefreshLock(released: true))
            .DeliverAsync(CreateDeliveryRequest(tenantId, userId), CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("session_unavailable");
        await Assert.That(fixture.Transport.TokenRequests).IsEqualTo(1);
        await Assert.That(fixture.Transport.PdsRequests).IsEqualTo(0);
    }

    [Test]
    public async Task DeliveryCancellationWhileWaitingReleasesScopeForNextAttempt()
    {
        var fixture = CreateFixture(Did);
        fixture.Transport.DeliverRecord = true;
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await PersistCurrentSessionAsync(fixture, tenantId, userId);
        var refreshLock = new BlockingRefreshLock(released: false);
        AtprotoPdsDeliveryGateway gateway = CreateDeliveryGateway(fixture, refreshLock);
        using var cancellation = new CancellationTokenSource();

        Task<AtprotoPdsDeliveryResult> cancelled = gateway.DeliverAsync(
            CreateDeliveryRequest(tenantId, userId),
            cancellation.Token);
        await refreshLock.Entered.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.That(async () => await cancelled).Throws<OperationCanceledException>();
        await Assert.That(refreshLock.IsHeld).IsFalse();
        await Assert.That(fixture.Transport.PdsRequests).IsEqualTo(0);
        refreshLock.Release();
        AtprotoPdsDeliveryResult retried = await gateway.DeliverAsync(
            CreateDeliveryRequest(tenantId, userId),
            CancellationToken.None);
        await Assert.That(retried.Succeeded).IsTrue();
        await Assert.That(refreshLock.IsHeld).IsFalse();
    }

    [Test]
    public async Task MissingScopedRefreshSessionRequiresReauthenticationWithoutProviderCall()
    {
        var fixture = CreateFixture(Did);

        var result = await fixture.Gateway.RefreshAsync(new AtprotoCurrentSessionIdentity(
            Guid.NewGuid(), Guid.NewGuid(), ParsedDid), CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("reauthentication_required");
        await Assert.That(fixture.Transport.MetadataRequests).IsEqualTo(0);
        await Assert.That(fixture.Transport.PdsRequests).IsEqualTo(0);
    }

    [Test]
    public async Task RevokeUsesRealCarpaSignOutThenDeletesTheExactSessionIdempotently()
    {
        var fixture = CreateFixture(Did);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await PersistCurrentSessionAsync(fixture, tenantId, userId);
        var identity = new AtprotoCurrentSessionIdentity(tenantId, userId, ParsedDid);

        var first = await fixture.Gateway.RevokeCurrentAsync(identity, CancellationToken.None);
        var repeated = await fixture.Gateway.RevokeCurrentAsync(identity, CancellationToken.None);

        await Assert.That(first.Outcome).IsEqualTo(AtprotoSessionRevocationOutcome.Revoked);
        await Assert.That(repeated.Outcome).IsEqualTo(AtprotoSessionRevocationOutcome.AlreadyAbsent);
        await Assert.That(fixture.Transport.RevocationRequests).IsEqualTo(1);
        await Assert.That(fixture.GetPersistedRow()).IsNull();
    }

    [Test]
    public async Task RemoteOutageStillDeletesTheLocalSession()
    {
        var fixture = CreateFixture(Did);
        fixture.Transport.FailRevocation = true;
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await PersistCurrentSessionAsync(fixture, tenantId, userId);

        var result = await fixture.Gateway.RevokeCurrentAsync(
            new AtprotoCurrentSessionIdentity(tenantId, userId, ParsedDid),
            CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(AtprotoSessionRevocationOutcome.RemoteFailedLocalCleared);
        await Assert.That(fixture.Transport.RevocationRequests).IsEqualTo(1);
        await Assert.That(fixture.GetPersistedRow()).IsNull();
    }

    [Test]
    public async Task CallerCancellationStillDeletesTheLocalSession()
    {
        var fixture = CreateFixture(Did);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await PersistCurrentSessionAsync(fixture, tenantId, userId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await fixture.Gateway.RevokeCurrentAsync(
            new AtprotoCurrentSessionIdentity(tenantId, userId, ParsedDid),
            cancellation.Token);

        await Assert.That(result.Outcome)
            .IsEqualTo(AtprotoSessionRevocationOutcome.RemoteFailedLocalCleared);
        await Assert.That(fixture.GetPersistedRow()).IsNull();
    }

    [Test]
    public async Task DifferentTenantOrUserCannotRevokeThePersistedSession()
    {
        var fixture = CreateFixture(Did);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await PersistCurrentSessionAsync(fixture, tenantId, userId);

        var result = await fixture.Gateway.RevokeCurrentAsync(
            new AtprotoCurrentSessionIdentity(Guid.NewGuid(), Guid.NewGuid(), ParsedDid),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(AtprotoSessionRevocationOutcome.AlreadyAbsent);
        await Assert.That(fixture.Transport.RevocationRequests).IsEqualTo(0);
        await Assert.That(fixture.GetPersistedRow()).IsNotNull();
    }

    [Test]
    public async Task PreparedPersistenceResolvesTheEncryptionKeyOnceAndSafelyReusesCiphertext()
    {
        var fixture = CreateFixture(Did);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var prepared = await fixture.Gateway.PreparePersistenceAsync(
            new AtprotoVerifiedOAuthSession(
                Explore.Domain.ValueObjects.AtprotoDid.Parse(Did),
                "gateway-user.example",
                new Uri(Pds),
                OAuthKeyId,
                JsonSerializer.SerializeToUtf8Bytes(CreateSession())),
            tenantId,
            userId,
            CancellationToken.None);

        await fixture.Gateway.PersistPreparedAsync(prepared, CancellationToken.None);
        var firstCiphertext = (fixture.GetPersistedRow() ?? throw new InvalidOperationException("Encrypted session row was not stored."))
            .SessionCiphertext.ToArray();
        await fixture.Gateway.PersistPreparedAsync(prepared, CancellationToken.None);
        var persisted = fixture.GetPersistedRow() ?? throw new InvalidOperationException("Encrypted session row was not stored.");
        await Assert.That(persisted.SessionCiphertext).IsEquivalentTo(firstCiphertext);
        await fixture.SecretResolver.Received(1).ResolveAsync(
            SecretDefinitionRegistry.Keys.Atproto.SessionEncryptionKeyRing,
            null,
            Arg.Any<CancellationToken>());
        await fixture.TokenRepository.Received(1).CreateAtprotoSessionAsync(
            Arg.Any<UserAuthenticationToken>(),
            Arg.Any<CancellationToken>());
        await fixture.TokenRepository.Received(1).UpdateAtprotoSessionAsync(
            Arg.Any<UserAuthenticationToken>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task VerificationInputCarriesTypedDidWithoutDiagnosticDisclosure()
    {
        var input = new AtprotoOAuthVerificationInput(
            ParsedDid,
            new Uri(Pds),
            OAuthKeyId,
            JsonSerializer.SerializeToUtf8Bytes(CreateSession()));

        await Assert.That(input.ExpectedDid).IsEqualTo(ParsedDid);
        await Assert.That(input.ToString()).DoesNotContain(Did);
    }

    [Test]
    public async Task VerifyMalformedProviderReturnedDidFailsAsBoundedIdentityMismatch()
    {
        const string malformedProviderDid = "did:plc:provider-sentinel#raw";
        var fixture = CreateFixture(malformedProviderDid);

        AtprotoOAuthVerificationResult result = await fixture.Gateway.VerifyAsync(
            new AtprotoOAuthVerificationInput(
                ParsedDid,
                new Uri(Pds),
                OAuthKeyId,
                JsonSerializer.SerializeToUtf8Bytes(CreateSession())),
            CancellationToken.None);

        await Assert.That(result.FailureCode).IsEqualTo("pds_identity_mismatch");
        await Assert.That(result.FailureCode).DoesNotContain(malformedProviderDid);
        await Assert.That(fixture.Transport.PdsRequests).IsEqualTo(1);
    }

    [Test]
    [Arguments("did:plc:delivery-sentinel?raw")]
    [Arguments("did:deleted:0198ab00000070008000000000000001")]
    public async Task DeliveryMalformedDidFailsBeforeRefreshLockRepositoryOrProviderTransport(string malformedDid)
    {
        var fixture = CreateFixture(Did);
        var refreshLock = new BlockingRefreshLock(released: true);
        AtprotoPdsDeliveryGateway gateway = CreateDeliveryGateway(fixture, refreshLock);
        AtprotoPdsDeliveryRequest request = CreateDeliveryRequest(Guid.CreateVersion7(), Guid.CreateVersion7())
            with
        { Did = malformedDid };

        AtprotoPdsDeliveryResult result = await gateway.DeliverAsync(request, CancellationToken.None);

        await Assert.That(result.FailureCode).IsEqualTo("session_unavailable");
        await Assert.That(result.FailureCode).DoesNotContain(malformedDid);
        await Assert.That(refreshLock.Entered.IsCompleted).IsFalse();
        await Assert.That(fixture.Transport.MetadataRequests).IsEqualTo(0);
        await Assert.That(fixture.Transport.PdsRequests).IsEqualTo(0);
    }

    private static async Task PersistCurrentSessionAsync(
        GatewayFixture fixture,
        Guid tenantId,
        Guid userId,
        OAuthSessionData? session = null)
    {
        var prepared = await fixture.Gateway.PreparePersistenceAsync(
            new AtprotoVerifiedOAuthSession(
                Explore.Domain.ValueObjects.AtprotoDid.Parse(Did),
                "gateway-user.example",
                new Uri(Pds),
                OAuthKeyId,
                JsonSerializer.SerializeToUtf8Bytes(session ?? CreateSession())),
            tenantId,
            userId,
            CancellationToken.None);
        await fixture.Gateway.PersistPreparedAsync(prepared, CancellationToken.None);
    }

    private static AtprotoPdsDeliveryRequest CreateDeliveryRequest(Guid tenantId, Guid userId) => new(
        tenantId,
        userId,
        Did,
        new Uri(Pds),
        "app.bsky.feed.post",
        "3kdeliverytest",
        Explore.Domain.Federation.PdsSyncOperation.Create,
        "{\"$type\":\"app.bsky.feed.post\",\"text\":\"bounded delivery\"}",
        null);

    private static AtprotoPdsDeliveryGateway CreateDeliveryGateway(
        GatewayFixture fixture,
        IAtprotoSessionRefreshLock refreshLock)
    {
        return new AtprotoPdsDeliveryGateway(
            fixture.TokenRepository,
            fixture.Protector,
            fixture.CoreFactory,
            refreshLock);
    }

    private static GatewayFixture CreateFixture(string pdsResponseDid)
    {
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var key = call.ArgAt<string>(0);
                var value = key == SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks
                    ? CreatePrivateJwks()
                    : CreateEncryptionRing();
                return SecretResolutionResult.Resolved(new ResolvedSecret(
                    key,
                    value,
                    SecretSourceType.Infisical,
                    SecretScope.Instance,
                    null,
                    DateTimeOffset.UtcNow));
            });
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var transport = new RecordingPrimaryHandlerFactory(pdsResponseDid);
        var oauthFactory = new AtprotoOAuthClientFactory(
            resolver,
            Options.Create(new AtprotoInfrastructureOptions
            {
                PublicUrl = "https://events.example.com/",
                CallbackPath = "/signin-atproto"
            }),
            environment,
            transport.CreateOAuthPrimary);
        var coreFactory = new AtprotoCoreClientFactory(oauthFactory, transport);
        var tokenRepository = Substitute.For<IUserAuthenticationTokenRepository>();
        UserAuthenticationToken? persistedRow = null;
        tokenRepository.GetAtprotoSessionForUpdateAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                "atproto",
                Did,
                Arg.Any<CancellationToken>())
            .Returns(call => IsExactScope(call, persistedRow) ? persistedRow : null);
        tokenRepository.GetAtprotoSessionForReadAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                "atproto",
                Did,
                Arg.Any<CancellationToken>())
            .Returns(call => IsExactScope(call, persistedRow) ? persistedRow : null);
        tokenRepository.CreateAtprotoSessionAsync(
                Arg.Any<UserAuthenticationToken>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                persistedRow = call.Arg<UserAuthenticationToken>();
                return Task.FromResult(persistedRow!);
            });
        tokenRepository.UpdateAtprotoSessionAsync(
                Arg.Any<UserAuthenticationToken>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => persistedRow = call.Arg<UserAuthenticationToken>());
        tokenRepository.DeleteAtprotoSessionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                "atproto",
                Did,
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (IsExactScope(call, persistedRow))
                {
                    persistedRow = null;
                }

                return Task.CompletedTask;
            });
        var protector = new AtprotoSessionEnvelopeProtector(resolver);
        var refreshLock = Substitute.For<IAtprotoSessionRefreshLock>();
        refreshLock.AcquireAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                "atproto",
                Did,
                Arg.Any<CancellationToken>())
            .Returns(new NoopAsyncDisposable());
        return new(
            new AtprotoOAuthSecurityGateway(
                coreFactory,
                oauthFactory,
                tokenRepository,
                protector,
            refreshLock,
            Substitute.For<ILogger<AtprotoOAuthSecurityGateway>>()),
            resolver,
            coreFactory,
            protector,
            tokenRepository,
            transport,
            () => persistedRow);
    }

    private static bool IsExactScope(NSubstitute.Core.CallInfo call, UserAuthenticationToken? row) =>
        row is not null
        && call.ArgAt<Guid>(0) == row.TenantId
        && call.ArgAt<Guid>(1) == row.UserId;

    private static OAuthSessionData CreateSession()
    {
        using var dpopKey = DPoPKeyPair.Generate();
        return new OAuthSessionData
        {
            DPoPKey = dpopKey.ExportKeyPair(),
            AuthMethod = "private_key_jwt",
            TokenSet = new TokenSet
            {
                Issuer = Issuer,
                Sub = Did,
                Audience = Pds,
                Scope = InfrastructureAtprotoOAuthTransportFactory.RequiredScope,
                AccessToken = AccessToken,
                RefreshToken = "gateway-refresh-canary",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            },
            ClientId = "https://events.example.com/oauth/client-metadata.json",
            RedirectUri = "https://events.example.com/signin-atproto",
            Scope = InfrastructureAtprotoOAuthTransportFactory.RequiredScope
        };
    }

    private static string CreatePrivateJwks()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "EC",
                    crv = "P-256",
                    x = Base64Url(parameters.Q.X!),
                    y = Base64Url(parameters.Q.Y!),
                    d = Base64Url(parameters.D!),
                    kid = OAuthKeyId,
                    use = "sig",
                    alg = "ES256",
                    status = "active"
                }
            }
        });
    }

    private static string CreateEncryptionRing() => JsonSerializer.Serialize(new
    {
        keys = new[]
        {
            new
            {
                kid = "encryption-active",
                k = Base64Url(Enumerable.Repeat((byte)7, 32).ToArray()),
                status = "active"
            }
        }
    });

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record GatewayFixture(
        AtprotoOAuthSecurityGateway Gateway,
        ISecretResolver SecretResolver,
        AtprotoCoreClientFactory CoreFactory,
        AtprotoSessionEnvelopeProtector Protector,
        IUserAuthenticationTokenRepository TokenRepository,
        RecordingPrimaryHandlerFactory Transport,
        Func<UserAuthenticationToken?> GetPersistedRow);

    private sealed class RecordingPrimaryHandlerFactory(string pdsResponseDid) : IAtprotoCorePrimaryHandlerFactory
    {
        public int MetadataRequests { get; private set; }
        public int TokenRequests { get; private set; }
        public int RevocationRequests { get; private set; }
        public int PdsRequests { get; private set; }
        public bool FailRevocation { get; set; }
        public bool DeliverRecord { get; set; }
        public bool ChallengeFirstPdsRequest { get; set; }
        public Func<bool>? RefreshLeaseHeld { get; set; }
        public bool TokenRequestOutsideRefreshLease { get; private set; }
        public string? LastPdsPath { get; private set; }
        public string? LastAuthorizationScheme { get; private set; }
        public bool SawDpopProof { get; private set; }

        public HttpMessageHandler CreateOAuthPrimary(AtprotoOutboundPolicy policy) =>
            new RecordingHandler(request =>
            {
                if (request.RequestUri?.AbsolutePath == "/oauth/token")
                {
                    TokenRequests++;
                    TokenRequestOutsideRefreshLease = RefreshLeaseHeld is not null && !RefreshLeaseHeld();
                    var response = JsonResponse(JsonSerializer.Serialize(new
                    {
                        access_token = "rotated-access-canary",
                        token_type = "DPoP",
                        expires_in = 3600,
                        refresh_token = "rotated-refresh-canary",
                        scope = InfrastructureAtprotoOAuthTransportFactory.RequiredScope,
                        sub = Did
                    }));
                    response.Headers.Add("DPoP-Nonce", "refresh-response-nonce");
                    return response;
                }

                if (request.RequestUri?.AbsolutePath == "/oauth/revoke")
                {
                    RevocationRequests++;
                    if (FailRevocation)
                    {
                        throw new HttpRequestException("bounded revocation outage");
                    }

                    return new HttpResponseMessage(HttpStatusCode.OK);
                }

                MetadataRequests++;
                return JsonResponse(AuthorizationServerMetadata());
            });

        public HttpMessageHandler CreatePdsPrimary(AtprotoOutboundPolicy policy) =>
            new RecordingHandler(request =>
            {
                PdsRequests++;
                LastPdsPath = request.RequestUri?.AbsolutePath;
                LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
                SawDpopProof = request.Headers.Contains("DPoP");
                if (DeliverRecord)
                {
                    if (ChallengeFirstPdsRequest && PdsRequests == 1)
                    {
                        return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                        {
                            Content = new StringContent(
                                "{\"error\":\"ExpiredToken\"}",
                                Encoding.UTF8,
                                "application/json")
                        };
                    }

                    if (request.Method == HttpMethod.Get)
                    {
                        return new HttpResponseMessage(HttpStatusCode.NotFound)
                        {
                            Content = new StringContent(
                                "{\"error\":\"RecordNotFound\"}",
                                Encoding.UTF8,
                                "application/json")
                        };
                    }

                    return JsonResponse(JsonSerializer.Serialize(new
                    {
                        uri = $"at://{Did}/app.bsky.feed.post/3kdeliverytest",
                        cid = "bafydeliverycid"
                    }));
                }

                return JsonResponse(JsonSerializer.Serialize(new
                {
                    did = pdsResponseDid,
                    handle = "gateway-user.example",
                    active = true
                }));
            });

        private static string AuthorizationServerMetadata() => $$"""
            {
              "issuer": "{{Issuer}}",
              "authorization_endpoint": "{{Issuer}}oauth/authorize",
              "token_endpoint": "{{Issuer}}oauth/token",
              "pushed_authorization_request_endpoint": "{{Issuer}}oauth/par",
              "revocation_endpoint": "{{Issuer}}oauth/revoke",
              "require_pushed_authorization_requests": true,
              "token_endpoint_auth_methods_supported": ["private_key_jwt"],
              "token_endpoint_auth_signing_alg_values_supported": ["ES256"],
              "dpop_signing_alg_values_supported": ["ES256"],
              "grant_types_supported": ["authorization_code", "refresh_token"],
              "response_types_supported": ["code"],
              "code_challenge_methods_supported": ["S256"],
              "authorization_response_iss_parameter_supported": true,
              "client_id_metadata_document_supported": true,
              "scopes_supported": ["atproto", "transition:generic"]
            }
            """;

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingRefreshLock : IAtprotoSessionRefreshLock
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingRefreshLock(bool released)
        {
            if (released)
            {
                _release.SetResult();
            }
        }

        public Task Entered => _entered.Task;
        public bool IsHeld { get; private set; }

        public async Task<IAsyncDisposable> AcquireAsync(
            Guid tenantId,
            Guid userId,
            string provider,
            string subjectDid,
            CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            IsHeld = true;
            return new ActionAsyncDisposable(() => IsHeld = false);
        }

        public void Release() => _release.TrySetResult();

        private sealed class ActionAsyncDisposable(Action dispose) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
