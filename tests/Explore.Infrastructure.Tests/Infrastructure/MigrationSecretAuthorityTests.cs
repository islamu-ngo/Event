using Explore.Persistence.Database;
using Microsoft.Extensions.Configuration;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class MigrationSecretAuthorityTests
{
    [Test]
    public async Task MigrationAcquisition_WhenUserSecretsIsSelectedInProduction_FailsClosed()
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = "UserSecrets",
        });

        Func<Task> act = async () =>
        {
            await using var acquisition = await MigrationDatabaseAcquisition.OpenAsync(
                builder.Build(), "Production", ["/database"]);
        };

        var exception = await Assert.That(act).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message)
            .IsEqualTo("secret_authority_user_secrets_environment_invalid");
    }
}
