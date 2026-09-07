using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Operations;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.API.IntegrationTests.Authentication;

[ClassDataSource<AtprotoSoleProviderFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("SecurityInfra")]
public sealed class AtprotoSoleProviderInvariantTests(
    AtprotoSoleProviderFixture fixture)
{
    [Test]
    public async Task UnlinkedDidOnFreshAtprotoInstanceCreatesOnePasswordlessPlatformAccount()
    {
        await fixture.ResetDatabaseAsync();
        await using AsyncServiceScope scope =
            fixture.Factory.Services.CreateAsyncScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        AtprotoDid did = AtprotoDid.Parse(
            $"did:plc:{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}");

        AtprotoSessionBootstrapResult result = await sender.Send(
            CreateBootstrapCommand(did),
            CancellationToken.None);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.UserId).IsNotNull();
        await Assert.That(result.ActorId).IsNotNull();

        ExploreDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        dbContext.ChangeTracker.Clear();
        await Assert.That(await dbContext.Users.CountAsync()).IsEqualTo(1);
        await Assert.That(await dbContext.Actors.CountAsync()).IsEqualTo(1);
        await Assert.That(await dbContext.UserExternalLogins.CountAsync(login =>
                login.AuthenticationProviderId
                    == (int)AuthenticationProviderKind.Atproto
                && login.ProviderKey == did.Value))
            .IsEqualTo(1);
        await Assert.That(await dbContext.LocalIdentityUsers.CountAsync())
            .IsEqualTo(0);
        await Assert.That(fixture.PersistedAtprotoSessionCount)
            .IsEqualTo(0);
    }

    [Test]
    public async Task LocalCredentialEndpointsRejectWithoutCreatingPasswordRecords()
    {
        await fixture.ResetDatabaseAsync();
        string password = CreateOpaqueValue();
        string email =
            $"{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}@example.test";

        HttpResponseMessage login = await fixture.Client.PostAsJsonAsync(
            "/api/auth/local/login",
            new LocalAuthRequestDto(email, password));
        HttpResponseMessage registration = await fixture.Client.PostAsJsonAsync(
            "/api/auth/local/register",
            new LocalRegistrationRequestDto(
                email,
                password,
                "Test",
                "Administrator"));

        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(registration.StatusCode)
            .IsEqualTo(HttpStatusCode.Conflict);

        await using AsyncServiceScope scope =
            fixture.Factory.Services.CreateAsyncScope();
        ExploreDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await dbContext.LocalIdentityUsers.CountAsync())
            .IsEqualTo(0);
        await Assert.That(fixture.PersistedAtprotoSessionCount)
            .IsEqualTo(0);
    }

    [Test]
    public async Task DistinctUnlinkedDidsCanOwnDistinctAccountsWithoutEmailAddresses()
    {
        await fixture.ResetDatabaseAsync();
        await using AsyncServiceScope scope =
            fixture.Factory.Services.CreateAsyncScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        AtprotoDid firstDid = AtprotoDid.Parse(
            $"did:plc:{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}");
        AtprotoDid secondDid = AtprotoDid.Parse(
            $"did:plc:{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}");

        AtprotoSessionBootstrapResult first = await sender.Send(
            CreateBootstrapCommand(firstDid),
            CancellationToken.None);
        AtprotoSessionBootstrapResult second = await sender.Send(
            CreateBootstrapCommand(secondDid),
            CancellationToken.None);

        await Assert.That(first.Success).IsTrue();
        await Assert.That(second.Success).IsTrue();
        ExploreDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        dbContext.ChangeTracker.Clear();
        await Assert.That(await dbContext.UserPii.CountAsync(
                pii => pii.Email == string.Empty))
            .IsEqualTo(2);
        await Assert.That(await dbContext.UserExternalLogins.CountAsync(
                login => login.AuthenticationProviderId
                         == (int)AuthenticationProviderKind.Atproto))
            .IsEqualTo(2);
        await Assert.That(await dbContext.LocalIdentityUsers.CountAsync())
            .IsEqualTo(0);
        await Assert.That(fixture.PersistedAtprotoSessionCount)
            .IsEqualTo(0);
    }

    [Test]
    public async Task ConcurrentFirstLoginForSameDidConvergesToOneAccount()
    {
        await fixture.ResetDatabaseAsync();
        AtprotoDid did = AtprotoDid.Parse(
            $"did:plc:{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}");
        await using AsyncServiceScope firstScope =
            fixture.Factory.Services.CreateAsyncScope();
        await using AsyncServiceScope secondScope =
            fixture.Factory.Services.CreateAsyncScope();
        ISender firstSender =
            firstScope.ServiceProvider.GetRequiredService<ISender>();
        ISender secondSender =
            secondScope.ServiceProvider.GetRequiredService<ISender>();

        AtprotoSessionBootstrapResult[] results = await Task.WhenAll(
            firstSender.Send(
                CreateBootstrapCommand(did),
                CancellationToken.None),
            secondSender.Send(
                CreateBootstrapCommand(did),
                CancellationToken.None));

        await Assert.That(results.All(result => result.Success)).IsTrue();
        await Assert.That(results.Select(result => result.UserId).Distinct())
            .Count().IsEqualTo(1);
        ExploreDbContext dbContext =
            firstScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        dbContext.ChangeTracker.Clear();
        await Assert.That(await dbContext.Users.CountAsync()).IsEqualTo(1);
        await Assert.That(await dbContext.Actors.CountAsync()).IsEqualTo(1);
        await Assert.That(await dbContext.UserExternalLogins.CountAsync())
            .IsEqualTo(1);
    }

    [Test]
    public async Task ConfiguredAtprotoAdministratorCompletesWithoutLocalPasswordRecords()
    {
        await fixture.ResetDatabaseAsync();
        AtprotoDid did = AtprotoDid.Parse(
            $"did:plc:{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}");
        fixture.ConfigureAdministrator(did);

        await using (AsyncServiceScope seedScope =
                     fixture.Factory.Services.CreateAsyncScope())
        {
            ExploreDbContext seedDb =
                seedScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            seedDb.Tenants.Add(new TenantBuilder()
                .WithId(PlatformDefaults.DefaultTenantId)
                .WithSlug("default")
                .Build());
            seedDb.InstanceBootstrapStates.Add(
                InstanceBootstrapState.CreateConfiguredAdministratorPending(
                    Guid.CreateVersion7(),
                    AuthenticationProviderKind.Atproto,
                    DeploymentMode.MultiTenant,
                    fixture.AdministratorGeneration,
                    fixture.AdministratorConfigurationFingerprint,
                    fixture.AdministratorIdentityFingerprint,
                    DateTime.UtcNow));
            await seedDb.SaveChangesAsync();
        }

        await using AsyncServiceScope scope =
            fixture.Factory.Services.CreateAsyncScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        AtprotoSessionBootstrapResult result = await sender.Send(
            CreateBootstrapCommand(did),
            CancellationToken.None);

        await Assert.That(result.Success).IsTrue();
        ExploreDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        dbContext.ChangeTracker.Clear();
        await Assert.That(await dbContext.PlatformUserRoles.CountAsync())
            .IsEqualTo(1);
        await Assert.That(await dbContext.InstanceBootstrapStates.CountAsync(
                state => state.Status == InstanceBootstrapStatus.Completed))
            .IsEqualTo(1);
        await Assert.That(await dbContext.LocalIdentityUsers.CountAsync())
            .IsEqualTo(0);
        await Assert.That(fixture.PersistedAtprotoSessionCount)
            .IsEqualTo(1);
    }

    [Test]
    public async Task EmergencyProvisionerHelpRunsWithoutDatabaseCredentials()
    {
        string repositoryRoot = Path.GetFullPath(
            "../../../../../",
            AppContext.BaseDirectory);
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--file");
        startInfo.ArgumentList.Add("eng/tools/EmergencyAdminProvisioner.cs");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--help");
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        string output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        string error = await process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);

        await Assert.That(process.ExitCode).IsEqualTo(0)
            .Because(error);
        await Assert.That(output).Contains("--grant-did");
        await Assert.That(output).DoesNotContain("DATABASE_PASSWORD");
    }

    [Test]
    public async Task EmergencyProvisionerPromotesExactLinkedDidIdempotently()
    {
        await fixture.ResetDatabaseAsync();
        AtprotoDid did = AtprotoDid.Parse(
            $"did:plc:{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}");
        await using AsyncServiceScope scope =
            fixture.Factory.Services.CreateAsyncScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        AtprotoSessionBootstrapResult bootstrap = await sender.Send(
            CreateBootstrapCommand(did),
            CancellationToken.None);
        await Assert.That(bootstrap.Success).IsTrue();

        ExploreDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var operation = new EmergencyAdminProvisioningOperation(dbContext);
        EmergencyAdminProvisioningOutcome first =
            await operation.GrantAsync(did, CancellationToken.None);
        EmergencyAdminProvisioningOutcome second =
            await operation.GrantAsync(did, CancellationToken.None);

        await Assert.That(first).IsEqualTo(
            EmergencyAdminProvisioningOutcome.Granted);
        await Assert.That(second).IsEqualTo(
            EmergencyAdminProvisioningOutcome.AlreadyPresent);
        await Assert.That(await dbContext.PlatformUserRoles.CountAsync())
            .IsEqualTo(1);
    }

    [Test]
    public async Task EmergencyProvisionerCanReassignExclusiveInstanceAuthority()
    {
        await fixture.ResetDatabaseAsync();
        AtprotoDid did = AtprotoDid.Parse(
            $"did:plc:{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}");
        await using AsyncServiceScope scope =
            fixture.Factory.Services.CreateAsyncScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        AtprotoSessionBootstrapResult bootstrap = await sender.Send(
            CreateBootstrapCommand(did),
            CancellationToken.None);
        await Assert.That(bootstrap.Success).IsTrue();

        ExploreDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        User oldAdministrator = new UserBuilder().Build();
        dbContext.Users.Add(oldAdministrator);
        dbContext.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = oldAdministrator.Id,
            User = oldAdministrator,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();

        var operation = new EmergencyAdminProvisioningOperation(dbContext);
        EmergencyAdminProvisioningOutcome outcome =
            await operation.GrantAsync(
                did,
                CancellationToken.None,
                revokeOtherAdministrators: true);

        await Assert.That(outcome).IsEqualTo(
            EmergencyAdminProvisioningOutcome.Reassigned);
        dbContext.ChangeTracker.Clear();
        Guid[] administratorUserIds = await dbContext.PlatformUserRoles
            .Where(grant => grant.RoleId == (int)RoleEnum.Admin)
            .Select(grant => grant.UserId)
            .ToArrayAsync();
        await Assert.That(administratorUserIds)
            .IsEquivalentTo([bootstrap.UserId!.Value]);
    }

    private static BootstrapAtprotoSessionCommand CreateBootstrapCommand(
        AtprotoDid did) =>
        new(
            did,
            "https://pds.example.test/",
            "test-oauth-key",
            AtprotoSubjectClassification.Person,
            RandomNumberGenerator.GetBytes(64));

    private static string CreateOpaqueValue() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}

