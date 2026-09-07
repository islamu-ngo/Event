using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Geocoding;

public sealed class PhotonStartupConfigurationContractTests
{
    [Test]
    public async Task ProviderNoneIsTheValidDefaultWithoutPhotonNetworkRegistration()
    {
        object options = PhotonDeploymentContractHost.CreateOptions();
        ValidateOptionsResult result = PhotonDeploymentContractHost.Validate(
            options,
            Environments.Production);
        ServiceCollection services = PhotonDeploymentContractHost.Compose(
            Environments.Production,
            Key("Geocoding:Provider", "None"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(PhotonDeploymentContractHost.ReadRequired(options, "Provider")?.ToString())
            .IsEqualTo("None");
        await Assert.That(PhotonDeploymentContractHost.ReadRequired(options, "Endpoint", "BaseAddress"))
            .IsNull();
        await Assert.That(services.Any(IsPhotonNetworkRegistration)).IsFalse();
        await Assert.That(services.Any(IsPhotonOptionsValidatorRegistration)).IsTrue();
    }

    [Test]
    public async Task ProductionPhotonRequiresExplicitHttpsEndpointAndRejectsPublicDemo()
    {
        object missing = PhotonDeploymentContractHost.CreateProductionPhotonOptions();
        PhotonDeploymentContractHost.SetRequired(missing, null, "Endpoint", "BaseAddress");
        object plaintext = PhotonDeploymentContractHost.CreateProductionPhotonOptions();
        PhotonDeploymentContractHost.SetRequired(
            plaintext,
            new Uri("http://photon.operator.example/"),
            "Endpoint",
            "BaseAddress");
        object publicDemo = PhotonDeploymentContractHost.CreateProductionPhotonOptions();
        PhotonDeploymentContractHost.SetRequired(
            publicDemo,
            new Uri("https://photon.komoot.io/"),
            "Endpoint",
            "BaseAddress");

        await Assert.That(PhotonDeploymentContractHost.Validate(missing, Environments.Production).Failed)
            .IsTrue();
        await Assert.That(PhotonDeploymentContractHost.Validate(plaintext, Environments.Production).Failed)
            .IsTrue();
        await Assert.That(PhotonDeploymentContractHost.Validate(publicDemo, Environments.Production).Failed)
            .IsTrue();
    }

    [Test]
    public async Task RuntimeOptionsContainNoDeploymentManifestMetadata()
    {
        string[] forbidden =
        [
            "OperatorOwner", "DeploymentOwner", "ReleaseVersion", "PhotonReleaseVersion",
            "ReleaseSha256", "ReleaseChecksumSha256", "DatasetManifestSha256",
            "DatasetChecksumSha256", "ActivationEvidenceId", "DeploymentManifestId"
        ];
        string[] properties = PhotonDeploymentContractHost.OptionsType.GetProperties()
            .Select(property => property.Name)
            .ToArray();

        await Assert.That(properties.Intersect(forbidden, StringComparer.Ordinal)).IsEmpty();
    }

    [Test]
    public async Task PhotonRequiresBoundedDatasetVersionForTokenInvalidation()
    {
        object missing = PhotonDeploymentContractHost.CreateProductionPhotonOptions();
        PhotonDeploymentContractHost.SetRequired(
            missing,
            string.Empty,
            "DatasetVersion");

        ValidateOptionsResult result = PhotonDeploymentContractHost.Validate(
            missing,
            Environments.Production);

        await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task RuntimeResilienceAndSelectionLifetimeRejectUnsafeBounds()
    {
        object noBudget = PhotonDeploymentContractHost.CreateProductionPhotonOptions();
        PhotonDeploymentContractHost.SetRequired(
            noBudget,
            0,
            "TotalTimeoutMilliseconds",
            "RequestTimeoutMilliseconds");
        object excessiveBudget = PhotonDeploymentContractHost.CreateProductionPhotonOptions();
        PhotonDeploymentContractHost.SetRequired(
            excessiveBudget,
            5_001,
            "TotalTimeoutMilliseconds",
            "RequestTimeoutMilliseconds");
        object excessiveRetries = PhotonDeploymentContractHost.CreateProductionPhotonOptions();
        PhotonDeploymentContractHost.SetRequired(excessiveRetries, 3, "MaximumRetryCount", "RetryCount");
        object unboundedDelays = PhotonDeploymentContractHost.CreateProductionPhotonOptions();
        PhotonDeploymentContractHost.SetRequired(
            unboundedDelays,
            new[] { 200, 5_000 },
            "RetryDelaysMilliseconds");
        object invalidLifetime = PhotonDeploymentContractHost.CreateProductionPhotonOptions();
        PhotonDeploymentContractHost.SetRequired(invalidLifetime, 0, "SelectionLifetimeSeconds");

        await Assert.That(PhotonDeploymentContractHost.Validate(noBudget, Environments.Production).Failed)
            .IsTrue();
        await Assert.That(PhotonDeploymentContractHost.Validate(excessiveBudget, Environments.Production).Failed)
            .IsTrue();
        await Assert.That(PhotonDeploymentContractHost.Validate(excessiveRetries, Environments.Production).Failed)
            .IsTrue();
        await Assert.That(PhotonDeploymentContractHost.Validate(unboundedDelays, Environments.Production).Failed)
            .IsTrue();
        await Assert.That(PhotonDeploymentContractHost.Validate(invalidLifetime, Environments.Production).Failed)
            .IsTrue();
    }

    [Test]
    public async Task PhotonOptionsExposeNoSecretCredentialOrTokenSetting()
    {
        string[] forbiddenFragments = ["secret", "credential", "token", "apikey", "password"];
        string[] propertyNames = PhotonDeploymentContractHost.OptionsType.GetProperties()
            .Select(property => property.Name.Replace("_", string.Empty).ToLowerInvariant())
            .ToArray();

        foreach (string propertyName in propertyNames)
        {
            foreach (string fragment in forbiddenFragments)
            {
                await Assert.That(propertyName).DoesNotContain(fragment);
            }
        }
    }

    [Test]
    public async Task InvalidConfigurationReturnsBoundedErrorsWithoutConfiguredValues()
    {
        string endpointCanary = $"endpoint-{Guid.CreateVersion7():N}.example";
        string queryCanary = $"query-{Guid.CreateVersion7():N}";
        string piiCanary = $"address-{Guid.CreateVersion7():N}";
        object options = PhotonDeploymentContractHost.CreateProductionPhotonOptions();
        PhotonDeploymentContractHost.SetRequired(
            options,
            new Uri($"https://{endpointCanary}/{piiCanary}?q={queryCanary}"),
            "Endpoint",
            "BaseAddress");
        ValidateOptionsResult result = PhotonDeploymentContractHost.Validate(
            options,
            Environments.Production);
        string errors = string.Join(" | ", result.Failures ?? []);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(errors.Length).IsLessThanOrEqualTo(1_024);
        await Assert.That(errors).DoesNotContain(endpointCanary);
        await Assert.That(errors).DoesNotContain(queryCanary);
        await Assert.That(errors).DoesNotContain(piiCanary);
        await Assert.That(errors).DoesNotContain("https://");
    }

    [Test]
    public async Task UnsupportedGoogleValueIsRejectedWithoutProviderRegistration()
    {
        object options = PhotonDeploymentContractHost.CreateOptions("GooglePlaces");
        ValidateOptionsResult result = PhotonDeploymentContractHost.Validate(
            options,
            Environments.Production);
        ServiceCollection services = PhotonDeploymentContractHost.Compose(
            Environments.Production,
            Key("Geocoding:Provider", "GooglePlaces"));

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(services.Any(descriptor =>
            Describe(descriptor).Contains("GooglePlaces", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    private static KeyValuePair<string, string?> Key(string key, string? value) => new(key, value);

    private static bool IsPhotonNetworkRegistration(ServiceDescriptor descriptor)
    {
        string description = Describe(descriptor);
        return description.Contains("Photon", StringComparison.OrdinalIgnoreCase)
            && (description.Contains("Http", StringComparison.OrdinalIgnoreCase)
                || description.Contains("Geocoder", StringComparison.OrdinalIgnoreCase)
                || description.Contains("Adapter", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPhotonOptionsValidatorRegistration(ServiceDescriptor descriptor) =>
        descriptor.ServiceType.IsGenericType
        && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IValidateOptions<>)
        && descriptor.ServiceType.GenericTypeArguments[0] == PhotonDeploymentContractHost.OptionsType
        && Describe(descriptor).Contains("PhotonOptionsValidator", StringComparison.Ordinal);

    private static string Describe(ServiceDescriptor descriptor) => string.Join(
        "|",
        descriptor.ServiceType.FullName,
        descriptor.ImplementationType?.FullName,
        descriptor.ImplementationInstance?.GetType().FullName,
        descriptor.ImplementationFactory?.Method.ReturnType.FullName);
}
