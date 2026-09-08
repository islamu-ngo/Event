extern alias migrationservice;

using Microsoft.Extensions.Configuration;
using MigrationConfigurationExtensions = migrationservice::Event.MigrationService.Extensions.ConfigurationExtensions;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class MigrationSecretAuthorityTests
{
    [Test]
    public async Task AddPrimaryDatabaseBootstrap_WhenUserSecretsIsSelectedInProduction_FailsClosed()
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = "UserSecrets",
        });

        Action act = () => MigrationConfigurationExtensions.AddPrimaryDatabaseBootstrap(
            builder,
            "Production");

        var exception = await Assert.That(act).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message)
            .IsEqualTo("secret_authority_user_secrets_environment_invalid");
    }
}
