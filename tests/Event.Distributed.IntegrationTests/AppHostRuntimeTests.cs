using Explore.AppHost;
using Aspire.Hosting.ApplicationModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Event.Distributed.IntegrationTests;

[Category("Distributed")]
public sealed partial class AppHostRuntimeTests
{
    [Test]
    public async Task CancelledDockerWaitReapsItsChildAndPreservesTheContainer()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var creation = await OwnedAppHostFixture.RunDockerAsync(["run", "--detach", "--entrypoint", "sleep",
            "postgres:18-alpine", "600"], deadline.Token);
        await Assert.That(creation.ExitCode).IsEqualTo(0);
        string container = creation.Output.Trim();
        try
        {
            using var cancellation = new CancellationTokenSource();
            Process? child = null;
            try
            {
                Task waiting = OwnedAppHostFixture.RunDockerAsync(["wait", container], cancellation.Token,
                    id => child = Process.GetProcessById(id));
                cancellation.Cancel();
                await Assert.That(async () => await waiting).Throws<OperationCanceledException>();
                await Assert.That(child!.WaitForExit(0)).IsTrue();
            }
            finally { child?.Dispose(); }
            var retained = await OwnedAppHostFixture.RunDockerAsync(["inspect", "--format", "{{.State.Running}}", container], deadline.Token);
            await Assert.That(retained.ExitCode).IsEqualTo(0);
            await Assert.That(retained.Output.Trim()).IsEqualTo("true");
        }
        finally
        {
            var removal = await OwnedAppHostFixture.RunDockerAsync(["rm", "--force", container], CancellationToken.None);
            await Assert.That(removal.ExitCode).IsEqualTo(0);
        }
    }

    [Test]
    public async Task BuilderFailureRetainsTheCauseAndRemovesItsOwnedRoot()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        string? root = null;
        var failure = await Assert.That(async () => await OwnedAppHostFixture.CreateAsync("Split",
            false, "SingleTenant", null, builder =>
            {
                root = builder.Configuration["ISLAMU_TEST_ROOT"];
                throw new InvalidOperationException("assurance_builder_fault");
            }, deadline.Token)).Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).IsEqualTo("assurance_builder_fault");
        await Assert.That(root).IsNotNull();
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    [Test]
    public async Task ForeignVolumeConsumerSurvivesOwnerCleanup()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var owner = await OwnedAppHostFixture.CreateAsync("Split", deadline.Token);
        string volume = $"islamu-test-{owner.RunId}-islamu-event-postgres-data";
        string? sentinel = null;
        try
        {
            var creation = await OwnedAppHostFixture.RunDockerAsync(["volume", "create", volume], deadline.Token);
            await Assert.That(creation.ExitCode).IsEqualTo(0);
            var container = await OwnedAppHostFixture.RunDockerAsync(["create", "--label",
                "islamu.assurance.owner=foreign", "--mount", $"type=volume,source={volume},target=/sentinel",
                "postgres:18-alpine"], deadline.Token);
            await Assert.That(container.ExitCode).IsEqualTo(0);
            sentinel = container.Output.Trim();

            var failure = await Assert.That(async () => await owner.DisposeAsync()).Throws<AggregateException>();
            await Assert.That(failure!.Flatten().InnerExceptions.Any(error =>
                error.Message == "test_resource_foreign_container")).IsTrue();
            var retained = await OwnedAppHostFixture.RunDockerAsync(["inspect", "--format",
                "{{index .Config.Labels \"islamu.assurance.owner\"}}", sentinel], deadline.Token);
            await Assert.That(retained.ExitCode).IsEqualTo(0);
            await Assert.That(retained.Output.Trim()).IsEqualTo("foreign");
            await Assert.That(Directory.Exists(owner.Root)).IsTrue();
        }
        finally
        {
            try { await using (owner) { } }
            catch (AggregateException) { }
            if (sentinel is not null)
            {
                var removal = await OwnedAppHostFixture.RunDockerAsync(["rm", sentinel], CancellationToken.None);
                await Assert.That(removal.ExitCode).IsEqualTo(0);
            }
            var volumeRemoval = await OwnedAppHostFixture.RunDockerAsync(["volume", "rm", volume], CancellationToken.None);
            await Assert.That(volumeRemoval.ExitCode).IsEqualTo(0);
            if (Directory.Exists(owner.Root))
            {
                new TestingResourceIsolation(owner.RunId, owner.Root).VerifyOwnership();
                Directory.Delete(owner.Root, recursive: true);
            }
        }
    }

    [Test]
    [Arguments("Split")]
    [Arguments("Standalone")]
    public async Task RealTopologyCompletesMigrationAndBecomesHealthy(string topology)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var owner = await OwnedAppHostFixture.CreateAsync(topology, deadline.Token);
        await owner.StartAsync(deadline.Token);
        using var client = owner.CreateHttpClient(topology == "Split" ? "explore-api" : "event-standalone", "operations");
        using var response = await client.GetAsync("/health", deadline.Token);
        await Assert.That(response.IsSuccessStatusCode).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancelledStartupDisposesOnlyItsOwnedRoot(bool cancelWhenPostgresStarting)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var owner = await OwnedAppHostFixture.CreateAsync("Split", deadline.Token);
        string root = owner.Root;
        try
        {
            using var cancellation = new CancellationTokenSource();
            Task postgresState = cancelWhenPostgresStarting
                ? owner.Application.ResourceNotifications.WaitForResourceAsync(
                    "postgres", KnownResourceStates.Starting, deadline.Token)
                : owner.Application.ResourceNotifications.WaitForResourceHealthyAsync("postgres", deadline.Token);
            Task startup = owner.StartAsync(cancellation.Token);
            await postgresState;
            cancellation.Cancel();
            await Assert.That(async () => await startup).Throws<OperationCanceledException>();
        }
        finally
        {
            await Task.WhenAll(owner.DisposeAsync().AsTask(), owner.DisposeAsync().AsTask());
        }
        await Assert.That(owner.ExecutionSettled).IsTrue();
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    [Test]
    public async Task ConcurrentOwnersRemainHealthyWhenTheirNeighbourStops()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await using var first = await OwnedAppHostFixture.CreateAsync("Split", deadline.Token, configuredAdministrator: true);
        await using var second = await OwnedAppHostFixture.CreateAsync("Standalone", deadline.Token, configuredAdministrator: true);
        await Task.WhenAll(first.StartAsync(deadline.Token), second.StartAsync(deadline.Token));
        await Assert.That(first.Root).IsNotEqualTo(second.Root);
        using var firstApi = first.CreateHttpClient("explore-api");
        using var secondApi = second.CreateHttpClient("event-standalone");
        Guid firstUserId = await first.AuthenticateBootstrapAsync(firstApi, deadline.Token);
        Guid secondUserId = await second.AuthenticateBootstrapAsync(secondApi, deadline.Token);
        var firstAuthorization = firstApi.DefaultRequestHeaders.Authorization;
        var secondAuthorization = secondApi.DefaultRequestHeaders.Authorization;
        firstApi.DefaultRequestHeaders.Authorization = secondAuthorization;
        using (var foreign = await firstApi.GetAsync("/api/user", deadline.Token))
            await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        firstApi.DefaultRequestHeaders.Authorization = firstAuthorization;
        secondApi.DefaultRequestHeaders.Authorization = firstAuthorization;
        using (var foreign = await secondApi.GetAsync("/api/user", deadline.Token))
            await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        secondApi.DefaultRequestHeaders.Authorization = secondAuthorization;
        using (var own = await firstApi.GetAsync("/api/user", deadline.Token))
        {
            own.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await own.Content.ReadAsStringAsync(deadline.Token));
            await Assert.That(body.RootElement.GetProperty("id").GetGuid()).IsEqualTo(firstUserId);
        }
        await first.DisposeAsync();
        using (var own = await secondApi.GetAsync("/api/user", deadline.Token))
        {
            own.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await own.Content.ReadAsStringAsync(deadline.Token));
            await Assert.That(body.RootElement.GetProperty("id").GetGuid()).IsEqualTo(secondUserId);
        }
        using var client = second.CreateHttpClient("event-standalone", "operations");
        using var response = await client.GetAsync("/health", deadline.Token);
        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(Directory.Exists(second.Root)).IsTrue();
    }

}
