namespace Explore.Secrets.UnitTests.SetupLive;

using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.SetupLive;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Secrets.Configuration;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Extensions;
using Explore.Secrets.Infrastructure;
using Explore.Secrets.Services;
using Infisical.Sdk.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

public sealed class SetupSecretBindingAuthorityTests
{
    [Test]
    [Arguments(SecretProviderType.Infisical, SecretSourceType.Infisical,
        SetupSecretBindingWriteOutcome.Ready, 1)]
    [Arguments(SecretProviderType.Environment, SecretSourceType.EnvironmentVariable,
        SetupSecretBindingWriteOutcome.Unavailable, 0)]
    [Arguments(SecretProviderType.UserSecrets, SecretSourceType.EnvironmentVariable,
        SetupSecretBindingWriteOutcome.Unavailable, 0)]
    [Arguments(SecretProviderType.Infisical, SecretSourceType.EnvironmentVariable,
        SetupSecretBindingWriteOutcome.Invalid, 0)]
    [Arguments(SecretProviderType.Environment, SecretSourceType.Infisical,
        SetupSecretBindingWriteOutcome.Invalid, 0)]
    [Arguments(SecretProviderType.UserSecrets, SecretSourceType.Infisical,
        SetupSecretBindingWriteOutcome.Invalid, 0)]
    public async Task RegisteredWriterUsesOnlyTheSelectedStoredAuthority(
        SecretProviderType selectedProvider,
        SecretSourceType storedSource,
        SetupSecretBindingWriteOutcome expectedOutcome,
        int expectedWrites)
    {
        SecretBinding binding = Binding(storedSource);
        ISecretBindingRepository repository = Substitute.For<ISecretBindingRepository>();
        repository.GetById(binding.Id).Returns(binding);
        var client = new RecordingInfisicalClient();
        await using ServiceProvider provider = Services(
            selectedProvider,
            repository,
            new FixedInfisicalClientFactory(client))
            .BuildServiceProvider();
        ISetupSecretBindingWriter writer =
            provider.GetRequiredService<ISetupSecretBindingWriter>();
        ISetupSecretBindingReadinessReader readinessReader =
            provider.GetRequiredService<ISetupSecretBindingReadinessReader>();
        byte[] secret = Encoding.UTF8.GetBytes(
            $"secret-{Guid.CreateVersion7():N}");
        try
        {
            var request = new SetupSecretBindingWriteRequest(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                1,
                Guid.CreateVersion7(),
                binding.Id,
                "setup.signing",
                secret);

            SetupSecretBindingWriteOutcome readiness =
                await readinessReader.GetReadinessAsync(
                    binding.Id,
                    "setup.signing",
                    CancellationToken.None);

            SetupSecretBindingWriteOutcome outcome = await writer.WriteAsync(
                request,
                CancellationToken.None);

            await Assert.That(readiness).IsEqualTo(expectedOutcome);
            await Assert.That(outcome).IsEqualTo(expectedOutcome);
            await Assert.That(client.WriteCount).IsEqualTo(expectedWrites);
            await Assert.That(client.ReadCount).IsEqualTo(0);
            if (expectedWrites == 1)
            {
                await Assert.That(client.LastEnvironment)
                    .IsEqualTo(binding.InfisicalEnvironment);
                await Assert.That(client.LastPath).IsEqualTo(binding.InfisicalPath);
                await Assert.That(client.LastName).IsEqualTo(binding.InfisicalKey);
                await Assert.That(client.LastSecretDigest)
                    .IsEqualTo(Digest(secret));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    [Arguments(WriterFailure.NullClient, SetupSecretBindingWriteOutcome.Unavailable, 0)]
    [Arguments(WriterFailure.FalseResult, SetupSecretBindingWriteOutcome.Unavailable, 1)]
    [Arguments(WriterFailure.Transient, SetupSecretBindingWriteOutcome.Unavailable, 1)]
    [Arguments(WriterFailure.Unauthorized, SetupSecretBindingWriteOutcome.Unauthorized, 1)]
    [Arguments(WriterFailure.Forbidden, SetupSecretBindingWriteOutcome.Unauthorized, 1)]
    public async Task RegisteredWriterTranslatesSelectedInfisicalFailuresWithoutFallback(
        WriterFailure failure,
        SetupSecretBindingWriteOutcome expected,
        int expectedWrites)
    {
        SecretBinding binding = Binding(SecretSourceType.Infisical);
        string environmentCanary = $"environment-{Guid.CreateVersion7():N}";
        string pathCanary = $"/path-{Guid.CreateVersion7():N}";
        string keyCanary = $"KEY_{Guid.CreateVersion7():N}";
        string exceptionCanary = $"exception-{Guid.CreateVersion7():N}";
        binding.InfisicalEnvironment = environmentCanary;
        binding.InfisicalPath = pathCanary;
        binding.InfisicalKey = keyCanary;
        ISecretBindingRepository repository = Substitute.For<ISecretBindingRepository>();
        repository.GetById(binding.Id).Returns(binding);
        var logs = new CaptureLoggerProvider();
        var client = new RecordingInfisicalClient
        {
            WriteResult = failure != WriterFailure.FalseResult,
            WriteException = failure switch
            {
                WriterFailure.Transient => new TimeoutException(exceptionCanary),
                WriterFailure.Unauthorized => new HttpRequestException(
                    exceptionCanary, null, HttpStatusCode.Unauthorized),
                WriterFailure.Forbidden => new HttpRequestException(
                    exceptionCanary, null, HttpStatusCode.Forbidden),
                _ => null
            }
        };
        var factory = new FixedInfisicalClientFactory(
            failure == WriterFailure.NullClient ? null : client);
        await using ServiceProvider provider = Services(
            SecretProviderType.Infisical,
            repository,
            factory,
            logs: logs)
            .BuildServiceProvider();
        ISetupSecretBindingWriter writer =
            provider.GetRequiredService<ISetupSecretBindingWriter>();
        byte[] secret = Encoding.UTF8.GetBytes(
            $"secret-{Guid.CreateVersion7():N}");
        try
        {
            SetupSecretBindingWriteOutcome outcome = await writer.WriteAsync(
                WriteRequest(binding, secret),
                CancellationToken.None);

            await Assert.That(outcome).IsEqualTo(expected);
            await Assert.That(client.WriteCount).IsEqualTo(expectedWrites);
            await Assert.That(client.ReadCount).IsEqualTo(0);
            await Assert.That(outcome.ToString()).DoesNotContain(binding.InfisicalPath!);
            await Assert.That(outcome.ToString()).DoesNotContain(binding.InfisicalKey!);
            await Assert.That(outcome.ToString()).DoesNotContain(Digest(secret));
            await AssertNoCapturedValue(
                logs,
                environmentCanary,
                pathCanary,
                keyCanary,
                exceptionCanary,
                binding.Id.ToString("D"),
                binding.SettingKey,
                Digest(secret));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    public async Task RegisteredWriterPropagatesCancellationWithoutProviderFallback()
    {
        SecretBinding binding = Binding(SecretSourceType.Infisical);
        ISecretBindingRepository repository = Substitute.For<ISecretBindingRepository>();
        repository.GetById(binding.Id).Returns(binding);
        var client = new RecordingInfisicalClient();
        await using ServiceProvider provider = Services(
            SecretProviderType.Infisical,
            repository,
            new FixedInfisicalClientFactory(client))
            .BuildServiceProvider();
        ISetupSecretBindingWriter writer =
            provider.GetRequiredService<ISetupSecretBindingWriter>();
        byte[] secret = Encoding.UTF8.GetBytes(
            $"secret-{Guid.CreateVersion7():N}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await Assert.That(async () => await writer.WriteAsync(
                    WriteRequest(binding, secret),
                    cancellation.Token))
                .Throws<OperationCanceledException>();
            await Assert.That(client.WriteCount).IsEqualTo(0);
            await Assert.That(client.ReadCount).IsEqualTo(0);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    public async Task RegisteredWriterRejectsMalformedUtf8BeforeProviderDispatch()
    {
        SecretBinding binding = Binding(SecretSourceType.Infisical);
        ISecretBindingRepository repository = Substitute.For<ISecretBindingRepository>();
        repository.GetById(binding.Id).Returns(binding);
        var client = new RecordingInfisicalClient();
        var logs = new CaptureLoggerProvider();
        await using ServiceProvider provider = Services(
            SecretProviderType.Infisical,
            repository,
            new FixedInfisicalClientFactory(client),
            logs: logs)
            .BuildServiceProvider();
        ISetupSecretBindingWriter writer =
            provider.GetRequiredService<ISetupSecretBindingWriter>();
        byte[] malformedUtf8 = [0xC3, 0x28];
        try
        {
            SetupSecretBindingWriteOutcome outcome = await writer.WriteAsync(
                WriteRequest(binding, malformedUtf8),
                CancellationToken.None);

            await Assert.That(outcome)
                .IsEqualTo(SetupSecretBindingWriteOutcome.Invalid);
            await Assert.That(client.WriteCount).IsEqualTo(0);
            await Assert.That(client.ReadCount).IsEqualTo(0);
            await Assert.That(logs.Snapshot()).IsEmpty();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(malformedUtf8);
        }
    }

    [Test]
    public async Task InfisicalFacadeDispatchesExactUpdateAndBoundsCancellation()
    {
        UpdateSecretOptions? captured = null;
        int calls = 0;
        var facade = new InfisicalClientFactory.InfisicalClientFacade(
            "project-canary",
            NullLogger.Instance,
            options =>
            {
                Interlocked.Increment(ref calls);
                captured = options;
                return Task.FromResult(true);
            });
        string secretText = $"secret-{Guid.CreateVersion7():N}";
        byte[] secret = Encoding.UTF8.GetBytes(secretText);
        try
        {
            bool written = await facade.WriteSecretAsync(
                "staging-canary",
                "/setup-canary",
                "SETUP_SIGNING_CANARY",
                secret,
                CancellationToken.None);

            await Assert.That(written).IsTrue();
            await Assert.That(calls).IsEqualTo(1);
            await Assert.That(captured).IsNotNull();
            await Assert.That(captured!.ProjectId).IsEqualTo("project-canary");
            await Assert.That(captured.EnvironmentSlug).IsEqualTo("staging-canary");
            await Assert.That(captured.SecretPath).IsEqualTo("/setup-canary");
            await Assert.That(captured.SecretName).IsEqualTo("SETUP_SIGNING_CANARY");
            await Assert.That(captured.NewSecretValue).IsEqualTo(secretText);

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.That(async () => await facade.WriteSecretAsync(
                    "staging-canary",
                    "/setup-canary",
                    "SETUP_SIGNING_CANARY",
                    secret,
                    cancellation.Token))
                .Throws<OperationCanceledException>();
            await Assert.That(calls).IsEqualTo(1);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    public async Task RegisteredCommitmentAuthorityUsesCanonicalPurposeAndSelectedResolver()
    {
        byte[] keyBytes = RandomNumberGenerator.GetBytes(32);
        byte[] secret = RandomNumberGenerator.GetBytes(64);
        string key = Convert.ToBase64String(keyBytes);
        var resolver = new RecordingCommitmentResolver(key);
        ISecretBindingRepository repository = Substitute.For<ISecretBindingRepository>();
        await using ServiceProvider provider = Services(
            SecretProviderType.Environment,
            repository,
            new FixedInfisicalClientFactory(new RecordingInfisicalClient()),
            resolver)
            .BuildServiceProvider();
        ISetupSecretBindingCommitmentAuthority authority =
            provider.GetRequiredService<ISetupSecretBindingCommitmentAuthority>();
        var request = new SetupSecretBindingCommitmentRequest(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            7,
            Guid.CreateVersion7(),
            "setup.encryption",
            secret);
        try
        {
            SetupSecretBindingCommitment commitment = await authority.CommitAsync(
                request,
                CancellationToken.None);

            await Assert.That(commitment.KeyVersion).IsEqualTo(1);
            await Assert.That(commitment.Commitment)
                .IsEqualTo(ExpectedCommitment(keyBytes, request));
            await Assert.That(resolver.CallCount).IsEqualTo(1);
            await Assert.That(resolver.LastSettingKey)
                .IsEqualTo(SetupSecretBindingContractMetadata.CommitmentAuthorityKey);
            await Assert.That(resolver.LastTenantId).IsNull();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    [Arguments(SecretResolutionStatus.Unconfigured)]
    [Arguments(SecretResolutionStatus.Unavailable)]
    [Arguments(SecretResolutionStatus.Unauthorized)]
    [Arguments(SecretResolutionStatus.Invalid)]
    public async Task CommitmentAuthorityFailsClosedForEveryUnresolvedKeyStatus(
        SecretResolutionStatus status)
    {
        var resolver = new RecordingCommitmentResolver(Result(status));
        ISecretBindingRepository repository = Substitute.For<ISecretBindingRepository>();
        await using ServiceProvider provider = Services(
            SecretProviderType.Environment,
            repository,
            new FixedInfisicalClientFactory(new RecordingInfisicalClient()),
            resolver)
            .BuildServiceProvider();
        ISetupSecretBindingCommitmentAuthority authority =
            provider.GetRequiredService<ISetupSecretBindingCommitmentAuthority>();
        byte[] secret = RandomNumberGenerator.GetBytes(64);
        try
        {
            await Assert.That(async () => await authority.CommitAsync(
                    CommitmentRequest(secret),
                    CancellationToken.None))
                .Throws<InvalidOperationException>();
            await Assert.That(resolver.CallCount).IsEqualTo(1);
            await Assert.That(resolver.LastSettingKey)
                .IsEqualTo(SetupSecretBindingContractMetadata.CommitmentAuthorityKey);
            await Assert.That(resolver.LastTenantId).IsNull();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    public async Task CommitmentAuthorityRejectsMalformedOrWeakKeys()
    {
        string[] invalidKeys =
        [
            "not-base64",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(31))
        ];
        foreach (string invalidKey in invalidKeys)
        {
            var resolver = new RecordingCommitmentResolver(invalidKey);
            ISecretBindingRepository repository = Substitute.For<ISecretBindingRepository>();
            await using ServiceProvider provider = Services(
                SecretProviderType.Environment,
                repository,
                new FixedInfisicalClientFactory(new RecordingInfisicalClient()),
                resolver)
                .BuildServiceProvider();
            ISetupSecretBindingCommitmentAuthority authority =
                provider.GetRequiredService<ISetupSecretBindingCommitmentAuthority>();
            byte[] secret = RandomNumberGenerator.GetBytes(64);
            try
            {
                await Assert.That(async () => await authority.CommitAsync(
                        CommitmentRequest(secret),
                        CancellationToken.None))
                    .Throws<InvalidOperationException>();
                await Assert.That(resolver.CallCount).IsEqualTo(1);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }
    }

    [Test]
    public async Task CommitmentAuthorityPropagatesResolverCancellation()
    {
        var resolver = new RecordingCommitmentResolver(
            new OperationCanceledException());
        ISecretBindingRepository repository = Substitute.For<ISecretBindingRepository>();
        await using ServiceProvider provider = Services(
            SecretProviderType.Environment,
            repository,
            new FixedInfisicalClientFactory(new RecordingInfisicalClient()),
            resolver)
            .BuildServiceProvider();
        ISetupSecretBindingCommitmentAuthority authority =
            provider.GetRequiredService<ISetupSecretBindingCommitmentAuthority>();
        byte[] secret = RandomNumberGenerator.GetBytes(64);
        try
        {
            await Assert.That(async () => await authority.CommitAsync(
                    CommitmentRequest(secret),
                    CancellationToken.None))
                .Throws<OperationCanceledException>();
            await Assert.That(resolver.CallCount).IsEqualTo(1);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    [Arguments(SecretProviderType.Environment, SecretSourceType.EnvironmentVariable)]
    [Arguments(SecretProviderType.Infisical, SecretSourceType.Infisical)]
    public async Task RegisteredCommitmentAuthorityUsesRealResolverAndCanonicalDefinition(
        SecretProviderType selectedProvider,
        SecretSourceType selectedSource)
    {
        byte[] keyBytes = RandomNumberGenerator.GetBytes(32);
        byte[] secret = RandomNumberGenerator.GetBytes(64);
        var environment = new RecordingSecretSource(
            SecretSourceType.EnvironmentVariable,
            Convert.ToBase64String(keyBytes));
        var infisical = new RecordingSecretSource(
            SecretSourceType.Infisical,
            Convert.ToBase64String(keyBytes));
        ISecretBindingRepository repository = Substitute.For<ISecretBindingRepository>();
        await using ServiceProvider provider = RealResolverServices(
            selectedProvider,
            repository,
            environment,
            infisical)
            .BuildServiceProvider();
        ISetupSecretBindingCommitmentAuthority authority =
            provider.GetRequiredService<ISetupSecretBindingCommitmentAuthority>();
        var request = CommitmentRequest(secret);
        try
        {
            SetupSecretBindingCommitment commitment = await authority.CommitAsync(
                request,
                CancellationToken.None);
            SecretDefinition definition = SecretDefinitionRegistry.GetRequired(
                SetupSecretBindingContractMetadata.CommitmentAuthorityKey);

            await Assert.That(commitment.Commitment)
                .IsEqualTo(ExpectedCommitment(keyBytes, request));
            await Assert.That(definition.AllowedScopes)
                .IsEquivalentTo([SecretScope.Instance]);
            await Assert.That(definition.AllowedSources)
                .IsEquivalentTo([
                    SecretSourceType.Infisical,
                    SecretSourceType.EnvironmentVariable]);
            await Assert.That(definition.DefaultInfisicalPath).IsEqualTo("/setup");
            await Assert.That(definition.DefaultInfisicalKey)
                .IsEqualTo("SETUP_SECRET_BINDING_COMMITMENT_HMAC_KEY");
            await Assert.That(definition.DefaultEnvironmentVariableName)
                .IsEqualTo("SETUP_SECRET_BINDING_COMMITMENT_HMAC_KEY");
            await Assert.That(environment.CallCount)
                .IsEqualTo(selectedSource == SecretSourceType.EnvironmentVariable ? 1 : 0);
            await Assert.That(infisical.CallCount)
                .IsEqualTo(selectedSource == SecretSourceType.Infisical ? 1 : 0);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    [Arguments(SecretProviderType.Environment, SecretSourceType.Infisical)]
    [Arguments(SecretProviderType.UserSecrets, SecretSourceType.Infisical)]
    [Arguments(SecretProviderType.Infisical, SecretSourceType.EnvironmentVariable)]
    public async Task RealResolverRejectsCommitmentAuthorityMismatchWithoutSourceAccess(
        SecretProviderType selectedProvider,
        SecretSourceType storedSource)
    {
        SecretBinding binding = CommitmentBinding(storedSource);
        ISecretBindingRepository repository = Substitute.For<ISecretBindingRepository>();
        repository.GetByKeyAndScopeAsync(
                SetupSecretBindingContractMetadata.CommitmentAuthorityKey,
                SecretScope.Instance,
                null,
                Arg.Any<CancellationToken>())
            .Returns(binding);
        var environment = new RecordingSecretSource(
            SecretSourceType.EnvironmentVariable,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var infisical = new RecordingSecretSource(
            SecretSourceType.Infisical,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        await using ServiceProvider provider = RealResolverServices(
            selectedProvider,
            repository,
            environment,
            infisical)
            .BuildServiceProvider();
        ISetupSecretBindingCommitmentAuthority authority =
            provider.GetRequiredService<ISetupSecretBindingCommitmentAuthority>();
        byte[] secret = RandomNumberGenerator.GetBytes(64);
        try
        {
            await Assert.That(async () => await authority.CommitAsync(
                    CommitmentRequest(secret),
                    CancellationToken.None))
                .Throws<InvalidOperationException>();
            await Assert.That(environment.CallCount).IsEqualTo(0);
            await Assert.That(infisical.CallCount).IsEqualTo(0);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    [Arguments(CommitmentResolverScenario.Success)]
    [Arguments(CommitmentResolverScenario.Mismatch)]
    [Arguments(CommitmentResolverScenario.Failure)]
    public async Task RealCommitmentResolverEmitsNoSourceOrProviderMetadata(
        CommitmentResolverScenario scenario)
    {
        byte[] keyBytes = RandomNumberGenerator.GetBytes(32);
        byte[] secret = RandomNumberGenerator.GetBytes(64);
        var logs = new CaptureLoggerProvider();
        using var metrics = new MetricCapture();
        SecretSourceType storedSource = scenario == CommitmentResolverScenario.Mismatch
            ? SecretSourceType.Infisical
            : SecretSourceType.EnvironmentVariable;
        SecretBinding binding = CommitmentBinding(storedSource);
        ISecretBindingRepository repository = Substitute.For<ISecretBindingRepository>();
        repository.GetByKeyAndScopeAsync(
                SetupSecretBindingContractMetadata.CommitmentAuthorityKey,
                SecretScope.Instance,
                null,
                Arg.Any<CancellationToken>())
            .Returns(binding);
        SecretResolutionResult result = scenario == CommitmentResolverScenario.Failure
            ? SecretResolutionResult.Unavailable
            : SecretResolutionResult.Resolved(new ResolvedSecret(
                binding.SettingKey,
                Convert.ToBase64String(keyBytes),
                SecretSourceType.EnvironmentVariable,
                SecretScope.Instance,
                null,
                DateTime.UtcNow));
        ServiceCollection services = Services(
            SecretProviderType.Environment,
            repository,
            new FixedInfisicalClientFactory(new RecordingInfisicalClient()),
            logs: logs);
        services.RemoveAll<ISecretSource>();
        services.AddSingleton<ISecretSource>(
            new OutcomeSecretSource(SecretSourceType.EnvironmentVariable, result));
        await using ServiceProvider provider = services.BuildServiceProvider();
        ISetupSecretBindingCommitmentAuthority authority =
            provider.GetRequiredService<ISetupSecretBindingCommitmentAuthority>();
        try
        {
            if (scenario == CommitmentResolverScenario.Success)
            {
                _ = await authority.CommitAsync(
                    CommitmentRequest(secret),
                    CancellationToken.None);
            }
            else
            {
                await Assert.That(async () => await authority.CommitAsync(
                        CommitmentRequest(secret),
                        CancellationToken.None))
                    .Throws<InvalidOperationException>();
            }

            string[] captured = [.. logs.Snapshot(), .. metrics.Snapshot()];
            await Assert.That(captured.Any(value =>
                    value.StartsWith("SourceType=", StringComparison.Ordinal)
                    || value.StartsWith("metric:source=", StringComparison.Ordinal)
                    || value.StartsWith("metric:provider=", StringComparison.Ordinal)))
                .IsFalse();
            await AssertNoCapturedValue(
                logs,
                binding.Id.ToString("D"),
                binding.SettingKey,
                binding.EnvironmentVariableName ?? string.Empty,
                binding.InfisicalEnvironment ?? string.Empty,
                binding.InfisicalPath ?? string.Empty,
                binding.InfisicalKey ?? string.Empty);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private static ServiceCollection Services(
        SecretProviderType provider,
        ISecretBindingRepository repository,
        IInfisicalClientFactory clientFactory,
        ISecretResolver? resolver = null,
        ILoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            if (logs is not null)
                builder.AddProvider(logs);
        });
        services.AddOptions<SecretProviderOptions>().Configure(options =>
        {
            options.Provider = provider;
            options.Infisical.Environment = "staging";
        });
        services.AddSingleton(repository);
        services.AddSingleton(clientFactory);
        if (resolver is not null)
            services.AddSingleton(resolver);
        services.AddSecretResolution();
        return services;
    }

    [Test]
    public async Task SecretsWriteAndCommitmentPathsEmitNoTelemetry()
    {
        var logs = new CaptureLoggerProvider();
        string environmentCanary = $"environment-{Guid.CreateVersion7():N}";
        string pathCanary = $"/path-{Guid.CreateVersion7():N}";
        string keyCanary = $"KEY_{Guid.CreateVersion7():N}";
        string exceptionCanary = $"exception-{Guid.CreateVersion7():N}";
        string secretCanary = $"secret-{Guid.CreateVersion7():N}";
        SecretBinding selected = Binding(SecretSourceType.Infisical);
        selected.InfisicalEnvironment = environmentCanary;
        selected.InfisicalPath = pathCanary;
        selected.InfisicalKey = keyCanary;
        SecretBinding mismatch = Binding(SecretSourceType.EnvironmentVariable);
        ISecretBindingRepository repository = Substitute.For<ISecretBindingRepository>();
        repository.GetById(selected.Id).Returns(selected);
        repository.GetById(mismatch.Id).Returns(mismatch);
        var client = new RecordingInfisicalClient
        {
            WriteException = new HttpRequestException(
                exceptionCanary,
                null,
                HttpStatusCode.Forbidden)
        };
        var resolver = new RecordingCommitmentResolver("malformed-key-canary");
        await using ServiceProvider provider = Services(
            SecretProviderType.Infisical,
            repository,
            new FixedInfisicalClientFactory(client),
            resolver,
            logs)
            .BuildServiceProvider();
        ISetupSecretBindingWriter writer =
            provider.GetRequiredService<ISetupSecretBindingWriter>();
        ISetupSecretBindingCommitmentAuthority commitment =
            provider.GetRequiredService<ISetupSecretBindingCommitmentAuthority>();
        byte[] secret = Encoding.UTF8.GetBytes(secretCanary);
        try
        {
            await Assert.That(await writer.WriteAsync(
                    WriteRequest(selected, secret),
                    CancellationToken.None))
                .IsEqualTo(SetupSecretBindingWriteOutcome.Unauthorized);
            await Assert.That(await writer.WriteAsync(
                    WriteRequest(mismatch, secret),
                    CancellationToken.None))
                .IsEqualTo(SetupSecretBindingWriteOutcome.Invalid);
            await Assert.That(async () => await commitment.CommitAsync(
                    CommitmentRequest(secret),
                    CancellationToken.None))
                .Throws<InvalidOperationException>();
            await Assert.That(logs.Snapshot()).IsEmpty();
            await AssertNoCapturedValue(
                logs,
                environmentCanary,
                pathCanary,
                keyCanary,
                exceptionCanary,
                secretCanary,
                selected.Id.ToString("D"),
                mismatch.Id.ToString("D"),
                selected.SettingKey,
                Digest(secret));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private static ServiceCollection RealResolverServices(
        SecretProviderType provider,
        ISecretBindingRepository repository,
        params ISecretSource[] sources)
    {
        ServiceCollection services = Services(
            provider,
            repository,
            new FixedInfisicalClientFactory(new RecordingInfisicalClient()));
        services.RemoveAll<ISecretSource>();
        foreach (ISecretSource source in sources)
            services.AddSingleton(source);
        return services;
    }

    private static SecretBinding Binding(SecretSourceType sourceType)
    {
        var binding = new SecretBinding
        {
            Id = Guid.CreateVersion7(),
            SettingKey = "setup.signing",
            Scope = SecretScope.Instance,
            SourceType = sourceType
        };
        if (sourceType == SecretSourceType.Infisical)
        {
            binding.InfisicalEnvironment = "staging";
            binding.InfisicalPath = "/setup";
            binding.InfisicalKey = "SETUP_SIGNING";
        }
        else
        {
            binding.EnvironmentVariableName = "ISLAMU_SETUP_SIGNING";
        }

        return binding;
    }

    private static SecretBinding CommitmentBinding(SecretSourceType sourceType)
    {
        SecretBinding binding = sourceType == SecretSourceType.Infisical
            ? SecretBinding.CreateInfisical(
                SetupSecretBindingContractMetadata.CommitmentAuthorityKey,
                SecretScope.Instance,
                null,
                "staging",
                "/setup",
                "SETUP_SECRET_BINDING_COMMITMENT_HMAC_KEY")
            : SecretBinding.CreateEnvironmentVariable(
                SetupSecretBindingContractMetadata.CommitmentAuthorityKey,
                SecretScope.Instance,
                null,
                "SETUP_SECRET_BINDING_COMMITMENT_HMAC_KEY");
        binding.Id = Guid.CreateVersion7();
        return binding;
    }

    private static SetupSecretBindingWriteRequest WriteRequest(
        SecretBinding binding,
        ReadOnlyMemory<byte> secret) => new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            Guid.CreateVersion7(),
            binding.Id,
            "setup.signing",
            secret);

    private static SetupSecretBindingCommitmentRequest CommitmentRequest(
        ReadOnlyMemory<byte> secret) => new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            7,
            Guid.CreateVersion7(),
            "setup.encryption",
            secret);

    private static SecretResolutionResult Result(SecretResolutionStatus status) => status switch
    {
        SecretResolutionStatus.Unconfigured => SecretResolutionResult.Unconfigured,
        SecretResolutionStatus.Unavailable => SecretResolutionResult.Unavailable,
        SecretResolutionStatus.Unauthorized => SecretResolutionResult.Unauthorized,
        SecretResolutionStatus.Invalid => SecretResolutionResult.Invalid,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static string ExpectedCommitment(
        byte[] key,
        SetupSecretBindingCommitmentRequest request)
    {
        byte[] prefix = Encoding.UTF8.GetBytes(
            $"setup-secret-binding-commitment-v1\n{request.TenantId:D}\n{request.ActorId:D}\n{request.EnrollmentId:D}\n{request.EnrollmentGeneration}\n{request.OperationKey:D}\n{request.BindingKey}\n");
        try
        {
            using IncrementalHash hmac = IncrementalHash.CreateHMAC(
                HashAlgorithmName.SHA256,
                key);
            hmac.AppendData(prefix);
            hmac.AppendData(request.SecretValue.Span);
            return Convert.ToHexString(hmac.GetHashAndReset()).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(prefix);
        }
    }

    private static string Digest(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static async Task AssertNoCapturedValue(
        CaptureLoggerProvider logs,
        params string[] canaries)
    {
        foreach (string captured in logs.Snapshot())
        foreach (string canary in canaries)
        {
            if (string.IsNullOrEmpty(canary))
                continue;
            await Assert.That(captured).DoesNotContain(canary);
        }
    }

    private sealed class FixedInfisicalClientFactory(IInfisicalClient? client) :
        IInfisicalClientFactory
    {
        public Task<IInfisicalClient?> GetClientAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IInfisicalClient?>(client);
    }

    private sealed class RecordingInfisicalClient : IInfisicalClient
    {
        private int _readCount;
        private int _writeCount;

        public int ReadCount => Volatile.Read(ref _readCount);
        public int WriteCount => Volatile.Read(ref _writeCount);
        public string? LastEnvironment { get; private set; }
        public string? LastPath { get; private set; }
        public string? LastName { get; private set; }
        public string? LastSecretDigest { get; private set; }
        public bool WriteResult { get; init; } = true;
        public Exception? WriteException { get; init; }

        public Task<string?> GetSecretAsync(
            string environment,
            string folderPath,
            string secretName,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _readCount);
            return Task.FromResult<string?>(null);
        }

        public Task<bool> WriteSecretAsync(
            string environment,
            string folderPath,
            string secretName,
            ReadOnlyMemory<byte> secretValue,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _writeCount);
            if (WriteException is not null)
                throw WriteException;
            LastEnvironment = environment;
            LastPath = folderPath;
            LastName = secretName;
            LastSecretDigest = Digest(secretValue.Span);
            return Task.FromResult(WriteResult);
        }
    }

    private sealed class RecordingCommitmentResolver : ISecretResolver
    {
        private readonly SecretResolutionResult? _result;
        private readonly Exception? _exception;
        private int _callCount;

        public RecordingCommitmentResolver(string value)
            : this(SecretResolutionResult.Resolved(new ResolvedSecret(
                SetupSecretBindingContractMetadata.CommitmentAuthorityKey,
                value,
                SecretSourceType.EnvironmentVariable,
                SecretScope.Instance,
                ScopeId: null,
                DateTime.UtcNow)))
        {
        }

        public RecordingCommitmentResolver(SecretResolutionResult result) =>
            _result = result;

        public RecordingCommitmentResolver(Exception exception) =>
            _exception = exception;

        public int CallCount => Volatile.Read(ref _callCount);
        public string? LastSettingKey { get; private set; }
        public Guid? LastTenantId { get; private set; }

        public Task<SecretResolutionResult> ResolveAsync(
            string settingKey,
            Guid? tenantId,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            LastSettingKey = settingKey;
            LastTenantId = tenantId;
            if (_exception is not null)
                throw _exception;
            return Task.FromResult(_result!);
        }

        public Task<SecretResolutionResult> ResolveQualifiedAsync(
            string settingKey,
            SecretScope scope,
            Guid? scopeId,
            string qualifier,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Invalid);

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(
            Guid tenantId,
            Guid bindingId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Invalid);

        public Task InvalidateAsync(
            string settingKey,
            SecretScope scope,
            Guid? scopeId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingSecretSource(
        SecretSourceType sourceType,
        string value) : ISecretSource
    {
        private int _callCount;
        public SecretSourceType SourceType => sourceType;
        public int CallCount => Volatile.Read(ref _callCount);
        public SecretBinding? LastBinding { get; private set; }

        public Task<SecretResolutionResult> GetSecretAsync(
            SecretBinding binding,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            LastBinding = binding;
            return Task.FromResult(SecretResolutionResult.Resolved(new ResolvedSecret(
                binding.SettingKey,
                value,
                binding.SourceType,
                binding.Scope,
                binding.ScopeId,
                DateTime.UtcNow)));
        }

        public Task<bool> ValidateAsync(
            SecretBinding binding,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class OutcomeSecretSource(
        SecretSourceType sourceType,
        SecretResolutionResult result) : ISecretSource
    {
        public SecretSourceType SourceType => sourceType;

        public Task<SecretResolutionResult> GetSecretAsync(
            SecretBinding binding,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);

        public Task<bool> ValidateAsync(
            SecretBinding binding,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly ConcurrentQueue<string> _captured = new();
        private readonly MeterListener _listener = new();

        public MetricCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (string.Equals(
                        instrument.Meter.Name,
                        Explore.Secrets.Observability.SecretResolverMetrics.MeterName,
                        StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(Record);
            _listener.SetMeasurementEventCallback<double>(Record);
            _listener.Start();
        }

        public string[] Snapshot() => _captured.ToArray();

        public void Dispose() => _listener.Dispose();

        private void Record<T>(
            Instrument instrument,
            T measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            foreach (KeyValuePair<string, object?> tag in tags)
                _captured.Enqueue($"metric:{tag.Key}={tag.Value}");
        }
    }

    public enum CommitmentResolverScenario
    {
        Success,
        Mismatch,
        Failure
    }

    public enum WriterFailure
    {
        NullClient,
        FalseResult,
        Transient,
        Unauthorized,
        Forbidden
    }

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _captured = new();

        public ILogger CreateLogger(string categoryName) =>
            new CaptureLogger(_captured);

        public string[] Snapshot() => _captured.ToArray();

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(
            ConcurrentQueue<string> captured) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                captured.Enqueue(state.ToString() ?? string.Empty);
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                captured.Enqueue(formatter(state, exception));
                if (exception is not null)
                    captured.Enqueue(exception.ToString());
                if (state is IEnumerable<KeyValuePair<string, object?>> values)
                {
                    foreach (KeyValuePair<string, object?> value in values)
                        captured.Enqueue($"{value.Key}={value.Value}");
                }
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose()
            {
            }
        }
    }
}
