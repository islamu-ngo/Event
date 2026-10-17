namespace Explore.Infrastructure.Tests.Infrastructure.ConfigurationManifest;

using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Persistence;
using Microsoft.Extensions.DependencyInjection;
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
        await using var provider = CreateServices(runner);
        await using var scope = provider.CreateAsyncScope();
        var sequence = scope.ServiceProvider.GetRequiredService<IConfigurationManifestPostMigrationSequence>();

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
        var events = new List<string>();
        var failure = new InvalidOperationException("migration failed");
        var runner = Substitute.For<IConfigurationManifestStartupRunner>();
        runner.RunAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            events.Add("manifest");
            return Task.CompletedTask;
        });
        await using var provider = CreateServices(runner);
        await using var scope = provider.CreateAsyncScope();
        var sequence = scope.ServiceProvider.GetRequiredService<IConfigurationManifestPostMigrationSequence>();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sequence.RunAsync(
                _ =>
                {
                    events.Add("migration");
                    throw failure;
                },
                CancellationToken.None));

        await Assert.That(thrown).IsSameReferenceAs(failure);
        await Assert.That(events).IsEquivalentTo(
            ["migration"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
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
        await using var provider = CreateServices(runner);
        await using var scope = provider.CreateAsyncScope();
        var sequence = scope.ServiceProvider.GetRequiredService<IConfigurationManifestPostMigrationSequence>();

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

    private static ServiceProvider CreateServices(IConfigurationManifestStartupRunner runner)
    {
        var services = new ServiceCollection();
        services.AddConfigurationManifestPersistence();
        services.AddSingleton(runner);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