public sealed class AtprotoSoleProviderFixture : PostgreSqlApiFixtureBase
{
    private readonly TestAtprotoOAuthSecurityGateway _securityGateway = new();
    private readonly TestAtprotoSessionTokenIssuer _tokenIssuer = new();
    private readonly TestConfiguredAdministratorBootstrapProvider
        _configuredAdministratorProvider = new();

    public long AdministratorGeneration =>
        _configuredAdministratorProvider.Generation;
    public int PersistedAtprotoSessionCount =>
        _securityGateway.PersistedSessionCount;

    public string AdministratorConfigurationFingerprint =>
        _configuredAdministratorProvider.ConfigurationFingerprint;

    public string AdministratorIdentityFingerprint =>
        _configuredAdministratorProvider.IdentityFingerprint;

    public void ConfigureAdministrator(AtprotoDid did) =>
        _configuredAdministratorProvider.Configure(did);

    public new async Task ResetDatabaseAsync()
    {
        _securityGateway.Reset();
        await base.ResetDatabaseAsync();
    }

    protected override Dictionary<string, string?> GetAdditionalConfiguration() =>
        new()
        {
            ["Testing:HostProfile"] = TestHostProfile.RealRuntime,
            ["RateLimiting:DisableInTesting"] = "true",
            ["Authentication:Provider"] = "atproto",
            ["Authentication:AtprotoLoginEnabled"] = "true",
        };

