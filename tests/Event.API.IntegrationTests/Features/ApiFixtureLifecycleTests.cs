using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using NSubstitute;
using NSubstitute.Extensions;

namespace Event.Api.IntegrationTests.Features;

public sealed class ApiFixtureLifecycleTests
{
    [Test]
    public async Task OrdinaryApiFixtureHasActiveDefaultAndDeniesProvisioning()
    {
        await using var fixture = new ApiTestFixture();
        await fixture.InitializeAsync();
        await AssertLifecycleBoundaryAsync(fixture.Factory, fixture.Client);
    }

    [Test]
    public async Task ExternalApiFixtureHasActiveDefaultAndDeniesProvisioning()
    {
        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant
        };
        using var client = factory.CreateClient();
        await AssertLifecycleBoundaryAsync(factory, client);
    }

    [Test]
    public async Task PublishedAuthenticatedFixtureHasActiveDefaultAndDeniesProvisioning()
    {
        await using var factory = new AuthenticatedWebApplicationFactory { SeedActiveDefaultTenant = true };
        using var client = factory.CreateClient();
        await using var database = factory.CreateMigratorContext();
        await AssertLifecycleBoundaryAsync(database, client);
    }

    [Test]
    public async Task FreshAuthenticatedFixtureDoesNotManufacturePublishedTenant()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        await using var database = factory.CreateMigratorContext();
        await Assert.That(await database.Tenants.AnyAsync()).IsFalse();
        using var response = await client.GetAsync("/api/category");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    private static async Task AssertLifecycleBoundaryAsync(WebApplicationFactory<Program> factory, HttpClient client)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await AssertLifecycleBoundaryAsync(database, client);
    }

    private static async Task AssertLifecycleBoundaryAsync(ExploreDbContext database, HttpClient client)
    {
        var tenant = await database.Tenants.SingleAsync(tenant => tenant.Id == PlatformDefaults.DefaultTenantId);
        await Assert.That(tenant.TenantStatusId).IsEqualTo((int)TenantStatusEnum.Active);
        using var active = await client.GetAsync("/api/category");
        await Assert.That(active.StatusCode).IsEqualTo(HttpStatusCode.OK);

        tenant.TenantStatusId = (int)TenantStatusEnum.Provisioning;
        await database.SaveChangesAsync();
        using var provisioning = await client.GetAsync("/api/category");
        await Assert.That(provisioning.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(provisioning.Headers.CacheControl!.NoStore).IsTrue();
        using var body = JsonDocument.Parse(await provisioning.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("code").GetString()).IsEqualTo("tenant_lifecycle_unavailable");
    }
}

public sealed class SecurityPrerequisiteOwnershipTests
{
    [Test]
    public async Task OrdinaryContextDoesNotSelectAnInstrumentedDaemon()
    {
        await Assert.That(await Event.Testing.AssuranceOwnershipContext.ValidateAssuranceContextAsync(
            null, null, Path.GetTempPath(), null)).IsNull();
        await Assert.That(await Event.Testing.AssuranceOwnershipContext.ValidateAssuranceContextAsync(
            null, null, Path.GetTempPath(), "tcp://unselected.invalid:2375")).IsNull();
    }

