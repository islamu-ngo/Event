using Explore.Application.Services;
using Explore.Secrets.Services;
using Microsoft.Extensions.Configuration;

namespace Explore.Secrets.UnitTests.Configuration;

[NotInParallel]
public sealed class ModerationReconciliationSecretTests
{
    private const string Variable = "MODERATION__RECONCILIATION__APIKEY";

    [Test]
    public async Task Selected_environment_credential_is_reread_after_rotation()
    {
        var original = Environment.GetEnvironmentVariable(Variable);
        var first = Credential();
        var rotated = Credential();
        try
        {
            var provider = new ModerationReconciliationSecretProvider(Configuration("Environment"));
            Environment.SetEnvironmentVariable(Variable, first);
            await Assert.That(await provider.ReadAsync(default)).IsEqualTo(first);
            Environment.SetEnvironmentVariable(Variable, rotated);
            await Assert.That(await provider.ReadAsync(default)).IsEqualTo(rotated);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, original);
        }
    }

    [Test]
    public async Task Ordinary_application_configuration_cannot_supply_the_selected_secret()
    {
        var original = Environment.GetEnvironmentVariable(Variable);
        try
        {
            Environment.SetEnvironmentVariable(Variable, null);
            var configuration = Configuration("Environment");
            configuration["Moderation:Reconciliation:ApiKey"] = Credential();
            await Assert.That(await new ModerationReconciliationSecretProvider(configuration)
                .ReadAsync(default) is null).IsTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, original);
        }
    }

    [Test]
    public async Task Production_rejects_user_secrets_instead_of_falling_back()
    {
        await Assert.That(async () =>
        {
            await new ModerationReconciliationSecretProvider(Configuration("UserSecrets"))
                .ReadAsync(default);
        }).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Cancelled_acquisition_does_not_contact_the_selected_authority()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(async () =>
        {
            await new ModerationReconciliationSecretProvider(Configuration("Unselected"))
                .ReadAsync(cancellation.Token);
        }).Throws<OperationCanceledException>();
    }

    private static string Credential() =>
        ApiKeyHashing.FormatPersistedApiKey(ApiKeyHashing.CreateKeyId(), ApiKeyHashing.CreateSecret());

    private static IConfiguration Configuration(string provider) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = provider,
            ["DOTNET_ENVIRONMENT"] = "Production"
        }).Build();
}
