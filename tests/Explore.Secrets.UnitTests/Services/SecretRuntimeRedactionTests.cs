using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Secrets.Observability;
using Explore.Secrets.Configuration;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Services;
using Explore.Secrets.Sources;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Secrets.UnitTests.Services;

public sealed class SecretRuntimeRedactionTests
{
    private const string ProviderBodyCanary = "provider-body-secret-canary";
    private const string EnvironmentCanary = "environment-coordinate-canary";
    private const string PathCanary = "/path-coordinate-canary";

    [Test]
    public async Task InfisicalFailureLogsContainOnlyBoundedReasonCodes()
    {
        var client = Substitute.For<IInfisicalClient>();
        client.GetSecretAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<string?>>(_ => throw new InvalidOperationException(ProviderBodyCanary));
        var factory = Substitute.For<IInfisicalClientFactory>();
        factory.GetClientAsync(Arg.Any<CancellationToken>()).Returns(client);
        var logger = new CollectingLogger<InfisicalSecretSource>();
        var source = new InfisicalSecretSource(factory, logger);
        SecretBinding binding = SecretBinding.CreateInfisical(
            SecretDefinitionRegistry.Keys.Smtp.Password,
            SecretScope.Instance,
            scopeId: null,
            EnvironmentCanary,
            PathCanary,
            "key-coordinate-canary");

        SecretResolutionResult result = await source.GetSecretAsync(binding, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(SecretResolutionStatus.Unavailable);
        await Assert.That(logger.Output).Contains("secret_source_unavailable");
        await Assert.That(logger.Output).DoesNotContain(ProviderBodyCanary);
        await Assert.That(logger.Output).DoesNotContain(EnvironmentCanary);
        await Assert.That(logger.Output).DoesNotContain(PathCanary);
    }

    [Test]
    public async Task ResolverFailureLogsDoNotIncludeProviderExceptionDetails()
    {
        SecretBinding binding = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.Smtp.Password,
            SecretScope.Instance,
            scopeId: null,
            "MAIL_SMTP_PASSWORD");
        var bindings = Substitute.For<ISecretBindingRepository>();
        bindings.GetByKeyAndScopeAsync(
                binding.SettingKey,
                SecretScope.Instance,
                null,
                Arg.Any<CancellationToken>())
            .Returns(binding);
        var source = Substitute.For<ISecretSource>();
        source.SourceType.Returns(SecretSourceType.EnvironmentVariable);
        source.GetSecretAsync(binding, Arg.Any<CancellationToken>())
            .Returns<Task<SecretResolutionResult>>(_ => throw new InvalidOperationException(ProviderBodyCanary));
        var logger = new CollectingLogger<SecretResolver>();
        var resolver = new SecretResolver(
            bindings,
            [source],
            new MemoryCache(new MemoryCacheOptions()),
            new SecretResolverMetrics(new TestMeterFactory()),
            logger,
            Options.Create(new SecretProviderOptions { Provider = SecretProviderType.Environment }));

        SecretResolutionResult result = await resolver.ResolveAsync(
            binding.SettingKey,
            tenantId: null,
            CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(SecretResolutionStatus.Unavailable);
        await Assert.That(logger.Output).Contains("secret_source_unavailable");
        await Assert.That(logger.Output).DoesNotContain(ProviderBodyCanary);
    }

    [Test]
    public async Task AuthorityStatusSuppressesProviderFailureDetails()
    {
        var provider = Substitute.For<ISecretProvider>();
        provider.GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns<Task<ProviderHealthInfo>>(_ => throw new InvalidOperationException(ProviderBodyCanary));
        var reader = new SecretAuthorityStatusReader(
            provider,
            Options.Create(new SecretProviderOptions { Provider = SecretProviderType.Environment }));

        SecretAuthorityStatusSnapshot status = await reader.ReadAsync(CancellationToken.None);

        await Assert.That(status.Status).IsEqualTo("degraded");
        await Assert.That(status.ToString()).DoesNotContain(ProviderBodyCanary);
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        private readonly List<string> _entries = [];

        public string Output => string.Join('\n', _entries);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add(formatter(state, exception));
            if (exception is not null)
            {
                _entries.Add(exception.ToString());
            }
        }
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options.Name ?? "test");
        public void Dispose() { }
    }
}
