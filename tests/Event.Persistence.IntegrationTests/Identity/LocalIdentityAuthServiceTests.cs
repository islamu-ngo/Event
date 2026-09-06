// ABOUTME: Exercises Local Identity issuance policy and lockout against real ASP.NET Core Identity stores.
// ABOUTME: Proves passwords are hashed, unverified email stays untrusted, and repeated failures lock accounts.

using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using Explore.Application.Configuration;
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
        LocalIdentityUser? stored = await fixture.UserManager.FindByEmailAsync(request.Email);
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
        var invalid = new LocalAuthRequestDto(Email: login.Email, Password: wrongPassword);

        LocalAuthResponseDto first = await fixture.Service.AuthenticateAsync(
            invalid,
            fixture.CancellationToken);
        LocalAuthResponseDto second = await fixture.Service.AuthenticateAsync(
            invalid,
            fixture.CancellationToken);
        LocalAuthResponseDto afterLockout = await fixture.Service.AuthenticateAsync(
            login,
            fixture.CancellationToken);

        await Assert.That(first.FailureCode).IsEqualTo("invalid_credentials");
        await Assert.That(second.FailureCode).IsEqualTo("account_locked");
        await Assert.That(afterLockout.FailureCode).IsEqualTo("account_locked");
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
        await Assert.That(result.FailureCode).IsEqualTo("email_verification_required");
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
        await Assert.That((await fixture.UserManager.FindByEmailAsync(login.Email))!.EmailConfirmed).IsFalse();
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
        await Assert.That(result.FailureCode).IsEqualTo("email_verification_required");
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
        await Assert.That(result.FailureCode).IsEqualTo("authentication_failed");
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
        await Assert.That(enabled.FailureCode).IsEqualTo("email_verification_required");
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
        await Assert.That(result.FailureCode).IsEqualTo("authentication_failed");
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
                context.Set<SettingValueTypeLookup>().Add(new SettingValueTypeLookup
                {
                    Id = (int)SettingValueType.Boolean,
                    MasterCode = "BOOLEAN",
                    FullName = "Boolean"
                });
                await context.SaveChangesAsync(fixture.CancellationToken);
                fixture._systemSettings = new SystemSettingRepository(context,
                    new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
                fixture.UserManager = fixture._provider
                    .GetRequiredService<UserManager<LocalIdentityUser>>();
                fixture.Service = new LocalIdentityAuthService(
                    fixture.UserManager,
                    tokenGenerator,
                    fixture._systemSettings);
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
            var user = new LocalIdentityUser
            {
                UserName = "local@example.test",
                Email = "local@example.test",
                FirstName = "Local",
                LastName = "User",
                EmailConfirmed = emailConfirmed,
                LockoutEnabled = true,
                CreatedAt = Now.UtcDateTime
            };
            IdentityResult creation = await UserManager.CreateAsync(user, password);
            await Assert.That(creation.Succeeded).IsTrue();
            return new LocalAuthRequestDto(Email: user.Email!, Password: password);
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
            var status = new TenantStatus
            {
                Id = (int)TenantStatusEnum.Active, MasterCode = "ACTIVE", FullName = "Active", IsActiveState = true
            };
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
