using Explore.Application.Contracts.Secrets;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Secrets.UnitTests.HealthChecks;

public sealed class SecretResolverHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_WhenRuntimeDatabaseSettingsAreInvalid_ReturnsSanitizedUnhealthyResult()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "UnsupportedProvider",
            ["Database:Host"] = "db.internal",
            ["Database:Database"] = "events",
            ["Database:Runtime:Username"] = "runtime",
            ["Database:Runtime:Password"] = "credential-canary"
        }).Build();
        var check = CreateCheck(
            Substitute.For<IInfisicalClientFactory>(),
            configuration);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).IsEqualTo("Secret resolver configuration is unavailable.");
        await Assert.That(result.Exception).IsNull();
        await Assert.That(result.Data["databaseConfiguration"]).IsEqualTo("invalid");
        await Assert.That(result.Description).DoesNotContain("credential-canary");
    }

    [Test]
    public async Task CheckHealthAsync_WhenSelectedInfisicalProviderFails_ReturnsBoundedUnhealthyState()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:Database"] = "health-check.db",
            ["SecretProvider:Provider"] = SecretProviderType.Infisical.ToString()
        }).Build();
        var factory = Substitute.For<IInfisicalClientFactory>();
        factory.GetClientAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IInfisicalClient?>>(_ => throw new InvalidOperationException("provider-secret-canary"));
        var check = CreateCheck(factory, configuration);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Data["providerState"]).IsEqualTo("unavailable");
        await Assert.That(result.Description).DoesNotContain("provider-secret-canary");
    }

    [Test]
    public async Task CheckHealthAsync_WhenSelectedInfisicalProviderIsUnconfigured_ReturnsUnhealthy()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:Database"] = "health-check.db",
            ["SecretProvider:Provider"] = SecretProviderType.Infisical.ToString()
        }).Build();
        var factory = Substitute.For<IInfisicalClientFactory>();
        factory.GetClientAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IInfisicalClient?>(null));
        var check = CreateCheck(factory, configuration);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Data["providerState"]).IsEqualTo("unavailable");
    }

    [Test]
    public async Task CheckHealthAsync_WhenUserSecretsIsSelected_DoesNotContactInfisical()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:Database"] = "health-check.db",
            ["SecretProvider:Provider"] = SecretProviderType.UserSecrets.ToString()
        }).Build();
        var factory = Substitute.For<IInfisicalClientFactory>();
        factory.GetClientAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IInfisicalClient?>>(_ => throw new InvalidOperationException("must-not-contact"));
        var check = CreateCheck(factory, configuration);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["providerState"]).IsEqualTo("available");
    }

    [Test]
    public async Task CheckHealthAsync_WhenUserSecretsIsSelectedInProduction_FailsClosed()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:Database"] = "health-check.db",
            ["SecretProvider:Provider"] = SecretProviderType.UserSecrets.ToString()
        }).Build();
        var check = CreateCheck(
            Substitute.For<IInfisicalClientFactory>(),
            configuration,
            "Production");

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Data["providerState"]).IsEqualTo("unavailable");
    }

    private static SecretResolverHealthCheck CreateCheck(
        IInfisicalClientFactory factory,
        IConfiguration configuration,
        string environmentName = "Testing") =>
        new(
            Substitute.For<ISecretResolver>(),
            factory,
            configuration,
            new UserSecretsAuthority(
                new TestHostEnvironment(environmentName),
                new ConfigurationBuilder().Build()),
            Substitute.For<ILogger<SecretResolverHealthCheck>>());

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Explore.Secrets.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
