using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Secrets;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;
using SecretKit.Core;
using SecretKit.Infisical;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class SecretKitRuntimeSourceTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
    private const string PasswordKey = "MAIL_SMTP_PASSWORD";

    [Test]
    [Arguments(SecretAuthorityKind.Environment)]
    [Arguments(SecretAuthorityKind.UserSecrets)]
    public async Task LocalValuesAndTokensBelongToOneImmutableOwnerGeneration(SecretAuthorityKind provider)
    {
        string original = Guid.NewGuid().ToString("N");
        string replacement = Guid.NewGuid().ToString("N");
        using var configuration = LocalConfiguration(original);
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(provider, "Testing", SecretConsumerRole.Api), configuration);
        SecretBinding binding = LocalBinding();
        var first = await new EnvironmentSecretSource(owner).GetSecretAsync(binding);
        configuration[PasswordKey] = replacement;
        var sameOwner = await new EnvironmentSecretSource(owner).GetSecretAsync(binding);
        await using var nextOwner = SecretAuthorityAcquisition.OpenLocal(owner.Selection, configuration);
        var nextGeneration = await new EnvironmentSecretSource(nextOwner).GetSecretAsync(binding);

        await Assert.That(first.Value).IsEqualTo(original);
        await Assert.That(sameOwner.Value).IsEqualTo(original);
        await Assert.That(nextGeneration.Value).IsEqualTo(replacement);
        await Assert.That(sameOwner.Secret!.BindingRevision).IsEqualTo(first.Secret!.BindingRevision);
        await Assert.That(nextGeneration.Secret!.BindingRevision).IsNotEqualTo(first.Secret.BindingRevision);
        await Assert.That(first.Secret.BindingRevision.Length).IsEqualTo(64);
        await Assert.That(new EnvironmentSecretSource(owner)
            .IsRevisionCurrent(binding, first.Secret.BindingRevision)).IsTrue();
        await Assert.That(new EnvironmentSecretSource(nextOwner)
            .IsRevisionCurrent(binding, first.Secret.BindingRevision)).IsFalse();
    }

    [Test]
    public async Task LocalComparisonIncludesBindingIdentityScopeQualifierCoordinatesAndAuditMetadata()
    {
        using var configuration = LocalConfiguration(Guid.NewGuid().ToString("N"));
        configuration["TENANT_MAIL_PASSWORD"] = Guid.NewGuid().ToString("N");
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), configuration);
        var source = new EnvironmentSecretSource(owner);
        SecretBinding binding = LocalBinding();
        var previous = await source.GetSecretAsync(binding);
        Action<SecretBinding>[] changes =
        [
            selected => selected.UpdatedAt = DateTime.UnixEpoch,
            selected => selected.IsLocked = true,
            selected => selected.Qualifier = "delivery",
            selected => { selected.Scope = SecretScope.Tenant; selected.ScopeId = Guid.CreateVersion7(); },
            selected => selected.EnvironmentVariableName = "TENANT_MAIL_PASSWORD",
            selected => selected.Id = Guid.CreateVersion7()
        ];
        foreach (var change in changes)
        {
            change(binding);
            await Assert.That(source.IsRevisionCurrent(binding, previous.Secret!.BindingRevision)).IsFalse();
            var current = await source.GetSecretAsync(binding);
            await Assert.That(current.IsResolved).IsTrue();
            await Assert.That(current.Secret!.BindingRevision).IsNotEqualTo(previous.Secret!.BindingRevision);
            previous = current;
        }
    }

    [Test]
    public async Task BoundedOwnerInventoryEvictionRequiresFreshComparison()
    {
        using var configuration = LocalConfiguration(Guid.NewGuid().ToString("N"));
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), configuration);
        var source = new EnvironmentSecretSource(owner);
        SecretBinding firstBinding = LocalBinding();
        var first = await source.GetSecretAsync(firstBinding);
        for (int index = 0; index < 4096; index++)
            await source.GetSecretAsync(LocalBinding());
        await Assert.That(source.IsRevisionCurrent(firstBinding, first.Secret!.BindingRevision)).IsFalse();
        var afterEviction = await source.GetSecretAsync(firstBinding);
        await Assert.That(afterEviction.Secret!.BindingRevision).IsNotEqualTo(first.Secret!.BindingRevision);
        await Assert.That(source.IsRevisionCurrent(firstBinding, first.Secret.BindingRevision)).IsFalse();
        await Assert.That(source.IsRevisionCurrent(firstBinding, afterEviction.Secret.BindingRevision)).IsTrue();
    }

    [Test]
    [Arguments(false, SecretResolutionStatus.Unconfigured)]
    [Arguments(true, SecretResolutionStatus.Invalid)]
    public async Task MissingOrEmptyLocalSelectionDoesNotReadAnotherCoordinate(
        bool empty, SecretResolutionStatus expected)
    {
        using var configuration = LocalConfiguration(empty ? string.Empty : null);
        configuration["ANOTHER_MAIL_PASSWORD"] = Guid.NewGuid().ToString("N");
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), configuration);
        var result = await new EnvironmentSecretSource(owner).GetSecretAsync(LocalBinding());
        await Assert.That(result.Status).IsEqualTo(expected);
        await Assert.That(result.Secret).IsNull();
    }

    [Test]
    public async Task InvalidConsumerBindingIsRejectedBeforeEvenAccessingRetiredOwner()
    {
        using var configuration = LocalConfiguration(Guid.NewGuid().ToString("N"));
        var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Bff), configuration);
        await owner.DisposeAsync();
        var result = await new EnvironmentSecretSource(owner).GetSecretAsync(LocalBinding());
        await Assert.That(result.Status).IsEqualTo(SecretResolutionStatus.Invalid);
        await Assert.That(result.Secret).IsNull();
    }

    [Test]
    public async Task CallerCancellationIsNotTranslatedToAbsenceOrProviderFailure()
    {
        using var configuration = LocalConfiguration(Guid.NewGuid().ToString("N"));
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), configuration);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Func<Task> resolve = async () =>
        {
            _ = await new EnvironmentSecretSource(owner).GetSecretAsync(LocalBinding(), cancellation.Token);
        };
        await Assert.That(resolve).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task QualifiedNativeRevisionChangesOnVersionRecreationAndBindingSelection()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values[PasswordKey] = Guid.NewGuid().ToString("N");
        authority.Values["TENANT_MAIL_PASSWORD"] = Guid.NewGuid().ToString("N");
        await using var owner = await NativeOwner(authority);
        var source = new InfisicalSecretSource(owner);
        SecretBinding binding = NativeBinding();
        var first = await source.GetSecretAsync(binding);
        var same = await new InfisicalSecretSource(owner).GetSecretAsync(binding);
        await Assert.That(first.IsResolved).IsTrue();
        await Assert.That(same.Secret!.BindingRevision).IsEqualTo(first.Secret!.BindingRevision);
        await Assert.That(source.IsRevisionCurrent(binding, first.Secret.BindingRevision)).IsTrue();

        authority.Version++;
        var rotated = await source.GetSecretAsync(binding);
        await Assert.That(rotated.Secret!.BindingRevision).IsNotEqualTo(first.Secret.BindingRevision);
        await Assert.That(source.IsRevisionCurrent(binding, first.Secret.BindingRevision)).IsFalse();
        authority.ResourceId = Guid.NewGuid().ToString("N");
        authority.Version = 1;
        var recreated = await source.GetSecretAsync(binding);
        await Assert.That(recreated.Secret!.BindingRevision).IsNotEqualTo(first.Secret.BindingRevision);
        await Assert.That(recreated.Secret.BindingRevision).IsNotEqualTo(rotated.Secret.BindingRevision);

        binding.InfisicalKey = "TENANT_MAIL_PASSWORD";
        var retargeted = await source.GetSecretAsync(binding);
        await Assert.That(retargeted.Value).IsEqualTo(authority.Values["TENANT_MAIL_PASSWORD"]);
        await Assert.That(retargeted.Secret!.BindingRevision).IsNotEqualTo(recreated.Secret.BindingRevision);
        binding.InfisicalKey = PasswordKey;
        var restored = await source.GetSecretAsync(binding);
        await Assert.That(restored.Secret!.BindingRevision).IsNotEqualTo(recreated.Secret.BindingRevision);
        await Assert.That(source.IsRevisionCurrent(binding, recreated.Secret.BindingRevision)).IsFalse();
        await Assert.That(authority.Logins).IsEqualTo(1);
    }

    [Test]
    public async Task LateNativeReadCannotReplaceNewerAdmittedRevision()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        string original = Guid.NewGuid().ToString("N");
        string replacement = Guid.NewGuid().ToString("N");
        authority.Values[PasswordKey] = original;
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        authority.BeforeReadResponse = async readNumber =>
        {
            if (readNumber != 1)
                return;
            captured.TrySetResult();
            await release.Task.WaitAsync(TestTimeout);
        };
        await using var owner = await NativeOwner(authority);
        SecretBinding binding = NativeBinding();
        var source = new InfisicalSecretSource(owner);
        Task entered = captured.Task.WaitAsync(TestTimeout);
        Task<SecretResolutionResult> pending = source.GetSecretAsync(binding);
        SecretResolutionResult newer;
        try
        {
            await entered;
            authority.Version = 2;
            authority.Values[PasswordKey] = replacement;
            newer = await new InfisicalSecretSource(owner).GetSecretAsync(binding).WaitAsync(TestTimeout);
            await Assert.That(newer.IsResolved).IsTrue();
            await Assert.That(newer.Value).IsEqualTo(replacement);
            await Assert.That(source.IsRevisionCurrent(binding, newer.Secret!.BindingRevision)).IsTrue();
        }
        finally
        {
            release.TrySetResult();
        }
        var late = await pending.WaitAsync(TestTimeout);

        await Assert.That(late.Status).IsEqualTo(SecretResolutionStatus.Unavailable);
        await Assert.That(late.Secret).IsNull();
        await Assert.That(source.IsRevisionCurrent(binding, newer.Secret!.BindingRevision)).IsTrue();
        await Assert.That(authority.Reads).IsEqualTo(2);
        await Assert.That(authority.Logins).IsEqualTo(1);
    }

    [Test]
    public async Task PendingNativeReadCannotRestoreEvidenceLostToInventoryEviction()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values[PasswordKey] = Guid.NewGuid().ToString("N");
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        authority.BeforeReadResponse = async readNumber =>
        {
            if (readNumber != 1)
                return;
            captured.TrySetResult();
            await release.Task.WaitAsync(TestTimeout);
        };
        await using var owner = await NativeOwner(authority);
        SecretBinding binding = NativeBinding();
        var source = new InfisicalSecretSource(owner);
        Task entered = captured.Task.WaitAsync(TestTimeout);
        Task<SecretResolutionResult> pending = source.GetSecretAsync(binding);
        try
        {
            await entered;
            authority.Version = 2;
            authority.Values[PasswordKey] = Guid.NewGuid().ToString("N");
            var newer = await source.GetSecretAsync(binding).WaitAsync(TestTimeout);
            await Assert.That(newer.IsResolved).IsTrue();
            var client = owner.NativeClient!;
            var read = await client.ReadNativeAsync(new SecretReference(client.Authority,
                new InfisicalSecretAddress("project", "testing", "/smtp", PasswordKey,
                    version: null, resolution: InfisicalResolution.Raw, includeImports: false)));
            await Assert.That(read.IsSuccess).IsTrue();
            using var evidence = read.Value;
            for (int index = 0; index < 4096; index++)
                SecretBindingRevisionInventory.Observe(owner, RuntimeSecretBinding.Capture(NativeBinding()),
                    evidence.Revision);
            await Assert.That(source.IsRevisionCurrent(binding, newer.Secret!.BindingRevision)).IsFalse();
        }
        finally
        {
            release.TrySetResult();
        }
        var late = await pending.WaitAsync(TestTimeout);
        await Assert.That(late.Status).IsEqualTo(SecretResolutionStatus.Unavailable);
        await Assert.That(late.Secret).IsNull();
        await Assert.That(authority.Reads).IsEqualTo(3);
    }

    [Test]
    public async Task SameNativeRevisionOnAnotherOwnerCannotReuseProvisioningApproval()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values[PasswordKey] = Guid.NewGuid().ToString("N");
        SecretBinding binding = NativeBinding();
        var owner = await NativeOwner(authority);
        var source = new InfisicalSecretSource(owner);
        var original = await source.GetSecretAsync(binding);
        await owner.DisposeAsync();
        await Assert.That(source.IsRevisionCurrent(binding, original.Secret!.BindingRevision)).IsFalse();
        var retired = await source.GetSecretAsync(binding);
        await using var replacement = await NativeOwner(authority);
        var reopened = await new InfisicalSecretSource(replacement).GetSecretAsync(binding);
        await Assert.That(retired.Status).IsEqualTo(SecretResolutionStatus.Unavailable);
        await Assert.That(retired.Secret).IsNull();
        await Assert.That(reopened.Secret!.BindingRevision).IsNotEqualTo(original.Secret!.BindingRevision);
    }

    [Test]
    public async Task NativeInventoryEvictionInvalidatesCachedTokenWithoutAnotherMaterialRead()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values[PasswordKey] = Guid.NewGuid().ToString("N");
        await using var owner = await NativeOwner(authority);
        var source = new InfisicalSecretSource(owner);
        SecretBinding binding = NativeBinding();
        var cached = await source.GetSecretAsync(binding);
        var client = owner.NativeClient!;
        var read = await client.ReadNativeAsync(new SecretReference(client.Authority,
            new InfisicalSecretAddress("project", "testing", "/smtp", PasswordKey,
                version: null, resolution: InfisicalResolution.Raw, includeImports: false)));
        await Assert.That(read.IsSuccess).IsTrue();
        using var evidence = read.Value;
        for (int index = 0; index < 4096; index++)
            SecretBindingRevisionInventory.Observe(owner, RuntimeSecretBinding.Capture(NativeBinding()),
                evidence.Revision);
        await Assert.That(source.IsRevisionCurrent(binding, cached.Secret!.BindingRevision)).IsFalse();
        await Assert.That(authority.Reads).IsEqualTo(2);
        var refreshed = await source.GetSecretAsync(binding);
        await Assert.That(refreshed.Secret!.BindingRevision).IsNotEqualTo(cached.Secret.BindingRevision);
        await Assert.That(source.IsRevisionCurrent(binding, cached.Secret.BindingRevision)).IsFalse();
    }

    [Test]
    [Arguments(404, SecretResolutionStatus.Unconfigured)]
    [Arguments(401, SecretResolutionStatus.Unauthorized)]
    [Arguments(403, SecretResolutionStatus.Unauthorized)]
    [Arguments(429, SecretResolutionStatus.Unavailable)]
    [Arguments(503, SecretResolutionStatus.Unavailable)]
    public async Task NativeFailurePreservesBoundedStatusWithoutRetryOrDiagnostics(
        int statusCode, SecretResolutionStatus expected)
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        string diagnostic = Guid.NewGuid().ToString("N");
        authority.ProviderMessage = diagnostic;
        authority.Values[PasswordKey] = Guid.NewGuid().ToString("N");
        await using var owner = await NativeOwner(authority);
        authority.ReadStatusCode = statusCode;
        var result = await new InfisicalSecretSource(owner).GetSecretAsync(NativeBinding());
        await Assert.That(result.Status).IsEqualTo(expected);
        await Assert.That(result.Secret).IsNull();
        await Assert.That(result.ToString().Contains(diagnostic, StringComparison.Ordinal)).IsFalse();
        await Assert.That(authority.Reads).IsEqualTo(1);
        await Assert.That(authority.Logins).IsEqualTo(1);
    }

    [Test]
    [Arguments("unqualified", SecretResolutionStatus.Unavailable)]
    [Arguments("foreign", SecretResolutionStatus.Invalid)]
    [Arguments("hidden", SecretResolutionStatus.Unauthorized)]
    public async Task UnqualifiedContradictoryOrHiddenNativeEvidenceNeverProducesComparison(
        string evidence, SecretResolutionStatus expected)
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values[PasswordKey] = Guid.NewGuid().ToString("N");
        await using var owner = await NativeOwner(authority);
        switch (evidence)
        {
            case "unqualified": authority.OmitRevisionEvidence = true; break;
            case "foreign": authority.ReturnedProject = Guid.NewGuid().ToString("N"); break;
            case "hidden": authority.SecretValueHidden = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(evidence));
        }
        var result = await new InfisicalSecretSource(owner).GetSecretAsync(NativeBinding());
        await Assert.That(result.Status).IsEqualTo(expected);
        await Assert.That(result.Secret).IsNull();
    }

    [Test]
    public async Task NativeAdmissionCannotReadForeignEnvironmentOrProvider()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values[PasswordKey] = Guid.NewGuid().ToString("N");
        await using var owner = await NativeOwner(authority);
        SecretBinding binding = NativeBinding();
        binding.InfisicalEnvironment = "production";
        var result = await new InfisicalSecretSource(owner).GetSecretAsync(binding);
        var wrongSource = await new EnvironmentSecretSource(owner).GetSecretAsync(LocalBinding());
        await Assert.That(result.Status).IsEqualTo(SecretResolutionStatus.Invalid);
        await Assert.That(wrongSource.Status).IsEqualTo(SecretResolutionStatus.Invalid);
        await Assert.That(authority.Reads).IsEqualTo(0);
    }

    [Test]
    public async Task BindingMutationDuringNativeReadCannotRetargetReturnedProvenance()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        string value = Guid.NewGuid().ToString("N");
        authority.Values[PasswordKey] = value;
        authority.BlockedKey = PasswordKey;
        await using var owner = await NativeOwner(authority);
        SecretBinding binding = NativeBinding();
        Guid tenant = Guid.CreateVersion7();
        binding.Scope = SecretScope.Tenant;
        binding.ScopeId = tenant;
        Task entered = authority.ReadEntered.Task.WaitAsync(TestTimeout);
        Task<SecretResolutionResult> pending = new InfisicalSecretSource(owner).GetSecretAsync(binding);
        try
        {
            await entered;
            binding.ScopeId = Guid.CreateVersion7();
            binding.InfisicalEnvironment = "production";
        }
        finally
        {
            authority.ReadReleased.TrySetResult();
        }
        var result = await pending.WaitAsync(TestTimeout);
        await Assert.That(result.Value).IsEqualTo(value);
        await Assert.That(result.ScopeId).IsEqualTo(tenant);
        await Assert.That(result.Source).IsEqualTo(SecretSourceType.Infisical);
    }

    [Test]
    public async Task InFlightCallerCancellationRemainsCancellationWithoutRelogin()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values[PasswordKey] = Guid.NewGuid().ToString("N");
        authority.BlockedKey = PasswordKey;
        await using var owner = await NativeOwner(authority);
        using var cancellation = new CancellationTokenSource();
        Task entered = authority.ReadEntered.Task.WaitAsync(TestTimeout);
        Task<SecretResolutionResult> pending =
            new InfisicalSecretSource(owner).GetSecretAsync(NativeBinding(), cancellation.Token);
        try
        {
            await entered;
            await cancellation.CancelAsync();
            Func<Task> observe = async () => { _ = await pending.WaitAsync(TestTimeout); };
            await Assert.That(observe).Throws<OperationCanceledException>();
        }
        finally
        {
            authority.ReadReleased.TrySetResult();
        }
        await Assert.That(authority.Logins).IsEqualTo(1);
    }

    private static ConfigurationRoot LocalConfiguration(string? value) =>
        (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [PasswordKey] = value }).Build();

    private static SecretBinding LocalBinding()
    {
        var binding = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.Smtp.Password, SecretScope.Instance, null, PasswordKey);
        binding.Id = Guid.CreateVersion7();
        return binding;
    }

    private static SecretBinding NativeBinding()
    {
        var binding = SecretBinding.CreateInfisical(
            SecretDefinitionRegistry.Keys.Smtp.Password, SecretScope.Instance, null,
            "testing", "/smtp", PasswordKey);
        binding.Id = Guid.CreateVersion7();
        return binding;
    }

    private static async Task<SecretAuthorityAcquisition> NativeOwner(SecretKitHttpAuthority authority)
    {
        using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
            [new("/smtp", PasswordKey, "runtime")],
            new(authority.Origin, "project", "testing", Guid.NewGuid().ToString("N"),
                InfisicalTokenLifetime.Finite, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5)),
            bootstrap, new());
        if (!opened.IsSuccess)
            throw new InvalidOperationException("test_authority_unavailable");
        return opened.Value;
    }
}