    protected override void ConfigureAdditionalTestServices(
        IServiceCollection services)
    {
        services.RemoveAll<IAtprotoOAuthSecurityGateway>();
        services.RemoveAll<IAtprotoSessionTokenIssuer>();
        services.RemoveAll<IConfiguredAdministratorBootstrapProvider>();
        services.AddSingleton<IAtprotoOAuthSecurityGateway>(_securityGateway);
        services.AddSingleton<IAtprotoSessionTokenIssuer>(_tokenIssuer);
        services.AddSingleton<IConfiguredAdministratorBootstrapProvider>(
            _configuredAdministratorProvider);
    }
}

internal sealed class TestAtprotoOAuthSecurityGateway
    : IAtprotoOAuthSecurityGateway
{
    private int _persistedSessionCount;

    public int PersistedSessionCount =>
        Volatile.Read(ref _persistedSessionCount);

    public void Reset() => Interlocked.Exchange(
        ref _persistedSessionCount,
        0);

    public Task<AtprotoOAuthVerificationResult> VerifyAsync(
        AtprotoOAuthVerificationInput request,
        CancellationToken cancellationToken) =>
        Task.FromResult(AtprotoOAuthVerificationResult.Verified(
            new AtprotoVerifiedOAuthSession(
                request.ExpectedDid,
                "verified.example.test",
                request.ExpectedPdsUri,
                request.OAuthClientKeyId,
                request.OAuthSessionPayload)));

    public Task<AtprotoPreparedOAuthSession> PreparePersistenceAsync(
        AtprotoVerifiedOAuthSession verifiedSession,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AtprotoPreparedOAuthSession(
            RandomNumberGenerator.GetBytes(64),
            "test-envelope-key",
            1,
            tenantId,
            userId,
            verifiedSession.Did,
            verifiedSession.PdsUri.AbsoluteUri,
            verifiedSession.OAuthClientKeyId,
            DateTime.UtcNow.AddMinutes(30)));

    public Task PersistPreparedAsync(
        AtprotoPreparedOAuthSession preparedSession,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _persistedSessionCount);
        return Task.CompletedTask;
    }

    public Task<AtprotoCurrentOAuthSession?> GetCurrentAsync(
        AtprotoCurrentSessionIdentity identity,
        CancellationToken cancellationToken) =>
        Task.FromResult<AtprotoCurrentOAuthSession?>(null);

    public Task<AtprotoOAuthRefreshResult> RefreshAsync(
        AtprotoCurrentSessionIdentity identity,
        CancellationToken cancellationToken) =>
        Task.FromResult(AtprotoOAuthRefreshResult.ReauthenticationRequired());

    public Task<AtprotoSessionRevocationResult> RevokeCurrentAsync(
        AtprotoCurrentSessionIdentity identity,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AtprotoSessionRevocationResult(
            AtprotoSessionRevocationOutcome.Revoked));
}

