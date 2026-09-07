namespace Explore.Infrastructure.Tests.Infrastructure.ConfigurationManifest;

using Explore.Infrastructure.ConfigurationManifest;
using NSubstitute;

public sealed class ConfigurationManifestPostMigrationSequenceTests
{
    [Test]
    public async Task RunAsync_CompletesMigrationBeforeManifestStartup()
    {
        var events = new List<string>();
        var runner = Substitute.For<IConfigurationManifestStartupRunner>();
        runner.RunAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                events.Add("manifest");
                return Task.CompletedTask;
            });
        var sequence = new ConfigurationManifestPostMigrationSequence(runner);

        await sequence.RunAsync(
            _ =>
            {
                events.Add("migration");
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await Assert.That(events).IsEquivalentTo(["migration", "manifest"]);
        await Assert.That(events[0]).IsEqualTo("migration");
        await Assert.That(events[1]).IsEqualTo("manifest");
    }

    [Test]
    public async Task RunAsync_MigrationFailure_DoesNotStartManifest()
    {
        var runner = Substitute.For<IConfigurationManifestStartupRunner>();
        var sequence = new ConfigurationManifestPostMigrationSequence(runner);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sequence.RunAsync(
                _ => throw new InvalidOperationException("migration failed"),
                CancellationToken.None));

        await runner.DidNotReceiveWithAnyArgs().RunAsync(default);
    }

    [Test]
    public async Task RunAsync_PassesCallerCancellationToBothStages()
    {
        using var cancellation = new CancellationTokenSource();
        var seen = new List<CancellationToken>();
        var runner = Substitute.For<IConfigurationManifestStartupRunner>();
        runner.RunAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                seen.Add(call.Arg<CancellationToken>());
                return Task.CompletedTask;
            });
        var sequence = new ConfigurationManifestPostMigrationSequence(runner);

        await sequence.RunAsync(
            token =>
            {
                seen.Add(token);
                return Task.CompletedTask;
            },
            cancellation.Token);

        await Assert.That(seen).Count().IsEqualTo(2);
        await Assert.That(seen.All(token => token == cancellation.Token)).IsTrue();
    }
}
