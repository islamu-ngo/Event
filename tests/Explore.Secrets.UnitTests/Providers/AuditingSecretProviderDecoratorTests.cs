using System.Security.Claims;
using Explore.Application.Constants;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Secrets.UnitTests.Providers;

public sealed class AuditingSecretProviderDecoratorTests
{
    private readonly ISecretProvider _inner = Substitute.For<ISecretProvider>();
    private readonly List<SecretAuditEntry> _entries = [];
    private readonly AuditingSecretProviderDecorator _decorator;

    public AuditingSecretProviderDecoratorTests()
    {
        var audit = Substitute.For<ISecretAuditLogger>();
        audit.LogAsync(Arg.Any<SecretAuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => _entries.Add(call.Arg<SecretAuditEntry>()));
        _decorator = new AuditingSecretProviderDecorator(
            _inner,
            audit,
            Substitute.For<ILogger<AuditingSecretProviderDecorator>>(),
            clock: new SecretsFixedTimeProvider());
    }

    [Test]
    public async Task InitializeAndRefreshCreateMutationAuditEntries()
    {
        _inner.ProviderType.Returns(SecretProviderType.Infisical);

        await _decorator.InitializeAsync();
        await _decorator.RefreshAsync();

        await Assert.That(_entries.Select(entry => entry.Operation))
            .IsEquivalentTo([SecretOperation.Initialize, SecretOperation.Refresh]);
        await Assert.That(_entries.All(entry => entry.Success)).IsTrue();
    }

    [Test]
    public async Task ProviderFailuresPersistOnlyBoundedCodes()
    {
        _inner.ProviderType.Returns(SecretProviderType.Infisical);
        _inner.InitializeAsync(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("provider-response-canary"));

        var action = () => _decorator.InitializeAsync();

        await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(_entries.Single().ErrorMessage)
            .IsEqualTo("secret_provider_initialization_failed");
    }

    [Test]
    public async Task ReadSurfacesNeverCreateAuditEntries()
    {
        _inner.GetSecretAsync("Database:Password", Arg.Any<CancellationToken>())
            .Returns("secret-canary");
        _inner.GetSecretWithMetadataAsync("Database:Password", Arg.Any<CancellationToken>())
            .Returns(new SecretValue("secret-canary"));
        _inner.GetSecretsByPathAsync("Database", Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string> { ["Database:Password"] = "secret-canary" });

        _ = await _decorator.GetSecretAsync("Database:Password");
        _ = await _decorator.GetSecretWithMetadataAsync("Database:Password");
        _ = await _decorator.GetSecretsByPathAsync("Database");

        await Assert.That(_entries).IsEmpty();
    }

    [Test]
    public async Task HealthReadsNeverCreateAuditEntries()
    {
        var expected = new ProviderHealthInfo(
            IsHealthy: true,
            ProviderType: SecretProviderType.Environment,
            LastSuccessfulRefresh: null,
            ConsecutiveFailures: 0);
        _inner.GetHealthAsync(Arg.Any<CancellationToken>()).Returns(expected);

        ProviderHealthInfo actual = await _decorator.GetHealthAsync();

        await Assert.That(actual).IsEquivalentTo(expected);
        await Assert.That(_entries).IsEmpty();
    }

    [Test]
    public async Task MutationAuditUsesCanonicalConflictingClaimPriority()
    {
        var subUserId = Guid.NewGuid();
        var internalUserId = Guid.NewGuid();
        var decorator = CreateDecorator(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", subUserId.ToString("D")),
            new Claim("internal_user_id", internalUserId.ToString("D"))
        ], "Bearer")));

        await decorator.InitializeAsync();

        await Assert.That(_entries.Single().UserId).IsEqualTo(subUserId.ToString("D"));
    }

    [Test]
    public async Task MutationAuditPurposeBoundPrincipalHasNoAmbientUser()
    {
        var decorator = CreateDecorator(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString("D"))],
            ApiAuthenticationSchemeNames.ApiKey)));

        await decorator.InitializeAsync();

        await Assert.That(_entries.Single().UserId).IsNull();
    }

    private AuditingSecretProviderDecorator CreateDecorator(ClaimsPrincipal principal)
    {
        var audit = Substitute.For<ISecretAuditLogger>();
        audit.LogAsync(Arg.Any<SecretAuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => _entries.Add(call.Arg<SecretAuditEntry>()));
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
        return new AuditingSecretProviderDecorator(
            _inner,
            audit,
            Substitute.For<ILogger<AuditingSecretProviderDecorator>>(),
            accessor,
            new SecretsFixedTimeProvider());
    }
}
