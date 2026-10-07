using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Secrets.UnitTests.Services;

public sealed class AuthenticationRuntimeSecretWriterTests
{
    [Test]
    [Arguments(AuthenticationRuntimeSecretTarget.GoogleClientSecret)]
    [Arguments(AuthenticationRuntimeSecretTarget.KeycloakBffClientSecret)]
    public async Task ExplicitWriteUsesOnlySelectedInstanceCoordinateWithoutReadingValues(
        AuthenticationRuntimeSecretTarget target)
    {
        using var fixture = new Fixture(target);
        var writer = fixture.Writer;
        var capability = await writer.GetWriteCapabilityAsync(target);
        await Assert.That(capability.Outcome).IsEqualTo(AuthenticationRuntimeWriteOutcome.ReadyToAttempt);
        await Assert.That(fixture.Client.Dispatched).IsFalse();
        await Assert.That(fixture.Client.ReadValue).IsFalse();
        var request = new AuthenticationRuntimeSecretWriteRequest(capability.Selection!, fixture.Credential);

        var result = await writer.WriteAsync(request);

        await Assert.That(result.Outcome).IsEqualTo(AuthenticationRuntimeWriteOutcome.Written);
        await Assert.That(result.Mutation).IsEqualTo(AuthenticationRuntimeSourceMutation.ConfirmedWritten);
        await Assert.That(fixture.Client.Destination).IsEqualTo(
            ("test", fixture.Definition.DefaultInfisicalPath, fixture.Definition.DefaultInfisicalKey));
        await Assert.That(fixture.Client.ReceivedExpectedValue).IsTrue();
        await Assert.That(fixture.Client.ReadValue).IsFalse();
        string canary = Encoding.UTF8.GetString(fixture.Credential);
        await Assert.That(JsonSerializer.Serialize(request)).DoesNotContain(canary);
        await Assert.That(request.ToString()).DoesNotContain(canary);
        await Assert.That(JsonSerializer.Serialize(result)).DoesNotContain(canary);
    }

    [Test]
    public async Task ReadinessDoesNotCertifyRemoteWritePermission()
    {
        using var fixture = new Fixture();
        fixture.Client.Failure = new(InfisicalOperationFailureKind.Unauthorized, true);
        var capability = await fixture.Writer.GetWriteCapabilityAsync(fixture.Target);
        await Assert.That(capability.Outcome).IsEqualTo(AuthenticationRuntimeWriteOutcome.ReadyToAttempt);
        await Assert.That(fixture.Client.Dispatched).IsFalse();

        var result = await fixture.Writer.WriteAsync(new(capability.Selection!, fixture.Credential));

        await Assert.That(result.Outcome).IsEqualTo(AuthenticationRuntimeWriteOutcome.Unauthorized);
        await Assert.That(result.Mutation).IsEqualTo(AuthenticationRuntimeSourceMutation.NotAttempted);
    }

    [Test]
    [Arguments(SecretProviderType.Environment, "Production")]
    [Arguments(SecretProviderType.UserSecrets, "Development")]
    [Arguments(SecretProviderType.UserSecrets, "Testing")]
    public async Task ReadOnlySelectedAuthorityRequiresOperatorInjectionWithoutFallback(
        SecretProviderType provider, string environment)
    {
        using var fixture = new Fixture(provider: provider, environment: environment);
        var result = await fixture.Writer.GetWriteCapabilityAsync(fixture.Target);

        await Assert.That(result.Outcome).IsEqualTo(AuthenticationRuntimeWriteOutcome.OperatorInjectionRequired);
        await Assert.That(result.OperatorVariable).IsEqualTo(fixture.Definition.DefaultEnvironmentVariableName);
        await Assert.That(result.Selection).IsNull();
        await Assert.That(fixture.Factory.Acquired).IsFalse();
        await Assert.That(fixture.Client.Dispatched).IsFalse();
    }

    [Test]
    public async Task UserSecretsOutsideDevelopmentAndTestingFailsClosed()
    {
        using var fixture = new Fixture(provider: SecretProviderType.UserSecrets, environment: "Production");
        var result = await fixture.Writer.GetWriteCapabilityAsync(fixture.Target);
        await Assert.That(result.Outcome).IsEqualTo(AuthenticationRuntimeWriteOutcome.Invalid);
        await Assert.That(fixture.Factory.Acquired).IsFalse();
    }

