using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Models;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Mail;
using Explore.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.Email)]
public sealed class SmtpConfigResolverTests : IDisposable
{
    private readonly IHierarchicalSettingsResolver _settings = Substitute.For<IHierarchicalSettingsResolver>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ISecretResolver _secrets = Substitute.For<ISecretResolver>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly Guid _tenantId = Guid.NewGuid();

    public SmtpConfigResolverTests() => _tenant.TenantId.Returns(_tenantId);

    public void Dispose() => _cache.Dispose();

    [Test]
    public async Task ResolveAsync_UnconfiguredCredentials_AllowsAnonymousSmtp()
    {
        ConfigureGovernance();
        _secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Unconfigured);

        var result = await CreateResolver().ResolveAsync();

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Username).IsNull();
        await Assert.That(result.Password).IsNull();
    }

    [Test]
    public async Task ResolveAsync_ResolvedCredentials_ComposesRuntimeConfiguration()
    {
        ConfigureGovernance();
        var username = $"user-{Guid.NewGuid():N}";
        var password = Guid.NewGuid().ToString("N");
        _secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Smtp.Username, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Resolved(SecretDefinitionRegistry.Keys.Smtp.Username, username));
        _secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Smtp.Password, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Resolved(SecretDefinitionRegistry.Keys.Smtp.Password, password));

        var result = await CreateResolver().ResolveAsync();

        await Assert.That(result!.Username).IsEqualTo(username);
        await Assert.That(result.Password).IsEqualTo(password);
        await Assert.That(result.Security).IsEqualTo(SmtpSecurityMode.StartTls);
    }

    [Test]
    public async Task ResolveAsync_UnauthorizedCredential_FailsClosed()
    {
        ConfigureGovernance();
        _secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Unauthorized);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateResolver().ResolveAsync());

        await Assert.That(exception.Message).IsEqualTo("smtp_secret_unavailable");
    }

    private SmtpConfigResolver CreateResolver() => new(
        _settings,
        _tenant,
        _cache,
        _secrets,
        Substitute.For<ILogger<SmtpConfigResolver>>());

    private void ConfigureGovernance()
    {
        _settings.ResolveAsync<string>(GovernanceSettingKeys.Email.SmtpHost, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("smtp.example.test");
        _settings.ResolveAsync<string>(GovernanceSettingKeys.Email.FromAddress, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("noreply@example.test");
        _settings.ResolveAsync<string>(GovernanceSettingKeys.Email.FromName, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("Events");
        _settings.ResolveAsync<string>(GovernanceSettingKeys.Email.SmtpSecurity, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("StartTls");
        _settings.ResolveAsync<int>(GovernanceSettingKeys.Email.SmtpPort, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(587);
        _settings.ResolveAsync<int>(GovernanceSettingKeys.Email.SmtpTimeoutSeconds, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(30);
    }

    private SecretResolutionResult Resolved(string key, string value) => SecretResolutionResult.Resolved(new ResolvedSecret(
        key,
        value,
        SecretSourceType.EnvironmentVariable,
        SecretScope.Tenant,
        _tenantId,
        DateTimeOffset.UtcNow));
}
