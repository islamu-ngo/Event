using Explore.Persistence.Identity;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class IdentityDatabaseUsernameDeclarationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOrExplicitEmptyWriterCannotBorrowPrimaryOrOtherIdentityPurposes(bool explicitEmpty)
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:LocalIdentityWriter:Username"] = "application_writer_unselected",
            ["IdentityDatabase:Username"] = "shared_identity_unselected",
            ["IdentityDatabase:Runtime:Username"] = "runtime_unselected",
            ["IdentityDatabase:Migrator:Username"] = "migrator_unselected"
        };
        if (explicitEmpty)
        {
            values["IdentityDatabase:LocalIdentityWriter:Username"] = string.Empty;
            values["IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_USERNAME"] = "masked_writer_alias";
        }
        var configuration = Configuration(values);

        await Assert.That(IdentityDatabaseConfiguration.TryBindUsername(configuration, PrimaryDatabaseRole.LocalIdentityWriter, out string? username)).IsFalse();
        await Assert.That(username).IsNull();
        await Assert.That(() => IdentityDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.LocalIdentityWriter))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime, "RUNTIME")]
    [Arguments(PrimaryDatabaseRole.Migrator, "MIGRATOR")]
    [Arguments(PrimaryDatabaseRole.LocalIdentityWriter, "LOCAL_IDENTITY_WRITER")]
    public async Task SupportedPurposeUsesCanonicalPrecedenceAndExactSpellingWithoutConnectionConfiguration(
        PrimaryDatabaseRole role, string alias)
    {
        const string selected = "Mixed \"Identity\" $keep\\é";
        string key = $"IdentityDatabase:{role}:Username";
        var values = new Dictionary<string, string?>
        {
            [key] = selected,
            [$"IDENTITY_DATABASE_{alias}_USERNAME"] = "Alias_Identity",
            ["IdentityDatabase:Username"] = "unselected_shared"
        };
        var configuration = Configuration(values);
        await Assert.That(IdentityDatabaseConfiguration.TryBindUsername(configuration, role, out string? structured)).IsTrue();
        await Assert.That(structured).IsEqualTo(selected);
        await Assert.That(IdentityDatabaseConfiguration.BindUsername(configuration, role)).IsEqualTo(selected);

        values.Remove(key);
        configuration = Configuration(values);
        await Assert.That(IdentityDatabaseConfiguration.TryBindUsername(configuration, role, out string? environment)).IsTrue();
        await Assert.That(environment).IsEqualTo("Alias_Identity");
        await Assert.That(IdentityDatabaseConfiguration.BindUsername(configuration, role)).IsEqualTo(environment);
    }

    [Test]
    [Arguments(" ")]
    [Arguments(" surrounded ")]
    [Arguments("line\nbreak")]
    public async Task MalformedNonemptyIdentityDeclarationCannotDisableTheProfileOrUseAnAlias(string malformed)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["IdentityDatabase:LocalIdentityWriter:Username"] = malformed,
            ["IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_USERNAME"] = "otherwise_valid_alias"
        });

        await Assert.That(() => IdentityDatabaseConfiguration.TryBindUsername(configuration, PrimaryDatabaseRole.LocalIdentityWriter, out _))
            .Throws<InvalidOperationException>();
        await Assert.That(() => IdentityDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.LocalIdentityWriter))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Global)]
    [Arguments(PrimaryDatabaseRole.InstanceKeyRead)]
    [Arguments((PrimaryDatabaseRole)999)]
    public async Task UnsupportedIdentityPurposeCannotBorrowAnyDeclaredUsername(PrimaryDatabaseRole role)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            [$"IdentityDatabase:{role}:Username"] = "unsupported_declared_identity",
            ["IdentityDatabase:Runtime:Username"] = "runtime_unselected",
            ["IdentityDatabase:Migrator:Username"] = "migrator_unselected"
        });

        await Assert.That(() => IdentityDatabaseConfiguration.TryBindUsername(configuration, role, out _))
            .Throws<InvalidOperationException>();
        await Assert.That(() => IdentityDatabaseConfiguration.BindUsername(configuration, role))
            .Throws<InvalidOperationException>();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().Add(new UsernameOnlySource(values)).Build();

    private sealed class UsernameOnlySource(Dictionary<string, string?> values) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => new UsernameOnlyProvider(values);
    }

    private sealed class UsernameOnlyProvider : ConfigurationProvider
    {
        internal UsernameOnlyProvider(Dictionary<string, string?> values) => Data = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);

        public override bool TryGet(string key, out string? value)
        {
            if (key.EndsWith("PASSWORD", StringComparison.OrdinalIgnoreCase)
                || key.Contains("CONNECTIONSTRING", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Username metadata cannot request password or connection-string authority.");
            return base.TryGet(key, out value);
        }
    }
}
