using Explore.Application.Contracts.Secrets;
using Explore.Infrastructure.Secrets;
using Explore.Persistence.HealthChecks;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class SecretResolverHealthCheckTests
{
    [Test]
    public async Task InvalidDatabaseSettingsReturnSanitizedUnhealthyResult()
    {
        string canary = Guid.NewGuid().ToString("N");
        using var configuration = Configuration("UnsupportedProvider");
        configuration["Database:Runtime:Password"] = canary;
        var result = await Check(Substitute.For<ISecretAuthorityStatusReader>(), configuration)
            .CheckHealthAsync(new HealthCheckContext());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Exception).IsNull();
        await Assert.That(result.Data["databaseConfiguration"]).IsEqualTo("invalid");
        await Assert.That(result.Description!.Contains(canary, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task AuthorityFailureCannotExportItsExceptionOrDiagnostic()
    {
        string canary = Guid.NewGuid().ToString("N") + "\r\nforged-health-event";
        using var configuration = Configuration();
        var logger = new SecretDiagnosticLog<SecretResolverHealthCheck>();
        var authority = Substitute.For<ISecretAuthorityStatusReader>();
        authority.ReadAsync(Arg.Any<CancellationToken>())
            .Returns<Task<SecretAuthorityStatusSnapshot>>(_ => throw new PrivateProviderException(canary));
        var check = new SecretResolverHealthCheck(Substitute.For<ISecretResolver>(), authority, configuration, logger);
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Data["providerState"]).IsEqualTo("unavailable");
        await Assert.That(result.Exception).IsNull();
        await Assert.That(result.Description!.Contains(canary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Join(" ", result.Data.Values).Contains(canary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(logger.Output).Contains("secret_provider_unavailable");
        await Assert.That(logger.Output).DoesNotContain(canary[..32]);
        await Assert.That(logger.Output).DoesNotContain(nameof(PrivateProviderException));
    }

    [Test]
    public async Task DegradedAuthorityCannotBeReportedAvailable()
    {
        using var configuration = Configuration();
        var authority = Substitute.For<ISecretAuthorityStatusReader>();
        authority.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new SecretAuthorityStatusSnapshot("Infisical", "degraded", "restore_selected_secret_authority"));
        var result = await Check(authority, configuration).CheckHealthAsync(new HealthCheckContext());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Data["providerState"]).IsEqualTo("unavailable");
    }

    [Test]
    [Arguments(SecretAuthorityKind.Environment)]
    [Arguments(SecretAuthorityKind.UserSecrets)]
    public async Task AdmittedLocalAuthorityNeedsNoRemoteConnection(SecretAuthorityKind kind)
    {
        using var configuration = Configuration();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(kind, "Testing", SecretConsumerRole.Api), configuration);
        var authority = new SecretAuthorityStatusReader(owner);
        var result = await Check(authority, configuration).CheckHealthAsync(new HealthCheckContext());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["providerState"]).IsEqualTo("available");
        await owner.DisposeAsync();
        var retired = await Check(authority, configuration).CheckHealthAsync(new HealthCheckContext());
        await Assert.That(retired.Status).IsEqualTo(HealthStatus.Unhealthy);
    }

    [Test]
    public async Task ProductionUserSecretsCannotCreateAnAuthorityForTheHealthPipeline()
    {
        using var configuration = Configuration();
        await Assert.That(() => SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.UserSecrets, "Production", SecretConsumerRole.Api), configuration))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CallerCancellationIsNotRewrittenAsAnUnhealthyProvider()
    {
        using var configuration = Configuration();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var authority = Substitute.For<ISecretAuthorityStatusReader>();
        authority.ReadAsync(Arg.Any<CancellationToken>())
            .Returns<Task<SecretAuthorityStatusSnapshot>>(_ => throw new OperationCanceledException(cancellation.Token));
        await Assert.That(async () => await Check(authority, configuration)
            .CheckHealthAsync(new HealthCheckContext(), cancellation.Token)).Throws<OperationCanceledException>();
    }

    private sealed class PrivateProviderException(string message) : Exception(message);

    private static SecretResolverHealthCheck Check(ISecretAuthorityStatusReader authority, IConfiguration configuration) =>
        new(Substitute.For<ISecretResolver>(), authority, configuration,
            Substitute.For<ILogger<SecretResolverHealthCheck>>());

    private static ConfigurationRoot Configuration(string provider = "Sqlite") =>
        (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = provider,
            ["Database:Database"] = "health-check.db"
        }).Build();
}