    [Test]
    [Arguments("source")]
    [Arguments("scope")]
    [Arguments("qualifier")]
    [Arguments("environment")]
    [Arguments("path")]
    [Arguments("key")]
    public async Task InvalidReferenceCannotAcquireAuthority(string field)
    {
        using var fixture = new Fixture();
        switch (field)
        {
            case "source": fixture.Binding!.SourceType = SecretSourceType.EnvironmentVariable; break;
            case "scope": fixture.Binding!.ScopeId = Guid.CreateVersion7(); break;
            case "qualifier": fixture.Binding!.Qualifier = "unapproved"; break;
            case "environment": fixture.Binding!.InfisicalEnvironment = "different"; break;
            case "path": fixture.Binding!.InfisicalPath = "/unrelated/backend"; break;
            case "key": fixture.Binding!.InfisicalKey = "UNRELATED_CREDENTIAL"; break;
        }

        var result = await fixture.Writer.GetWriteCapabilityAsync(fixture.Target);

        await Assert.That(result.Outcome).IsEqualTo(AuthenticationRuntimeWriteOutcome.Invalid);
        await Assert.That(fixture.Factory.Acquired).IsFalse();
        await Assert.That(fixture.Client.Dispatched).IsFalse();
    }

    [Test]
    public async Task MissingStoredBindingCannotSilentlyUseRegistryDefault()
    {
        using var fixture = new Fixture();
        fixture.Binding = null;
        var result = await fixture.Writer.GetWriteCapabilityAsync(fixture.Target);
        await Assert.That(result.Outcome).IsEqualTo(AuthenticationRuntimeWriteOutcome.Missing);
        await Assert.That(fixture.Factory.Acquired).IsFalse();
    }

    [Test]
    public async Task ChangedAuthorityCannotRedirectPreparedWrite()
    {
        using var fixture = new Fixture();
        var capability = await fixture.Writer.GetWriteCapabilityAsync(fixture.Target);
        fixture.Options.Infisical.ProjectId = "different-project";
        var result = await fixture.Writer.WriteAsync(new(capability.Selection!, fixture.Credential));
        await Assert.That(result.Outcome).IsEqualTo(AuthenticationRuntimeWriteOutcome.Invalid);
        await Assert.That(result.Mutation).IsEqualTo(AuthenticationRuntimeSourceMutation.NotAttempted);
        await Assert.That(fixture.Client.Dispatched).IsFalse();
    }

    [Test]
    [Arguments("empty")]
    [Arguments("malformed")]
    [Arguments("oversized")]
    public async Task InvalidCredentialCannotDispatch(string form)
    {
        using var fixture = new Fixture();
        var capability = await fixture.Writer.GetWriteCapabilityAsync(fixture.Target);
        byte[] input = form switch
        {
            "empty" => [],
            "malformed" => [0xff],
            _ => RandomNumberGenerator.GetBytes(16 * 1024 + 1)
        };
        try
        {
            var result = await fixture.Writer.WriteAsync(new(capability.Selection!, input));
            await Assert.That(result.Outcome).IsEqualTo(AuthenticationRuntimeWriteOutcome.Invalid);
            await Assert.That(result.Mutation).IsEqualTo(AuthenticationRuntimeSourceMutation.NotAttempted);
            await Assert.That(fixture.Client.Dispatched).IsFalse();
        }
        finally { CryptographicOperations.ZeroMemory(input); }
    }

    [Test]
    [Arguments(InfisicalOperationFailureKind.Missing, AuthenticationRuntimeWriteOutcome.Missing)]
    [Arguments(InfisicalOperationFailureKind.Invalid, AuthenticationRuntimeWriteOutcome.Invalid)]
    [Arguments(InfisicalOperationFailureKind.Unauthorized, AuthenticationRuntimeWriteOutcome.Unauthorized)]
    [Arguments(InfisicalOperationFailureKind.Unavailable, AuthenticationRuntimeWriteOutcome.Unavailable)]
    public async Task DefiniteProviderRejectionPreservesBoundedCategory(
        InfisicalOperationFailureKind failure, AuthenticationRuntimeWriteOutcome expected)
    {
        using var fixture = new Fixture();
        var capability = await fixture.Writer.GetWriteCapabilityAsync(fixture.Target);
        fixture.Client.Failure = new(failure, true);
        var result = await fixture.Writer.WriteAsync(new(capability.Selection!, fixture.Credential));
        await Assert.That(result.Outcome).IsEqualTo(expected);
        await Assert.That(result.Mutation).IsEqualTo(AuthenticationRuntimeSourceMutation.NotAttempted);
    }

    [Test]
    public async Task LostDispatchCompletionRemainsUnknown()
    {
        using var fixture = new Fixture();
        var capability = await fixture.Writer.GetWriteCapabilityAsync(fixture.Target);
        fixture.Client.Failure = new(InfisicalOperationFailureKind.Unavailable);
        var result = await fixture.Writer.WriteAsync(new(capability.Selection!, fixture.Credential));
        await Assert.That(result.Outcome).IsEqualTo(AuthenticationRuntimeWriteOutcome.Unavailable);
        await Assert.That(result.Mutation).IsEqualTo(AuthenticationRuntimeSourceMutation.Unknown);
    }

