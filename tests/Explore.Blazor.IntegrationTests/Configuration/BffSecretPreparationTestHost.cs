using Event.Testing.DataProtection;
using Explore.Blazor.Configuration;
using Explore.Blazor.Hosting;
using Explore.ServiceDefaults.DataProtection;
using Explore.ServiceDefaults.Secrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.IntegrationTests.Configuration;

/// <summary>Uses the serving host's actual preparation with a selected test authority and admitted BFF ring.</summary>
internal sealed class BffSecretPreparationTestHost : IAsyncDisposable
{
    private readonly string? _previousHash = Environment.GetEnvironmentVariable("EVENT_DP_BFF_ADMISSION_SHA256");
    private readonly string? _previousFloor = Environment.GetEnvironmentVariable("EVENT_DP_BFF_MINIMUM_GENERATION");
    private readonly ExternalDataProtectionInput _dataProtection = new ExternalDataProtectionTestAuthority("bff").Input;
    private ConfigurationRoot? _localAuthority;
    private SecretAuthorityAcquisition? _owner;

    internal BffSecretPreparationTestHost()
    {
        Builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        Builder.Logging.ClearProviders();
        Builder.Configuration.Sources.Clear();
        Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = "Environment",
            ["AUTHENTICATION_CREDENTIAL_PROFILE_REVISION"] = ProfileRevision.ToString()
        });
        Environment.SetEnvironmentVariable("EVENT_DP_BFF_ADMISSION_SHA256", _dataProtection.AdmissionSha256);
        Environment.SetEnvironmentVariable("EVENT_DP_BFF_MINIMUM_GENERATION", "1");
    }

    internal WebApplicationBuilder Builder { get; }
    internal Guid ProfileRevision { get; } = Guid.CreateVersion7();
    internal SecretAuthorityLoginAttempt LoginAttempt { get; } = new();
    internal SecretAuthorityAcquisition Owner => _owner ?? throw new InvalidOperationException("Authority not opened.");
    internal AuthenticationCredentialAcquisitionReceipts Receipts =>
        (AuthenticationCredentialAcquisitionReceipts)Builder.Services.Single(descriptor =>
            descriptor.ServiceType == typeof(AuthenticationCredentialAcquisitionReceipts)).ImplementationInstance!;
    internal Dictionary<string, string?> DataProtectionValues => new()
    {
        ["EVENT_DP_BFF_RING"] = _dataProtection.Ring,
        ["EVENT_DP_BFF_ADMISSION"] = _dataProtection.Admission
    };

    internal async Task PrepareLocalAsync(
        Dictionary<string, string?>? values = null, SecretAuthorityKind provider = SecretAuthorityKind.Environment)
    {
        var selectedValues = DataProtectionValues;
        foreach (var value in values ?? [])
            selectedValues.Add(value.Key, value.Value);
        _localAuthority = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(selectedValues).Build();
        Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = provider.ToString()
        });
        _owner = await SecretAuthorityBootstrap.OpenAsync(
            Builder.Configuration, "Testing", SecretConsumerRole.Bff,
            [new("/runtime/bff/oidc/keycloak", "KEYCLOAK_ENDPOINT", "KEYCLOAK_ENDPOINT")],
            LoginAttempt, _localAuthority);
        await Builder.PrepareBlazorHostSecretsAsync(BlazorHostProfile.Split, _owner);
    }

    internal async Task PrepareInfisicalAsync(string url, string? folder = null)
    {
        Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = "Infisical",
            ["SecretProvider:Infisical:Url"] = url,
            ["SecretProvider:Infisical:ProjectId"] = Guid.CreateVersion7().ToString("N"),
            ["SecretProvider:Infisical:ClientId"] = Guid.CreateVersion7().ToString("N"),
            ["SecretProvider:Infisical:ClientSecret"] = Guid.NewGuid().ToString("N"),
            ["SecretProvider:Infisical:Environment"] = "testing",
            ["SecretProvider:Infisical:TokenLifetime"] = "Finite"
        });
        _owner = await SecretAuthorityBootstrap.OpenAsync(
            Builder.Configuration, "Testing", SecretConsumerRole.Bff,
            [new(folder ?? "/runtime/bff/oidc/keycloak", "KEYCLOAK_ENDPOINT", "KEYCLOAK_ENDPOINT")],
            LoginAttempt);
        await Builder.PrepareBlazorHostSecretsAsync(BlazorHostProfile.Split, _owner);
    }

    internal async Task AssertNothingPublished()
    {
        await Assert.That(Builder.Configuration["Keycloak:ClientSecret"]).IsNull();
        await Assert.That(Builder.Configuration["Google:ClientSecret"]).IsNull();
        await Assert.That(Builder.Configuration["Atproto:OauthClientPrivateJwks"]).IsNull();
        await Assert.That(Builder.Services.Any(descriptor =>
            descriptor.ServiceType == typeof(AuthenticationCredentialAcquisitionReceipts)
            || descriptor.ServiceType == typeof(IExternalDataProtectionSource)
            || descriptor.ServiceType == typeof(SecretAuthorityAcquisition))).IsFalse();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_owner is not null)
                await _owner.DisposeAsync();
        }
        finally
        {
            _localAuthority?.Dispose();
            ((IDisposable)Builder.Configuration).Dispose();
            Environment.SetEnvironmentVariable("EVENT_DP_BFF_ADMISSION_SHA256", _previousHash);
            Environment.SetEnvironmentVariable("EVENT_DP_BFF_MINIMUM_GENERATION", _previousFloor);
        }
    }
}
