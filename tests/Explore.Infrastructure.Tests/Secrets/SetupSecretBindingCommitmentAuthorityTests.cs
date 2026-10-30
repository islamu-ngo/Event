namespace Explore.Infrastructure.Tests.Secrets;

using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.SetupLive;
using Explore.Domain.Enums;
using Explore.Infrastructure.Secrets;
using Explore.Domain.Secrets;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

public sealed class SetupSecretBindingCommitmentAuthorityTests
{
    [Test]
    [Arguments("setup.signing")]
    [Arguments("setup.encryption")]
    public async Task CommitmentAuthorityUsesAuthoritativePurposeAndSelectedResolver(
        string purpose)
    {
        byte[] keyBytes = RandomNumberGenerator.GetBytes(32);
        byte[] secret = RandomNumberGenerator.GetBytes(64);
        string key = Convert.ToBase64String(keyBytes);
        var resolver = new RecordingCommitmentResolver(key);
        var authority = new SetupSecretBindingCommitmentAuthority(resolver);
        var request = new SetupSecretBindingCommitmentRequest(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            7,
            Guid.CreateVersion7(),
            purpose,
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
        var authority = new SetupSecretBindingCommitmentAuthority(resolver);
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
            var authority = new SetupSecretBindingCommitmentAuthority(resolver);
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
        var authority = new SetupSecretBindingCommitmentAuthority(resolver);
        byte[] secret = RandomNumberGenerator.GetBytes(64);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await Assert.That(async () => await authority.CommitAsync(
                    CommitmentRequest(secret),
                    cancellation.Token))
                .Throws<OperationCanceledException>();
            await Assert.That(resolver.CallCount).IsEqualTo(1);
            await Assert.That(resolver.LastCancellationToken)
                .IsEqualTo(cancellation.Token);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    [Arguments(CommitmentField.Tenant)]
    [Arguments(CommitmentField.Actor)]
    [Arguments(CommitmentField.Enrollment)]
    [Arguments(CommitmentField.Generation)]
    [Arguments(CommitmentField.Operation)]
    [Arguments(CommitmentField.BindingKey)]
    [Arguments(CommitmentField.SecretBytes)]
    [Arguments(CommitmentField.AuthorityKey)]
    public async Task CommitmentBindsEveryIdentityEnrollmentOperationKeyAndSecretField(
        CommitmentField field)
    {
        byte[] keyBytes = RandomNumberGenerator.GetBytes(32);
        byte[] otherKeyBytes = RandomNumberGenerator.GetBytes(32);
        byte[] secret = RandomNumberGenerator.GetBytes(64);
        byte[] otherSecret = secret.ToArray();
        otherSecret[^1] ^= 1;
        var request = CommitmentRequest(secret);
        var changed = new SetupSecretBindingCommitmentRequest(
            field == CommitmentField.Tenant ? Guid.CreateVersion7() : request.TenantId,
            field == CommitmentField.Actor ? Guid.CreateVersion7() : request.ActorId,
            field == CommitmentField.Enrollment ? Guid.CreateVersion7() : request.EnrollmentId,
            field == CommitmentField.Generation ? request.EnrollmentGeneration + 1 : request.EnrollmentGeneration,
            field == CommitmentField.Operation ? Guid.CreateVersion7() : request.OperationKey,
            field == CommitmentField.BindingKey ? "setup.signing" : request.BindingKey,
            field == CommitmentField.SecretBytes ? otherSecret : secret);
        var authority = new SetupSecretBindingCommitmentAuthority(
            new RecordingCommitmentResolver(Convert.ToBase64String(keyBytes)));
        var changedAuthority = new SetupSecretBindingCommitmentAuthority(
            new RecordingCommitmentResolver(Convert.ToBase64String(
                field == CommitmentField.AuthorityKey ? otherKeyBytes : keyBytes)));
        try
        {
            SetupSecretBindingCommitment original = await authority.CommitAsync(
                request, CancellationToken.None);
            SetupSecretBindingCommitment repeated = await authority.CommitAsync(
                request, CancellationToken.None);
            SetupSecretBindingCommitment different = await changedAuthority.CommitAsync(
                changed, CancellationToken.None);

            await Assert.That(repeated.Commitment).IsEqualTo(original.Commitment);
            await Assert.That(different.Commitment).IsNotEqualTo(original.Commitment);
            await Assert.That(original.Commitment).IsNotEqualTo(Digest(secret));
            await Assert.That(original.Commitment.Length).IsEqualTo(64);
            await Assert.That(original.Commitment.All(character =>
                    character is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
                .IsTrue();
            await Assert.That(original.KeyVersion).IsEqualTo(1);
            await Assert.That(different.KeyVersion).IsEqualTo(1);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(otherKeyBytes);
            CryptographicOperations.ZeroMemory(secret);
            CryptographicOperations.ZeroMemory(otherSecret);
        }
    }

    [Test]
    public async Task CommitmentConsumesExactTransientBytesWithoutChangingTheBuffer()
    {
        byte[] keyBytes = RandomNumberGenerator.GetBytes(32);
        byte[] buffer = RandomNumberGenerator.GetBytes(68);
        buffer[2] = 0xC3;
        buffer[3] = 0x28;
        buffer[4] = 0;
        byte[] original = buffer.ToArray();
        var request = CommitmentRequest(buffer.AsMemory(2, 64));
        var authority = new SetupSecretBindingCommitmentAuthority(
            new RecordingCommitmentResolver(Convert.ToBase64String(keyBytes)));
        try
        {
            SetupSecretBindingCommitment commitment = await authority.CommitAsync(
                request, CancellationToken.None);

            await Assert.That(commitment.Commitment)
                .IsEqualTo(ExpectedCommitment(keyBytes, request));
            await Assert.That(buffer.SequenceEqual(original)).IsTrue();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(buffer);
            CryptographicOperations.ZeroMemory(original);
        }
    }

    [Test]
    public async Task CommitmentAndKeyFailureDoNotExposeConfidentialInput()
    {
        byte[] keyBytes = RandomNumberGenerator.GetBytes(32);
        byte[] secret = Encoding.UTF8.GetBytes($"secret-{Guid.CreateVersion7():N}");
        string key = Convert.ToBase64String(keyBytes);
        var request = CommitmentRequest(secret);
        var authority = new SetupSecretBindingCommitmentAuthority(
            new RecordingCommitmentResolver(key));
        string malformedKey = $"malformed-{Guid.CreateVersion7():N}";
        var unavailable = new SetupSecretBindingCommitmentAuthority(
            new RecordingCommitmentResolver(malformedKey));
        try
        {
            SetupSecretBindingCommitment commitment = await authority.CommitAsync(
                request, CancellationToken.None);
            InvalidOperationException? failure = null;
            try
            {
                await unavailable.CommitAsync(request, CancellationToken.None);
            }
            catch (InvalidOperationException exception)
            {
                failure = exception;
            }

            await Assert.That(failure).IsNotNull();
            string[] captured = [commitment.Commitment, commitment.ToString()!, failure!.ToString()];
            string[] canaries =
            [
                key,
                malformedKey,
                Encoding.UTF8.GetString(secret),
                Digest(secret),
                request.TenantId.ToString("D"),
                request.ActorId.ToString("D"),
                request.EnrollmentId.ToString("D"),
                request.OperationKey.ToString("D"),
                request.BindingKey,
                SetupSecretBindingContractMetadata.CommitmentAuthorityKey
            ];
            foreach (string value in captured)
                foreach (string canary in canaries)
                    await Assert.That(value).DoesNotContain(canary);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    [Arguments(SecretAuthorityKind.Environment)]
    [Arguments(SecretAuthorityKind.UserSecrets)]
    [Arguments(SecretAuthorityKind.Infisical)]
    public async Task RegisteredCommitmentUsesTheRealSelectedResolver(SecretAuthorityKind kind)
    {
        byte[] keyBytes = RandomNumberGenerator.GetBytes(32);
        byte[] secret = RandomNumberGenerator.GetBytes(64);
        const string coordinate = "SETUP_SECRET_BINDING_COMMITMENT_HMAC_KEY";
        await using var selected = kind == SecretAuthorityKind.Infisical
            ? await ResolverTestAuthority.Infisical()
            : ResolverTestAuthority.Local(new Dictionary<string, string?>
            {
                [coordinate] = Convert.ToBase64String(keyBytes)
            }, provider: kind);
        if (selected.Http is not null) selected.Http.Values[coordinate] = Convert.ToBase64String(keyBytes);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(selected.Owner);
        services.AddScoped<ISecretBindingRepository>(_ => Substitute.For<ISecretBindingRepository>());
        services.AddNativeSecretResolution();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var authority = scope.ServiceProvider.GetRequiredService<ISetupSecretBindingCommitmentAuthority>();
        var request = CommitmentRequest(secret);
        try
        {
            var commitment = await authority.CommitAsync(request, CancellationToken.None);
            await Assert.That(commitment.Commitment).IsEqualTo(ExpectedCommitment(keyBytes, request));
            var definition = SecretDefinitionRegistry.GetRequired(SetupSecretBindingContractMetadata.CommitmentAuthorityKey);
            await Assert.That(definition.AllowedScopes).IsEquivalentTo([SecretScope.Instance]);
            await Assert.That(definition.AllowedSources).IsEquivalentTo([SecretSourceType.Infisical, SecretSourceType.EnvironmentVariable]);
            await Assert.That(definition.DefaultInfisicalPath).IsEqualTo("/setup");
            await Assert.That(definition.DefaultInfisicalKey).IsEqualTo(coordinate);
            await Assert.That(definition.DefaultEnvironmentVariableName).IsEqualTo(coordinate);
            if (selected.Http is not null) await Assert.That(selected.Http.Reads).IsEqualTo(1);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Test]
    [Arguments(SecretAuthorityKind.Environment, true)]
    [Arguments(SecretAuthorityKind.UserSecrets, true)]
    [Arguments(SecretAuthorityKind.Infisical, false)]
    public async Task RealResolverRejectsForeignCommitmentSourceBeforeReading(SecretAuthorityKind kind, bool remoteBinding)
    {
        const string coordinate = "SETUP_SECRET_BINDING_COMMITMENT_HMAC_KEY";
        string canary = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var selected = kind == SecretAuthorityKind.Infisical
            ? await ResolverTestAuthority.Infisical()
            : ResolverTestAuthority.Local(new Dictionary<string, string?> { [coordinate] = canary }, provider: kind);
        var binding = remoteBinding
            ? SecretBinding.CreateInfisical(SetupSecretBindingContractMetadata.CommitmentAuthorityKey, SecretScope.Instance, null, "staging", "/setup", coordinate)
            : SecretBinding.CreateEnvironmentVariable(SetupSecretBindingContractMetadata.CommitmentAuthorityKey, SecretScope.Instance, null, coordinate);
        binding.Id = Guid.CreateVersion7();
        var repository = Substitute.For<ISecretBindingRepository>();
        repository.GetByKeyAndScopeAsync(binding.SettingKey, SecretScope.Instance, null, Arg.Any<CancellationToken>()).Returns(binding);
        var logs = new SecretDiagnosticLog<SecretResolver>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILogger<SecretResolver>>(logs);
        services.AddSingleton(selected.Owner);
        services.AddScoped<ISecretBindingRepository>(_ => repository);
        services.AddNativeSecretResolution();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var authority = scope.ServiceProvider.GetRequiredService<ISetupSecretBindingCommitmentAuthority>();
        byte[] secret = RandomNumberGenerator.GetBytes(64);
        try
        {
            await Assert.That(async () => await authority.CommitAsync(CommitmentRequest(secret), CancellationToken.None))
                .Throws<InvalidOperationException>();
            await Assert.That(logs.Output).DoesNotContain(canary);
            await Assert.That(logs.Output).DoesNotContain(coordinate);
            await Assert.That(logs.Output).DoesNotContain(binding.Id.ToString("D"));
            if (selected.Http is not null) await Assert.That(selected.Http.Reads).IsEqualTo(0);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

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
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<SecretResolutionResult> ResolveAsync(
            string settingKey,
            Guid? tenantId,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            LastSettingKey = settingKey;
            LastTenantId = tenantId;
            LastCancellationToken = cancellationToken;
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

    public enum CommitmentField
    {
        Tenant,
        Actor,
        Enrollment,
        Generation,
        Operation,
        BindingKey,
        SecretBytes,
        AuthorityKey
    }
}