    [Test]
    public async Task CacheFailureCannotUndoAcknowledgedSourceMutation()
    {
        using var fixture = new Fixture();
        fixture.Resolver.InvalidateAsync(Arg.Any<string>(), SecretScope.Instance, null, Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("activation_unavailable"));
        var capability = await fixture.Writer.GetWriteCapabilityAsync(fixture.Target);
        var result = await fixture.Writer.WriteAsync(new(capability.Selection!, fixture.Credential));
        await Assert.That(result.Mutation).IsEqualTo(AuthenticationRuntimeSourceMutation.ConfirmedWritten);
        await Assert.That(result.Outcome).IsEqualTo(AuthenticationRuntimeWriteOutcome.Unavailable);
        await Assert.That(fixture.Client.ReceivedExpectedValue).IsTrue();
    }

    [Test]
    public async Task PreDispatchCancellationCannotMutateSource()
    {
        using var fixture = new Fixture();
        var capability = await fixture.Writer.GetWriteCapabilityAsync(fixture.Target);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(async () => await fixture.Writer.WriteAsync(
            new(capability.Selection!, fixture.Credential), cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(fixture.Client.Dispatched).IsFalse();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _services;
        public AuthenticationRuntimeSecretTarget Target { get; }
        public SecretDefinition Definition { get; }
        public SecretBinding? Binding { get; set; }
        public SecretProviderOptions Options { get; }
        public byte[] Credential { get; } = Encoding.UTF8.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        public RecordingClient Client { get; }
        public RecordingFactory Factory { get; }
        public ISecretResolver Resolver { get; } = Substitute.For<ISecretResolver>();
        public IAuthenticationRuntimeSecretWriter Writer =>
            _services.GetRequiredService<IAuthenticationRuntimeSecretWriter>();

        public Fixture(AuthenticationRuntimeSecretTarget target = AuthenticationRuntimeSecretTarget.GoogleClientSecret,
            SecretProviderType provider = SecretProviderType.Infisical, string environment = "Testing")
        {
            Target = target;
            Definition = SecretDefinitionRegistry.GetRequired(target == AuthenticationRuntimeSecretTarget.GoogleClientSecret
                ? SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret
                : SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret);
            Binding = SecretBinding.CreateInfisical(Definition.Key, SecretScope.Instance, null,
                "test", Definition.DefaultInfisicalPath, Definition.DefaultInfisicalKey);
            Binding.Id = Guid.CreateVersion7();
            Options = new() { Provider = provider, Infisical = new() { Url = "http://authority.invalid", ProjectId = "project", Environment = "test" } };
            Client = new(Credential);
            Factory = new(Client);
            var repository = Substitute.For<ISecretBindingRepository>();
            repository.GetByKeyAndScopeAsync(Definition.Key, SecretScope.Instance, null, Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(Binding));
            var host = Substitute.For<IHostEnvironment>();
            host.EnvironmentName.Returns(environment);
            var services = new ServiceCollection();
            services.AddSingleton(repository);
            services.AddSingleton<IInfisicalClientFactory>(Factory);
            services.AddSingleton<IHostEnvironment>(host);
            services.AddSingleton<IOptions<SecretProviderOptions>>(Microsoft.Extensions.Options.Options.Create(Options));
            services.AddSecretResolution();
            services.RemoveAll<ISecretResolver>();
            services.AddSingleton(Resolver);
            _services = services.BuildServiceProvider();
        }

        public void Dispose()
        {
            _services.Dispose();
            CryptographicOperations.ZeroMemory(Credential);
        }
    }

    private sealed class RecordingFactory(RecordingClient client) : IInfisicalClientFactory
    {
        public bool Acquired { get; private set; }
        public Task<IInfisicalClient?> GetClientAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Acquired = true;
            return Task.FromResult<IInfisicalClient?>(client);
        }
    }

    private sealed class RecordingClient(byte[] expected) : IInfisicalClient
    {
        public bool ReadValue { get; private set; }
        public bool Dispatched { get; private set; }
        public bool ReceivedExpectedValue { get; private set; }
        public (string Environment, string Path, string Key)? Destination { get; private set; }
        public InfisicalOperationException? Failure { get; set; }
        public Task<SecretProviderValue?> GetSecretAsync(string environment, string folderPath,
            string secretName, CancellationToken cancellationToken = default)
        {
            ReadValue = true;
            return Task.FromResult<SecretProviderValue?>(null);
        }
        public Task<bool> WriteSecretAsync(string environment, string folderPath, string secretName,
            ReadOnlyMemory<byte> secretValue, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Dispatched = true;
            Destination = (environment, folderPath, secretName);
            if (Failure is not null) throw Failure;
            ReceivedExpectedValue = secretValue.Span.SequenceEqual(expected);
            return Task.FromResult(true);
        }
    }
}
