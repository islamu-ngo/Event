using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.UnitTests.Database;

public sealed class EmbeddedPrivacyErasureAuthorityOptionsTests
{
    [Test]
    public async Task MissingConfigurationUsesProductionDefaults()
    {
        EmbeddedPrivacyErasureAuthorityOptions options =
            EmbeddedPrivacyErasureAuthorityOptions.Bind(BuildConfiguration([]));

        await Assert.That(options.Path).IsEqualTo(EmbeddedPrivacyErasureAuthorityOptions.DefaultPath);
        await Assert.That(options.WriterReplicaCount).IsEqualTo(1);
        await Assert.That(options.BusyTimeoutSeconds).IsEqualTo(EmbeddedPrivacyErasureAuthorityOptions.DefaultBusyTimeoutSeconds);
    }

    [Test]
    public async Task ConnectionStringUsesPrivatePersistedFileComposition()
    {
        string path = Path.Combine(Path.GetTempPath(), "privacy-erasure-authority.db");
        EmbeddedPrivacyErasureAuthorityOptions options =
            EmbeddedPrivacyErasureAuthorityOptions.Bind(BuildConfiguration(new()
            {
                ["PrivacyErasureAuthorityEmbedded:Path"] = path,
                ["PrivacyErasureAuthorityEmbedded:BusyTimeoutSeconds"] = "17",
            }));

        var connection = new SqliteConnectionStringBuilder(options.BuildConnectionString());

        await Assert.That(connection.DataSource).IsEqualTo(Path.GetFullPath(path));
        await Assert.That(connection.Mode).IsEqualTo(SqliteOpenMode.ReadWriteCreate);
        await Assert.That(connection.Cache).IsEqualTo(SqliteCacheMode.Private);
        await Assert.That(connection.DefaultTimeout).IsEqualTo(17);
    }

    [Test]
    [Arguments("relative.db")]
    [Arguments("file:/tmp/authority.db")]
    [Arguments("https://example.test/authority.db")]
    [Arguments("\\\\server\\authority.db")]
    public async Task NonLocalOrNonAbsolutePathFailsClosed(string path)
    {
        Action act = () => EmbeddedPrivacyErasureAuthorityOptions.Bind(
            BuildConfiguration(new()
            {
                ["PrivacyErasureAuthorityEmbedded:Path"] = path,
            }));

        await Assert.That(act).Throws<OptionsValidationException>();
    }

    [Test]
    [Arguments("0")]
    [Arguments("2")]
    public async Task WriterReplicaCountOtherThanOneFailsClosed(string replicas)
    {
        Action act = () => EmbeddedPrivacyErasureAuthorityOptions.Bind(
            BuildConfiguration(new()
            {
                ["PrivacyErasureAuthorityEmbedded:WriterReplicaCount"] = replicas,
            }));

        await Assert.That(act).Throws<OptionsValidationException>()
            .WithMessageContaining("WriterReplicaCount must be exactly 1");
    }

    [Test]
    [Arguments("0")]
    [Arguments("301")]
    [Arguments("not-an-integer")]
    public async Task BusyTimeoutOutsideBoundsFailsClosed(string timeout)
    {
        Action act = () => EmbeddedPrivacyErasureAuthorityOptions.Bind(
            BuildConfiguration(new()
            {
                ["PrivacyErasureAuthorityEmbedded:BusyTimeoutSeconds"] = timeout,
            }));

        await Assert.That(act).Throws<OptionsValidationException>();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
