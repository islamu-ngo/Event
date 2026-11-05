using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using static ISLAMU.ReleaseEngineering.SecurityExecutionEvidence;

namespace ISLAMU.ReleaseEngineering.Tests;

[Category("NativeManagedCleanup")]
public sealed class SecurityExecutionEvidenceManagedCleanupTests
{
    [Test]
    [Explicit]
    public Task ExclusiveOwnedResourcesAreRemovedAfterNativeOwnerIsKilled() => RunAsync(null);

    [Test]
    [Explicit]
    public Task ForeignNamedVolumeConsumerSurvivesNativeOwnerCleanup() => RunAsync("volume");

    [Test]
    [Explicit]
    public Task ForeignBindConsumerSurvivesNativeOwnerCleanup() => RunAsync("bind");

    [Test]
    [Explicit]
    public Task StoppedForeignVolumeConsumerSurvivesNativeOwnerCleanup() => RunAsync("volume", stopForeign: true);

    [Test]
    [Explicit]
    public Task ReplacementVolumeSurvivesNativeOwnerCleanup() => RunAsync(null, replaceVolume: true);

    [Test]
    [Explicit]
    public async Task TeardownRefusesAnObservablyReplacedVolume()
    {
        using var docker = CreateDockerClient(SelectedDockerSocket());
        string name = "islamu-cleanup-replacement-" + Guid.NewGuid().ToString("N");
        JsonElement? owned = null;
        Exception? primary = null;
        var teardownFailures = new List<Exception>();
        try
        {
            using var first = await SendAsync(docker, HttpMethod.Post, "volumes/create", new
            {
                Name = name,
                Labels = new Dictionary<string, string> { ["islamu.assurance.incarnation"] = Guid.NewGuid().ToString("N") }
            }, CancellationToken.None);
            owned = first.RootElement.Clone();
            JsonElement original = owned.Value;
            await RemoveWitnessedVolumeAsync(docker, name, original);
            owned = null;
            using var second = await SendAsync(docker, HttpMethod.Post, "volumes/create", new
            {
                Name = name,
                Labels = new Dictionary<string, string> { ["islamu.assurance.incarnation"] = Guid.NewGuid().ToString("N") }
            }, CancellationToken.None);
            owned = second.RootElement.Clone();
            await Assert.ThrowsAsync<InvalidDataException>(() => RemoveWitnessedVolumeAsync(docker, name, original));
            using var retained = await InspectAsync(docker, "volumes", name, CancellationToken.None);
            await Assert.That(retained is not null && SameVolumeIncarnation(owned.Value, retained.RootElement)).IsTrue();
            await RemoveWitnessedVolumeAsync(docker, name, owned.Value);
            owned = null;
            using var absent = await InspectAsync(docker, "volumes", name, CancellationToken.None);
            await Assert.That(absent is null).IsTrue();
        }
        catch (Exception failure)
        {
            primary = failure;
        }
        finally
        {
            if (owned is { } current)
                await CaptureTeardownAsync(() => RemoveWitnessedVolumeAsync(docker, name, current), teardownFailures);
        }
        if (teardownFailures.Count != 0)
            throw new AggregateException("Volume replacement probe and teardown failures.",
                primary is null ? teardownFailures : new[] { primary }.Concat(teardownFailures));
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private static async Task RunAsync(string? foreignMountType, bool stopForeign = false, bool replaceVolume = false)
    {
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException("Native cleanup probes require Linux.");
        string socketPath = SelectedDockerSocket();
        string repository = Environment.GetEnvironmentVariable("ISLAMU_NATIVE_CLEANUP_REPOSITORY") ?? FindRepository();
        Require(Path.IsPathFullyQualified(repository), "The selected native candidate repository must be absolute.");
        string privateRoot = Directory.CreateTempSubdirectory("mc-").FullName;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(12));
        using var docker = CreateDockerClient(socketPath);
        IReadOnlyList<ConfiguredOwnedResource> witnessed = [];
        var volumeIdentities = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        ObservedContainerMount? consumed = null;
        JsonElement? replacedOriginal = null;
        bool replacementEstablished = false;
        string? foreignId = null;
        int callbackCount = 0;
        bool nativeSettled = false;
        Exception? primary = null;
        var teardownFailures = new List<Exception>();
        try
        {
            var git = Command(ReleaseToolPaths.Git, repository, "rev-parse", "--verify", "HEAD");
            git.Environment["GIT_MASTER"] = "1";
            string candidate;
            using (var process = new Process { StartInfo = git, EnableRaisingEvents = true })
            {
                var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                process.Exited += (_, _) => exited.TrySetResult();
                process.Start();
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                try { await exited.Task.WaitAsync(TimeSpan.FromSeconds(15), deadline.Token); }
                finally
                {
                    if (!process.HasExited) process.Kill();
                    await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
                }
                Require(process.ExitCode == 0 && (await error).Length == 0, "Candidate HEAD discovery failed.");
                candidate = (await output).TrimEnd('\r', '\n');
            }
            Require(candidate.Length is 40 or 64 && candidate.All(char.IsAsciiHexDigitLower),
                "Candidate HEAD must be an exact lowercase object ID.");
            const string project = "tests/Event.Distributed.IntegrationTests/Event.Distributed.IntegrationTests.csproj";
            string assembly = Path.Combine(repository,
                "tests/Event.Distributed.IntegrationTests/bin/Release/net10.0/Event.Distributed.IntegrationTests.dll");
            Require(File.Exists(assembly), "The genuine committed candidate child must already be compiled.");
            const string childType = "ManagedCleanupChildTests";
            const string childMethod = "OwnedFixtureRemainsLiveUntilItsNativeOwnerStopsIt";
            var discovery = Command(ReleaseToolPaths.Dotnet, repository, assembly, "--list-tests", "json",
                "--treenode-filter", "/*/*/" + childType + "/" + childMethod, "--no-ansi");
            discovery.Environment["TMPDIR"] = privateRoot;
            var listed = await RunOwnedNativeProcessAsync(discovery, TimeSpan.FromSeconds(30), deadline.Token);
            Require(listed.Succeeded && listed.StandardError.Length == 0, "Native child UID discovery failed.");
            using var listing = JsonDocument.Parse(listed.StandardOutput);
            Require(listing.RootElement.GetProperty("schemaVersion").GetInt32() == 1,
                "Native discovery schema must be version one.");
            JsonElement tests = listing.RootElement.GetProperty("tests");
            Require(tests.GetArrayLength() == 1, "Native discovery must select exactly one child.");
            JsonElement node = tests[0];
            JsonElement type = node.GetProperty("type");
            Require(type.GetProperty("namespace").GetString() == "Event.Distributed.IntegrationTests"
                && type.GetProperty("typeName").GetString() == childType
                && type.GetProperty("methodName").GetString() == childMethod, "Native child identity differs.");
            string uid = node.GetProperty("uid").GetString()
                ?? throw new InvalidDataException("Native discovery returned no UID.");
            Require(!string.IsNullOrWhiteSpace(uid), "Native discovery returned an empty UID.");

            var result = await RunManagedCleanupProbeAsync(repository, candidate, socketPath, project, assembly, uid,
                privateRoot, async (resources, kill, token) =>
                {
                    Require(Interlocked.Increment(ref callbackCount) == 1, "Qualified callback ran more than once.");
                    witnessed = resources;
                    Require(resources.Count != 0, "Qualified callback must witness owned containers.");
                    Require(resources.Select(resource => resource.Container.ContainerId)
                        .Distinct(StringComparer.Ordinal).Count() == resources.Count, "Owned container IDs are ambiguous.");
                    var live = new HashSet<string>(StringComparer.Ordinal);
                    foreach (ConfiguredOwnedResource resource in resources)
                    {
                        Require(FullContainerId(resource.Container.ContainerId), "Owned container ID is not exact.");
                        using var actual = await InspectAsync(docker, "containers", resource.Container.ContainerId, token);
                        Require(actual is not null && actual.RootElement.GetProperty("Id").GetString() == resource.Container.ContainerId
                            && actual.RootElement.GetProperty("Image").GetString() == resource.Container.ImageId,
                            "Qualified owned container identity changed.");
                        if (actual!.RootElement.GetProperty("State").GetProperty("Running").GetBoolean())
                            live.Add(resource.Container.ContainerId);
                    }
                    Require(live.Count != 0, "The child must still own a running container before termination.");
                    var volumes = resources.SelectMany(resource => resource.Mounts)
                        .Where(mount => mount.Type == "volume").ToArray();
                    Require(volumes.Length != 0, "Qualified inventory contains no native volume.");
                    foreach (ObservedContainerMount volume in volumes)
                        volumeIdentities[volume.Name] = await RequireVolumeAsync(docker, volume, token);
                    if (foreignMountType is not null)
                    {
                        ConfiguredOwnedResource resource = resources.First(item => live.Contains(item.Container.ContainerId)
                            && item.Mounts.Any(mount => mount.Type == "volume"));
                        consumed = resource.Mounts.First(mount => mount.Type == "volume");
                        using var created = await SendAsync(docker, HttpMethod.Post, "containers/create", new
                        {
                            Image = resource.Container.ImageId,
                            Entrypoint = new[] { "/bin/sleep" },
                            Cmd = new[] { "600" },
                            HostConfig = new
                            {
                                Mounts = new[]
                                {
                                    new
                                    {
                                        Type = foreignMountType,
                                        Source = foreignMountType == "volume" ? consumed.Name : consumed.Source,
                                        Target = "/foreign",
                                        ReadOnly = true
                                    }
                                }
                            }
                        }, token);
                        foreignId = created.RootElement.GetProperty("Id").GetString();
                        Require(foreignId is not null && FullContainerId(foreignId), "Foreign creation returned no exact ID.");
                        Require(resources.All(item => item.Container.ContainerId != foreignId),
                            "Foreign consumer unexpectedly belongs to the child inventory.");
                        using var started = await SendAsync(docker, HttpMethod.Post,
                            "containers/" + foreignId + "/start", null, token);
                        if (stopForeign)
                        {
                            using var stopped = await SendAsync(docker, HttpMethod.Post,
                                "containers/" + foreignId + "/stop?t=5", null, token);
                        }
                        await RequireForeignAsync(docker, foreignId!, consumed, foreignMountType, !stopForeign, token);
                    }
                    await kill();
                    if (replaceVolume)
                    {
                        consumed = Volumes(resources).First();
                        foreach (ConfiguredOwnedResource resource in resources.Where(resource =>
                            resource.Mounts.Any(mount => mount.Type == "volume" && mount.Name == consumed.Name)))
                            await RemoveContainerAsync(docker, resource.Container.ContainerId);
                        JsonElement original = volumeIdentities[consumed.Name];
                        replacedOriginal = original;
                        await RemoveWitnessedVolumeAsync(docker, consumed.Name, original);
                        using var replacement = await SendAsync(docker, HttpMethod.Post, "volumes/create", new
                        {
                            Name = consumed.Name,
                            Labels = new Dictionary<string, string> { ["islamu.assurance.incarnation"] = Guid.NewGuid().ToString("N") }
                        }, deadline.Token);
                        volumeIdentities[consumed.Name] = replacement.RootElement.Clone();
                        Require(!SameVolumeIncarnation(original, replacement.RootElement),
                            "Replacement probe did not change the observed volume identity.");
                        replacementEstablished = true;
                    }
                }, deadline.Token);

            nativeSettled = result.Process.CleanupSucceeded && result.Process.ObserverSettled
                && !result.Process.ResidualOwnedChildren;
            if (callbackCount == 0)
                throw new AggregateException($"Native child exited {result.Process.ExitCode} before qualified inventory; private evidence retained at {privateRoot}.",
                    result.Process.ObserverFailures);
            await Assert.That(callbackCount).IsEqualTo(1);
            if (replaceVolume) await Assert.That(replacementEstablished).IsTrue();
            if (consumed is not null)
                await Assert.That(Volumes(witnessed).Any(volume => volume.Name != consumed.Name)).IsTrue();
            await Assert.That(result.Process.ExitCode).IsEqualTo(137);
            await Assert.That(result.Process.Succeeded).IsFalse();
            await Assert.That(result.Admitted).IsFalse();
            await Assert.That(result.Process.TimedOut).IsFalse();
            await Assert.That(result.Process.Cancelled).IsFalse();
            await Assert.That(result.Process.ResidualOwnedChildren).IsFalse();
            await Assert.That(result.Process.CleanupSucceeded).IsTrue();
            await Assert.That(result.Process.ObserverSettled).IsTrue();
            // Forced death can legitimately retain missing-terminal evidence as ObserverFailures.
            // Settlement and native custody, rather than absence of admission diagnostics, are required here.
            await Assert.That(result.Process.OutputLimitExceeded).IsFalse();
            foreach (ConfiguredOwnedResource resource in witnessed)
            {
                using var remaining = await InspectAsync(docker, "containers", resource.Container.ContainerId, deadline.Token);
                await Assert.That(remaining is null).IsTrue();
            }
            foreach (ObservedContainerMount volume in Volumes(witnessed))
            {
                if (consumed is not null && volume.Name == consumed.Name)
                {
                    JsonElement surviving = await RequireVolumeAsync(docker, volume, deadline.Token);
                    if (replaceVolume)
                    {
                        await Assert.That(SameVolumeIncarnation(volumeIdentities[volume.Name], surviving)).IsTrue();
                        await Assert.That(replacedOriginal.HasValue
                            && !SameVolumeIncarnation(replacedOriginal.Value, surviving)).IsTrue();
                    }
                }
                else
                {
                    using var remaining = await InspectAsync(docker, "volumes", volume.Name, deadline.Token);
                    await Assert.That(remaining is null).IsTrue();
                }
            }
            if (foreignMountType is not null)
                await RequireForeignAsync(docker, foreignId!, consumed!, foreignMountType, !stopForeign, deadline.Token);
            await Assert.That(result.ResourceCleanupComplete).IsEqualTo(consumed is null);
            await Assert.That(Directory.EnumerateDirectories(privateRoot, "m-*").Count())
                .IsEqualTo(consumed is null ? 0 : 1);
        }
        catch (Exception failure)
        {
            primary = failure;
        }
        finally
        {
            // Teardown uses only exact IDs and volume identities witnessed above. Each failure survives independently.
            if (foreignId is not null && FullContainerId(foreignId))
                await CaptureTeardownAsync(async () => await RemoveContainerAsync(docker, foreignId), teardownFailures);
            foreach (ConfiguredOwnedResource resource in witnessed)
                await CaptureTeardownAsync(async () => await RemoveContainerAsync(docker, resource.Container.ContainerId), teardownFailures);
            foreach (ObservedContainerMount volume in Volumes(witnessed))
                await CaptureTeardownAsync(async () =>
                {
                    Require(volumeIdentities.TryGetValue(volume.Name, out JsonElement original),
                        "Teardown refused an unqualified volume.");
                    await RemoveWitnessedVolumeAsync(docker, volume.Name, original);
                }, teardownFailures);
            if (primary is null && teardownFailures.Count == 0 && callbackCount == 1 && nativeSettled)
                await CaptureTeardownAsync(() =>
                {
                    Directory.Delete(privateRoot, recursive: true);
                    return Task.CompletedTask;
                }, teardownFailures);
        }
        if (teardownFailures.Count != 0)
            throw new AggregateException("Managed cleanup probe and teardown failures.",
                primary is null ? teardownFailures : new[] { primary }.Concat(teardownFailures));
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private static string FindRepository()
    {
        for (DirectoryInfo? directory = new FileInfo(typeof(SecurityExecutionEvidenceManagedCleanupTests).Assembly.Location).Directory;
            directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                && (File.Exists(Path.Combine(directory.FullName, ".git")) || Directory.Exists(Path.Combine(directory.FullName, ".git"))))
                return directory.FullName;
        throw new DirectoryNotFoundException("No repository contains this test assembly.");
    }

    private static ProcessStartInfo Command(string executable, string repository, params string[] arguments)
    {
        var command = new ProcessStartInfo(executable)
        {
            WorkingDirectory = repository,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) command.ArgumentList.Add(argument);
        return command;
    }

    private static IEnumerable<ObservedContainerMount> Volumes(IReadOnlyList<ConfiguredOwnedResource> resources) =>
        resources.SelectMany(resource => resource.Mounts).Where(mount => mount.Type == "volume")
            .DistinctBy(mount => (mount.Name, mount.Source));

    private static bool FullContainerId(string id) => id.Length == 64 && id.All(char.IsAsciiHexDigitLower);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static bool SameVolumeIncarnation(JsonElement original, JsonElement current) =>
        new[] { "CreatedAt", "Driver", "Scope", "Labels", "Options" }.All(property =>
            original.TryGetProperty(property, out JsonElement expected)
            && current.TryGetProperty(property, out JsonElement actual)
            && JsonElement.DeepEquals(expected, actual));

    private static async Task RemoveWitnessedVolumeAsync(HttpClient docker, string name, JsonElement original)
    {
        using var current = await InspectAsync(docker, "volumes", name, CancellationToken.None);
        if (current is null) return;
        string source = original.GetProperty("Mountpoint").GetString()!;
        Require(original.GetProperty("Name").GetString() == name
            && current.RootElement.GetProperty("Name").GetString() == name
            && current.RootElement.GetProperty("Mountpoint").GetString() == source
            && SameVolumeIncarnation(original, current.RootElement),
            "Teardown refused a replaced or unqualified volume.");
        using var containers = await SendAsync(docker, HttpMethod.Get, "containers/json?all=true", null, CancellationToken.None);
        Require(VolumeConsumersAbsent(containers.RootElement,
            new Dictionary<string, string>(StringComparer.Ordinal) { [name] = source }),
            "Teardown refused incomplete mounts or a remaining volume consumer.");
        using var removed = await SendAsync(docker, HttpMethod.Delete,
            "volumes/" + Uri.EscapeDataString(name) + "?force=false", null, CancellationToken.None);
    }

    private static string SelectedDockerSocket()
    {
        string dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST")
            ?? throw new InvalidOperationException("DOCKER_HOST must explicitly select a local Unix socket.");
        if (!dockerHost.StartsWith("unix:///", StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(dockerHost["unix://".Length..])
            || dockerHost.Contains('?') || dockerHost.Contains('#'))
            throw new InvalidOperationException("DOCKER_HOST must be unix:// followed by an absolute socket path.");
        return dockerHost["unix://".Length..];
    }

    private static HttpClient CreateDockerClient(string socketPath)
    {
        SocketsHttpHandler? handler = null;
        try
        {
            handler = new SocketsHttpHandler();
            handler.UseProxy = false;
            handler.UseCookies = false;
            handler.AllowAutoRedirect = false;
            handler.ActivityHeadersPropagator = null;
            handler.ConnectTimeout = TimeSpan.FromSeconds(3);
            handler.ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
            var client = new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = new Uri("http://localhost/"),
                Timeout = TimeSpan.FromSeconds(30)
            };
            handler = null;
            return client;
        }
        finally
        {
            handler?.Dispose();
        }
    }

    private static async Task<JsonElement> RequireVolumeAsync(HttpClient docker, ObservedContainerMount volume, CancellationToken token)
    {
        using var actual = await InspectAsync(docker, "volumes", volume.Name, token);
        Require(actual is not null && actual.RootElement.GetProperty("Name").GetString() == volume.Name
            && actual.RootElement.GetProperty("Mountpoint").GetString() == volume.Source, "Observed volume identity changed or disappeared.");
        Require(actual!.RootElement.TryGetProperty("CreatedAt", out JsonElement created)
            && created.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(created.GetString()),
            "Observed volume has no creation identity.");
        return actual.RootElement.Clone();
    }

    private static async Task RequireForeignAsync(HttpClient docker, string id, ObservedContainerMount volume,
        string mountType, bool running, CancellationToken token)
    {
        using var actual = await InspectAsync(docker, "containers", id, token);
        Require(actual is not null && actual.RootElement.GetProperty("Id").GetString() == id
            && actual.RootElement.GetProperty("State").GetProperty("Running").GetBoolean() == running,
            "Exact foreign consumer disappeared or changed running state.");
        Require(actual!.RootElement.GetProperty("Mounts").EnumerateArray().Any(mount =>
            mount.GetProperty("Type").GetString() == mountType
            && mount.GetProperty("Source").GetString() == volume.Source
            && mount.GetProperty("Destination").GetString() == "/foreign"
            && !mount.GetProperty("RW").GetBoolean()
            && (mountType != "volume" || mount.GetProperty("Name").GetString() == volume.Name)),
            "Exact foreign consumer no longer holds the witnessed read-only mount.");
    }

    private static async Task<JsonDocument?> InspectAsync(HttpClient docker, string kind, string identity, CancellationToken token)
    {
        string suffix = kind == "containers" ? "/json" : string.Empty;
        using var response = await docker.GetAsync(kind + "/" + Uri.EscapeDataString(identity) + suffix, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(token));
    }

    private static async Task<JsonDocument> SendAsync(HttpClient docker, HttpMethod method, string path,
        object? body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await docker.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(token);
        return JsonDocument.Parse(bytes.Length == 0 ? "{}"u8.ToArray() : bytes);
    }

    private static async Task RemoveContainerAsync(HttpClient docker, string id)
    {
        Require(FullContainerId(id), "Teardown refused a non-exact container ID.");
        using var response = await docker.DeleteAsync("containers/" + id + "?force=true&v=false", CancellationToken.None);
        if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }

    private static async Task CaptureTeardownAsync(Func<Task> action, List<Exception> failures)
    {
        try { await action(); }
        catch (Exception failure) { failures.Add(failure); }
    }
}
