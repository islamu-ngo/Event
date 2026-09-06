// ABOUTME: Exercises Local Identity registration and lockout against the real ASP.NET Core Identity stores.
// ABOUTME: Proves passwords are hashed, unverified email stays untrusted, and repeated failures lock accounts.

using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Authentication;
using Explore.Persistence;
using Explore.Persistence.Identity;
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
    public async Task RegistrationHashesPasswordAndDoesNotTrustUnverifiedEmail()
    {
        await using TestFixture fixture = await CreateFixtureAsync();
        string password = CreateValidPassword();
        var request = new LocalRegistrationRequestDto(
            "admin@example.test",
            password,
            "Site",
            "Administrator");

        LocalRegistrationResponseDto result = await fixture.Service.RegisterAsync(
            request,
            CancellationToken.None);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Authentication).IsNotNull();
        await Assert.That(result.Authentication!.EmailVerified).IsFalse();
        LocalIdentityUser? stored = await fixture.UserManager.FindByEmailAsync(request.Email);
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.PasswordHash).IsNotNull();
        await Assert.That(stored.PasswordHash).IsNotEqualTo(password);
        await Assert.That(await fixture.UserManager.CheckPasswordAsync(stored, password)).IsTrue();
    }

    [Test]
    public async Task ConsecutiveInvalidCredentialsLockTheExistingAccount()
    {
        await using TestFixture fixture = await CreateFixtureAsync();
        string password = CreateValidPassword();
        var registration = new LocalRegistrationRequestDto(
            "admin@example.test",
            password,
            "Site",
            "Administrator");
        await fixture.Service.RegisterAsync(registration, CancellationToken.None);
        string wrongPassword = CreateValidPassword();
        var invalid = new LocalAuthRequestDto(registration.Email, wrongPassword);

        LocalAuthResponseDto first = await fixture.Service.AuthenticateAsync(
            invalid,
            CancellationToken.None);
        LocalAuthResponseDto second = await fixture.Service.AuthenticateAsync(
            invalid,
            CancellationToken.None);
        LocalAuthResponseDto afterLockout = await fixture.Service.AuthenticateAsync(
            new LocalAuthRequestDto(registration.Email, password),
            CancellationToken.None);

        await Assert.That(first.FailureCode).IsEqualTo("invalid_credentials");
        await Assert.That(second.FailureCode).IsEqualTo("account_locked");
        await Assert.That(afterLockout.FailureCode).IsEqualTo("account_locked");
    }

    [Test]
    public async Task RegistrationIssuesASecretBackedSignedToken()
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

        LocalRegistrationResponseDto result = await fixture.Service.RegisterAsync(
            new LocalRegistrationRequestDto(
                "admin@example.test",
                CreateValidPassword(),
                "Site",
                "Administrator"),
            CancellationToken.None);

        await Assert.That(result.Authentication).IsNotNull();
        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };
        handler.ValidateToken(
            result.Authentication!.Token,
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
            new("Data Source=:memory:");

        private ServiceProvider? _provider;
        private AsyncServiceScope? _scope;

        private TestFixture()
        {
        }

        internal UserManager<LocalIdentityUser> UserManager { get; private set; } = null!;
        internal LocalIdentityAuthService Service { get; private set; } = null!;

        internal static async Task<TestFixture> CreateAsync(
            ILocalJwtTokenGenerator tokenGenerator)
        {
            var fixture = new TestFixture();
            try
            {
                await fixture._connection.OpenAsync();
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddDbContext<ExploreDbContext>(options =>
                    options.UseSqlite(fixture._connection)
                        .UseSnakeCaseNamingConvention());
                services.AddIdentityCore<LocalIdentityUser>(options =>
                    {
                        options.User.RequireUniqueEmail = true;
                        options.Lockout.AllowedForNewUsers = true;
                        options.Lockout.MaxFailedAccessAttempts = 2;
                        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                    })
                    .AddRoles<LocalIdentityRole>()
                    .AddEntityFrameworkStores<ExploreDbContext>();
                fixture._provider = services.BuildIsolatedServiceProvider(validateScopes: true);
                fixture._scope = fixture._provider.CreateAsyncScope();
                IServiceProvider scopedServices = fixture._scope.Value.ServiceProvider;
                var context = scopedServices.GetRequiredService<ExploreDbContext>();
                await context.Database.EnsureCreatedAsync();
                fixture.UserManager = scopedServices
                    .GetRequiredService<UserManager<LocalIdentityUser>>();
                fixture.Service = new LocalIdentityAuthService(
                    fixture.UserManager,
                    tokenGenerator,
                    new FixedTimeProvider(Now));
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_scope is { } scope)
            {
                await scope.DisposeAsync();
            }

            if (_provider is not null)
            {
                await _provider.DisposeAsync();
            }

            await _connection.DisposeAsync();
        }
    }
}
