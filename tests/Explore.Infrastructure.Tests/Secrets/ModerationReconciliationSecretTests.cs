using Explore.Application.Services;
using Explore.Application.Contracts.Secrets;
using Explore.Infrastructure.Secrets;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class ModerationReconciliationSecretTests
{
    private const string Variable = "MODERATION__RECONCILIATION__APIKEY";

    [Test]
    public async Task LocalRotationRequiresANewSelectedOwnerGeneration()
    {
        var first = Credential();
        var rotated = Credential();
        using var selected = Configuration(first);
        await using var original = Owner(selected);
        var provider = new ModerationReconciliationSecretProvider(original);
        await Assert.That(await provider.ReadAsync(default)).IsEqualTo(first);
        selected[Variable] = rotated;
        await Assert.That(await provider.ReadAsync(default)).IsEqualTo(first);
        await using var replacement = Owner(selected);
        await Assert.That(await new ModerationReconciliationSecretProvider(replacement)
            .ReadAsync(default)).IsEqualTo(rotated);
    }

    [Test]
    public async Task Ordinary_application_configuration_cannot_supply_the_selected_secret()
    {
        using var selected = Configuration(null);
        using var ordinary = Configuration(null);
        ordinary["Moderation:Reconciliation:ApiKey"] = Credential();
        await using var owner = Owner(selected);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(ordinary);
        services.AddSingleton(owner);
        services.AddNativeSecretResolution();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IModerationReconciliationSecretProvider>();
        await Assert.That(await reader.ReadAsync(default) is null).IsTrue();
    }

    [Test]
    public async Task Production_rejects_user_secrets_instead_of_falling_back()
    {
        using var selected = Configuration(null);
        await Assert.That(() => SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.UserSecrets, "Production", SecretConsumerRole.Api),
            selected)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Cancelled_acquisition_does_not_contact_the_selected_authority()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var selected = Configuration(null);
        await using var owner = Owner(selected);
        await owner.DisposeAsync();
        await Assert.That(async () =>
        {
            await new ModerationReconciliationSecretProvider(owner)
                .ReadAsync(cancellation.Token);
        }).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task ModerationMaintenanceCredentialUsesItsExactApiAuthorityKey()
    {
        var credential = Credential();
        using var selected = Configuration(credential);
        await using var owner = Owner(selected);
        var prepared = await owner.PrepareConfigurationGroupAsync(["/api"]);
        await Assert.That(prepared.IsSuccess).IsTrue();
        await Assert.That(prepared.Value["Moderation:Reconciliation:ApiKey"]).IsEqualTo(credential);
    }

    [Test]
    public async Task RetiredAuthorityCannotBeReportedAsAnAbsentCredential()
    {
        using var selected = Configuration(Credential());
        await using var owner = Owner(selected);
        await owner.DisposeAsync();
        await Assert.That(async () =>
        {
            await new ModerationReconciliationSecretProvider(owner).ReadAsync(default);
        }).Throws<ObjectDisposedException>();
    }

    [Test]
    [Arguments(SecretConsumerRole.Api)]
    [Arguments(SecretConsumerRole.Bff)]
    [Arguments(SecretConsumerRole.Combined)]
    [Arguments(SecretConsumerRole.Migration)]
    public async Task ServingAndMigrationOwnersCannotAcquireMeasurementRoot(SecretConsumerRole consumer)
    {
        using var selected = Configuration(null);
        selected["MODERATION_MEASUREMENT_SIGNING_PRIVATE_KEY_PEM"] = Credential();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", consumer), selected);
        await Assert.That(async () =>
        {
            await owner.PrepareConfigurationGroupAsync(["/moderation"]);
        }).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MeasurementOwnerCannotAcquireReconciliationCredential()
    {
        using var selected = Configuration(Credential());
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.ModerationMeasurement), selected);
        await Assert.That(async () =>
        {
            await new ModerationReconciliationSecretProvider(owner).ReadAsync(default);
        }).Throws<InvalidOperationException>();
    }

    private static string Credential() =>
        ApiKeyHashing.FormatPersistedApiKey(ApiKeyHashing.CreateKeyId(), ApiKeyHashing.CreateSecret());

    private static SecretAuthorityAcquisition Owner(IConfiguration selected) =>
        SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), selected);

    private static ConfigurationRoot Configuration(string? credential) =>
        (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Variable] = credential
        }).Build();
}
