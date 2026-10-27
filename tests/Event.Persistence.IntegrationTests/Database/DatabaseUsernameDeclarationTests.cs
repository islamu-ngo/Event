using Explore.Persistence.Database;
using Microsoft.Extensions.Configuration;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class DatabaseUsernameDeclarationTests
{
    private const PrimaryDatabaseRole Writer = PrimaryDatabaseRole.LocalIdentityWriter;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AbsentOrExplicitEmptyWriterCannotBorrowAnotherPurpose(bool explicitEmpty)
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:Username"] = "shared_unselected",
            ["DATABASE_USERNAME"] = "shared_alias_unselected",
            ["Database:Runtime:Username"] = "runtime_unselected",
            ["Database:Migrator:Username"] = "migrator_unselected",
            ["Database:Global:Username"] = "global_unselected"
        };
        if (explicitEmpty)
        {
            values["Database:LocalIdentityWriter:Username"] = string.Empty;
            values["DATABASE_LOCAL_IDENTITY_WRITER_USERNAME"] = "masked_writer_alias";
        }

        bool present = PrimaryDatabaseConfiguration.TryBindUsername(Configuration(values), Writer, out string? username);

        await Assert.That(present).IsFalse();
        await Assert.That(username).IsNull();
    }

    [Test]
    public async Task StructuredDeclaredLoginWinsExactlyWithoutPasswordOrConnectionBinding()
    {
        const string selected = "Mixed \"Owner\" $keep\\é";
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Database:LocalIdentityWriter:Username"] = selected,
            ["DATABASE_LOCAL_IDENTITY_WRITER_USERNAME"] = "unselected_alias",
            ["Database:Runtime:Username"] = "unselected_runtime"
        });

        await Assert.That(PrimaryDatabaseConfiguration.TryBindUsername(configuration, Writer, out string? username)).IsTrue();
        await Assert.That(username).IsEqualTo(selected);
        await Assert.That(PrimaryDatabaseConfiguration.BindUsername(configuration, Writer)).IsEqualTo(selected);
    }

    [Test]
    public async Task ExplicitEnvironmentAliasSelectsOnlyItsWriterPurpose()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["DATABASE_LOCAL_IDENTITY_WRITER_USERNAME"] = "Declared_Writer",
            ["DATABASE_RUNTIME_USERNAME"] = "unselected_runtime",
            ["Database:Username"] = "unselected_shared"
        });

        await Assert.That(PrimaryDatabaseConfiguration.TryBindUsername(configuration, Writer, out string? username)).IsTrue();
        await Assert.That(username).IsEqualTo("Declared_Writer");
    }

    [Test]
    [Arguments(" ")]
    [Arguments(" surrounded ")]
    [Arguments("line\nbreak")]
    public async Task MalformedNonemptyDeclarationCannotDisappearOrFallBack(string malformed)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Database:LocalIdentityWriter:Username"] = malformed,
            ["DATABASE_LOCAL_IDENTITY_WRITER_USERNAME"] = "otherwise_valid_alias"
        });

        await Assert.That(() => PrimaryDatabaseConfiguration.TryBindUsername(configuration, Writer, out _))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UndefinedPurposeCannotAcquireAUsernameDeclaration()
    {
        var configuration = Configuration(new Dictionary<string, string?> { ["Database:Username"] = "unselected_shared" });

        await Assert.That(() => PrimaryDatabaseConfiguration.TryBindUsername(configuration, (PrimaryDatabaseRole)999, out _))
            .Throws<ArgumentOutOfRangeException>();
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