internal sealed class TestAtprotoSessionTokenIssuer
    : IAtprotoSessionTokenIssuer
{
    public Task<AtprotoIssuedSessionToken> IssueAsync(
        Guid userId,
        Guid tenantId,
        AtprotoDid did,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AtprotoIssuedSessionToken(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            DateTimeOffset.UtcNow.AddMinutes(30)));
}

internal sealed class TestConfiguredAdministratorBootstrapProvider
    : IConfiguredAdministratorBootstrapProvider
{
    private ProviderAccountKey? _expectedAccount;

    public long Generation { get; } = 9;
    public string ConfigurationFingerprint { get; } =
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();
    public string IdentityFingerprint { get; } =
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();

    public void Configure(AtprotoDid did) =>
        _expectedAccount =
            PlatformIdentityPrincipalExtensions.CreateAtprotoAccountKey(did);

    public Task<ConfiguredAdministratorBootstrapBinding?> GetVerifiedBindingAsync(
        ProviderAccountKey authenticatedAccount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConfiguredAdministratorBootstrapBinding? binding =
            _expectedAccount is { } expected
            && authenticatedAccount == expected
                ? new ConfiguredAdministratorBootstrapBinding(
                    expected,
                    Generation,
                    IdentityFingerprint,
                    new CompleteInstanceOnboardingRequest
                    {
                        DeploymentMode = DeploymentMode.MultiTenant,
                        SiteProfile = new SelfHostOnboardingProfileDto
                        {
                            SiteName = "AT Protocol Test Instance",
                        },
                        AdministrationAccessMode =
                            CompleteInstanceOnboardingRequest
                                .EmbeddedAdministrationAccess,
                    },
                    new ConfiguredAdministratorProfile(
                        $"{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}@example.test",
                        "Configured",
                        "Administrator"))
                : null;
        return Task.FromResult(binding);
    }
}
