using Event.Testing.DataProtection;
using Event.Standalone.Hosting;
using Explore.API.Hosting;
using Explore.Blazor.Configuration;
using Explore.Blazor.Extensions;
using Explore.Blazor.Hosting;
using Explore.ServiceDefaults.DataProtection;
using Explore.ServiceDefaults.Secrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Standalone.IntegrationTests;

[NotInParallel]
public sealed class SecretKitServingHostPreparationTests
{
    [Test]
    public async Task CombinedPreparationSharesAuthorityAndAdmittedKeySourceAcrossBothHostModules()
    {
        var input = new ExternalDataProtectionTestAuthority("combined").Input;
        using var pins = new ProcessPins(input);
        string clientSecret = Guid.NewGuid().ToString("N");
        string databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        using var authority = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["EVENT_DP_COMBINED_RING"] = input.Ring,
                ["EVENT_DP_COMBINED_ADMISSION"] = input.Admission,
                ["GOOGLE_CLIENT_ID"] = Guid.NewGuid().ToString("N"),
                ["GOOGLE_CLIENT_SECRET"] = clientSecret,
                ["Database:Provider"] = "Sqlite",
                ["Database:Name"] = databasePath
            }).Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Combined), authority);
        var builder = Builder("Testing");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = "Environment",
            ["Database:Provider"] = "Sqlite",
            ["Database:Name"] = databasePath,
            ["Deployment:Mode"] = "SingleTenant",
            ["Deployment:DefaultTenantId"] = Guid.CreateVersion7().ToString()
        });
        await builder.PrepareApiHostSecretsAsync(owner, SecretConsumerRole.Combined);
        var admitted = builder.Services.Single(descriptor =>
            descriptor.ServiceType == typeof(IExternalDataProtectionSource)).ImplementationInstance;
        builder.AddApiHostServices(static () => false, secretConsumer: SecretConsumerRole.Combined);
        await builder.PrepareBlazorHostSecretsAsync(BlazorHostProfile.Combined, owner);
        var shutdownState = new GracefulShutdownState();
        using var shutdown = shutdownState.CancellationTokenSource;
        builder.AddBlazorHostServices(BlazorHostProfile.Combined, shutdownState);
        builder.Services.AddCombinedApiDataProtection();

        await Assert.That(builder.Services.Count(descriptor =>
            descriptor.ServiceType == typeof(SecretAuthorityAcquisition))).IsEqualTo(1);
        await Assert.That(builder.Services.Single(descriptor =>
            descriptor.ServiceType == typeof(IExternalDataProtectionSource)).ImplementationInstance).IsSameReferenceAs(admitted);
        await Assert.That(builder.Configuration["Google:ClientSecret"]).IsEqualTo(clientSecret);
        await using var provider = builder.Services.BuildServiceProvider();
        await Assert.That(provider.GetRequiredService<ExternalDataProtectionPartition>().Name).IsEqualTo("combined");
        var receipts = provider.GetRequiredService<AuthenticationCredentialAcquisitionReceipts>().Snapshot();
        await Assert.That(receipts.Count).IsEqualTo(1);
        var protector = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector(Guid.NewGuid().ToString("N"));
        byte[] plaintext = Guid.NewGuid().ToByteArray();
        await Assert.That(protector.Unprotect(protector.Protect(plaintext))).IsEquivalentTo(plaintext);
    }

    [Test]
    public async Task ProductionCompositionCannotSubstituteAnOwnerRegistrationForPreparation()
    {
        var builder = Builder("Production");
        using var authority = (ConfigurationRoot)new ConfigurationBuilder().Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), authority);
        builder.Services.AddSingleton(owner);
        Action compose = () => builder.AddApiHostServices(static () => false);
        var failure = await Assert.That(compose).Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).IsEqualTo("api_secret_preparation_required");
    }

    [Test]
    public async Task ApiPreparationRejectsAFrontendOnlyOwnerBeforePublishingServices()
    {
        var builder = Builder("Testing");
        using var authority = (ConfigurationRoot)new ConfigurationBuilder().Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Bff), authority);
        Func<Task> prepare = () => builder.PrepareApiHostSecretsAsync(owner, SecretConsumerRole.Api).AsTask();
        await Assert.That(prepare).Throws<InvalidOperationException>();
        await Assert.That(builder.Services.Any(descriptor =>
            descriptor.ServiceType == typeof(SecretAuthorityAcquisition))).IsFalse();
    }

    private static WebApplicationBuilder Builder(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.Sources.Clear();
        return builder;
    }

    private sealed class ProcessPins : IDisposable
    {
        private readonly string? _hash = Environment.GetEnvironmentVariable("EVENT_DP_COMBINED_ADMISSION_SHA256");
        private readonly string? _floor = Environment.GetEnvironmentVariable("EVENT_DP_COMBINED_MINIMUM_GENERATION");

        internal ProcessPins(ExternalDataProtectionInput input)
        {
            Environment.SetEnvironmentVariable("EVENT_DP_COMBINED_ADMISSION_SHA256", input.AdmissionSha256);
            Environment.SetEnvironmentVariable("EVENT_DP_COMBINED_MINIMUM_GENERATION", "1");
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("EVENT_DP_COMBINED_ADMISSION_SHA256", _hash);
            Environment.SetEnvironmentVariable("EVENT_DP_COMBINED_MINIMUM_GENERATION", _floor);
        }
    }
}
