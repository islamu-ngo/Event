using Explore.Blazor.Client.Clients;
using Explore.Blazor.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Explore.Blazor.IntegrationTests.HealthChecks;

public sealed class ExploreApiReadinessProbeTests
{
    [Test]
    public async Task EnsureReadyAsync_WhenApiIsImmediatelyAvailable_SucceedsWithoutRetries()
    {
        var apiClient = Substitute.For<IInstanceMessagingSettingsClient>();
        apiClient.GetInstanceResolverConfigurationAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ResolverConfigurationDto { PathEnabled = false }));

        var probe = new ExploreApiReadinessProbe(
            apiClient,
            MsOptions.Create(new ExploreApiReadinessOptions
            {
                StartupTimeout = TimeSpan.FromSeconds(5),
                RetryDelay = TimeSpan.Zero,
                AttemptTimeout = TimeSpan.FromSeconds(2)
            }),
            NullLogger<ExploreApiReadinessProbe>.Instance);

        await probe.EnsureReadyAsync();

        await apiClient.Received(1).GetInstanceResolverConfigurationAsync(
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EnsureReadyAsync_WhenApiBecomesAvailableAfterTransientErrors_RetriesAndSucceeds()
    {
        var apiClient = Substitute.For<IInstanceMessagingSettingsClient>();
        var callCount = 0;
        apiClient.GetInstanceResolverConfigurationAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return callCount switch
                {
                    1 => Task.FromException<ResolverConfigurationDto>(new HttpRequestException("Connection refused")),
                    2 => Task.FromException<ResolverConfigurationDto>(new TaskCanceledException("ConnectTimeout expired")),
                    _ => Task.FromResult(new ResolverConfigurationDto { PathEnabled = false })
                };
            });

        var probe = new ExploreApiReadinessProbe(
            apiClient,
            MsOptions.Create(new ExploreApiReadinessOptions
            {
                StartupTimeout = TimeSpan.FromSeconds(5),
                RetryDelay = TimeSpan.Zero,
                AttemptTimeout = TimeSpan.FromSeconds(2)
            }),
            NullLogger<ExploreApiReadinessProbe>.Instance);

        await probe.EnsureReadyAsync();

        await Assert.That(callCount).IsEqualTo(3);
    }

    [Test]
    public async Task EnsureReadyAsync_WhenTransientErrorsExceedTimeout_ThrowsLastException()
    {
        var apiClient = Substitute.For<IInstanceMessagingSettingsClient>();
        apiClient.GetInstanceResolverConfigurationAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ResolverConfigurationDto>(
                new HttpRequestException("Kestrel starting up")));

        var probe = new ExploreApiReadinessProbe(
            apiClient,
            MsOptions.Create(new ExploreApiReadinessOptions
            {
                StartupTimeout = TimeSpan.FromMilliseconds(50),
                RetryDelay = TimeSpan.FromMilliseconds(1),
                AttemptTimeout = TimeSpan.FromSeconds(2)
            }),
            NullLogger<ExploreApiReadinessProbe>.Instance);

        var act = () => probe.EnsureReadyAsync();

        await Assert.That(act).Throws<HttpRequestException>();
    }

    [Test]
    public async Task EnsureReadyAsync_WhenCallerTokenIsCancelled_AbortsPromptly()
    {
        var apiClient = Substitute.For<IInstanceMessagingSettingsClient>();
        apiClient.GetInstanceResolverConfigurationAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ResolverConfigurationDto>(
                new HttpRequestException("Not ready")));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var probe = new ExploreApiReadinessProbe(
            apiClient,
            MsOptions.Create(new ExploreApiReadinessOptions
            {
                StartupTimeout = TimeSpan.FromSeconds(30),
                RetryDelay = TimeSpan.FromMilliseconds(10),
                AttemptTimeout = TimeSpan.FromSeconds(2)
            }),
            NullLogger<ExploreApiReadinessProbe>.Instance);

        var act = () => probe.EnsureReadyAsync(cts.Token);

        await Assert.That(act).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task EnsureReadyAsync_WhenNonTransientExceptionOccurs_ThrowsImmediatelyWithoutRetrying()
    {
        var apiClient = Substitute.For<IInstanceMessagingSettingsClient>();
        var callCount = 0;
        apiClient.GetInstanceResolverConfigurationAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return Task.FromException<ResolverConfigurationDto>(
                    new InvalidOperationException("Fatal unexpected error"));
            });

        var probe = new ExploreApiReadinessProbe(
            apiClient,
            MsOptions.Create(new ExploreApiReadinessOptions
            {
                StartupTimeout = TimeSpan.FromSeconds(10),
                RetryDelay = TimeSpan.FromMilliseconds(1),
                AttemptTimeout = TimeSpan.FromSeconds(2)
            }),
            NullLogger<ExploreApiReadinessProbe>.Instance);

        var act = () => probe.EnsureReadyAsync();

        await Assert.That(act).Throws<InvalidOperationException>();
        await Assert.That(callCount).IsEqualTo(1);
    }
}
