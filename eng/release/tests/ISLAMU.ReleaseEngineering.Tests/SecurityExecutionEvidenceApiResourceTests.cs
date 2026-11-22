using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using static ISLAMU.ReleaseEngineering.SecurityExecutionEvidence;

namespace ISLAMU.ReleaseEngineering.Tests;

[SupportedOSPlatform("linux")]
public sealed class SecurityExecutionEvidenceApiResourceTests
{
    private const string ProbeImage = "ghcr.io/cerbos/cerbos:0.53.0";

    [Test]
    [Explicit]
    public async Task InterruptedApiOwnerPreservesNewForeignAndAnonymousVolumes()
    {
        string endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST")
            ?? throw new InvalidOperationException("DOCKER_HOST must select a native Unix socket.");
        if (!endpoint.StartsWith("unix:///", StringComparison.Ordinal)) throw new InvalidOperationException("A native Unix socket is required.");
        using var fixture = new SecurityExecutionEvidenceProcessTests.ProcessFixture();
        string owner = Guid.CreateVersion7().ToString("N");
        string root = Path.Combine(fixture.Root, "islamu-assurance", owner);
        Directory.CreateDirectory(root);
        foreach (string directory in new[] { fixture.Root, Path.GetDirectoryName(root)!, root })
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string marker = Path.Combine(root, ".islamu-test-owner");
        await File.WriteAllTextAsync(marker, owner);
        File.SetUnixFileMode(marker, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        ProcessStartInfo command = await fixture.CommandAsync("api-mounted");
        command.Environment["ISLAMU_TEST_RUN_ID"] = owner;
        command.Environment["ISLAMU_TEST_ROOT"] = root;
        string volumeName = "islamu-api-foreign-" + owner;
        command.Environment["ISLAMU_API_PROBE_VOLUME"] = volumeName;
        using var daemon = new SelectedDaemon(endpoint["unix://".Length..]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        Task<(OwnedNativeProcessResult Process, bool Admitted, byte[] Evidence, bool ResourceCleanupComplete)>? execution = null;
        string? container = null;
        var volumes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var failures = new List<Exception>();
        bool volumeCreationAttempted = false;
        async Task CaptureAsync(Func<Task> operation)
        {
            try { await operation(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        using JsonDocument baseline = await daemon.GetAsync("/v1.40/volumes", deadline.Token);
        JsonElement baselineVolumes = baseline.RootElement.GetProperty("Volumes");
        var initialNames = baselineVolumes.ValueKind == JsonValueKind.Null
            ? new HashSet<string>(StringComparer.Ordinal)
            : baselineVolumes.EnumerateArray().Select(volume => volume.GetProperty("Name").GetString()!).ToHashSet(StringComparer.Ordinal);
        if (initialNames.Contains(volumeName)) throw new InvalidDataException("api_probe_volume_already_exists");
        try
        {
            execution = daemon.RunAsync(owner, fixture.Root, root, command, false, TimeSpan.FromSeconds(30), deadline.Token);
            await WaitForFileAsync(Path.Combine(root, "api-create-ready"), deadline.Token);
            volumeCreationAttempted = true;
            _ = await DockerAsync(endpoint, ["volume", "create", "--label", "islamu.assurance.probe=" + owner, volumeName], deadline.Token);
            using (JsonDocument volume = await daemon.GetAsync("/v1.40/volumes/" + volumeName, deadline.Token))
                volumes.Add(volumeName, volume.RootElement.Clone());
            await File.WriteAllTextAsync(Path.Combine(root, "api-volume-ready"), string.Empty, deadline.Token);
            await WaitForFileAsync(Path.Combine(root, "api-container-ready"), deadline.Token);
            container = await File.ReadAllTextAsync(Path.Combine(root, "api-container-ready"), deadline.Token);
            using (JsonDocument inspection = await daemon.GetAsync("/v1.40/containers/" + container + "/json", deadline.Token))
            {
                foreach (JsonElement mount in inspection.RootElement.GetProperty("Mounts").EnumerateArray())
                {
                    string name = mount.GetProperty("Name").GetString()!;
                    using JsonDocument volume = await daemon.GetAsync("/v1.40/volumes/" + name, deadline.Token);
                    volumes[name] = volume.RootElement.Clone();
                }
            }
            var result = await execution;
            using JsonDocument evidence = JsonDocument.Parse(result.Evidence);
            await Assert.That(evidence.RootElement.GetProperty("creatorObservations").GetArrayLength()).IsEqualTo(1);
            await Assert.That(evidence.RootElement.GetProperty("resources").GetArrayLength()).IsEqualTo(1);
            await Assert.That(evidence.RootElement.GetProperty("observedVolumeCount").GetInt32()).IsEqualTo(3);
            await Assert.That(result.Process.TimedOut).IsTrue();
            await Assert.That(result.Admitted).IsFalse();
            await Assert.That(result.ResourceCleanupComplete).IsFalse();
            using JsonDocument containers = await daemon.GetAsync("/v1.40/containers/json?all=true", deadline.Token);
            await Assert.That(containers.RootElement.EnumerateArray().Any(row => row.GetProperty("Id").GetString() == container)).IsFalse();
            foreach (var volume in volumes)
            {
                using JsonDocument retained = await daemon.GetAsync("/v1.40/volumes/" + volume.Key, deadline.Token);
                await Assert.That(SameVolumeIncarnation(volume.Value, retained.RootElement)).IsTrue();
            }
        }
        catch (Exception failure) { failures.Add(failure); }
        await CaptureAsync(() => deadline.CancelAsync());
        if (execution is not null) await CaptureAsync(async () => { _ = await execution; });
        if (container is null && File.Exists(Path.Combine(root, "api-container-ready")))
            await CaptureAsync(async () => container = await File.ReadAllTextAsync(Path.Combine(root, "api-container-ready"), CancellationToken.None));
        if (container is not null)
        {
            await CaptureAsync(async () =>
            {
                if (container.Length != 64 || !container.All(char.IsAsciiHexDigitLower)) throw new InvalidDataException("api_probe_container_invalid");
                using JsonDocument remaining = await daemon.GetAsync("/v1.40/containers/json?all=true", CancellationToken.None);
                if (!remaining.RootElement.EnumerateArray().Any(row => row.GetProperty("Id").GetString() == container)) return;
                using JsonDocument inspection = await daemon.GetAsync("/v1.40/containers/" + container + "/json", CancellationToken.None);
                JsonElement value = inspection.RootElement;
                if (value.GetProperty("Config").GetProperty("Image").GetString() != ProbeImage
                    || value.GetProperty("Config").GetProperty("Labels").GetProperty("islamu.assurance.owner").GetString() != owner)
                    throw new InvalidDataException("api_probe_container_changed");
                foreach (JsonElement mount in value.GetProperty("Mounts").EnumerateArray())
                {
                    string name = mount.GetProperty("Name").GetString()!;
                    if (initialNames.Contains(name)) throw new InvalidDataException("api_probe_initial_volume_mounted");
                    using JsonDocument volume = await daemon.GetAsync("/v1.40/volumes/" + name, CancellationToken.None);
                    if (volumes.TryGetValue(name, out JsonElement original) && !SameVolumeIncarnation(original, volume.RootElement))
                        throw new InvalidDataException("api_probe_volume_replaced");
                    volumes[name] = volume.RootElement.Clone();
                }
                _ = await DockerAsync(endpoint, ["rm", "--force", container], CancellationToken.None);
            });
        }
        if (volumeCreationAttempted && !volumes.ContainsKey(volumeName)) await CaptureAsync(async () =>
        {
            using JsonDocument inventory = await daemon.GetAsync("/v1.40/volumes", CancellationToken.None);
            JsonElement available = inventory.RootElement.GetProperty("Volumes");
            if (available.ValueKind == JsonValueKind.Null
                || !available.EnumerateArray().Any(volume => volume.GetProperty("Name").GetString() == volumeName)) return;
            using JsonDocument current = await daemon.GetAsync("/v1.40/volumes/" + volumeName, CancellationToken.None);
            if (current.RootElement.GetProperty("Labels").GetProperty("islamu.assurance.probe").GetString() != owner)
                throw new InvalidDataException("api_probe_volume_unknown");
            volumes.Add(volumeName, current.RootElement.Clone());
        });
        foreach (var volume in volumes)
        {
            await CaptureAsync(async () =>
            {
                if (initialNames.Contains(volume.Key)) throw new InvalidDataException("api_probe_initial_volume_mounted");
                using JsonDocument current = await daemon.GetAsync("/v1.40/volumes/" + volume.Key, CancellationToken.None);
                if (!SameVolumeIncarnation(volume.Value, current.RootElement)) throw new InvalidDataException("api_probe_volume_replaced");
                using JsonDocument consumers = await daemon.GetAsync("/v1.40/containers/json?all=true", CancellationToken.None);
                if (!VolumeConsumersAbsent(consumers.RootElement, new Dictionary<string, string>(StringComparer.Ordinal)
                { [volume.Key] = current.RootElement.GetProperty("Mountpoint").GetString()! }))
                    throw new InvalidDataException("api_probe_volume_consumed");
                _ = await DockerAsync(endpoint, ["volume", "rm", volume.Key], CancellationToken.None);
            });
        }
        if (failures.Count > 1) throw new AggregateException("API mount probe and teardown failures.", failures);
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
    }

    internal static async Task RunMountedNativeChildAsync()
    {
        string endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST")!;
        string owner = Environment.GetEnvironmentVariable("ISLAMU_TEST_RUN_ID")!;
        string root = Environment.GetEnvironmentVariable("ISLAMU_TEST_ROOT")!;
        string volume = Environment.GetEnvironmentVariable("ISLAMU_API_PROBE_VOLUME")!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        await File.WriteAllTextAsync(Path.Combine(root, "api-create-ready"), string.Empty, deadline.Token);
        await WaitForFileAsync(Path.Combine(root, "api-volume-ready"), deadline.Token);
        string creation = Guid.CreateVersion7().ToString("N");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.Read,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        };
        string stem = Path.Combine(root, "api-container-cerbos-" + creation);
        byte[] declaration = JsonSerializer.SerializeToUtf8Bytes(new
        { Schema = 1, RunId = owner, CreatorPid = Environment.ProcessId, CreationId = creation, Resource = "cerbos", ImageReference = ProbeImage });
        using var declared = new FileStream(stem + ".pending", options);
        await declared.WriteAsync(declaration, deadline.Token);
        declared.Flush(true);
        File.Move(stem + ".pending", stem + "-declaration.json");
        string container = (await DockerAsync(endpoint,
            ["create", "--pull=never", "--label", "islamu.assurance.owner=" + owner,
             "--label", "islamu.assurance.resource=cerbos", "--mount", "type=volume,source=" + volume + ",target=/tmp", ProbeImage], deadline.Token)).Trim();
        if (container.Length != 64 || !container.All(char.IsAsciiHexDigitLower)) throw new InvalidDataException("api_probe_container_invalid");
        string ready = Path.Combine(root, "api-container-ready");
        await File.WriteAllTextAsync(ready + ".pending", container, deadline.Token);
        File.Move(ready + ".pending", ready);
        using var receipt = new FileStream(stem + ".created-pending", options);
        await receipt.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Schema = 1,
            RunId = owner,
            CreatorPid = Environment.ProcessId,
            CreationId = creation,
            Resource = "cerbos",
            ImageReference = ProbeImage,
            ContainerId = container,
            DeclarationSha256 = Convert.ToHexStringLower(SHA256.HashData(declaration))
        }), deadline.Token);
        receipt.Flush(true);
        File.Move(stem + ".created-pending", stem + "-created-" + container + ".json");
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    private static async Task WaitForFileAsync(string path, CancellationToken token)
    {
        while (!File.Exists(path)) await Task.Delay(20, token);
    }

    private static async Task<string> DockerAsync(string endpoint, string[] arguments, CancellationToken token)
    {
        var command = new ProcessStartInfo("/usr/bin/docker")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        command.ArgumentList.Add("--host");
        command.ArgumentList.Add(endpoint);
        foreach (string argument in arguments) command.ArgumentList.Add(argument);
        using Process process = Process.Start(command) ?? throw new InvalidDataException("api_probe_docker_start_failed");
        Task<string> output = process.StandardOutput.ReadToEndAsync(token);
        Task<string> error = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(15), token); }
        finally
        {
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        if (process.ExitCode != 0 || (await error).Length != 0) throw new InvalidDataException("api_probe_docker_failed");
        return await output;
    }

    [Test]
    [Arguments("declared", true)]
    [Arguments("pre-existing", false)]
    [Arguments("bind", false)]
    [Arguments("read-only", false)]
    [Arguments("source-changed", false)]
    [Arguments("volume-unknown", false)]
    [Arguments("missing-target", false)]
    [Arguments("extra-target", false)]
    [Arguments("duplicate-target", false)]
    [Arguments("image-unknown", false)]
    public async Task ImageDeclaredVolumesJoinOnlyFreshAuthenticatedStorage(string scenario, bool accepted)
    {
        string owner = new('a', 32), container = new('b', 64), image = "sha256:" + new string('c', 64);
        const string reference = "registry.example.test/security:control";
        var creation = new NativeCreatedContainer(owner, Guid.CreateVersion7().ToString("N"),
            Environment.ProcessId, "cerbos", reference, container, new string('d', 64));
        var snapshot = new NativeContainerCreationSnapshot([creation], new HashSet<string>(StringComparer.Ordinal), []);
        var images = new Dictionary<string, string>(StringComparer.Ordinal) { [reference] = image };
        var initial = new InitialNativeResources(new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal) { image });
        string[] destinations = ["/.cache", "/policies", "/runtime-scratch"];
        var volumes = new Dictionary<string, string>(StringComparer.Ordinal);
        var targets = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        { [image] = destinations.ToHashSet(StringComparer.Ordinal) };
        var mounts = new JsonArray(destinations.Select(destination =>
        {
            string name = Guid.CreateVersion7().ToString("N");
            volumes.Add(name, "/native/volumes/" + name);
            return new JsonObject
            {
                ["Type"] = "volume",
                ["Name"] = name,
                ["Source"] = "/native/volumes/" + name,
                ["Destination"] = destination,
                ["RW"] = true
            };
        }).ToArray());
        var row = new JsonObject
        {
            ["Id"] = container,
            ["Image"] = reference,
            ["ImageID"] = image,
            ["State"] = "running",
            ["Labels"] = new JsonObject { ["islamu.assurance.owner"] = owner, ["islamu.assurance.resource"] = "cerbos" },
            ["Mounts"] = mounts
        };
        switch (scenario)
        {
            case "declared": break;
            case "pre-existing":
                initial = initial with { VolumeNames = new HashSet<string>(StringComparer.Ordinal) { volumes.Keys.First() } };
                break;
            case "bind": mounts[0]!["Type"] = "bind"; break;
            case "read-only": mounts[0]!["RW"] = false; break;
            case "source-changed": mounts[0]!["Source"] = "/other/storage"; break;
            case "volume-unknown": volumes.Clear(); break;
            case "missing-target": mounts.RemoveAt(0); break;
            case "extra-target": mounts[0]!["Destination"] = "/unexpected"; break;
            case "duplicate-target": mounts[1]!["Destination"] = "/.cache"; break;
            case "image-unknown": targets.Clear(); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        await Assert.That(TryReadApiCreatedResources(JsonSerializer.SerializeToUtf8Bytes(new JsonArray(row)),
            new(owner, snapshot, images, initial, targets, volumes), out var observed)).IsEqualTo(accepted);
        if (accepted)
        {
            ConfiguredOwnedResource resource = observed?.Single() ?? throw new InvalidDataException("api_resource_join_missing");
            await Assert.That(resource.Container).IsEqualTo(new ObservedOwnedResource(container, image, "cerbos", true));
            await Assert.That(resource.Mounts.Count).IsEqualTo(3);
        }
    }
}
