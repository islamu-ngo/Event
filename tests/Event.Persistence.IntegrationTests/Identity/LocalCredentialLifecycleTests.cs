
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Authentication;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class LocalCredentialLifecycleTests
{
    public enum MetadataCorruption
    {
        MalformedJson,
        UnsupportedVersion,
        UndefinedState,
        EmptyOperationId,
        EmptyApplicationUserId,
        DuplicateState
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ValidPasswordWithoutLifecycleStateCannotIssueNormalAccessToken(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);

        LocalAuthResponseDto result = await fixture.AuthenticateAsync();

        await AssertDeniedAsync(result);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ReadyLifecycleStatePermitsNativeSignedAccessToken(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync(topology);

        LocalAuthResponseDto result = await fixture.AuthenticateAsync();

        await Assert.That(result.Success).IsTrue();
        await Assert.That(string.IsNullOrEmpty(result.Token)).IsFalse();
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(result.Token!, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(fixture.SigningKey),
            ValidateIssuer = true,
            ValidIssuer = LocalIdentityOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = LocalIdentityOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        }, out _);
        await Assert.That(principal.FindFirst("auth_provider")?.Value).IsEqualTo("local");
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, LocalCredentialState.ProvisioningPending)]
    [Arguments(IdentityDatabaseTopology.Colocated, LocalCredentialState.ChangeRequired)]
    [Arguments(IdentityDatabaseTopology.External, LocalCredentialState.ProvisioningPending)]
    [Arguments(IdentityDatabaseTopology.External, LocalCredentialState.ChangeRequired)]
    public async Task NonReadyLifecycleCannotIssueNormalAccessToken(
        IdentityDatabaseTopology topology, LocalCredentialState state)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await fixture.SetStateAsync(state);

        LocalAuthResponseDto result = await fixture.AuthenticateAsync();

        await AssertDeniedAsync(result);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, MetadataCorruption.MalformedJson)]
    [Arguments(IdentityDatabaseTopology.Colocated, MetadataCorruption.UnsupportedVersion)]
    [Arguments(IdentityDatabaseTopology.Colocated, MetadataCorruption.UndefinedState)]
    [Arguments(IdentityDatabaseTopology.Colocated, MetadataCorruption.EmptyOperationId)]
    [Arguments(IdentityDatabaseTopology.Colocated, MetadataCorruption.EmptyApplicationUserId)]
    [Arguments(IdentityDatabaseTopology.Colocated, MetadataCorruption.DuplicateState)]
    [Arguments(IdentityDatabaseTopology.External, MetadataCorruption.MalformedJson)]
    [Arguments(IdentityDatabaseTopology.External, MetadataCorruption.UnsupportedVersion)]
    [Arguments(IdentityDatabaseTopology.External, MetadataCorruption.UndefinedState)]
    [Arguments(IdentityDatabaseTopology.External, MetadataCorruption.EmptyOperationId)]
    [Arguments(IdentityDatabaseTopology.External, MetadataCorruption.EmptyApplicationUserId)]
    [Arguments(IdentityDatabaseTopology.External, MetadataCorruption.DuplicateState)]
    public async Task CorruptedLifecycleMetadataCannotAuthorizeNormalAccessToken(
        IdentityDatabaseTopology topology, MetadataCorruption corruption)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        JsonObject metadata = JsonSerializer.SerializeToNode(fixture.Metadata(LocalCredentialState.Ready))!.AsObject();
        switch (corruption)
        {
            case MetadataCorruption.MalformedJson: break;
            case MetadataCorruption.DuplicateState: break;
            case MetadataCorruption.UnsupportedVersion: metadata["Version"] = 2; break;
            case MetadataCorruption.UndefinedState: metadata["State"] = 99; break;
            case MetadataCorruption.EmptyOperationId: metadata["OperationId"] = Guid.Empty.ToString("D"); break;
            case MetadataCorruption.EmptyApplicationUserId: metadata["ApplicationUserId"] = Guid.Empty.ToString("D"); break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        string value = corruption switch
        {
            MetadataCorruption.MalformedJson => "{",
            MetadataCorruption.DuplicateState => metadata.ToJsonString().Insert(1, "\"State\":1,"),
            _ => metadata.ToJsonString()
        };
        await fixture.SetRawStateAsync(value);

        LocalAuthResponseDto result = await fixture.AuthenticateAsync();

        await AssertDeniedAsync(result);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task UnavailableLifecycleStorageCannotBecomeSuccessfulAuthentication(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync(topology);
        var mapping = fixture.Identity.Model.FindEntityType(typeof(IdentityUserToken<Guid>))!;
        string table = fixture.Identity.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(mapping.GetTableName()!, mapping.GetSchema());
        await fixture.Identity.Database.ExecuteSqlRawAsync($"DROP TABLE {table}", fixture.CancellationToken);
        await Assert.That(await fixture.PasswordIsValidAsync()).IsTrue();

        LocalAuthResponseDto result = await fixture.AuthenticateAsync();

        await Assert.That(string.IsNullOrEmpty(result.Token)).IsTrue();
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(LocalAuthFailure.AuthenticationFailed);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task CancellationDuringAuthoritativeStateReadPropagatesInsteadOfIssuingSession(
        IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync(topology);
        fixture.CancelOnNextStateRead();

        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.AuthenticateAsync());

        await Assert.That(fixture.StateReadCancellationObserved).IsTrue();
    }

    [Test]
    public async Task ApplicationStoreReadyDecoyCannotAuthorizeExternalIdentityWithoutItsOwnState()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        fixture.Application.LocalIdentityUsers.Add(new LocalIdentityUser
        {
            Id = fixture.User.Id,
            UserName = fixture.User.UserName,
            NormalizedUserName = fixture.User.NormalizedUserName,
            Email = fixture.User.Email,
            NormalizedEmail = fixture.User.NormalizedEmail,
            FirstName = fixture.User.FirstName,
            LastName = fixture.User.LastName,
            PasswordHash = fixture.User.PasswordHash,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        });
        fixture.Application.Set<IdentityUserToken<Guid>>().Add(new IdentityUserToken<Guid>
        {
            UserId = fixture.User.Id,
            LoginProvider = LocalCredentialStateMetadata.TokenLoginProvider,
            Name = LocalCredentialStateMetadata.TokenName,
            Value = JsonSerializer.Serialize(fixture.Metadata(LocalCredentialState.Ready))
        });
        await fixture.Application.SaveChangesAsync(fixture.CancellationToken);
        await Assert.That(await fixture.Application.Set<IdentityUserToken<Guid>>()
            .CountAsync(token => token.UserId == fixture.User.Id, fixture.CancellationToken)).IsEqualTo(1);
        await Assert.That(await fixture.Identity.Set<IdentityUserToken<Guid>>()
            .AnyAsync(token => token.UserId == fixture.User.Id, fixture.CancellationToken)).IsFalse();
        await Assert.That(await fixture.PasswordIsValidAsync()).IsTrue();

        LocalAuthResponseDto result = await fixture.AuthenticateAsync();

        await AssertDeniedAsync(result);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task PreviouslyTrackedReadyStateCannotHideIndependentlyCommittedPendingState(
        IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync(topology);
        IdentityUserToken<Guid> tracked = await fixture.Identity.Set<IdentityUserToken<Guid>>().SingleAsync(
            token => token.UserId == fixture.User.Id && token.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                && token.Name == LocalCredentialStateMetadata.TokenName, fixture.CancellationToken);
        string? originalValue = tracked.Value;
        await Assert.That((await fixture.AuthenticateAsync()).Success).IsTrue();

        await using (AsyncServiceScope writer = fixture.Provider.CreateAsyncScope())
        {
            var manager = writer.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            LocalIdentityUser current = (await manager.FindByIdAsync(fixture.User.Id.ToString("D"))
                .WaitAsync(fixture.CancellationToken))!;
            IdentityResult changed = await manager.SetAuthenticationTokenAsync(current,
                    LocalCredentialStateMetadata.TokenLoginProvider, LocalCredentialStateMetadata.TokenName,
                    JsonSerializer.Serialize(fixture.Metadata(LocalCredentialState.ProvisioningPending)))
                .WaitAsync(fixture.CancellationToken);
            await Assert.That(changed.Succeeded).IsTrue();
        }
        await Assert.That(tracked.Value).IsEqualTo(originalValue);

        LocalAuthResponseDto result = await fixture.AuthenticateAsync();

        await AssertDeniedAsync(result);
    }

    private static async Task AssertDeniedAsync(LocalAuthResponseDto result)
    {
        await Assert.That(string.IsNullOrEmpty(result.Token)).IsTrue();
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(LocalAuthFailure.InvalidCredentials);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _applicationConnection = new(
            new SqliteConnectionStringBuilder { DataSource = ":memory:" }.ToString());
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));
        private readonly MemoryCache _metadataCache = new(new MemoryCacheOptions());
        private Guid _operationId = Guid.CreateVersion7();
        private Guid _applicationUserId = Guid.CreateVersion7();
        private readonly StateReadCancellationInterceptor _readCancellation = new();
        private SqliteConnection? _externalConnection;
        private ServiceProvider? _provider;
        private AsyncServiceScope? _scope;
        private UserManager<LocalIdentityUser> _manager = null!;
        private ILocalIdentityAuthService _authentication = null!;
        private LocalAuthRequestDto _request = null!;

        internal ServiceProvider Provider => _provider!;
        internal ExploreDbContext Application { get; private set; } = null!;
        internal DbContext Identity { get; private set; } = null!;
        internal LocalIdentityUser User { get; private set; } = null!;
        internal byte[] SigningKey { get; } = RandomNumberGenerator.GetBytes(64);
        internal CancellationToken CancellationToken => _timeout.Token;
        internal bool StateReadCancellationObserved => _readCancellation.Observed;

        private enum CredentialSetup { MissingState, Ready }

        internal static Task<Fixture> CreateAsync(IdentityDatabaseTopology topology) =>
            CreateAsync(topology, CredentialSetup.MissingState);

        internal static Task<Fixture> CreateReadyAsync(IdentityDatabaseTopology topology) =>
            CreateAsync(topology, CredentialSetup.Ready);

        private static async Task<Fixture> CreateAsync(IdentityDatabaseTopology topology, CredentialSetup setup)
        {
            await LocalIdentitySqliteTemplate.InitializeAsync();
            var fixture = new Fixture();
            try
            {
                await fixture.InitializeAsync(topology, setup);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        internal LocalCredentialStateMetadata Metadata(LocalCredentialState state) => new(
            version: LocalCredentialStateMetadata.CurrentVersion,
            state: state,
            operationId: _operationId,
            applicationUserId: _applicationUserId);

        internal Task SetStateAsync(LocalCredentialState state) => SetRawStateAsync(JsonSerializer.Serialize(Metadata(state)));

        internal async Task SetRawStateAsync(string value)
        {
            IdentityResult result = await _manager.SetAuthenticationTokenAsync(User,
                    LocalCredentialStateMetadata.TokenLoginProvider, LocalCredentialStateMetadata.TokenName, value)
                .WaitAsync(CancellationToken);
            await Assert.That(result.Succeeded).IsTrue();
        }

        internal Task<LocalAuthResponseDto> AuthenticateAsync() => _authentication.AuthenticateAsync(_request, CancellationToken);

        internal Task<bool> PasswordIsValidAsync() =>
            _manager.CheckPasswordAsync(User, _request.Password).WaitAsync(CancellationToken);

        internal void CancelOnNextStateRead() => _readCancellation.Arm(
            identity: Identity,
            tokenTable: Identity.Model.FindEntityType(typeof(IdentityUserToken<Guid>))!.GetTableName()!,
            cancellation: _timeout);

        private async Task InitializeAsync(IdentityDatabaseTopology topology, CredentialSetup setup)
        {
            await _applicationConnection.OpenAsync(CancellationToken);
            await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(_applicationConnection, CancellationToken);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ExploreDbContext>(options => options.UseSqlite(_applicationConnection)
                .UseSnakeCaseNamingConvention().UseMemoryCache(_metadataCache)
                .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance, _readCancellation));
            IdentityBuilder builder = services.AddIdentityCore<LocalIdentityUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
            }).AddRoles<LocalIdentityRole>();
            if (topology == IdentityDatabaseTopology.External)
            {
                _externalConnection = new SqliteConnection(
                    new SqliteConnectionStringBuilder { DataSource = ":memory:" }.ToString());
                await _externalConnection.OpenAsync(CancellationToken);
                services.AddDbContext<ExternalIdentityDbContext>(options => options.UseSqlite(_externalConnection)
                    .UseSnakeCaseNamingConvention().UseMemoryCache(_metadataCache)
                    .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance, _readCancellation));
                builder.AddEntityFrameworkStores<ExternalIdentityDbContext>();
            }
            else
            {
                builder.AddEntityFrameworkStores<ExploreDbContext>();
            }
            _provider = services.BuildIsolatedServiceProvider();
            _scope = _provider.CreateAsyncScope();
            IServiceProvider scoped = _scope.Value.ServiceProvider;
            var application = scoped.GetRequiredService<ExploreDbContext>();
            Application = application;
            Identity = topology == IdentityDatabaseTopology.External
                ? scoped.GetRequiredService<ExternalIdentityDbContext>() : application;
            if (topology == IdentityDatabaseTopology.External)
            {
                await Identity.Database.EnsureCreatedAsync(CancellationToken);
            }
            var settings = new SystemSettingRepository(dbContext: application,
                mutationLock: new RelationalSettingMutationLock(dbContext: application,
                    unitOfWork: new EfCoreUnitOfWork(application)));
            await EmailDispatchSqliteFixture.SetEmailSettingAsync(application,
                GovernanceSettingKeys.Email.DeliveryEnabled, "false", cancellationToken: CancellationToken);
            _manager = scoped.GetRequiredService<UserManager<LocalIdentityUser>>();
            string password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}";
            string email = $"lifecycle-{Guid.CreateVersion7():N}@example.test";
            if (setup == CredentialSetup.Ready)
            {
                await ProvisionReadyAsync(email: email, password: password);
            }
            else
            {
                User = new LocalIdentityUser
                {
                    UserName = email, Email = email, FirstName = "Local", LastName = "Lifecycle",
                    EmailConfirmed = false, CreatedAt = DateTime.UtcNow
                };
                IdentityResult created = await _manager.CreateAsync(User, password).WaitAsync(CancellationToken);
                await Assert.That(created.Succeeded).IsTrue();
                await Assert.That(await Identity.Set<IdentityUserToken<Guid>>().AnyAsync(token => token.UserId == User.Id, CancellationToken))
                    .IsFalse();
            }
            await Assert.That(await _manager.CheckPasswordAsync(User, password).WaitAsync(CancellationToken)).IsTrue();
            await Assert.That(await Identity.Set<LocalIdentityUser>().CountAsync(user => user.Id == User.Id, CancellationToken))
                .IsEqualTo(1);
            await Assert.That(await application.LocalIdentityUsers.CountAsync(CancellationToken))
                .IsEqualTo(topology == IdentityDatabaseTopology.Colocated ? 1 : 0);
            _request = new LocalAuthRequestDto(Identifier: email, Password: password);
            var secrets = Substitute.For<ISecretResolver>();
            secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey, null, Arg.Any<CancellationToken>())
                .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                    SettingKey: SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey,
                    Value: Convert.ToBase64String(SigningKey), Source: SecretSourceType.EnvironmentVariable,
                    Scope: SecretScope.Instance, ScopeId: null, ResolvedAt: DateTimeOffset.UtcNow)));
            var signer = new LocalJwtTokenGenerator(secretResolver: secrets,
                options: Options.Create(new LocalIdentityOptions()), timeProvider: TimeProvider.System);
            _authentication = new LocalIdentityAuthService(userManager: _manager, tokenGenerator: signer,
                systemSettings: settings,
                credentialStates: new LocalIdentityCredentialStateStore(
                    identityDbContext: Identity, applicationDbContext: Application,
                    userManager: _manager, timeProvider: TimeProvider.System));
        }

        private async Task ProvisionReadyAsync(string email, string password)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var initiator = new User
            {
                Id = Guid.CreateVersion7(), EmailVerified = true, CreatedAt = now.UtcDateTime,
                Pii = new UserPii { Email = "initiator@example.test", FirstName = "Instance", LastName = "Operator" }
            };
            Application.Users.Add(initiator);
            await Application.SaveChangesAsync(CancellationToken);
            var credentials = new LocalIdentityCredentialStateStore(
                identityDbContext: Identity, applicationDbContext: Application,
                userManager: _manager, timeProvider: TimeProvider.System);
            LocalCredentialCreateResult created = await credentials.CreatePendingAsync(new LocalCredentialCreateRequest(
                operationId: Guid.CreateVersion7(), initiatingApplicationUserId: initiator.Id,
                email: email, firstName: "Local", lastName: "Lifecycle"), CancellationToken);
            await Assert.That(created.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
            LocalCredentialOperationReceipt receipt = created.Receipt!;
            _operationId = receipt.OperationId;
            _applicationUserId = receipt.LocalSubjectId;
            var applicationUser = new User
            {
                Id = receipt.LocalSubjectId, EmailVerified = true, CreatedAt = now.UtcDateTime,
                Pii = new UserPii { Email = email, FirstName = "Local", LastName = "Lifecycle" }
            };
            Application.Actors.Add(new Actor
            {
                Id = receipt.PersonalActorId, UserId = applicationUser.Id, User = applicationUser,
                ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                Pii = new ActorPii { DisplayName = "Local Lifecycle" }, CreatedAt = now.UtcDateTime
            });
            Application.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = receipt.ExternalLoginId, UserId = applicationUser.Id, User = applicationUser,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Local, AuthenticationProvider = null!,
                ProviderKey = applicationUser.Id.ToString("D"), CreatedAt = now.UtcDateTime
            });
            await Application.SaveChangesAsync(CancellationToken);
            LocalCredentialProvisioningSnapshot pending = (await credentials.ReadProvisioningAsync(receipt.OperationId, CancellationToken))!;
            await Assert.That(await credentials.ActivateChangeRequiredAsync(new LocalCredentialActivationRequest(
                operationId: receipt.OperationId, expectedOperationConcurrencyStamp: pending.OperationConcurrencyStamp), CancellationToken))
                .IsEqualTo(LocalCredentialActivationOutcome.Activated);
            User = (await _manager.FindByIdAsync(receipt.LocalSubjectId.ToString("D")))!;
            await Assert.That(await credentials.ReplaceAsync(new LocalCredentialReplacementRequest(
                authority: new LocalCredentialReplacementAuthority(
                    subject: new LocalCredentialReplacementSubject(localSubjectId: User.Id,
                        operationId: receipt.OperationId, securityStamp: User.SecurityStamp!),
                    issuedAtUtc: now, expiresAtUtc: now.AddMinutes(5)),
                newPassword: password), CancellationToken)).IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
            await Identity.Entry(User).ReloadAsync(CancellationToken);
            User.EmailConfirmed = false;
            await Assert.That((await _manager.UpdateAsync(User)).Succeeded).IsTrue();
            applicationUser = await Application.Users.SingleAsync(row => row.Id == receipt.LocalSubjectId, CancellationToken);
            applicationUser.EmailVerified = false;
            await Application.SaveChangesAsync(CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_scope.HasValue) await _scope.Value.DisposeAsync();
                if (_provider is not null) await _provider.DisposeAsync();
            }
            finally
            {
                if (_externalConnection is not null) await _externalConnection.DisposeAsync();
                await _applicationConnection.DisposeAsync();
                _metadataCache.Dispose();
                _timeout.Dispose();
            }
        }
    }

    private sealed class StateReadCancellationInterceptor : DbCommandInterceptor
    {
        private DbContext? _identity;
        private string? _tokenTable;
        private CancellationTokenSource? _cancellation;
        internal bool Observed { get; private set; }

        internal void Arm(DbContext identity, string tokenTable, CancellationTokenSource cancellation)
        {
            _identity = identity;
            _tokenTable = tokenTable;
            _cancellation = cancellation;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _identity) && _tokenTable is not null
                && command.CommandText.Contains(_tokenTable, StringComparison.Ordinal))
            {
                Observed = true;
                _cancellation!.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
