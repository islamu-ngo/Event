using System.Security.Cryptography;
using System.Text.Json;
using Event.Testing.DataProtection;
using Explore.ServiceDefaults.DataProtection;
using Explore.ServiceDefaults.Secrets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using SecretKit.Core;
using SecretKit.Infisical;

namespace Explore.Infrastructure.Tests.Secrets;

[NotInParallel]
public sealed class SecretKitDataProtectionAdmissionTests
{
    [Test]
    public async Task PreparedRegistrationCanConstructProviderWithoutStartingHostOrBorrowingOwnerLifetime()
    {
        var authority = new ExternalDataProtectionTestAuthority("api");
        using var environment = new ProcessExpectations(authority.Input);
        using var configuration = LocalValues(authority.Input);
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), configuration);
        var services = new ServiceCollection();
        services.AddLogging();

        await services.PrepareExternalDataProtectionAsync(owner, "api");

        await using (var container = services.BuildServiceProvider())
        {
            var protector = container.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(Guid.NewGuid().ToString("N"));
            byte[] ciphertext = protector.Protect([1, 2, 3]);
            await Assert.That(protector.Unprotect(ciphertext)).IsEquivalentTo(new byte[] { 1, 2, 3 });
        }
        using var retained = await owner.PrepareDataProtectionAsync("api");
        using var ring = retained.ReadRing();
        await Assert.That(ring.Material.Reveal()).IsEqualTo(authority.Input.Ring);
    }

    [Test]
    public async Task RejectedPreparationCannotRegisterDependentProviderOrSource()
    {
        var authority = new ExternalDataProtectionTestAuthority("api");
        using var environment = new ProcessExpectations(authority.Input);
        using var configuration = LocalValues(authority.Input);
        configuration["EVENT_DP_API_RING"] = Guid.NewGuid().ToString("N");
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), configuration);
        var services = new ServiceCollection();

        await Assert.That(async () => await services.PrepareExternalDataProtectionAsync(owner, "api"))
            .Throws<InvalidOperationException>();
        await Assert.That(services.Any(descriptor => descriptor.ServiceType == typeof(IDataProtectionProvider)))
            .IsFalse();
        await Assert.That(services.Any(descriptor => descriptor.ServiceType == typeof(IExternalDataProtectionSource)))
            .IsFalse();
    }

    [Test]
    public async Task CandidateAuthorityCannotSupplyItsOwnDeploymentPin()
    {
        var authority = new ExternalDataProtectionTestAuthority("api");
        using var environment = new ProcessExpectations(authority.Input, includeMaterial: true);
        using var configuration = LocalValues(authority.Input);
        configuration["EVENT_DP_API_ADMISSION_SHA256"] = authority.Input.AdmissionSha256;
        Environment.SetEnvironmentVariable("EVENT_DP_API_ADMISSION_SHA256", null);
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), configuration);
        var source = new ExternalDataProtectionSource(owner, new("api"));
        await Assert.That(async () => await source.PrepareAsync()).Throws<InvalidOperationException>();
        await Assert.That(() => source.Read()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SynchronousReadCannotAcquireBeforeExplicitPreparation()
    {
        var authority = new ExternalDataProtectionTestAuthority("api");
        using var environment = new ProcessExpectations(authority.Input, includeMaterial: true);
        using var configuration = LocalValues(authority.Input);
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), configuration);
        var source = new ExternalDataProtectionSource(owner, new("api"));
        await Assert.That(() => source.Read()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task PreparedReadRemainsMemoryAfterAuthorityRemovalAndOwnerRetirement()
    {
        var authority = new ExternalDataProtectionTestAuthority("api");
        using var environment = new ProcessExpectations(authority.Input);
        using var configuration = LocalValues(authority.Input);
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), configuration);
        var source = new ExternalDataProtectionSource(owner, new("api"));
        await source.PrepareAsync();
        configuration["EVENT_DP_API_RING"] = null;
        configuration["EVENT_DP_API_ADMISSION"] = null;
        Environment.SetEnvironmentVariable("EVENT_DP_API_ADMISSION_SHA256", null);
        await owner.DisposeAsync();
        await Assert.That(source.Read()).IsEqualTo(authority.Input);
        Func<Task> fresh = async () => await source.ReadFreshAsync();
        await Assert.That(fresh).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(SecretAuthorityKind.Environment)]
    [Arguments(SecretAuthorityKind.UserSecrets)]
    public async Task FreshLocalReadUsesOnlyTheSelectedAuthority(SecretAuthorityKind kind)
    {
        var authority = new ExternalDataProtectionTestAuthority("api");
        using var environment = new ProcessExpectations(authority.Input, includeMaterial: true);
        using var configuration = LocalValues(authority.Input);
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(kind, "Testing", SecretConsumerRole.Api), configuration);
        var source = new ExternalDataProtectionSource(owner, new("api"));
        await source.PrepareAsync();
        configuration["EVENT_DP_API_ADMISSION"] = null;
        using var provider = ExternalDataProtectionProvider.Create(source.Read(), "api", TimeProvider.System);
        var protector = provider.CreateProtector(Guid.NewGuid().ToString("N"));
        byte[] ciphertext = protector.Protect([1]);
        var result = await new ExternalDataProtectionReadiness(source, provider, TimeProvider.System)
            .CheckHealthAsync(new());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(() => protector.Unprotect(ciphertext)).Throws<CryptographicException>();
        await Assert.That(() => protector.Protect([2])).Throws<CryptographicException>();
        await Assert.That(source.Read()).IsEqualTo(authority.Input);
    }

    [Test]
    [Arguments(SecretConsumerRole.Api, "api")]
    [Arguments(SecretConsumerRole.Bff, "bff")]
    [Arguments(SecretConsumerRole.Combined, "combined")]
    public async Task OnlyMatchingConsumerPartitionCanPrepare(SecretConsumerRole consumer, string partition)
    {
        var authority = new ExternalDataProtectionTestAuthority(partition);
        using var environment = new ProcessExpectations(authority.Input, partition: partition);
        using var configuration = LocalValues(authority.Input, partition);
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", consumer), configuration);
        var source = new ExternalDataProtectionSource(owner, new(partition));
        await source.PrepareAsync();
        using var provider = ExternalDataProtectionProvider.Create(source.Read(), partition, TimeProvider.System);
        provider.ProveActiveKey();
        await Assert.That(SecretAcquisitionPolicy.AllowsRoot(consumer, $"/runtime/{partition}/data-protection")).IsFalse();
        await Assert.That(SecretAcquisitionPolicy.AllowsRoot(consumer, $"/admission/{partition}/data-protection")).IsFalse();
        Func<Task> bulk = async () => await owner.PrepareGroupAsync(
            [new($"/runtime/{partition}/data-protection", "ring", "Ring")]);
        await Assert.That(bulk).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(SecretConsumerRole.Api, "bff")]
    [Arguments(SecretConsumerRole.Bff, "api")]
    [Arguments(SecretConsumerRole.Combined, "api")]
    [Arguments(SecretConsumerRole.Migration, "api")]
    public async Task ForeignPartitionCannotPublishPreparedInput(SecretConsumerRole consumer, string partition)
    {
        var authority = new ExternalDataProtectionTestAuthority(partition);
        using var environment = new ProcessExpectations(authority.Input, partition: partition);
        using var configuration = LocalValues(authority.Input, partition);
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", consumer), configuration);
        var source = new ExternalDataProtectionSource(owner, new(partition));
        await Assert.That(async () => await source.PrepareAsync()).Throws<InvalidOperationException>();
        await Assert.That(() => source.Read()).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOrContradictoryProviderMemberNeverPublishesPartialAdmission(bool contradictory)
    {
        var material = new ExternalDataProtectionTestAuthority("api");
        using var environment = new ProcessExpectations(material.Input);
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values["EVENT_DP_API_RING"] = material.Input.Ring;
        if (contradictory)
        {
            authority.Values["EVENT_DP_API_ADMISSION"] = material.Input.Admission;
            authority.ReturnedProject = Guid.NewGuid().ToString("N");
        }
        await using var owner = await OpenRemote(authority);
        var source = new ExternalDataProtectionSource(owner, new("api"));
        await Assert.That(async () => await source.PrepareAsync()).Throws<InvalidOperationException>();
        await Assert.That(() => source.Read()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CancellationAfterProviderDispatchPermanentlyRejectsCachedProtectors()
    {
        var material = new ExternalDataProtectionTestAuthority("api");
        using var environment = new ProcessExpectations(material.Input);
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values["EVENT_DP_API_RING"] = material.Input.Ring;
        authority.Values["EVENT_DP_API_ADMISSION"] = material.Input.Admission;
        await using var owner = await OpenRemote(authority);
        var source = new ExternalDataProtectionSource(owner, new("api"));
        await source.PrepareAsync();
        using var provider = ExternalDataProtectionProvider.Create(source.Read(), "api", TimeProvider.System);
        var protector = provider.CreateProtector(Guid.NewGuid().ToString("N"));
        byte[] ciphertext = protector.Protect([1]);
        authority.BlockedKey = "EVENT_DP_API_RING";
        using var cancellation = new CancellationTokenSource();
        var pending = new ExternalDataProtectionReadiness(source, provider, TimeProvider.System)
            .CheckHealthAsync(new(), cancellation.Token);
        try
        {
            await authority.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellation.CancelAsync();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
            await Assert.That(() => protector.Protect([2])).Throws<CryptographicException>();
            await Assert.That(() => protector.Unprotect(ciphertext)).Throws<CryptographicException>();
        }
        finally
        {
            authority.ReadReleased.TrySetResult();
        }
    }

    [Test]
    public async Task RetirementCancelsPendingPreparationAndRejectsLatePublication()
    {
        var material = new ExternalDataProtectionTestAuthority("api");
        using var environment = new ProcessExpectations(material.Input);
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values["EVENT_DP_API_RING"] = material.Input.Ring;
        authority.Values["EVENT_DP_API_ADMISSION"] = material.Input.Admission;
        authority.BlockedKey = "EVENT_DP_API_ADMISSION";
        await using var owner = await OpenRemote(authority);
        var source = new ExternalDataProtectionSource(owner, new("api"));
        Task preparation = source.PrepareAsync().AsTask();
        try
        {
            await authority.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(async () => await preparation).Throws<OperationCanceledException>();
            await Assert.That(() => source.Read()).Throws<InvalidOperationException>();
        }
        finally
        {
            authority.ReadReleased.TrySetResult();
        }
    }

    [Test]
    [Arguments("pin")]
    [Arguments("floor")]
    [Arguments("ring")]
    [Arguments("admission")]
    public async Task ActualSelectedOwnerFreshDivergenceRejectsServingAndNeverReplacesSnapshot(string changed)
    {
        var material = new ExternalDataProtectionTestAuthority("api");
        using var environment = new ProcessExpectations(material.Input);
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values["EVENT_DP_API_RING"] = material.Input.Ring;
        authority.Values["EVENT_DP_API_ADMISSION"] = material.Input.Admission;
        await using var owner = await OpenRemote(authority);
        var source = new ExternalDataProtectionSource(owner, new("api"));
        await source.PrepareAsync();
        using var provider = ExternalDataProtectionProvider.Create(source.Read(), "api", TimeProvider.System);
        var protector = provider.CreateProtector(Guid.NewGuid().ToString("N"));
        byte[] ciphertext = protector.Protect([1]);
        if (changed == "pin")
            Environment.SetEnvironmentVariable("EVENT_DP_API_ADMISSION_SHA256",
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        else if (changed == "floor")
            Environment.SetEnvironmentVariable("EVENT_DP_API_MINIMUM_GENERATION", "2");
        else
        {
            var replacement = new ExternalDataProtectionTestAuthority("api").Input;
            authority.Values[changed == "ring" ? "EVENT_DP_API_RING" : "EVENT_DP_API_ADMISSION"] =
                changed == "ring" ? replacement.Ring : replacement.Admission;
        }
        var readiness = new ExternalDataProtectionReadiness(source, provider, TimeProvider.System);
        var result = await readiness.CheckHealthAsync(new());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(source.Read()).IsEqualTo(material.Input);
        await Assert.That(() => protector.Protect([2])).Throws<CryptographicException>();
        await Assert.That(() => protector.Unprotect(ciphertext)).Throws<CryptographicException>();
        Environment.SetEnvironmentVariable("EVENT_DP_API_ADMISSION_SHA256", material.Input.AdmissionSha256);
        Environment.SetEnvironmentVariable("EVENT_DP_API_MINIMUM_GENERATION", "1");
        authority.Values["EVENT_DP_API_RING"] = material.Input.Ring;
        authority.Values["EVENT_DP_API_ADMISSION"] = material.Input.Admission;
        await readiness.CheckHealthAsync(new());
        await Assert.That(() => protector.Protect([2])).Throws<CryptographicException>();
        await Assert.That(() => protector.Unprotect(ciphertext)).Throws<CryptographicException>();
    }

    [Test]
    public async Task AdmissionKeepsActual24HourCeilingAndExpiresCachedServingAtBoundary()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        var material = new ExternalDataProtectionTestAuthority("api", clock);
        var admission = JsonSerializer.Deserialize<ExternalDataProtectionAdmission>(material.Input.Admission)!;
        string json = JsonSerializer.Serialize(admission with { NotAfterUtc = admission.NotBeforeUtc.AddHours(24) });
        var input = material.Input with { Admission = json, AdmissionSha256 = ExternalDataProtectionInput.Hash(json) };
        using var environment = new ProcessExpectations(input);
        using var configuration = LocalValues(input);
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), configuration);
        var source = new ExternalDataProtectionSource(owner, new("api"), clock);
        await source.PrepareAsync();
        using var provider = ExternalDataProtectionProvider.Create(source.Read(), "api", clock);
        var protector = provider.CreateProtector(Guid.NewGuid().ToString("N"));
        byte[] ciphertext = protector.Protect([1]);
        clock.Now = admission.NotBeforeUtc.AddHours(24);
        await Assert.That(() => protector.Protect([2])).Throws<CryptographicException>();
        await Assert.That(() => protector.Unprotect(ciphertext)).Throws<CryptographicException>();
        clock.Now = admission.NotBeforeUtc;
        string tooLong = JsonSerializer.Serialize(admission with { NotAfterUtc = admission.NotBeforeUtc.AddHours(24).AddTicks(1) });
        configuration["EVENT_DP_API_ADMISSION"] = tooLong;
        Environment.SetEnvironmentVariable("EVENT_DP_API_ADMISSION_SHA256", ExternalDataProtectionInput.Hash(tooLong));
        var rejected = new ExternalDataProtectionSource(owner, new("api"), clock);
        await Assert.That(async () => await rejected.PrepareAsync()).Throws<InvalidOperationException>();
        await Assert.That(() => rejected.Read()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task FloorChangeInsideAlreadyAdmittedGenerationStillRequiresRestart()
    {
        var authority = new ExternalDataProtectionTestAuthority("api");
        var ring = JsonSerializer.Deserialize<ExternalDataProtectionRing>(authority.Input.Ring)!;
        var admission = JsonSerializer.Deserialize<ExternalDataProtectionAdmission>(authority.Input.Admission)!;
        string ringJson = JsonSerializer.Serialize(ring with { Generation = 2 });
        string admissionJson = JsonSerializer.Serialize(admission with
        {
            Generation = 2, RingSha256 = ExternalDataProtectionInput.Hash(ringJson)
        });
        var input = new ExternalDataProtectionInput(ringJson, admissionJson, ExternalDataProtectionInput.Hash(admissionJson), 1);
        var source = new FreshAuthority(input) { Fresh = input with { MinimumGeneration = 2 } };
        using var provider = ExternalDataProtectionProvider.Create(input, "api", TimeProvider.System);
        var result = await new ExternalDataProtectionReadiness(source, provider, TimeProvider.System).CheckHealthAsync(new());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(() => provider.CreateProtector("floor").Protect([1])).Throws<CryptographicException>();
    }

    [Test]
    [Arguments("pin")]
    [Arguments("floor")]
    [Arguments("ring")]
    [Arguments("admission")]
    public async Task FreshDivergencePermanentlyRejectsCachedProtectors(string changed)
    {
        var authority = new ExternalDataProtectionTestAuthority("api");
        var source = new FreshAuthority(authority.Input);
        using var provider = ExternalDataProtectionProvider.Create(source.Read(), "api", TimeProvider.System);
        var protector = provider.CreateProtector(Guid.NewGuid().ToString("N"));
        byte[] ciphertext = protector.Protect([1, 2, 3]);
        source.Fresh = changed switch
        {
            "pin" => authority.Input with { AdmissionSha256 = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) },
            "floor" => authority.Input with { MinimumGeneration = 2 },
            "ring" => authority.Input with { Ring = new ExternalDataProtectionTestAuthority("api").Input.Ring },
            _ => authority.Input with { Admission = new ExternalDataProtectionTestAuthority("api").Input.Admission }
        };
        var readiness = new ExternalDataProtectionReadiness(source, provider, TimeProvider.System);
        var result = await readiness.CheckHealthAsync(new());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(() => protector.Protect([4])).Throws<CryptographicException>();
        await Assert.That(() => protector.Unprotect(ciphertext)).Throws<CryptographicException>();
        source.Fresh = authority.Input;
        await readiness.CheckHealthAsync(new());
        await Assert.That(() => protector.Unprotect(ciphertext)).Throws<CryptographicException>();
    }

    [Test]
    public async Task CancellationBeforeAttemptDoesNotRejectServingAdmission()
    {
        var authority = new ExternalDataProtectionTestAuthority("api");
        using var provider = ExternalDataProtectionProvider.Create(authority.Input, "api", TimeProvider.System);
        var source = new FreshAuthority(authority.Input);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Func<Task> check = async () => await new ExternalDataProtectionReadiness(source, provider, TimeProvider.System)
            .CheckHealthAsync(new(), cancellation.Token);
        await Assert.That(check).Throws<OperationCanceledException>();
        var protector = provider.CreateProtector(Guid.NewGuid().ToString("N"));
        byte[] ciphertext = protector.Protect([1]);
        await Assert.That(protector.Unprotect(ciphertext).Length).IsEqualTo(1);
    }

    [Test]
    public async Task UncertainFreshReadRejectsServingAdmission()
    {
        var authority = new ExternalDataProtectionTestAuthority("api");
        var source = new FreshAuthority(authority.Input) { Failure = new OperationCanceledException() };
        using var provider = ExternalDataProtectionProvider.Create(authority.Input, "api", TimeProvider.System);
        var protector = provider.CreateProtector(Guid.NewGuid().ToString("N"));
        byte[] ciphertext = protector.Protect([1]);
        var result = await new ExternalDataProtectionReadiness(source, provider, TimeProvider.System)
            .CheckHealthAsync(new());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(() => protector.Protect([2])).Throws<CryptographicException>();
        await Assert.That(() => protector.Unprotect(ciphertext)).Throws<CryptographicException>();
    }

    private sealed class FreshAuthority : IExternalDataProtectionSource
    {
        private readonly ExternalDataProtectionInput prepared;
        public FreshAuthority(ExternalDataProtectionInput input)
        {
            prepared = input;
            Fresh = input;
        }
        public ExternalDataProtectionInput Fresh { get; set; }
        public Exception? Failure { get; init; }
        public ExternalDataProtectionInput Read() => prepared;
        public ValueTask PrepareAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<ExternalDataProtectionInput> ReadFreshAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure is null ? ValueTask.FromResult(Fresh) : ValueTask.FromException<ExternalDataProtectionInput>(Failure);
        }
    }

    private sealed class ProcessExpectations : IDisposable
    {
        private readonly Dictionary<string, string?> previous = [];
        public ProcessExpectations(ExternalDataProtectionInput input, bool includeMaterial = false, string partition = "api")
        {
            string prefix = ExternalDataProtectionRegistration.EnvironmentPrefix(partition);
            Set(prefix + "_ADMISSION_SHA256", input.AdmissionSha256);
            Set(prefix + "_MINIMUM_GENERATION", input.MinimumGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (includeMaterial)
            {
                Set(prefix + "_RING", input.Ring);
                Set(prefix + "_ADMISSION", input.Admission);
            }
        }
        private void Set(string key, string value)
        {
            previous.Add(key, Environment.GetEnvironmentVariable(key));
            Environment.SetEnvironmentVariable(key, value);
        }
        public void Dispose()
        {
            foreach (var pair in previous)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    private static ConfigurationRoot LocalValues(ExternalDataProtectionInput input, string partition = "api")
    {
        string prefix = ExternalDataProtectionRegistration.EnvironmentPrefix(partition);
        return (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [prefix + "_RING"] = input.Ring,
                [prefix + "_ADMISSION"] = input.Admission
            }).Build();
    }

    private static async Task<SecretAuthorityAcquisition> OpenRemote(SecretKitHttpAuthority authority)
    {
        using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        var result = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
            [new("/database", "unused", "Database:Unused")],
            new(authority.Origin, "project", "testing", Guid.NewGuid().ToString("N"),
                InfisicalTokenLifetime.Finite, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
            bootstrap, new());
        if (!result.IsSuccess)
            throw new InvalidOperationException("test_authority_open_failed");
        return result.Value;
    }

    private sealed class ControlledClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
