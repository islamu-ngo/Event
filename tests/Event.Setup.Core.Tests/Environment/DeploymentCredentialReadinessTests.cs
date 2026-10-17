using System.Security.Cryptography;
using ISLAMU.Event.Setup.Core.Environment;

namespace ISLAMU.Setup.Core.EnvironmentTests;

public sealed class DeploymentCredentialReadinessTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("explore")]
    public async Task MissingEmptyAndRetiredDefaultPasswordsBlockReadiness(string? password)
    {
        var entries = Credentials();
        entries.RemoveAll(entry => entry.Key == "DATABASE_RUNTIME_PASSWORD");
        if (password is not null) entries.Add(Entry("DATABASE_RUNTIME_PASSWORD", password, true));
        var result = Evaluate(entries);
        await Assert.That(result.State).IsEqualTo(DotenvReadinessState.Blocked);
        await Assert.That(result.Blocked).Contains("DATABASE_RUNTIME_PASSWORD");
    }

    [Test]
    public async Task IndependentInjectedCredentialsAreReadyWithoutValueDiagnostics()
    {
        var entries = Credentials();
        var result = Evaluate(entries);
        await Assert.That(result.State).IsEqualTo(DotenvReadinessState.Ready);
        await Assert.That(result.Diagnostics).IsEmpty();
        foreach (var entry in entries.Where(entry => entry.IsSecret))
            await Assert.That(result.ToString()).DoesNotContain(entry.Value!);
    }

    [Test]
    public async Task RuntimeCannotReuseMigratorLoginOrPassword()
    {
        foreach (var field in new[] { "USERNAME", "PASSWORD" })
        {
            var entries = Credentials();
            var migrator = entries.Single(entry => entry.Key == $"DATABASE_MIGRATOR_{field}");
            entries.RemoveAll(entry => entry.Key == $"DATABASE_RUNTIME_{field}");
            entries.Add(Entry($"DATABASE_RUNTIME_{field}", migrator.Value!, field == "PASSWORD"));
            var result = Evaluate(entries);
            await Assert.That(result.State).IsEqualTo(DotenvReadinessState.Blocked);
            await Assert.That(result.Diagnostics.Select(item => item.Code)).Contains("database-role-credentials-not-distinct");
        }
    }

    private static DotenvReadinessResult Evaluate(List<DotenvEntry> entries) =>
        DotenvReadiness.Evaluate(PlatformEnvironmentCatalogue.Catalogue
            .Relevant(new EnvironmentActivationContext("split", ["database"], ["postgresql"]))
            .Where(definition => definition.Key is "DATABASE_RUNTIME_USERNAME" or "DATABASE_RUNTIME_PASSWORD"
                or "DATABASE_MIGRATOR_USERNAME" or "DATABASE_MIGRATOR_PASSWORD"
                or "DATABASE_GLOBAL_USERNAME" or "DATABASE_GLOBAL_PASSWORD"),
            new DotenvDocument(entries));

    private static List<DotenvEntry> Credentials() =>
    [
        Entry("DATABASE_RUNTIME_USERNAME", "event_runtime", false),
        Entry("DATABASE_RUNTIME_PASSWORD", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), true),
        Entry("DATABASE_MIGRATOR_USERNAME", "event_migrator", false),
        Entry("DATABASE_MIGRATOR_PASSWORD", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), true),
        Entry("DATABASE_GLOBAL_USERNAME", "event_global", false),
        Entry("DATABASE_GLOBAL_PASSWORD", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), true)
    ];

    private static DotenvEntry Entry(string key, string value, bool secret) =>
        new(key, value, DotenvEntryKind.LocalHumanValue, secret, DotenvProvenance.UserInput);
}
