// ABOUTME: Exercises Local Identity issuance policy and lockout against real ASP.NET Core Identity stores.
// ABOUTME: Proves passwords are hashed, unverified email stays untrusted, and repeated failures lock accounts.

using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
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
using Explore.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class LocalIdentityAuthServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 15, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ControlledCredentialsStayHashedAndLoginDoesNotTrustUnverifiedEmail()
    {
        await using TestFixture fixture = await CreateFixtureAsync();
        LocalAuthRequestDto request = await fixture.SeedUserAsync(emailConfirmed: false);
        LocalAuthResponseDto result = await fixture.Service.AuthenticateAsync(
            request,
            fixture.CancellationToken);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Token).IsNotNull();
        await Assert.That(result.EmailVerified).IsFalse();
        LocalIdentityUser? stored = await fixture.UserManager.FindByEmailAsync(request.Identifier);
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.PasswordHash).IsNotNull();
        await Assert.That(stored.PasswordHash).IsNotEqualTo(request.Password);
        await Assert.That(await fixture.UserManager.CheckPasswordAsync(stored, request.Password)).IsTrue();
    }

    [Test]
    public async Task ConsecutiveInvalidCredentialsLockTheExistingAccount()
    {
        await using TestFixture fixture = await CreateFixtureAsync();
        LocalAuthRequestDto login = await fixture.SeedUserAsync(emailConfirmed: false);
        await fixture.SetInstanceIntentAsync("true");
        string wrongPassword = CreateValidPassword();
        var invalid = new LocalAuthRequestDto(Identifier: login.Identifier, Password: wrongPassword);

        LocalAuthResponseDto first = await fixture.Service.AuthenticateAsync(
            invalid,
            fixture.CancellationToken);
        LocalAuthResponseDto second = await fixture.Service.AuthenticateAsync(
            invalid,
            fixture.CancellationToken);
        LocalAuthResponseDto afterLockout = await fixture.Service.AuthenticateAsync(
            login,
            fixture.CancellationToken);

        await Assert.That(first.Failure).IsEqualTo(LocalAuthFailure.InvalidCredentials);
        await Assert.That(second.Failure).IsEqualTo(LocalAuthFailure.AccountLocked);
        await Assert.That(afterLockout.Failure).IsEqualTo(LocalAuthFailure.AccountLocked);
    }

    [Test]
    public async Task LoginIssuesASecretBackedSignedToken()
    {
        byte[] key = RandomNumberGenerator.GetBytes(64);
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync(
                SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey,
                null,
                Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey,
                Convert.ToBase64String(key),
                SecretSourceType.EnvironmentVariable,
                SecretScope.Instance,
                null,
                Now)));
        var tokenGenerator = new LocalJwtTokenGenerator(
            resolver,
            Options.Create(new LocalIdentityOptions()),
            new FixedTimeProvider(Now));
        await using TestFixture fixture = await CreateFixtureAsync(tokenGenerator);

        LocalAuthRequestDto login = await fixture.SeedUserAsync(emailConfirmed: true);
        LocalAuthResponseDto result = await fixture.Service.AuthenticateAsync(login, fixture.CancellationToken);

        await Assert.That(result.Token).IsNotNull();
        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };
        handler.ValidateToken(
            result.Token!,
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = LocalIdentityOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = LocalIdentityOptions.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                LifetimeValidator = (_, expires, _, _) =>
                    expires == Now.AddMinutes(30).UtcDateTime
            },
            out _);
    }

    private static Task<TestFixture> CreateFixtureAsync(
        ILocalJwtTokenGenerator? tokenGenerator = null) =>
        TestFixture.CreateAsync(tokenGenerator ?? new RecordingTokenGenerator());

    [Test]
    public async Task EnabledInstanceIntentRejectsUnverifiedLoginWithoutSmtpConfiguration()
    {
        await using TestFixture fixture = await CreateSignedFixtureAsync();
        LocalAuthRequestDto login = await fixture.SeedUserAsync(emailConfirmed: false);
        await fixture.SetInstanceIntentAsync("true");

        LocalAuthResponseDto result = await fixture.Service.AuthenticateAsync(login, fixture.CancellationToken);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(LocalAuthFailure.EmailVerificationRequired);
        await Assert.That(result.Token).IsNull();
        await Assert.That(result.ExpiresAt).IsNull();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task DisabledOrAbsentInstanceIntentAllowsLoginWithoutConfirmingAddress(bool omitSetting)
    {
        await using TestFixture fixture = await CreateSignedFixtureAsync();
        LocalAuthRequestDto login = await fixture.SeedUserAsync(emailConfirmed: false);
        if (!omitSetting)
            await fixture.SetInstanceIntentAsync("false");

        LocalAuthResponseDto result = await fixture.Service.AuthenticateAsync(login, fixture.CancellationToken);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Token).IsNotNull();
        await Assert.That(result.EmailVerified).IsFalse();
        await Assert.That((await fixture.UserManager.FindByEmailAsync(login.Identifier))!.EmailConfirmed).IsFalse();
    }

    [Test]
    public async Task VerifiedUserCanLoginWithEnabledIntentAndNoSmtpTransport()
    {
        await using TestFixture fixture = await CreateSignedFixtureAsync();
        LocalAuthRequestDto login = await fixture.SeedUserAsync(emailConfirmed: true);
        await fixture.SetInstanceIntentAsync("true");

        LocalAuthResponseDto result = await fixture.Service.AuthenticateAsync(login, fixture.CancellationToken);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Token).IsNotNull();
        await Assert.That(result.EmailVerified).IsTrue();
    }

    [Test]
    public async Task TenantDisabledOverrideCannotWeakenEnabledInstanceVerification()
    {
        await using TestFixture fixture = await CreateSignedFixtureAsync();
        LocalAuthRequestDto login = await fixture.SeedUserAsync(emailConfirmed: false);
        await fixture.SetInstanceIntentAsync("true");
        await fixture.SeedDisabledTenantOverrideAsync();

        LocalAuthResponseDto result = await fixture.Service.AuthenticateAsync(login, fixture.CancellationToken);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(LocalAuthFailure.EmailVerificationRequired);
        await Assert.That(result.Token).IsNull();
    }

    [Test]
    [Arguments("\"false\"")]
    [Arguments("null")]
    [Arguments("FALSE")]
    [Arguments("")]
    [Arguments("0")]
    [Arguments("{}")]
    public async Task MalformedInstanceIntentDoesNotSilentlyPermitUnverifiedLogin(string rawValue)
    {
        await using TestFixture fixture = await CreateSignedFixtureAsync();
        LocalAuthRequestDto login = await fixture.SeedUserAsync(emailConfirmed: false);
        await fixture.SetInstanceIntentAsync(rawValue);

        LocalAuthResponseDto result = await fixture.Service.AuthenticateAsync(login, fixture.CancellationToken);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(LocalAuthFailure.AuthenticationFailed);
        await Assert.That(result.Token).IsNull();
    }

    [Test]
    public async Task RepeatedLoginReadsCurrentInstanceIntent()
    {
        await using TestFixture fixture = await CreateSignedFixtureAsync();
        LocalAuthRequestDto login = await fixture.SeedUserAsync(emailConfirmed: false);
        await fixture.SetInstanceIntentAsync("false");
        LocalAuthResponseDto before = await fixture.Service.AuthenticateAsync(login, fixture.CancellationToken);

        await fixture.SetInstanceIntentAsync("true");
        LocalAuthResponseDto enabled = await fixture.Service.AuthenticateAsync(login, fixture.CancellationToken);

        await fixture.SetInstanceIntentAsync("false");
        LocalAuthResponseDto disabled = await fixture.Service.AuthenticateAsync(login, fixture.CancellationToken);

        await Assert.That(before.Success).IsTrue();
        await Assert.That(enabled.Failure).IsEqualTo(LocalAuthFailure.EmailVerificationRequired);
        await Assert.That(enabled.Token).IsNull();
        await Assert.That(disabled.Success).IsTrue();
        await Assert.That(disabled.EmailVerified).IsFalse();
    }

    [Test]
    public async Task UnavailableInstanceIntentReadDoesNotIssueAnUnverifiedSession()
    {
        await using TestFixture fixture = await CreateSignedFixtureAsync();
        LocalAuthRequestDto login = await fixture.SeedUserAsync(emailConfirmed: false);
        await fixture.SetInstanceIntentAsync("false");
        await fixture.Context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE ie_system_settings RENAME TO unavailable_system_settings",
            fixture.CancellationToken);

        LocalAuthResponseDto result = await fixture.Service.AuthenticateAsync(login, fixture.CancellationToken);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(LocalAuthFailure.AuthenticationFailed);
        await Assert.That(result.Token).IsNull();
    }

    private static Task<TestFixture> CreateSignedFixtureAsync()
    {
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey, null, Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                SettingKey: SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey,
                Value: Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                Source: SecretSourceType.EnvironmentVariable,
                Scope: SecretScope.Instance,
                ScopeId: null,
                ResolvedAt: Now)));
        return TestFixture.CreateAsync(new LocalJwtTokenGenerator(
            secrets, Options.Create(new LocalIdentityOptions()), new FixedTimeProvider(Now)));
    }

    private static string CreateValidPassword() =>
        $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}";

    private sealed class RecordingTokenGenerator : ILocalJwtTokenGenerator
    {
        public Task<LocalIssuedReplacementChallenge> GenerateReplacementChallengeAsync(
            LocalCredentialReplacementSubject subject,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Replacement challenge issuance is unexpected in this fixture.");

        public Task<LocalIssuedToken> GenerateAsync(
            LocalJwtTokenSubject subject,
            CancellationToken cancellationToken) =>
            Task.FromResult(new LocalIssuedToken(
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                Now.AddMinutes(30)));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection =
            new(new SqliteConnectionStringBuilder { DataSource = ":memory:" }.ToString());
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));

        private ServiceProvider? _provider;

        private TestFixture()
        {
        }

        internal UserManager<LocalIdentityUser> UserManager { get; private set; } = null!;
        internal LocalIdentityAuthService Service { get; private set; } = null!;
        internal ExploreDbContext Context { get; private set; } = null!;
        internal CancellationToken CancellationToken => _timeout.Token;
        private SystemSettingRepository _systemSettings = null!;

        internal static async Task<TestFixture> CreateAsync(
            ILocalJwtTokenGenerator tokenGenerator)
        {
            var fixture = new TestFixture();
            try
            {
                await fixture._connection.OpenAsync(fixture.CancellationToken);
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddDbContext<ExploreDbContext>(options =>
                    options.UseSqlite(fixture._connection)
                        .UseSnakeCaseNamingConvention()
                        .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance));
                services.AddIdentityCore<LocalIdentityUser>(options =>
                    {
                        options.User.RequireUniqueEmail = true;
                        options.Lockout.AllowedForNewUsers = true;
                        options.Lockout.MaxFailedAccessAttempts = 2;
                        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                    })
                    .AddRoles<LocalIdentityRole>()
                    .AddEntityFrameworkStores<ExploreDbContext>();
                fixture._provider = services.BuildServiceProvider();
                var context = fixture._provider.GetRequiredService<ExploreDbContext>();
                await context.Database.EnsureCreatedAsync(fixture.CancellationToken);
                fixture.Context = context;
                await LookupTableSeeder.SeedAsync(context, fixture.CancellationToken);
                fixture._systemSettings = new SystemSettingRepository(context,
                    new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
                fixture.UserManager = fixture._provider
                    .GetRequiredService<UserManager<LocalIdentityUser>>();
                fixture.Service = new LocalIdentityAuthService(
                    fixture.UserManager,
                    tokenGenerator,
                    fixture._systemSettings,
                    new LocalIdentityCredentialStateStore(
                        identityDbContext: context,
                        applicationDbContext: context,
                        userManager: fixture.UserManager,
                        timeProvider: new FixedTimeProvider(Now)));
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        internal async Task<LocalAuthRequestDto> SeedUserAsync(bool emailConfirmed)
        {
            string password = CreateValidPassword();
            var initiator = new User
            {
                Id = Guid.CreateVersion7(),
                Pii = new UserPii { Email = "initiator@example.test", FirstName = "Instance", LastName = "Operator" },
                EmailVerified = true,
                CreatedAt = Now.UtcDateTime
            };
            Context.Users.Add(initiator);
            await Context.SaveChangesAsync(CancellationToken);
            var credentials = new LocalIdentityCredentialStateStore(
                identityDbContext: Context, applicationDbContext: Context,
                userManager: UserManager, timeProvider: new FixedTimeProvider(Now));
            LocalCredentialCreateResult created = await credentials.CreatePendingAsync(new LocalCredentialCreateRequest(
                operationId: Guid.CreateVersion7(), initiatingApplicationUserId: initiator.Id,
                email: "local@example.test", firstName: "Local", lastName: "User"), CancellationToken);
            await Assert.That(created.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
            LocalCredentialOperationReceipt receipt = created.Receipt!;
            var applicationUser = new User
            {
                Id = receipt.LocalSubjectId, EmailVerified = true, CreatedAt = Now.UtcDateTime,
                Pii = new UserPii { Email = "local@example.test", FirstName = "Local", LastName = "User" }
            };
            Context.Actors.Add(new Actor
            {
                Id = receipt.PersonalActorId, UserId = applicationUser.Id, User = applicationUser,
                ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                Pii = new ActorPii { DisplayName = "Local User" }, CreatedAt = Now.UtcDateTime
            });
            Context.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = receipt.ExternalLoginId, UserId = applicationUser.Id, User = applicationUser,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Local, AuthenticationProvider = null!,
                ProviderKey = applicationUser.Id.ToString("D"), CreatedAt = Now.UtcDateTime
            });
            await Context.SaveChangesAsync(CancellationToken);
            LocalCredentialProvisioningSnapshot pending = (await credentials.ReadProvisioningAsync(receipt.OperationId, CancellationToken))!;
            await Assert.That(await credentials.ActivateChangeRequiredAsync(new LocalCredentialActivationRequest(
                operationId: receipt.OperationId, expectedOperationConcurrencyStamp: pending.OperationConcurrencyStamp), CancellationToken))
                .IsEqualTo(LocalCredentialActivationOutcome.Activated);
            LocalIdentityUser user = (await UserManager.FindByIdAsync(receipt.LocalSubjectId.ToString("D")))!;
            await Assert.That(await credentials.ReplaceAsync(new LocalCredentialReplacementRequest(
                authority: new LocalCredentialReplacementAuthority(
                    subject: new LocalCredentialReplacementSubject(localSubjectId: user.Id,
                        operationId: receipt.OperationId, securityStamp: user.SecurityStamp!),
                    issuedAtUtc: Now, expiresAtUtc: Now.AddMinutes(5)),
                newPassword: password), CancellationToken)).IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
            await Context.Entry(user).ReloadAsync(CancellationToken);
            user.EmailConfirmed = emailConfirmed;
            user.LockoutEnabled = true;
            await Assert.That((await UserManager.UpdateAsync(user)).Succeeded).IsTrue();
            applicationUser = await Context.Users.SingleAsync(row => row.Id == receipt.LocalSubjectId, CancellationToken);
            applicationUser.EmailVerified = emailConfirmed;
            await Context.SaveChangesAsync(CancellationToken);
            return new LocalAuthRequestDto(Identifier: user.Email!, Password: password);
        }

        internal Task<string?> SetInstanceIntentAsync(string value) => _systemSettings.UpsertAsync(new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = GovernanceSettingKeys.Email.DeliveryEnabled,
            Value = value,
            ValueType = SettingValueType.Boolean,
            IsLocked = false,
            CreatedAt = Now.UtcDateTime
        }, cancellationToken: CancellationToken);

        internal async Task SeedDisabledTenantOverrideAsync()
        {
            TenantStatus status = await Context.Set<TenantStatus>().SingleAsync(
                row => row.Id == (int)TenantStatusEnum.Active, CancellationToken);
            var tenant = new Tenant
            {
                Id = Guid.CreateVersion7(), FullName = "Local policy tenant", Slug = "local-policy",
                TenantStatusId = status.Id, TenantStatus = status, CreatedAt = Now.UtcDateTime
            };
            Context.TenantContext = new FixedTenantContext(tenant.Id);
            Context.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
                SettingKey = GovernanceSettingKeys.Email.DeliveryEnabled, Value = "false",
                IsLocked = false, CreatedAt = Now.UtcDateTime
            });
            await Context.SaveChangesAsync(CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (_provider is not null)
            {
                await _provider.DisposeAsync();
            }

            await _connection.DisposeAsync();
            _timeout.Dispose();
        }
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
    }
}