    [Test]
    [Arguments("valid")]
    [Arguments("missing")]
    [Arguments("wrong-scheme")]
    public async Task OwnedContextRequiresItsExplicitUnixDaemon(string variant)
    {
        using var files = new OwnershipFiles();
        string host = "unix://" + Path.Combine(files.TemporaryRoot, "daemon.sock");
        await Assert.That(await Event.Testing.AssuranceOwnershipContext.ValidateAssuranceOwnershipAsync(
            files.RunId, files.Root, files.TemporaryRoot)).IsEqualTo(files.RunId);
        Event.Testing.AssuranceOwnershipContext? baseline = null;
        try
        {
            baseline = await Event.Testing.AssuranceOwnershipContext.ValidateAssuranceContextAsync(
                files.RunId, files.Root, files.TemporaryRoot, host);
        }
        catch (InvalidOperationException exception) when (exception.Message == "test_resource_ownership_invalid") { }
        await Assert.That(baseline).IsNotNull();
        await Assert.That(baseline!.Owner).IsEqualTo(files.RunId);
        await Assert.That(baseline.DockerEndpoint.OriginalString).IsEqualTo(host);
        await Assert.That(baseline.Root).IsEqualTo(files.Root);

        string? selected = variant switch { "missing" => null, "wrong-scheme" => "tcp://localhost:2375", _ => host };
        Event.Testing.AssuranceOwnershipContext? context = null;
        bool rejected = false;
        try
        {
            context = await Event.Testing.AssuranceOwnershipContext.ValidateAssuranceContextAsync(
                files.RunId, files.Root, files.TemporaryRoot, selected);
        }
        catch (InvalidOperationException exception) when (exception.Message == "test_resource_ownership_invalid")
        {
            rejected = true;
        }
        await Assert.That(rejected).IsEqualTo(variant != "valid");
        if (variant == "valid")
        {
            await Assert.That(context!.Owner).IsEqualTo(files.RunId);
            await Assert.That(context.DockerEndpoint.OriginalString).IsEqualTo(host);
        }
        else await Assert.That(context is null).IsTrue();
    }

    [Test]
    public async Task AbsentOwnershipContextPreservesOrdinaryFixtureAdmission()
    {
        string? owner = await Event.Testing.AssuranceOwnershipContext.ValidateAssuranceOwnershipAsync(null, null, Path.GetTempPath());
        await Assert.That(owner).IsNull();
    }

    [Test]
    public async Task ExistingPrivateRootAndExactMarkerAdmitTheirOwner()
    {
        using var files = new OwnershipFiles();
        string? owner = await Event.Testing.AssuranceOwnershipContext.ValidateAssuranceOwnershipAsync(files.RunId, files.Root, files.TemporaryRoot);
        await Assert.That(owner).IsEqualTo(files.RunId);
    }

    [Test]
    [Arguments("missing-run")]
    [Arguments("missing-root")]
    [Arguments("empty-pair")]
    [Arguments("invalid-run")]
    [Arguments("relative-root")]
    [Arguments("foreign-root")]
    [Arguments("missing-root-directory")]
    [Arguments("missing-marker")]
    [Arguments("wrong-marker")]
    [Arguments("oversized-marker")]
    [Arguments("readable-root")]
    [Arguments("readable-marker")]
    [Arguments("linked-root")]
    [Arguments("linked-ancestor")]
    [Arguments("linked-marker")]
    [Arguments("directory-marker")]
    [Arguments("hardlinked-marker")]
    [Arguments("fifo-marker")]
    public async Task UntrustedOwnershipContextIsRejectedBeforeContainerAdmission(string variant)
    {
        using var files = new OwnershipFiles();
        string? run = files.RunId;
        string? root = files.Root;
        switch (variant)
        {
            case "missing-run": run = null; break;
            case "missing-root": root = null; break;
            case "empty-pair": run = root = string.Empty; break;
            case "invalid-run": run = new string('z', 32); break;
            case "relative-root": root = Path.GetRelativePath(files.TemporaryRoot, files.Root); break;
            case "foreign-root": root = files.TemporaryRoot; break;
            case "missing-root-directory": Directory.Delete(files.Root, true); break;
            case "missing-marker": File.Delete(files.Marker); break;
            case "wrong-marker": File.WriteAllText(files.Marker, new string('b', 32)); break;
            case "oversized-marker": File.WriteAllText(files.Marker, new string('a', 4096)); break;
            case "readable-root":
                if (OperatingSystem.IsLinux()) File.SetUnixFileMode(files.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead);
                break;
            case "readable-marker":
                if (OperatingSystem.IsLinux()) File.SetUnixFileMode(files.Marker, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
                break;
            case "linked-root":
                string moved = Path.Combine(files.TemporaryRoot, "retained-root");
                Directory.Move(files.Root, moved);
                Directory.CreateSymbolicLink(files.Root, moved);
                break;
            case "linked-ancestor":
                string ancestor = Path.Combine(files.TemporaryRoot, "islamu-assurance");
                string retained = Path.Combine(files.TemporaryRoot, "retained-parent");
                Directory.Move(ancestor, retained);
                Directory.CreateSymbolicLink(ancestor, retained);
                break;
            case "linked-marker":
                string original = Path.Combine(files.TemporaryRoot, "retained-marker");
                File.Move(files.Marker, original);
                File.CreateSymbolicLink(files.Marker, original);
                break;
            case "directory-marker": File.Delete(files.Marker); Directory.CreateDirectory(files.Marker); break;
            case "hardlinked-marker": await RunFileToolAsync("/usr/bin/ln", files.Marker, Path.Combine(files.TemporaryRoot, "marker-link")); break;
            case "fifo-marker": File.Delete(files.Marker); await RunFileToolAsync("/usr/bin/mkfifo", files.Marker); break;
        }
        Exception? failure = null;
        try { _ = await Event.Testing.AssuranceOwnershipContext.ValidateAssuranceOwnershipAsync(run, root, files.TemporaryRoot); }
        catch (InvalidOperationException exception) { failure = exception; }
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task BothRealFileResourcesCloseAndEveryDisposalFailureSurvives(bool primaryFails, bool cleanupFails)
    {
        using var files = new OwnershipFiles();
        string firstPath = Path.Combine(files.Root, "primary-owned-file");
        string secondPath = Path.Combine(files.Root, "cleanup-owned-file");
        Exception? primary = primaryFails ? new IOException("primary_disposal_failure") : null;
        Exception? cleanup = cleanupFails ? new IOException("cleanup_disposal_failure") : null;
        using var firstResource = new FailureAfterFileDisposal(firstPath, primary);
        using var secondResource = new FailureAfterFileDisposal(secondPath, cleanup);
        await Assert.That(File.Exists(firstPath)).IsTrue();
        await Assert.That(File.Exists(secondPath)).IsTrue();
        Exception? observed = null;
        try
        {
            await SecurityInfrastructureFixture.DisposePrerequisitesAsync(firstResource, secondResource);
        }
        catch (Exception exception) { observed = exception; }

        await Assert.That(File.Exists(firstPath)).IsFalse();
        await Assert.That(File.Exists(secondPath)).IsFalse();
        if (primaryFails && cleanupFails)
        {
            await Assert.That(observed is AggregateException).IsTrue();
            var failures = ((AggregateException)observed!).Flatten().InnerExceptions;
            await Assert.That(failures.Count).IsEqualTo(2);
            await Assert.That(failures.Contains(primary!)).IsTrue();
            await Assert.That(failures.Contains(cleanup!)).IsTrue();
        }
        else if (primaryFails || cleanupFails) await Assert.That(ReferenceEquals(observed, primary ?? cleanup)).IsTrue();
        else await Assert.That(observed).IsNull();
    }

    [Test]
    public async Task CreationRecordsKeepDistinctFinalImagesAndIdsPrivateUntilIndependentDisposal()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("test_creation_observation_requires_linux");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var files = new OwnershipFiles();
        var context = await Event.Testing.AssuranceOwnershipContext.ValidateAssuranceContextAsync(
            files.RunId, files.Root, files.TemporaryRoot, "unix://" + Path.Combine(files.TemporaryRoot, "daemon.sock"))
            ?? throw new InvalidOperationException("test_creation_context_missing");
        string firstImage = "registry.example.invalid/first:control";
        string secondImage = "registry.example.invalid/final:control";
        string firstId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        string secondId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var firstImageValue = Substitute.For<IImage>();
        firstImageValue.FullName.Returns(firstImage);
        var secondImageValue = Substitute.For<IImage>();
        secondImageValue.FullName.Returns(secondImage);
        await using var firstContainer = Substitute.For<IContainer>();
        firstContainer.Image.Returns(firstImageValue);
        firstContainer.Id.Returns(firstId);
        await using var secondContainer = Substitute.For<IContainer>();
        secondContainer.Image.Returns(secondImageValue);
        secondContainer.Id.Returns(secondId);
        await using var ordinary = await SecurityInfrastructureFixture.BeginCreationObservationAsync(
            null, "keycloak", firstContainer, deadline.Token);
        await Assert.That(ordinary).IsNull();
        await using var first = await SecurityInfrastructureFixture.BeginCreationObservationAsync(
            context, "keycloak", firstContainer, deadline.Token)
            ?? throw new InvalidOperationException("test_creation_observation_missing");
        await using var second = await SecurityInfrastructureFixture.BeginCreationObservationAsync(
            context, "keycloak", secondContainer, deadline.Token)
            ?? throw new InvalidOperationException("test_creation_observation_missing");
        await Assert.That(first.CreationId != second.CreationId && first.CreationId.Length == 32
            && second.CreationId.Length == 32 && first.CreationId[12] == '7' && second.CreationId[12] == '7').IsTrue();
        await Assert.That(await first.RecordCreatedAsync(null, deadline.Token)).IsFalse();
        firstContainer.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        await SecurityInfrastructureFixture.StartObservedContainerAsync(firstContainer, first, deadline.Token);
        var partialStartup = new InvalidOperationException("test_creation_partial_start");
        secondContainer.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(partialStartup));
        var partialFailure = await Assert.That(async () => await SecurityInfrastructureFixture.StartObservedContainerAsync(
            secondContainer, second, deadline.Token)).Throws<InvalidOperationException>();
        await Assert.That(ReferenceEquals(partialFailure, partialStartup)).IsTrue();
        foreach (var expected in new[] { (first, firstImage, firstId), (second, secondImage, secondId) })
        {
            string prefix = Path.Combine(files.Root, $"api-container-keycloak-{expected.Item1.CreationId}");
            byte[] declaration = await File.ReadAllBytesAsync(prefix + "-declaration.json", deadline.Token);
            using var declared = JsonDocument.Parse(declaration);
            using var created = JsonDocument.Parse(await File.ReadAllBytesAsync(prefix + "-created-" + expected.Item3 + ".json", deadline.Token));
            var selected = declared.RootElement;
            var observed = created.RootElement;
            string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(declaration)).ToLowerInvariant();
            await Assert.That(selected.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { "CreationId", "CreatorPid", "ImageReference", "Resource", "RunId", "Schema" })
                && observed.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { "ContainerId", "CreationId", "CreatorPid", "DeclarationSha256", "ImageReference", "Resource", "RunId", "Schema" })
                && selected.GetProperty("Schema").GetInt32() == 1 && observed.GetProperty("Schema").GetInt32() == 1
                && selected.GetProperty("Resource").GetString() == "keycloak"
                && observed.GetProperty("Resource").GetString() == "keycloak"
                && selected.GetProperty("ImageReference").GetString() == expected.Item2
                && selected.GetProperty("CreationId").GetString() == expected.Item1.CreationId
                && selected.GetProperty("RunId").GetString() == files.RunId
                && selected.GetProperty("CreatorPid").GetInt32() == Environment.ProcessId
                && observed.GetProperty("ContainerId").GetString() == expected.Item3
                && observed.GetProperty("ImageReference").GetString() == expected.Item2
                && observed.GetProperty("CreationId").GetString() == expected.Item1.CreationId
                && observed.GetProperty("DeclarationSha256").GetString() == digest).IsTrue();
        }
        var primary = new InvalidOperationException("test_creation_start_primary");
        firstContainer.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(primary));
        var failure = await Assert.That(async () => await SecurityInfrastructureFixture.StartObservedContainerAsync(
            firstContainer, first, deadline.Token)).Throws<AggregateException>();
        await Assert.That(failure!.InnerExceptions.Any(error => ReferenceEquals(error, primary))
            && failure.InnerExceptions.Any(error => error.Message == "test_creation_identity_duplicate")).IsTrue();
        string[] records = Directory.GetFiles(files.Root, "api-container-*.json");
        bool privateModes = true;
        foreach (string path in records)
            privateModes &= File.GetUnixFileMode(path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await Assert.That(records.Length == 4 && privateModes && HeldRecordCount() == 4).IsTrue();
        foreach (var owned in new (IContainer Container, IAsyncDisposable Fixture, int HeldBefore)[]
        {
            (firstContainer, new KeycloakContainerFixture(firstContainer, first), 4),
            (secondContainer, new CerbosContainerFixture(secondContainer, second), 2)
        })
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool heldDuringContainerDisposal = false;
            bool containerDisposalFails = true;
            var cleanup = new IOException("test_creation_container_cleanup");
            owned.Container.ReturnsForAll<ValueTask>(_ => containerDisposalFails
                ? new ValueTask(FailAfterReleaseAsync()) : ValueTask.CompletedTask);
            Task? firstDisposal = null;
            Task? secondDisposal = null;
            Exception?[] outcomes = [];
            try
            {
                firstDisposal = owned.Fixture.DisposeAsync().AsTask();
                await entered.Task.WaitAsync(deadline.Token);
                secondDisposal = owned.Fixture.DisposeAsync().AsTask();
                await Assert.That(!firstDisposal.IsCompleted && !secondDisposal.IsCompleted
                    && heldDuringContainerDisposal && HeldRecordCount() == owned.HeldBefore).IsTrue();
            }
            finally
            {
                containerDisposalFails = false;
                release.TrySetResult();
                outcomes = await Task.WhenAll(CaptureFailureAsync(firstDisposal), CaptureFailureAsync(secondDisposal));
            }
            await Assert.That(outcomes.All(error => ReferenceEquals(error, cleanup))
                && HeldRecordCount() == owned.HeldBefore - 2).IsTrue();

            async Task FailAfterReleaseAsync()
            {
                heldDuringContainerDisposal = HeldRecordCount() == owned.HeldBefore;
                entered.TrySetResult();
                await release.Task.WaitAsync(deadline.Token);
                throw cleanup;
            }

            static async Task<Exception?> CaptureFailureAsync(Task? disposal)
            {
                if (disposal is null) return null;
                try { await disposal; return null; }
                catch (Exception exception) { return exception; }
            }
        }
        await Assert.That(HeldRecordCount() == 0 && records.All(File.Exists)).IsTrue();

        int HeldRecordCount()
        {
            string[] descriptors = Directory.EnumerateFileSystemEntries("/proc/self/fd").Take(4097).ToArray();
            if (descriptors.Length > 4096) throw new InvalidOperationException("test_creation_descriptor_limit");
            int count = 0;
            foreach (string descriptor in descriptors)
            {
                try
                {
                    string? target = File.ResolveLinkTarget(descriptor, returnFinalTarget: true)?.FullName;
                    if (target is not null && records.Contains(target, StringComparer.Ordinal)) count++;
                }
                catch (IOException) { /* Unrelated descriptors may close during the bounded self-only scan. */ }
            }
            return count;
        }
    }

    private sealed class FailureAfterFileDisposal(string path, Exception? failure) : IAsyncDisposable, IDisposable
    {
        private readonly FileStream file = new(path, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 1, FileOptions.DeleteOnClose);

        public void Dispose() => file.Dispose();

        public async ValueTask DisposeAsync()
        {
            await file.DisposeAsync();
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static async Task RunFileToolAsync(string executable, params string[] arguments)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false }
        };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await process.WaitForExitAsync(deadline.Token); }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        }
        if (process.ExitCode != 0) throw new InvalidOperationException("ownership_test_file_setup_failed");
    }

    private sealed class OwnershipFiles : IDisposable
    {
        internal string RunId { get; } = Guid.NewGuid().ToString("N");
        internal string TemporaryRoot { get; }
        internal string Root { get; }
        internal string Marker => Path.Combine(Root, ".islamu-test-owner");

        internal OwnershipFiles()
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            TemporaryRoot = Directory.CreateTempSubdirectory("security-prerequisite-").FullName;
            File.SetUnixFileMode(TemporaryRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Root = Path.Combine(TemporaryRoot, "islamu-assurance", RunId);
            Directory.CreateDirectory(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var marker = new FileStream(Marker, new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            });
            marker.Write(System.Text.Encoding.UTF8.GetBytes(RunId));
        }

        public void Dispose() => Directory.Delete(TemporaryRoot, true);
    }
}
