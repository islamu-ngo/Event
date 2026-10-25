using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.Text.Json;
using System.Reflection;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class SecurityExecutionEvidenceIntegrationTests
{
    [Test]
    [Arguments("security-integration", "600s", 615)]
    [Arguments("owned-load-correctness", "1500s", 1515)]
    public async Task TypedUidLaunchBudgetPreservesOrdinaryAndTwentyMinuteLoadRegimes(
        string lane, string expectedTimeout, int expectedDeadlineSeconds)
    {
        var requirement = SecurityControlRegister.ExecutionRequirements[lane];
        var launch = SecurityExecutionEvidence.NativeExecutionStartInfo("/owned/tests.dll", ["opaque uid"],
            "/owned/tmp/results", "/owned/tmp", "execution.trx", requirement.HostProfile, lane);
        string[] arguments = launch.Command.ArgumentList.ToArray();
        await Assert.That(arguments[1]).IsEqualTo("--filter-uid");
        await Assert.That(arguments[2]).IsEqualTo("opaque uid");
        int timeout = Array.IndexOf(arguments, "--timeout");
        await Assert.That(timeout >= 0).IsTrue();
        await Assert.That(arguments[timeout + 1]).IsEqualTo(expectedTimeout);
        await Assert.That(launch.Deadline.TotalSeconds).IsEqualTo((double)expectedDeadlineSeconds);
    }

    [Test]
    [Arguments("owned-runtime", 1, "720s", 735)]
    [Arguments("owned-browser-journeys", 2, "2400s", 2415)]
    [Arguments("owned-browser-security", 2, "1500s", 1515)]
    [Arguments("owned-browser-journeys", 1, "1200s", 1215)]
    [Arguments("owned-browser-journeys", 33, "19800s", 19815)]
    public async Task SerialOwnedBatchBudgetIncludesEverySelectedCaseAndItsLifecycleAllowance(
        string lane, int count, string expectedTimeout, int expectedDeadlineSeconds)
    {
        var requirement = SecurityControlRegister.ExecutionRequirements[lane];
        string[] uids = Enumerable.Range(0, count).Select(index => "opaque selected uid " + index).ToArray();
        var launch = SecurityExecutionEvidence.NativeExecutionStartInfo("/owned/tests.dll", uids,
            "/owned/tmp/results", "/owned/tmp", "execution.trx", requirement.HostProfile, lane);
        string[] arguments = launch.Command.ArgumentList.ToArray();
        await Assert.That(arguments[1]).IsEqualTo("--filter-uid");
        await Assert.That(arguments.Skip(2).Take(count).SequenceEqual(uids)).IsTrue();
        int timeout = Array.IndexOf(arguments, "--timeout");
        await Assert.That(timeout).IsEqualTo(2 + count);
        await Assert.That(arguments[timeout + 1]).IsEqualTo(expectedTimeout);
        await Assert.That(launch.Deadline.TotalSeconds).IsEqualTo((double)expectedDeadlineSeconds);
        if (lane == "owned-runtime")
        {
            bool rejected = false;
            try
            {
                _ = SecurityExecutionEvidence.NativeExecutionStartInfo("/owned/tests.dll", ["first uid", "second uid"],
                    "/owned/tmp/results", "/owned/tmp", "execution.trx", requirement.HostProfile, lane);
            }
            catch (InvalidDataException) { rejected = true; }
            await Assert.That(rejected).IsTrue();
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(1025)]
    public async Task NativeBatchRejectsEmptyOrUnboundedSelectionBeforeLaunching(int count)
    {
        string[] uids = Enumerable.Range(0, count).Select(index => "bounded uid " + index).ToArray();
        bool rejected = false;
        try
        {
            _ = SecurityExecutionEvidence.NativeExecutionStartInfo("/owned/tests.dll", uids,
                "/owned/tmp/results", "/owned/tmp", "execution.trx", "owned-apphost-assurance", "owned-runtime");
        }
        catch (InvalidDataException) { rejected = true; }
        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task ExplicitSyntheticRegistrationNormalizesAnAlreadyCompleteSourceRegister()
    {
        string shipped = RegisterPath();
        await Assert.That(SecurityControlRegister.TryValidate(shipped, out _)).IsTrue();
        await Assert.That(SecurityControlRegister.ReadExecutionRequirements(shipped).Count).IsEqualTo(7);
        string directory = Path.Combine(Path.GetTempPath(), "islamu-synthetic-normalization-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string normalized = Path.Combine(directory, "register.json");
            File.WriteAllText(normalized, SecurityExecutionEvidenceFixture.OwnedRegister(shipped).ToJsonString());
            await Assert.That(SecurityControlRegister.TryValidate(normalized, out _)).IsTrue();
            var requirements = SecurityControlRegister.ReadExecutionRequirements(normalized);
            await Assert.That(requirements.Count).IsEqualTo(7);
            foreach (var requirement in requirements.Where(item => item.Key.StartsWith("owned-", StringComparison.Ordinal)))
            {
                await Assert.That(requirement.Value.Tests.Count).IsEqualTo(2);
                await Assert.That(requirement.Value.Tests.All(test => test.ClassName == "Synthetic.OwnedProfile")).IsTrue();
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string RegisterPath([CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath) ?? throw new InvalidDataException(),
            "..", "..", "..", "security", "security-control-register.json"));
}

public sealed class SecurityExecutionEvidenceManagedHintTests
{
    [Test]
    [Arguments("complete", true)]
    [Arguments("lead-without-inputs", true)]
    [Arguments("lead-null-inputs", true)]
    [Arguments("selected-peer", true)]
    [Arguments("wrong-schema", false)]
    [Arguments("wrong-owner", false)]
    [Arguments("unknown-resource", false)]
    [Arguments("invalid-pid", false)]
    [Arguments("missing-instance", false)]
    [Arguments("unknown-field", false)]
    [Arguments("wrong-ancestry-head", false)]
    [Arguments("duplicate-file", false)]
    [Arguments("negative-file-stamp", false)]
    [Arguments("duplicate-json-field", false)]
    public async Task PrivateHintGrammarPreservesExactLeadsWithoutGrantingNativeAuthority(string variant, bool expected)
    {
        string owner = new('1', 32);
        string instance = new('A', 64);
        const string entry = "/candidate/src/Explore.MigrationService/bin/Release/net10.0/Explore.MigrationService.dll";
        string[] files = ["/runtime/dotnet", entry, Path.ChangeExtension(entry, ".deps.json"),
            Path.ChangeExtension(entry, ".runtimeconfig.json")];
        JsonObject document = new()
        {
            ["Schema"] = 1, ["RunId"] = owner,
            ["Hint"] = new JsonObject
            {
                ["Resource"] = "event-migrationservice", ["InstanceSha256"] = instance, ["Pid"] = 123, ["Start"] = 42,
                ["Diagnostic"] = "managed_launch_os_inputs_observed",
                ["Inputs"] = new JsonObject
                {
                    ["Ancestry"] = new JsonArray(new JsonObject
                    {
                        ["Pid"] = 123, ["Parent"] = 122, ["User"] = 1000, ["Group"] = 123,
                        ["Session"] = 123, ["Start"] = 42, ["State"] = "S"
                    }),
                    ["Executable"] = files[0], ["WorkingDirectory"] = "/candidate/src/Explore.MigrationService",
                    ["Entry"] = entry,
                    ["Files"] = new JsonArray(files.Select(path => (JsonNode)new JsonObject
                    {
                        ["Path"] = path, ["Bytes"] = 16, ["Sha256"] = new string('B', 64),
                        ["LastWriteUtcTicks"] = 638000000000000000L, ["CreationUtcTicks"] = 638000000000000000L
                    }).ToArray())
                }
            }
        };
        byte[] baselineBytes = Encoding.UTF8.GetBytes(document.ToJsonString());
        await Assert.That(SecurityExecutionEvidence.TryReadManagedLaunchHint(baselineBytes, owner,
            out var baseline, out string diagnostic)).IsTrue();
        await Assert.That(diagnostic).IsEqualTo(string.Empty);
        await Assert.That(baseline!.RunId).IsEqualTo(owner);
        await Assert.That(baseline.Resource).IsEqualTo("event-migrationservice");
        await Assert.That(baseline.InstanceSha256).IsEqualTo(instance);
        await Assert.That(baseline.Pid).IsEqualTo(123);
        await Assert.That(baseline.Start).IsEqualTo(42UL);
        await Assert.That(baseline.Inputs.HasValue).IsTrue();
        JsonElement baselineInputs = baseline.Inputs.GetValueOrDefault();
        await Assert.That(baselineInputs.GetProperty("Entry").GetString()).IsEqualTo(entry);
        await Assert.That(baselineInputs.GetProperty("Files").EnumerateArray()
            .Select(file => file.GetProperty("Path").GetString()).SequenceEqual(files)).IsTrue();
        JsonObject hint = document["Hint"]!.AsObject();
        foreach (string invalidStart in new[] { "missing", "zero", "different-from-inputs" })
        {
            JsonObject malformed = document.DeepClone().AsObject();
            JsonObject malformedHint = malformed["Hint"]!.AsObject();
            if (invalidStart == "missing") malformedHint.Remove("Start");
            else malformedHint["Start"] = invalidStart == "zero" ? 0 : 43;
            await Assert.That(SecurityExecutionEvidence.TryReadManagedLaunchHint(
                JsonSerializer.SerializeToUtf8Bytes(malformed), owner, out _, out _)).IsFalse();
        }
        switch (variant)
        {
            case "lead-without-inputs": hint.Remove("Inputs"); hint["Diagnostic"] = "managed_launch_inputs_unavailable"; break;
            case "lead-null-inputs": hint["Inputs"] = null; hint["Diagnostic"] = "managed_launch_inputs_unavailable"; break;
            case "selected-peer":
                hint["Resource"] = "explore-blazor-peer";
                hint["InstanceSha256"] = new string('C', 64);
                hint["Pid"] = 321;
                hint["Start"] = 43;
                hint["Inputs"]!["Ancestry"]![0]!["Pid"] = 321;
                hint["Inputs"]!["Ancestry"]![0]!["Parent"] = 320;
                hint["Inputs"]!["Ancestry"]![0]!["Group"] = 321;
                hint["Inputs"]!["Ancestry"]![0]!["Session"] = 321;
                hint["Inputs"]!["Ancestry"]![0]!["Start"] = 43;
                const string peerEntry = "/candidate/src/Explore.Blazor/bin/Release/net10.0/Explore.Blazor.dll";
                hint["Inputs"]!["WorkingDirectory"] = "/candidate/src/Explore.Blazor";
                hint["Inputs"]!["Entry"] = peerEntry;
                hint["Inputs"]!["Files"]![1]!["Path"] = peerEntry;
                hint["Inputs"]!["Files"]![2]!["Path"] = Path.ChangeExtension(peerEntry, ".deps.json");
                hint["Inputs"]!["Files"]![3]!["Path"] = Path.ChangeExtension(peerEntry, ".runtimeconfig.json");
                break;
            case "wrong-schema": document["Schema"] = 2; break;
            case "wrong-owner": document["RunId"] = new string('2', 32); break;
            case "unknown-resource": hint["Resource"] = "unselected-host"; break;
            case "invalid-pid": hint["Pid"] = 0; break;
            case "missing-instance": hint.Remove("InstanceSha256"); break;
            case "unknown-field": hint["Environment"] = "untrusted"; break;
            case "wrong-ancestry-head": hint["Inputs"]!["Ancestry"]![0]!["Pid"] = 124; break;
            case "duplicate-file":
                hint["Inputs"]!["Entry"] = files[0];
                hint["Inputs"]!["Files"]![1] = hint["Inputs"]!["Files"]![0]!.DeepClone();
                hint["Inputs"]!["Files"]![2]!["Path"] = Path.ChangeExtension(files[0], ".deps.json");
                hint["Inputs"]!["Files"]![3]!["Path"] = Path.ChangeExtension(files[0], ".runtimeconfig.json");
                break;
            case "negative-file-stamp": hint["Inputs"]!["Files"]![0]!["LastWriteUtcTicks"] = -1; break;
        }
        string json = document.ToJsonString();
        if (variant == "duplicate-json-field") json = json.Replace("\"Schema\":1", "\"Schema\":1,\"Schema\":1", StringComparison.Ordinal);
        bool accepted = SecurityExecutionEvidence.TryReadManagedLaunchHint(Encoding.UTF8.GetBytes(json), owner,
            out var result, out _);
        await Assert.That(accepted).IsEqualTo(expected);
        if (expected)
        {
            await Assert.That(result!.RunId).IsEqualTo(owner);
            await Assert.That(result.Resource).IsEqualTo(variant == "selected-peer" ? "explore-blazor-peer" : "event-migrationservice");
            await Assert.That(result.InstanceSha256).IsEqualTo(variant == "selected-peer" ? new string('C', 64) : instance);
            await Assert.That(result.Pid).IsEqualTo(variant == "selected-peer" ? 321 : 123);
            await Assert.That(result.Start).IsEqualTo(variant == "selected-peer" ? 43UL : 42UL);
            await Assert.That(result.Inputs.HasValue).IsEqualTo(variant is not ("lead-without-inputs" or "lead-null-inputs"));
            if (result.Inputs is JsonElement inputs)
            {
                await Assert.That(inputs.GetProperty("Entry").GetString()).IsEqualTo(variant == "selected-peer"
                    ? "/candidate/src/Explore.Blazor/bin/Release/net10.0/Explore.Blazor.dll" : entry);
                await Assert.That(inputs.GetProperty("Ancestry")[0].GetProperty("Pid").GetInt32())
                    .IsEqualTo(variant == "selected-peer" ? 321 : 123);
            }
        }
    }
}

public sealed class SecurityExecutionEvidenceParentModelTests
{
    [Test]
    public async Task LiveChildPossessionCannotBeReplacedByCopiedBytesOrDifferentProcessInputs()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new SecurityExecutionEvidenceProcessTests.ProcessFixture();
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        ProcessStartInfo command = await fixture.CommandAsync("managed-witness");
        ProcessStartInfo laterCommand = await fixture.CommandAsync("managed-claim");
        string temporary = command.WorkingDirectory;
        command.Environment["TMPDIR"] = temporary;
        laterCommand.ArgumentList[laterCommand.ArgumentList.IndexOf("--results-directory") + 1] =
            Path.Combine(temporary, "later-results");
        File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string owner = Path.GetFileName(temporary)[^32..];
        string root = Path.Combine(temporary, "islamu-assurance", owner);
        string lead = Path.Combine(root, "managed-control-lead.json");
        string entry = typeof(SecurityExecutionEvidenceParentModelTests).Assembly.Location;
        string otherEntry = typeof(SecurityExecutionEvidence).Assembly.Location;
        await Assert.That(File.Exists(entry) && File.Exists(otherEntry)).IsTrue();
        await Assert.That(otherEntry).IsNotEqualTo(entry);
        string version = typeof(SecurityExecutionEvidence).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        string candidate = version[(version.LastIndexOf('+') + 1)..];
        await Assert.That(candidate.Length is 40 or 64 && candidate.All(character =>
            char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')).IsTrue();
        SecurityExecutionEvidence.OwnedNativeProcessContext? identity = null;
        SecurityExecutionEvidence.NativeManagedLaunchHint? hint = null;
        var result = await SecurityExecutionEvidence.RunOwnedNativeProcessAsync(command,
            TimeSpan.FromSeconds(55), caller.Token, async (context, stop) =>
            {
                if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
                identity = context;
                using var ready = CancellationTokenSource.CreateLinkedTokenSource(stop);
                ready.CancelAfter(TimeSpan.FromSeconds(10));
                while (!File.Exists(lead)) await Task.Delay(10, ready.Token);
                await Assert.That(SecurityExecutionEvidence.TryReadManagedLaunchHint(File.ReadAllBytes(lead), owner,
                    out hint, out _)).IsTrue();
                var selectedHint = hint ?? throw new InvalidDataException("native_managed_control_lead_missing");
                await Assert.That(selectedHint.Pid).IsEqualTo(context.Pid);
                var baseline = await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(context,
                    temporary, root, selectedHint, entry, candidate, stop);
                await Assert.That(baseline is not null).IsTrue();
                await Assert.That(baseline!.PrivateManifestSha256).IsEqualTo(
                    Convert.ToHexString(SHA256.HashData(baseline.PrivateManifest)).ToLowerInvariant());
                using JsonDocument manifest = JsonDocument.Parse(baseline.PrivateManifest);
                JsonElement process = manifest.RootElement.GetProperty("process");
                await Assert.That(process.GetProperty("pid").GetInt32()).IsEqualTo(context.Pid);
                await Assert.That(process.GetProperty("start").GetUInt64()).IsEqualTo(context.Start);
                await Assert.That(process.GetProperty("entry").GetString()).IsEqualTo(entry);
                await Assert.That(process.GetProperty("resource").GetString()).IsEqualTo("event-migrationservice");
                await Assert.That(process.GetProperty("instanceSha256").GetString()).IsEqualTo(new string('c', 64));
                await Assert.That(manifest.RootElement.GetProperty("candidateOid").GetString()).IsEqualTo(candidate);
                var declared = await SecurityExecutionEvidence.CaptureLiveFixtureModelAsync(context,
                    temporary, root, owner, stop);
                await Assert.That(declared is not null).IsTrue();
                await Assert.That(declared!.Containers.Count).IsEqualTo(0);
                await Assert.That(declared.ModelSha256).IsEqualTo(
                    Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "container-model.json")))));
                var creations = await SecurityExecutionEvidence.CaptureLiveNativeContainerCreationsAsync(
                    context, temporary, owner, "api", stop);
                var observedCreations = creations
                    ?? throw new InvalidDataException("native_container_creator_possession_not_joined");
                await Assert.That(observedCreations.Created.Count).IsEqualTo(1);
                await Assert.That(observedCreations.PendingCreationIds.Count).IsEqualTo(0);
                string declarationPath = Directory.GetFiles(root, "api-container-*-declaration.json").Single();
                byte[] declarationBytes = File.ReadAllBytes(declarationPath);
                using JsonDocument creationDocument = JsonDocument.Parse(declarationBytes);
                string creationId = creationDocument.RootElement.GetProperty("CreationId").GetString()!;
                string declarationDigest = Convert.ToHexStringLower(SHA256.HashData(declarationBytes));
                await Assert.That(observedCreations.Created.Single()).IsEqualTo(new SecurityExecutionEvidence.NativeCreatedContainer(
                    owner, creationId, context.Pid, "keycloak", "example.invalid/creator-control:local",
                    new string('a', 64), declarationDigest));
                string createdPath = Directory.GetFiles(root, "api-container-*-created-*.json").Single();
                File.Move(createdPath, createdPath + ".held-control");
                try
                {
                    var pendingCreation = await SecurityExecutionEvidence.CaptureLiveNativeContainerCreationsAsync(
                        context, temporary, owner, "api", stop);
                    await Assert.That(pendingCreation is not null).IsTrue();
                    await Assert.That(pendingCreation!.Created.Count).IsEqualTo(0);
                    await Assert.That(pendingCreation.PendingCreationIds.SetEquals([creationId])).IsTrue();
                }
                finally { File.Move(createdPath + ".held-control", createdPath); }
                foreach (string pairPath in new[] { declarationPath, createdPath })
                {
                    byte[] originalBytes = File.ReadAllBytes(pairPath);
                    File.Move(pairPath, pairPath + ".held-control");
                    try
                    {
                        using (var copied = new FileStream(pairPath, new FileStreamOptions
                        {
                            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read,
                            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                        }))
                        {
                            copied.Write(originalBytes);
                            copied.Flush(true);
                        }
                        await Assert.That((await SecurityExecutionEvidence.CaptureLiveNativeContainerCreationsAsync(
                            context, temporary, owner, "api", stop)) is null).IsTrue();
                    }
                    finally
                    {
                        File.Delete(pairPath);
                        File.Move(pairPath + ".held-control", pairPath);
                    }
                    await Assert.That((await SecurityExecutionEvidence.CaptureLiveNativeContainerCreationsAsync(
                        context, temporary, owner, "api", stop)) is not null).IsTrue();
                }
                using (var nextCreatorSignal = new FileStream(Path.Combine(root, "managed-control-next-creation"),
                    new FileStreamOptions
                    {
                        Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read,
                        UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                    }))
                    nextCreatorSignal.Flush(true);
                string nextCreatorReadyPath = Path.Combine(root, "managed-control-next-creation-ready");
                using var nextCreatorReady = CancellationTokenSource.CreateLinkedTokenSource(stop);
                nextCreatorReady.CancelAfter(TimeSpan.FromSeconds(10));
                while (!File.Exists(nextCreatorReadyPath)) await Task.Delay(10, nextCreatorReady.Token);
                string nextCreatorId = File.ReadAllText(nextCreatorReadyPath, Encoding.ASCII);
                await Assert.That(nextCreatorId.Length == 32 && nextCreatorId != creationId).IsTrue();
                var nextCreatorCapture = await SecurityExecutionEvidence.CaptureLiveNativeContainerCreationsAsync(
                    context, temporary, owner, "api", stop, nextCreatorId);
                if (nextCreatorCapture is null)
                    throw new InvalidDataException("native_api_next_live_creation_not_joined");
                await Assert.That(nextCreatorCapture.Created.Count).IsEqualTo(1);
                await Assert.That(nextCreatorCapture.PendingCreationIds.Count).IsEqualTo(0);
                string nextCreatorDeclaration = Path.Combine(root, "api-container-cerbos-" + nextCreatorId + "-declaration.json");
                await Assert.That(nextCreatorCapture.Created.Single()).IsEqualTo(new SecurityExecutionEvidence.NativeCreatedContainer(
                    owner, nextCreatorId, context.Pid, "cerbos", "example.invalid/creator-control:next",
                    new string('d', 64), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(nextCreatorDeclaration)))));
                await Assert.That((await SecurityExecutionEvidence.CaptureLiveNativeContainerCreationsAsync(
                    context, temporary, owner, "api", stop, creationId)) is null).IsTrue();
                await Assert.That((await SecurityExecutionEvidence.CaptureLiveNativeContainerCreationsAsync(
                    context, temporary, owner, "api", stop)) is null).IsTrue();
                await Assert.That((await SecurityExecutionEvidence.CaptureLiveNativeContainerCreationsAsync(
                    context, temporary, owner, "api", stop, nextCreatorId)) is not null).IsTrue();
                string hostedRecord = Path.Combine(root, $"managed-host-root-{context.Pid}-{context.Start}.json");
                File.Move(hostedRecord, hostedRecord + ".not-hosted");
                try
                {
                    await Assert.That((await SecurityExecutionEvidence.CaptureLiveFixtureModelAsync(context,
                        temporary, root, owner, stop)) is not null).IsTrue();
                    await Assert.That((await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(context,
                        temporary, root, selectedHint, entry, candidate, stop)) is null).IsTrue();
                }
                finally { File.Move(hostedRecord + ".not-hosted", hostedRecord); }
                string liveStat = File.ReadAllText("/proc/" + context.Pid + "/stat");
                string[] liveFields = liveStat[(liveStat.LastIndexOf(')') + 2)..].Split(' ');
                string executable = process.GetProperty("executable").GetString()!;
                string[] inputPaths = [executable, entry, Path.ChangeExtension(entry, ".deps.json"),
                    Path.ChangeExtension(entry, ".runtimeconfig.json")];
                JsonObject supplied = new()
                {
                    ["Ancestry"] = new JsonArray(new JsonObject
                    {
                        ["Pid"] = context.Pid, ["Parent"] = int.Parse(liveFields[1], System.Globalization.CultureInfo.InvariantCulture),
                        ["User"] = process.GetProperty("user").GetUInt32(), ["Group"] = context.Group,
                        ["Session"] = context.Session, ["Start"] = context.Start, ["State"] = liveFields[0]
                    }),
                    ["Executable"] = executable, ["WorkingDirectory"] = process.GetProperty("workingDirectory").GetString(),
                    ["Entry"] = entry, ["Files"] = new JsonArray(inputPaths.Select(path =>
                    {
                        var file = new FileInfo(path);
                        using var bytes = File.OpenRead(path);
                        return (JsonNode)new JsonObject
                        {
                            ["Path"] = path, ["Bytes"] = file.Length,
                            ["Sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                            ["LastWriteUtcTicks"] = file.LastWriteTimeUtc.Ticks, ["CreationUtcTicks"] = file.CreationTimeUtc.Ticks
                        };
                    }).ToArray())
                };
                byte[] HintBytes(JsonObject inputs) => JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Schema = 1, RunId = owner, Hint = new { selectedHint.Resource, selectedHint.InstanceSha256,
                        selectedHint.Pid, selectedHint.Start, Diagnostic = "managed_launch_os_inputs_observed", Inputs = inputs }
                });
                await Assert.That(SecurityExecutionEvidence.TryReadManagedLaunchHint(HintBytes(supplied), owner,
                    out var parsedInputs, out _)).IsTrue();
                var withInputs = parsedInputs ?? throw new InvalidDataException("native_managed_control_inputs_missing");
                await Assert.That((await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(context,
                    temporary, root, withInputs, entry, candidate, stop)) is not null).IsTrue();
                foreach (string conflict in new[] { "group", "session", "parent", "bytes", "sha256", "modified", "created" })
                {
                    JsonObject bad = supplied.DeepClone().AsObject();
                    JsonObject head = bad["Ancestry"]![0]!.AsObject(), file = bad["Files"]![1]!.AsObject();
                    switch (conflict)
                    {
                        case "group": head["Group"] = context.Group + 1; break;
                        case "session": head["Session"] = context.Session + 1; break;
                        case "parent": head["Parent"] = head["Parent"]!.GetValue<int>() + 1; break;
                        case "bytes": file["Bytes"] = file["Bytes"]!.GetValue<long>() + 1; break;
                        case "sha256": file["Sha256"] = new string('0', 64); break;
                        case "modified": file["LastWriteUtcTicks"] = file["LastWriteUtcTicks"]!.GetValue<long>() + 1; break;
                        case "created": file["CreationUtcTicks"] = file["CreationUtcTicks"]!.GetValue<long>() + 1; break;
                    }
                    await Assert.That(SecurityExecutionEvidence.TryReadManagedLaunchHint(HintBytes(bad), owner,
                        out var parsedConflict, out _)).IsTrue();
                    var conflicting = parsedConflict ?? throw new InvalidDataException("native_managed_control_conflict_missing");
                    var rejected = await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(context,
                        temporary, root, conflicting, entry, candidate, stop);
                    if (rejected is not null) throw new InvalidDataException("native_managed_conflict_accepted_" + conflict);
                }
                using (var laterCaller = CancellationTokenSource.CreateLinkedTokenSource(stop))
                {
                    laterCaller.CancelAfter(TimeSpan.FromSeconds(20));
                    var laterResult = await SecurityExecutionEvidence.RunOwnedNativeProcessAsync(laterCommand,
                        TimeSpan.FromSeconds(25), laterCaller.Token, async (later, laterStop) =>
                        {
                            using var laterReady = CancellationTokenSource.CreateLinkedTokenSource(laterStop);
                            laterReady.CancelAfter(TimeSpan.FromSeconds(10));
                            string receipt = Path.Combine(root, "managed-claim-ready-" + later.Pid);
                            while (!File.Exists(receipt)) await Task.Delay(10, laterReady.Token);
                            await Assert.That(File.ReadAllText(receipt)).IsEqualTo("claim-held");
                            await Assert.That(later.Pid).IsNotEqualTo(context.Pid);
                            await Assert.That(later.Start > context.Start).IsTrue();
                            await Assert.That(Directory.EnumerateFileSystemEntries("/proc/" + later.Pid + "/fd")
                                .Any(path => new FileInfo(path).LinkTarget == Path.Combine(root, ".apphost-claim"))).IsTrue();
                            await Assert.That((await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(context,
                                temporary, root, selectedHint, entry, candidate, laterStop)) is not null).IsTrue();
                            var earlierTarget = await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(later,
                                temporary, root, selectedHint, entry, candidate, laterStop);
                            await Assert.That(earlierTarget is null).IsTrue();
                            await laterCaller.CancelAsync();
                        });
                    if (laterResult.ObserverFailures.Count != 0)
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(laterResult.ObserverFailures[0]).Throw();
                    await Assert.That(laterResult.Cancelled).IsTrue();
                    await Assert.That(laterResult.Succeeded).IsFalse();
                    await Assert.That(laterResult.CleanupSucceeded && laterResult.ObserverSettled).IsTrue();
                    await Assert.That((await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(context,
                        temporary, root, selectedHint, entry, candidate, stop)) is not null).IsTrue();
                }
                foreach (string variant in new[] { "wrong-pid", "wrong-start", "wrong-target-start", "outside-root", "wrong-entry", "wrong-candidate" })
                {
                    var selected = variant == "wrong-pid" ? selectedHint with { Pid = Environment.ProcessId }
                        : variant == "wrong-target-start" ? selectedHint with { Start = selectedHint.Start + 1 } : selectedHint;
                    var invocation = variant == "wrong-start" ? context with { Start = context.Start + 1 } : context;
                    var rejected = await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(invocation,
                        temporary, variant == "outside-root" ? temporary : root, selected,
                        variant == "wrong-entry" ? otherEntry : entry,
                        variant == "wrong-candidate" ? new string('0', candidate.Length) : candidate, stop);
                    await Assert.That(rejected is null).IsTrue();
                }
                File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
                try
                {
                    await Assert.That((await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(context,
                        temporary, root, selectedHint, entry, candidate, stop)) is null).IsTrue();
                }
                finally
                {
                    File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                string record = Path.Combine(root, $"managed-host-root-{context.Pid}-{context.Start}.json");
                string original = record + ".original";
                File.Move(record, original);
                try
                {
                    using (var replacement = new FileStream(record, new FileStreamOptions
                    {
                        Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read,
                        UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                    }))
                        await replacement.WriteAsync(File.ReadAllBytes(original), stop);
                    await Assert.That((await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(context,
                        temporary, root, selectedHint, entry, candidate, stop)) is null).IsTrue();
                }
                finally
                {
                    File.Delete(record);
                    File.Move(original, record);
                }
                await Assert.That((await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(context,
                    temporary, root, selectedHint, entry, candidate, stop)) is not null).IsTrue();
                await caller.CancelAsync();
            });
        if (result.ObserverFailures.Count != 0)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(result.ObserverFailures[0]).Throw();
        await Assert.That(result.Cancelled).IsTrue();
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.CleanupSucceeded).IsTrue();
        await Assert.That(result.ObserverSettled).IsTrue();
        await Assert.That(fixture.OwnedChildrenRemain()).IsFalse();
        string cleanupRoot = Path.Combine(temporary, "api-cleanup-custody");
        Directory.CreateDirectory(cleanupRoot);
        string retainedDiagnostic = Path.Combine(cleanupRoot, "observation.json");
        File.WriteAllText(retainedDiagnostic, "retained-observation");
        byte[] originalDaemonIdentity = Encoding.UTF8.GetBytes("original-daemon");
        bool resourceCleanupComplete = true;
        try
        {
            SecurityExecutionEvidence.RequireSameApiDaemonIdentity(originalDaemonIdentity, Encoding.UTF8.GetBytes("replacement-daemon"));
        }
        catch (Exception)
        { resourceCleanupComplete = false; }
        await Assert.That(SecurityExecutionEvidence.TryRemoveOwnedChildTemporary(cleanupRoot, result, resourceCleanupComplete)).IsFalse();
        await Assert.That(File.ReadAllText(retainedDiagnostic)).IsEqualTo("retained-observation");
        SecurityExecutionEvidence.RequireSameApiDaemonIdentity(originalDaemonIdentity, originalDaemonIdentity.ToArray());
        await Assert.That(SecurityExecutionEvidence.TryRemoveOwnedChildTemporary(cleanupRoot, result, true)).IsTrue();
        await Assert.That(Directory.Exists(cleanupRoot)).IsFalse();
        await Assert.That((await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(identity!,
            temporary, root, hint!, entry, candidate, CancellationToken.None)) is null).IsTrue();

        using var launcherFixture = new SecurityExecutionEvidenceProcessTests.ProcessFixture();
        using var launcherCaller = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        ProcessStartInfo launcherCommand = await launcherFixture.CommandAsync("managed-launcher");
        ProcessStartInfo unrelatedCommand = await launcherFixture.CommandAsync("managed-claim");
        string launcherTemporary = launcherCommand.WorkingDirectory;
        File.SetUnixFileMode(launcherTemporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string launcherOwner = Path.GetFileName(launcherTemporary)[^32..];
        string launcherRoot = Path.Combine(launcherTemporary, "islamu-assurance", launcherOwner);
        unrelatedCommand.ArgumentList[unrelatedCommand.ArgumentList.IndexOf("--results-directory") + 1] =
            Path.Combine(launcherTemporary, "unrelated-results");
        var launcherResult = await SecurityExecutionEvidence.RunOwnedNativeProcessAsync(launcherCommand,
            TimeSpan.FromSeconds(45), launcherCaller.Token, async (launcher, launcherStop) =>
            {
                if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
                using var launcherReady = CancellationTokenSource.CreateLinkedTokenSource(launcherStop);
                launcherReady.CancelAfter(TimeSpan.FromSeconds(10));
                string launcherLead = Path.Combine(launcherRoot, "managed-control-lead.json");
                while (!File.Exists(launcherLead)) await Task.Delay(10, launcherReady.Token);
                await Assert.That(SecurityExecutionEvidence.TryReadManagedLaunchHint(File.ReadAllBytes(launcherLead),
                    launcherOwner, out var parsedLauncher, out _)).IsTrue();
                var launcherHint = parsedLauncher ?? throw new InvalidDataException("native_launcher_hint_missing");
                await Assert.That(launcherHint.Pid == launcher.Pid && launcherHint.Start == launcher.Start).IsTrue();
                using var unrelatedCaller = CancellationTokenSource.CreateLinkedTokenSource(launcherStop);
                unrelatedCaller.CancelAfter(TimeSpan.FromSeconds(25));
                var unrelatedResult = await SecurityExecutionEvidence.RunOwnedNativeProcessAsync(unrelatedCommand,
                    TimeSpan.FromSeconds(30), unrelatedCaller.Token, async (unrelated, unrelatedStop) =>
                    {
                        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
                        using var hostReady = CancellationTokenSource.CreateLinkedTokenSource(unrelatedStop);
                        hostReady.CancelAfter(TimeSpan.FromSeconds(10));
                        string unrelatedReceipt = Path.Combine(launcherRoot, "managed-claim-ready-" + unrelated.Pid);
                        while (!File.Exists(unrelatedReceipt)) await Task.Delay(10, hostReady.Token);
                        await Assert.That(File.ReadAllText(unrelatedReceipt)).IsEqualTo("claim-held");
                        await Assert.That(unrelated.Start > launcher.Start && unrelated.Pid != launcher.Pid).IsTrue();
                        string[] siblingReceipts;
                        do
                        {
                            siblingReceipts = Directory.GetFiles(launcherRoot, "managed-claim-ready-*")
                                .Where(path => path != unrelatedReceipt).ToArray();
                            if (siblingReceipts.Length == 0) await Task.Delay(10, hostReady.Token);
                        } while (siblingReceipts.Length == 0);
                        await Assert.That(siblingReceipts.Length).IsEqualTo(1);
                        await Assert.That(File.ReadAllText(siblingReceipts[0])).IsEqualTo("claim-held");
                        int siblingPid = int.Parse(Path.GetFileName(siblingReceipts[0])["managed-claim-ready-".Length..],
                            System.Globalization.CultureInfo.InvariantCulture);
                        string siblingStat = File.ReadAllText("/proc/" + siblingPid + "/stat");
                        string[] siblingFields = siblingStat[(siblingStat.LastIndexOf(')') + 2)..].Split(' ');
                        ulong siblingStart = ulong.Parse(siblingFields[19], System.Globalization.CultureInfo.InvariantCulture);
                        await Assert.That(int.Parse(siblingFields[1], System.Globalization.CultureInfo.InvariantCulture))
                            .IsEqualTo(launcher.Pid);
                        await Assert.That(int.Parse(siblingFields[2], System.Globalization.CultureInfo.InvariantCulture))
                            .IsEqualTo(launcher.Group);
                        await Assert.That(int.Parse(siblingFields[3], System.Globalization.CultureInfo.InvariantCulture))
                            .IsEqualTo(launcher.Session);
                        await Assert.That(siblingPid != unrelated.Pid && siblingStart >= launcher.Start).IsTrue();
                        string launcherUser = File.ReadLines("/proc/" + launcher.Pid + "/status")
                            .Single(line => line.StartsWith("Uid:", StringComparison.Ordinal));
                        await Assert.That(File.ReadLines("/proc/" + siblingPid + "/status")
                            .Single(line => line.StartsWith("Uid:", StringComparison.Ordinal))).IsEqualTo(launcherUser);
                        await Assert.That(Directory.EnumerateFileSystemEntries("/proc/" + siblingPid + "/fd")
                            .Any(path => new FileInfo(path).LinkTarget == Path.Combine(launcherRoot, ".apphost-claim"))).IsTrue();
                        // This private control signal coordinates real process order; it is not an evidence carrier.
                        using (var signal = new FileStream(Path.Combine(launcherRoot, "managed-control-start-host"),
                            new FileStreamOptions
                            {
                                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read,
                                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                            }))
                            await signal.FlushAsync(unrelatedStop);
                        string[] hostRecords;
                        do
                        {
                            hostRecords = Directory.GetFiles(launcherRoot, "managed-host-root-*.json");
                            if (hostRecords.Length == 0) await Task.Delay(10, hostReady.Token);
                        } while (hostRecords.Length == 0);
                        await Assert.That(hostRecords.Length).IsEqualTo(1);
                        using JsonDocument hostRecord = JsonDocument.Parse(File.ReadAllBytes(hostRecords[0]));
                        int hostPid = hostRecord.RootElement.GetProperty("Pid").GetInt32();
                        ulong hostStart = hostRecord.RootElement.GetProperty("Start").GetUInt64();
                        string hostStat = File.ReadAllText("/proc/" + hostPid + "/stat");
                        string[] hostFields = hostStat[(hostStat.LastIndexOf(')') + 2)..].Split(' ');
                        await Assert.That(int.Parse(hostFields[1], System.Globalization.CultureInfo.InvariantCulture))
                            .IsEqualTo(launcher.Pid);
                        await Assert.That(ulong.Parse(hostFields[19], System.Globalization.CultureInfo.InvariantCulture))
                            .IsEqualTo(hostStart);
                        await Assert.That(hostStart >= unrelated.Start && hostPid != launcher.Pid && hostPid != unrelated.Pid).IsTrue();
                        await Assert.That(hostPid != siblingPid && hostStart >= siblingStart).IsTrue();
                        await Assert.That(File.ReadLines("/proc/" + hostPid + "/status")
                            .Single(line => line.StartsWith("Uid:", StringComparison.Ordinal))).IsEqualTo(launcherUser);
                        await Assert.That(int.Parse(hostFields[2], System.Globalization.CultureInfo.InvariantCulture))
                            .IsEqualTo(launcher.Group);
                        await Assert.That(int.Parse(hostFields[3], System.Globalization.CultureInfo.InvariantCulture))
                            .IsEqualTo(launcher.Session);
                        await Assert.That(File.Exists(Path.Combine(launcherRoot,
                            $"managed-host-root-{launcher.Pid}-{launcher.Start}.json"))).IsFalse();
                        var hostHint = launcherHint with { Pid = hostPid, Start = hostStart };
                        var hostedBaseline = await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(launcher,
                            launcherTemporary, launcherRoot, hostHint, entry, candidate, unrelatedStop);
                        await Assert.That(hostedBaseline is not null).IsTrue();
                        var joined = await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(launcher,
                            launcherTemporary, launcherRoot, launcherHint, entry, candidate, unrelatedStop);
                        if (joined is null) throw new InvalidDataException("native_launcher_host_ancestry_not_joined");
                        using JsonDocument joinedManifest = JsonDocument.Parse(joined.PrivateManifest);
                        await Assert.That(joinedManifest.RootElement.GetProperty("process").GetProperty("pid").GetInt32())
                            .IsEqualTo(hostPid);
                        await Assert.That(joinedManifest.RootElement.GetProperty("process").GetProperty("start").GetUInt64())
                            .IsEqualTo(hostStart);
                        await Assert.That(joinedManifest.RootElement.GetProperty("process").GetProperty("entry").GetString())
                            .IsEqualTo(entry);
                        using JsonDocument baselineManifest = JsonDocument.Parse(hostedBaseline!.PrivateManifest);
                        await Assert.That(joinedManifest.RootElement.GetProperty("hostRoot").GetRawText())
                            .IsEqualTo(baselineManifest.RootElement.GetProperty("hostRoot").GetRawText());
                        JsonElement sdkLead = joinedManifest.RootElement.GetProperty("sdkLead");
                        await Assert.That(sdkLead.GetProperty("pid").GetInt32()).IsEqualTo(launcher.Pid);
                        await Assert.That(sdkLead.GetProperty("start").GetUInt64()).IsEqualTo(launcher.Start);
                        string launcherStat = File.ReadAllText("/proc/" + launcher.Pid + "/stat");
                        string[] launcherFields = launcherStat[(launcherStat.LastIndexOf(')') + 2)..].Split(' ');
                        int launcherParent = int.Parse(launcherFields[1], System.Globalization.CultureInfo.InvariantCulture);
                        uint user = baselineManifest.RootElement.GetProperty("process").GetProperty("user").GetUInt32();
                        await Assert.That(sdkLead.GetProperty("hostAncestry").EnumerateArray().Select(node =>
                            (node.GetProperty("Pid").GetInt32(), node.GetProperty("Start").GetUInt64(),
                                node.GetProperty("Parent").GetInt32(), node.GetProperty("User").GetUInt32(),
                                node.GetProperty("Group").GetInt32(), node.GetProperty("Session").GetInt32()))
                            .SequenceEqual(new[]
                            {
                                (hostPid, hostStart, launcher.Pid, user, launcher.Group, launcher.Session),
                                (launcher.Pid, launcher.Start, launcherParent, user, launcher.Group, launcher.Session)
                            })).IsTrue();
                        foreach (var invalidLead in new[]
                        {
                            launcherHint with { Start = launcherHint.Start + 1 },
                            launcherHint with { Pid = siblingPid, Start = siblingStart },
                            launcherHint with { Pid = unrelated.Pid, Start = unrelated.Start }
                        })
                            await Assert.That((await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(launcher,
                                launcherTemporary, launcherRoot, invalidLead, entry, candidate, unrelatedStop)) is null).IsTrue();
                        using (var signal = new FileStream(Path.Combine(launcherRoot, "managed-control-start-second-host"),
                            new FileStreamOptions
                            {
                                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read,
                                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                            }))
                            await signal.FlushAsync(unrelatedStop);
                        using var secondReady = CancellationTokenSource.CreateLinkedTokenSource(unrelatedStop);
                        secondReady.CancelAfter(TimeSpan.FromSeconds(10));
                        string[] secondRecords;
                        do
                        {
                            secondRecords = Directory.GetFiles(launcherRoot, "managed-host-root-*.json")
                                .Where(path => path != hostRecords[0]).ToArray();
                            if (secondRecords.Length == 0) await Task.Delay(10, secondReady.Token);
                        } while (secondRecords.Length == 0);
                        await Assert.That(secondRecords.Length).IsEqualTo(1);
                        using JsonDocument secondRecord = JsonDocument.Parse(File.ReadAllBytes(secondRecords[0]));
                        int secondPid = secondRecord.RootElement.GetProperty("Pid").GetInt32();
                        ulong secondStart = secondRecord.RootElement.GetProperty("Start").GetUInt64();
                        await Assert.That(secondPid != hostPid && secondStart >= hostStart).IsTrue();
                        string secondStat = File.ReadAllText("/proc/" + secondPid + "/stat");
                        string[] secondFields = secondStat[(secondStat.LastIndexOf(')') + 2)..].Split(' ');
                        await Assert.That(int.Parse(secondFields[1], System.Globalization.CultureInfo.InvariantCulture))
                            .IsEqualTo(launcher.Pid);
                        await Assert.That(int.Parse(secondFields[2], System.Globalization.CultureInfo.InvariantCulture))
                            .IsEqualTo(launcher.Group);
                        await Assert.That(int.Parse(secondFields[3], System.Globalization.CultureInfo.InvariantCulture))
                            .IsEqualTo(launcher.Session);
                        var secondHint = launcherHint with { Pid = secondPid, Start = secondStart };
                        await Assert.That((await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(launcher,
                            launcherTemporary, launcherRoot, secondHint, entry, candidate, unrelatedStop)) is not null).IsTrue();
                        await Assert.That((await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(launcher,
                            launcherTemporary, launcherRoot, hostHint, entry, candidate, unrelatedStop)) is not null).IsTrue();
                        await Assert.That((await SecurityExecutionEvidence.CaptureLiveManagedInputsAsync(launcher,
                            launcherTemporary, launcherRoot, launcherHint, entry, candidate, unrelatedStop)) is null).IsTrue();
                        await unrelatedCaller.CancelAsync();
                    });
                if (unrelatedResult.ObserverFailures.Count != 0)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(unrelatedResult.ObserverFailures[0]).Throw();
                await Assert.That(unrelatedResult.Cancelled && !unrelatedResult.Succeeded
                    && unrelatedResult.CleanupSucceeded && unrelatedResult.ObserverSettled).IsTrue();
                await launcherCaller.CancelAsync();
            });
        if (launcherResult.ObserverFailures.Count != 0)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(launcherResult.ObserverFailures[0]).Throw();
        await Assert.That(launcherResult.Cancelled && !launcherResult.Succeeded
            && launcherResult.CleanupSucceeded && launcherResult.ObserverSettled).IsTrue();
        await Assert.That(launcherFixture.OwnedChildrenRemain()).IsFalse();
    }

    [Test]
    public async Task PrivateDeclarationRequiresExactOwnerResourceImageAndMountInventory()
    {
        const string owner = "4f9adeeb61e4486599a1fc15b6d31d27";
        string source = "islamu-test-" + owner + "-data";
        JsonObject Model() => new()
        {
            ["Schema"] = 1, ["RunId"] = owner,
            ["Projects"] = new JsonArray(new JsonObject
            {
                ["Name"] = "event-migrationservice",
                ["ProjectPath"] = "/candidate/src/Event.MigrationService/Event.MigrationService.csproj"
            }),
            ["Containers"] = new JsonArray(new JsonObject
            {
                ["Name"] = "native-model-control", ["Image"] = "registry.example.test/native/model:control",
                ["PullPolicy"] = "Never", ["Mounts"] = new JsonArray(new JsonObject
                {
                    ["Type"] = "Volume", ["Source"] = source, ["Target"] = "/native-data", ["ReadOnly"] = false
                })
            })
        };
        bool admitted = SecurityExecutionEvidence.TryReadConfiguredContainerDeclarations(
            JsonSerializer.SerializeToUtf8Bytes(Model()), owner, out var baseline, out var baselineProjects);
        await Assert.That(admitted).IsTrue();
        await Assert.That(baselineProjects!.Single()).IsEqualTo(new SecurityExecutionEvidence.ConfiguredProjectDeclaration(
            "event-migrationservice", "/candidate/src/Event.MigrationService/Event.MigrationService.csproj"));
        await Assert.That(baseline!.Count).IsEqualTo(1);
        await Assert.That(baseline[0].Name).IsEqualTo("native-model-control");
        await Assert.That(baseline[0].Image).IsEqualTo("registry.example.test/native/model:control");
        await Assert.That(baseline[0].Mounts.Single()).IsEqualTo(
            new SecurityExecutionEvidence.ConfiguredMountDeclaration("Volume", source, "/native-data", false));

        JsonObject sharedProject = Model();
        var peerProject = sharedProject["Projects"]![0]!.DeepClone().AsObject();
        peerProject["Name"] = "explore-blazor-peer";
        sharedProject["Projects"]!.AsArray().Add(peerProject);
        await Assert.That(SecurityExecutionEvidence.TryReadConfiguredContainerDeclarations(
            JsonSerializer.SerializeToUtf8Bytes(sharedProject), owner, out _, out var peerProjects)).IsTrue();
        await Assert.That(peerProjects!.ToHashSet().SetEquals([
            new SecurityExecutionEvidence.ConfiguredProjectDeclaration("event-migrationservice",
                "/candidate/src/Event.MigrationService/Event.MigrationService.csproj"),
            new SecurityExecutionEvidence.ConfiguredProjectDeclaration("explore-blazor-peer",
                "/candidate/src/Event.MigrationService/Event.MigrationService.csproj")])).IsTrue();
        foreach (string invalidProject in new[] { "missing", "unknown-field", "relative-path", "duplicate-name",
            "container-name", "too-many-projects" })
        {
            JsonObject malformed = Model();
            JsonArray projects = malformed["Projects"]!.AsArray();
            JsonObject project = projects[0]!.AsObject();
            switch (invalidProject)
            {
                case "missing": malformed.Remove("Projects"); break;
                case "unknown-field": project["TargetPath"] = "/guessed/entry.dll"; break;
                case "relative-path": project["ProjectPath"] = "src/project.csproj"; break;
                case "duplicate-name": projects.Add(project.DeepClone()); break;
                case "container-name": project["Name"] = "native-model-control"; break;
                case "too-many-projects":
                    for (int index = 1; index <= 64; index++)
                    {
                        JsonObject extra = project.DeepClone().AsObject();
                        extra["Name"] = "project-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        projects.Add(extra);
                    }
                    break;
            }
            await Assert.That(SecurityExecutionEvidence.TryReadConfiguredContainerDeclarations(
                JsonSerializer.SerializeToUtf8Bytes(malformed), owner, out _)).IsFalse();
        }

        foreach (string variant in new[] { "wrong-owner", "unknown-field", "pull-policy", "duplicate-resource",
            "duplicate-target", "foreign-volume", "writable-bind", "relative-target", "build-backed",
            "too-many-containers", "too-many-mounts" })
        {
            JsonObject model = Model();
            JsonObject container = model["Containers"]![0]!.AsObject();
            JsonObject mount = container["Mounts"]![0]!.AsObject();
            switch (variant)
            {
                case "wrong-owner": model["RunId"] = new string('a', 32); break;
                case "unknown-field": model["Succeeded"] = true; break;
                case "pull-policy": container["PullPolicy"] = "Always"; break;
                case "duplicate-resource": model["Containers"]!.AsArray().Add(container.DeepClone()); break;
                case "duplicate-target": container["Mounts"]!.AsArray().Add(mount.DeepClone()); break;
                case "foreign-volume": mount["Source"] = "foreign-volume"; break;
                case "writable-bind": mount["Type"] = "BindMount"; mount["Source"] = "/native-source"; break;
                case "relative-target": mount["Target"] = "native-data"; break;
                case "build-backed": container["DockerfileBuild"] = true; break;
                case "too-many-containers":
                    for (int index = 1; index <= 64; index++)
                    {
                        var additional = container.DeepClone().AsObject();
                        additional["Name"] = "native-control-" + index;
                        model["Containers"]!.AsArray().Add(additional);
                    }
                    break;
                case "too-many-mounts":
                    for (int index = 1; index <= 32; index++)
                    {
                        var additional = mount.DeepClone().AsObject();
                        additional["Target"] = "/native-data-" + index;
                        container["Mounts"]!.AsArray().Add(additional);
                    }
                    break;
            }
            bool accepted = SecurityExecutionEvidence.TryReadConfiguredContainerDeclarations(
                JsonSerializer.SerializeToUtf8Bytes(model), owner, out var rejected);
            await Assert.That(accepted).IsFalse();
            await Assert.That(rejected is null).IsTrue();
        }
        JsonObject empty = Model();
        empty["Containers"] = new JsonArray();
        empty["Projects"] = new JsonArray();
        await Assert.That(SecurityExecutionEvidence.TryReadConfiguredContainerDeclarations(
            JsonSerializer.SerializeToUtf8Bytes(empty), owner, out var zero, out var zeroProjects)).IsTrue();
        await Assert.That(zero!.Count).IsEqualTo(0);
        await Assert.That(zeroProjects!.Count).IsEqualTo(0);
        JsonObject bound = Model();
        JsonObject boundMount = bound["Containers"]![0]!["Mounts"]![0]!.AsObject();
        boundMount["Type"] = "BindMount";
        boundMount["Source"] = "/native-readonly-source";
        boundMount["ReadOnly"] = true;
        await Assert.That(SecurityExecutionEvidence.TryReadConfiguredContainerDeclarations(
            JsonSerializer.SerializeToUtf8Bytes(bound), owner, out var readonlyBind)).IsTrue();
        await Assert.That(readonlyBind![0].Mounts.Single()).IsEqualTo(
            new SecurityExecutionEvidence.ConfiguredMountDeclaration("BindMount", "/native-readonly-source", "/native-data", true));
        string containerId = new('3', 64), imageId = "sha256:" + new string('7', 64);
        string mountpoint = "/native-volumes/" + source + "/data";
        var expectedModel = baseline ?? throw new InvalidDataException("native_control_model_missing");
        var expected = new SecurityExecutionEvidence.ConfiguredResourceInputs(owner, expectedModel,
            new Dictionary<string, string>(StringComparer.Ordinal) { [expectedModel[0].Image] = imageId },
            new SecurityExecutionEvidence.InitialNativeResources(new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal) { imageId }),
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal) { [source] = mountpoint });
        JsonObject RuntimeContainer() => new()
        {
            ["Id"] = containerId, ["ImageID"] = imageId, ["Image"] = expectedModel[0].Image, ["State"] = "running",
            ["Labels"] = new JsonObject { ["islamu.assurance.owner"] = owner, ["islamu.assurance.resource"] = "native-model-control" },
            ["Mounts"] = new JsonArray(new JsonObject
            {
                ["Type"] = "volume", ["Name"] = source, ["Source"] = mountpoint,
                ["Destination"] = "/native-data", ["RW"] = true
            })
        };
        byte[] RuntimeBytes(JsonObject container) => JsonSerializer.SerializeToUtf8Bytes(new JsonArray(container));
        await Assert.That(SecurityExecutionEvidence.TryReadConfiguredOwnedResources(RuntimeBytes(RuntimeContainer()),
            expected, out var observed)).IsTrue();
        var exactObserved = observed ?? throw new InvalidDataException("native_control_resource_missing");
        await Assert.That(exactObserved.Single().Container).IsEqualTo(
            new SecurityExecutionEvidence.ObservedOwnedResource(containerId, imageId, "native-model-control", true));
        await Assert.That(exactObserved.Single().Mounts.Single()).IsEqualTo(
            new SecurityExecutionEvidence.ObservedContainerMount("volume", source, mountpoint, "/native-data", true));
        foreach (string invalid in new[] { "initial-container", "initial-volume", "image-not-in-initial-cache", "unknown-resource", "wrong-image-id",
            "wrong-image-ref", "wrong-volume-name", "wrong-source-mountpoint", "wrong-target", "missing-rw", "duplicate-target" })
        {
            JsonObject runtime = RuntimeContainer(), mount = runtime["Mounts"]![0]!.AsObject();
            var selected = expected;
            switch (invalid)
            {
                case "initial-container": selected = expected with { Initial = expected.Initial with { ContainerIds = new HashSet<string> { containerId } } }; break;
                case "initial-volume": selected = expected with { Initial = expected.Initial with { VolumeNames = new HashSet<string> { source } } }; break;
                case "image-not-in-initial-cache": selected = expected with { Initial = expected.Initial with { ImageIds = new HashSet<string>() } }; break;
                case "unknown-resource": runtime["Labels"]!["islamu.assurance.resource"] = "unmodeled-resource"; break;
                case "wrong-image-id": runtime["ImageID"] = "sha256:" + new string('8', 64); break;
                case "wrong-image-ref":
                    const string foreignReference = "registry.example.test/native/foreign:control";
                    runtime["Image"] = foreignReference;
                    selected = expected with { Images = new Dictionary<string, string>
                        { [expectedModel[0].Image] = imageId, [foreignReference] = imageId } };
                    break;
                case "wrong-volume-name":
                    mount["Name"] = source + "-foreign";
                    mount["Source"] = "/foreign-volume-data";
                    selected = expected with { VolumeMountpoints = new Dictionary<string, string>
                        { [source] = mountpoint, [source + "-foreign"] = "/foreign-volume-data" } };
                    break;
                case "wrong-source-mountpoint": mount["Source"] = "/foreign-volume-data"; break;
                case "wrong-target": mount["Destination"] = "/foreign-target"; break;
                case "missing-rw": mount.Remove("RW"); break;
                case "duplicate-target": runtime["Mounts"]!.AsArray().Add(mount.DeepClone()); break;
            }
            if (SecurityExecutionEvidence.TryReadConfiguredOwnedResources(RuntimeBytes(runtime), selected, out _))
                throw new InvalidDataException("native_resource_conflict_accepted_" + invalid);
        }
        JsonObject foreignOwner = RuntimeContainer();
        foreignOwner["Labels"]!["islamu.assurance.owner"] = new string('a', 32);
        await Assert.That(SecurityExecutionEvidence.TryReadConfiguredOwnedResources(
            RuntimeBytes(foreignOwner.DeepClone().AsObject()), expected, out _)).IsFalse();
        foreignOwner["Mounts"] = new JsonArray();
        await Assert.That(SecurityExecutionEvidence.TryReadConfiguredOwnedResources(RuntimeBytes(foreignOwner), expected,
            out var unselected)).IsTrue();
        await Assert.That(unselected!.Count).IsEqualTo(0);
        string anonymous = new('9', 64), anonymousMountpoint = "/native-volumes/" + anonymous + "/data";
        JsonObject withImplicit = RuntimeContainer();
        withImplicit["Mounts"]!.AsArray().Add(new JsonObject
        {
            ["Type"] = "volume", ["Name"] = anonymous, ["Source"] = anonymousMountpoint,
            ["Destination"] = "/image-declared-data", ["RW"] = true
        });
        var implicitExpected = expected with
        {
            ImageVolumeTargets = new Dictionary<string, IReadOnlySet<string>>
                { [imageId] = new HashSet<string> { "/image-declared-data" } },
            VolumeMountpoints = new Dictionary<string, string>
                { [source] = mountpoint, [anonymous] = anonymousMountpoint }
        };
        await Assert.That(SecurityExecutionEvidence.TryReadConfiguredOwnedResources(RuntimeBytes(withImplicit.DeepClone().AsObject()),
            implicitExpected, out var implicitObserved)).IsTrue();
        var exactImplicit = implicitObserved ?? throw new InvalidDataException("native_control_implicit_resource_missing");
        await Assert.That(exactImplicit.Single().Mounts.Count).IsEqualTo(2);
        await Assert.That(exactImplicit.Single().Mounts.ToHashSet().SetEquals(
        [
            new SecurityExecutionEvidence.ObservedContainerMount("volume", source, mountpoint, "/native-data", true),
            new SecurityExecutionEvidence.ObservedContainerMount("volume", anonymous, anonymousMountpoint, "/image-declared-data", true)
        ])).IsTrue();
        await Assert.That(SecurityExecutionEvidence.TryReadConfiguredOwnedResources(RuntimeBytes(withImplicit.DeepClone().AsObject()),
            implicitExpected with { ImageVolumeTargets = new Dictionary<string, IReadOnlySet<string>>() }, out _)).IsFalse();
        await Assert.That(SecurityExecutionEvidence.TryReadConfiguredOwnedResources(RuntimeBytes(withImplicit.DeepClone().AsObject()),
            implicitExpected with { ImageVolumeTargets = new Dictionary<string, IReadOnlySet<string>>
                { ["sha256:" + new string('8', 64)] = new HashSet<string> { "/image-declared-data" } } }, out _)).IsFalse();
        await Assert.That(SecurityExecutionEvidence.TryReadConfiguredOwnedResources(RuntimeBytes(withImplicit.DeepClone().AsObject()),
            implicitExpected with { Initial = expected.Initial with { VolumeNames = new HashSet<string> { anonymous } } }, out _)).IsFalse();
        JsonObject foreignConsumer = RuntimeContainer();
        foreignConsumer["Id"] = new string('4', 64);
        foreignConsumer["Labels"]!["islamu.assurance.owner"] = new string('b', 32);
        await Assert.That(SecurityExecutionEvidence.TryReadConfiguredOwnedResources(
            JsonSerializer.SerializeToUtf8Bytes(new JsonArray(RuntimeContainer(), foreignConsumer)), expected, out _)).IsFalse();
        JsonObject readonlyRuntime = RuntimeContainer();
        JsonObject readonlyRuntimeMount = readonlyRuntime["Mounts"]![0]!.AsObject();
        readonlyRuntimeMount["Type"] = "bind";
        readonlyRuntimeMount.Remove("Name");
        readonlyRuntimeMount["Source"] = "/native-readonly-source";
        readonlyRuntimeMount["RW"] = false;
        var readonlyExpected = expected with { Containers = readonlyBind ?? throw new InvalidDataException("native_control_bind_missing"),
            VolumeMountpoints = new Dictionary<string, string>() };
        await Assert.That(SecurityExecutionEvidence.TryReadConfiguredOwnedResources(RuntimeBytes(readonlyRuntime.DeepClone().AsObject()),
            readonlyExpected, out var readonlyObserved)).IsTrue();
        await Assert.That(readonlyObserved!.Single().Mounts.Single()).IsEqualTo(
            new SecurityExecutionEvidence.ObservedContainerMount("bind", string.Empty, "/native-readonly-source", "/native-data", false));
        readonlyRuntimeMount["RW"] = true;
        await Assert.That(SecurityExecutionEvidence.TryReadConfiguredOwnedResources(RuntimeBytes(readonlyRuntime),
            readonlyExpected, out _)).IsFalse();
        foreach (var binding in new[]
        {
            (Reason: "exact", Source: mountpoint),
            (Reason: "descendant", Source: Path.Combine(mountpoint, "child")),
            (Reason: "ancestor", Source: Path.GetDirectoryName(mountpoint)!),
            (Reason: "root", Source: "/"),
            (Reason: "relative", Source: "native-data"),
            (Reason: "noncanonical", Source: mountpoint + "/../data"),
            (Reason: "trailing", Source: mountpoint + "/"),
            (Reason: "double-slash", Source: mountpoint + "//child")
        })
        {
            JsonObject boundConsumer = RuntimeContainer();
            boundConsumer["Id"] = new string('4', 64);
            boundConsumer["Labels"]!["islamu.assurance.owner"] = new string('b', 32);
            boundConsumer["Mounts"] = new JsonArray(new JsonObject
            {
                ["Type"] = "bind", ["Source"] = binding.Source, ["Destination"] = "/foreign-use", ["RW"] = true
            });
            if (SecurityExecutionEvidence.TryReadConfiguredOwnedResources(RuntimeBytes(boundConsumer), expected, out _))
                throw new InvalidDataException("native_volume_bind_consumer_accepted_" + binding.Reason);
        }
        JsonObject unrelatedBind = RuntimeContainer();
        unrelatedBind["Id"] = new string('4', 64);
        unrelatedBind["Labels"]!["islamu.assurance.owner"] = new string('b', 32);
        unrelatedBind["Mounts"] = new JsonArray(new JsonObject
        {
            ["Type"] = "bind", ["Source"] = mountpoint + "-foreign", ["Destination"] = "/foreign-use", ["RW"] = true
        });
        await Assert.That(SecurityExecutionEvidence.TryReadConfiguredOwnedResources(RuntimeBytes(unrelatedBind), expected,
            out var unrelatedBinding)).IsTrue();
        await Assert.That(unrelatedBinding!.Count).IsEqualTo(0);
        JsonObject retainedImplicitConsumer = RuntimeContainer();
        retainedImplicitConsumer["Id"] = new string('4', 64);
        retainedImplicitConsumer["Labels"]!["islamu.assurance.owner"] = new string('b', 32);
        retainedImplicitConsumer["Mounts"] = new JsonArray(withImplicit["Mounts"]![1]!.DeepClone());
        if (SecurityExecutionEvidence.TryReadConfiguredOwnedResources(RuntimeBytes(retainedImplicitConsumer), implicitExpected, out _))
            throw new InvalidDataException("native_retained_implicit_consumer_accepted");
        JsonObject retainedImplicitBind = RuntimeContainer();
        retainedImplicitBind["Id"] = new string('4', 64);
        retainedImplicitBind["Labels"]!["islamu.assurance.owner"] = new string('b', 32);
        retainedImplicitBind["Mounts"] = new JsonArray(new JsonObject
        {
            ["Type"] = "bind", ["Source"] = anonymousMountpoint, ["Destination"] = "/foreign-use", ["RW"] = false
        });
        if (SecurityExecutionEvidence.TryReadConfiguredOwnedResources(RuntimeBytes(retainedImplicitBind), implicitExpected, out _))
            throw new InvalidDataException("native_retained_implicit_bind_consumer_accepted");
        await Assert.That(SecurityExecutionEvidence.TryReadConfiguredOwnedResources(Encoding.UTF8.GetBytes("[]"), expected,
            out var pendingResources)).IsTrue();
        await Assert.That(pendingResources!.Count).IsEqualTo(0);
        await Assert.That(SecurityExecutionEvidence.TryReadConfiguredOwnedResources(Encoding.UTF8.GetBytes("[]"),
            expected with { Containers = zero ?? throw new InvalidDataException("native_control_empty_missing"),
                Images = new Dictionary<string, string>(), VolumeMountpoints = new Dictionary<string, string>() },
            out var emptyResources)).IsTrue();
        await Assert.That(emptyResources!.Count).IsEqualTo(0);

        string apiCreatedId = new string('a', 64), apiCreatedImageId = "sha256:" + new string('b', 64);
        const string apiCreatedReference = "registry.example.test/native/creator:control";
        const string apiAliasReference = "registry.example.test/native/creator:alias";
        var apiCreatedPair = new SecurityExecutionEvidence.NativeCreatedContainer(owner,
            Guid.CreateVersion7().ToString("N"), Environment.ProcessId, "keycloak", apiCreatedReference,
            apiCreatedId, new string('c', 64));
        var apiCreatedSnapshot = new SecurityExecutionEvidence.NativeContainerCreationSnapshot([apiCreatedPair],
            new HashSet<string>(StringComparer.Ordinal), []);
        var apiCreatedImages = new Dictionary<string, string>(StringComparer.Ordinal)
            { [apiCreatedReference] = apiCreatedImageId, [apiAliasReference] = apiCreatedImageId };
        var apiCreatedInitial = new SecurityExecutionEvidence.InitialNativeResources(
            new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal) { apiCreatedImageId, "sha256:" + new string('e', 64) });
        JsonObject ApiCreatedRow() => new()
        {
            ["Id"] = apiCreatedId, ["Image"] = apiCreatedReference, ["ImageID"] = apiCreatedImageId,
            ["State"] = "running", ["Mounts"] = new JsonArray(),
            ["Labels"] = new JsonObject { ["islamu.assurance.owner"] = owner, ["islamu.assurance.resource"] = "keycloak" }
        };
        byte[] ApiCreatedBytes(JsonObject row) => JsonSerializer.SerializeToUtf8Bytes(new JsonArray(row));
        if (!SecurityExecutionEvidence.TryReadApiCreatedResources(ApiCreatedBytes(ApiCreatedRow()), owner,
            apiCreatedSnapshot, apiCreatedImages, apiCreatedInitial, out var apiCreatedObserved))
            throw new InvalidDataException("native_api_creator_resource_join_missing");
        await Assert.That(apiCreatedObserved!.Single()).IsEqualTo(
            new SecurityExecutionEvidence.ObservedOwnedResource(apiCreatedId, apiCreatedImageId, "keycloak", true));
        JsonObject apiUnrelatedRow = ApiCreatedRow();
        apiUnrelatedRow["Id"] = new string('d', 64);
        apiUnrelatedRow["Labels"]!["islamu.assurance.owner"] = new string('d', 32);
        await Assert.That(SecurityExecutionEvidence.TryReadApiCreatedResources(ApiCreatedBytes(apiUnrelatedRow), owner,
            apiCreatedSnapshot, apiCreatedImages, apiCreatedInitial, out var apiUnrelatedObserved)).IsTrue();
        await Assert.That(apiUnrelatedObserved!.Count).IsEqualTo(0);
        foreach (string apiConflict in new[] { "same-image-alias", "owner", "resource", "image-id", "container-id",
            "missing-pair", "pending-pair", "pair-image", "pair-id", "initial-container", "uncached-image", "mount" })
        {
            JsonObject apiConflictingRow = ApiCreatedRow();
            var apiConflictingSnapshot = apiCreatedSnapshot;
            var apiConflictingInitial = apiCreatedInitial;
            switch (apiConflict)
            {
                case "same-image-alias": apiConflictingRow["Image"] = apiAliasReference; break;
                case "owner": apiConflictingRow["Labels"]!["islamu.assurance.owner"] = new string('d', 32); break;
                case "resource": apiConflictingRow["Labels"]!["islamu.assurance.resource"] = "cerbos"; break;
                case "image-id": apiConflictingRow["ImageID"] = "sha256:" + new string('e', 64); break;
                case "container-id": apiConflictingRow["Id"] = new string('f', 64); break;
                case "missing-pair": apiConflictingSnapshot = apiCreatedSnapshot with { Created = [] }; break;
                case "pending-pair": apiConflictingSnapshot = apiCreatedSnapshot with
                    { Created = [], PendingCreationIds = new HashSet<string>(StringComparer.Ordinal) { apiCreatedPair.CreationId } }; break;
                case "pair-image": apiConflictingSnapshot = apiCreatedSnapshot with
                    { Created = [apiCreatedPair with { ImageReference = apiAliasReference }] }; break;
                case "pair-id": apiConflictingSnapshot = apiCreatedSnapshot with
                    { Created = [apiCreatedPair with { ContainerId = new string('f', 64) }] }; break;
                case "initial-container": apiConflictingInitial = apiCreatedInitial with
                    { ContainerIds = new HashSet<string>(StringComparer.Ordinal) { apiCreatedId } }; break;
                case "uncached-image": apiConflictingInitial = apiCreatedInitial with
                    { ImageIds = new HashSet<string>(StringComparer.Ordinal) }; break;
                case "mount": apiConflictingRow["Mounts"] = new JsonArray(new JsonObject
                    { ["Type"] = "volume", ["Name"] = "unqualified-volume", ["Source"] = "/unqualified",
                        ["Destination"] = "/data", ["RW"] = true }); break;
            }
            if (SecurityExecutionEvidence.TryReadApiCreatedResources(ApiCreatedBytes(apiConflictingRow), owner,
                apiConflictingSnapshot, apiCreatedImages, apiConflictingInitial, out _))
                throw new InvalidDataException("native_api_creator_resource_conflict_accepted_" + apiConflict);
        }
        await Assert.That(SecurityExecutionEvidence.TryReadApiCreatedResources(Encoding.UTF8.GetBytes("[]"), owner,
            apiCreatedSnapshot, apiCreatedImages, apiCreatedInitial, out var apiCreatedGone)).IsTrue();
        await Assert.That(apiCreatedGone!.Count).IsEqualTo(0);
        await Assert.That(SecurityExecutionEvidence.TryReadApiCreatedResources(Encoding.UTF8.GetBytes("[]"), owner,
            apiCreatedSnapshot with { Created = [], PendingCreationIds = new HashSet<string>(StringComparer.Ordinal)
                { apiCreatedPair.CreationId } }, apiCreatedImages, apiCreatedInitial, out var apiCreationNotYetVisible)).IsTrue();
        await Assert.That(apiCreationNotYetVisible!.Count).IsEqualTo(0);
    }
}

public sealed class SecurityExecutionEvidenceNativeUidChildTests
{
    [Test]
    public async Task IndependentCompiledTestExecutesItsDiscoveredUidAndProducesOneFreshJoinedResult()
    {
        string root = Directory.CreateTempSubdirectory("native-uid-proof-").FullName;
        try
        {
            const string targetClass = "SecurityExecutionEvidenceOwnedCommandTests";
            const string targetMethod = "PublicCommandIdentifiesTheOwnerAndRejectsTheHistoricalUnownedCommand";
            string assembly = typeof(SecurityExecutionEvidenceNativeUidChildTests).Assembly.Location;
            var discovery = new ProcessStartInfo(ReleaseToolPaths.Dotnet)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true, WorkingDirectory = root
            };
            discovery.Environment["TMPDIR"] = root;
            foreach (string argument in new[] { assembly, "--list-tests", "json", "--treenode-filter",
                "/*/*/" + targetClass + "/" + targetMethod, "--no-ansi" })
                discovery.ArgumentList.Add(argument);
            var listed = await SecurityExecutionEvidence.RunOwnedNativeProcessAsync(discovery,
                TimeSpan.FromSeconds(15), CancellationToken.None);
            await Assert.That(listed.Succeeded).IsTrue();
            await Assert.That(listed.StandardError.Length).IsEqualTo(0);
            using JsonDocument document = JsonDocument.Parse(listed.StandardOutput);
            await Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32()).IsEqualTo(1);
            JsonElement nodes = document.RootElement.GetProperty("tests");
            await Assert.That(nodes.GetArrayLength()).IsEqualTo(1);
            JsonElement node = nodes[0];
            JsonElement type = node.GetProperty("type");
            await Assert.That(type.GetProperty("namespace").GetString()).IsEqualTo("ISLAMU.ReleaseEngineering.Tests");
            await Assert.That(type.GetProperty("typeName").GetString()).IsEqualTo(targetClass);
            await Assert.That(type.GetProperty("methodName").GetString()).IsEqualTo(targetMethod);
            string uid = node.GetProperty("uid").GetString()!;
            string invocation = Path.Combine(root, "invocation");
            Directory.CreateDirectory(invocation);
            string results = Path.Combine(invocation, "results");
            string reportPath = Path.Combine(results, "native-uid-child.trx");
            await Assert.That(File.Exists(reportPath)).IsFalse();
            var command = SecurityExecutionEvidence.NativeTestStartInfo(assembly, [uid], results,
                invocation, "native-uid-child.trx", 15);
            var executed = await SecurityExecutionEvidence.RunOwnedNativeProcessAsync(command,
                TimeSpan.FromSeconds(20), CancellationToken.None);
            await Assert.That(executed.Succeeded).IsTrue();
            await Assert.That(File.Exists(reportPath)).IsTrue();
            await Assert.That(Directory.EnumerateFiles(results, "*.trx", SearchOption.AllDirectories)
                .ToHashSet(StringComparer.Ordinal).SetEquals([reportPath])).IsTrue();
            await Assert.That(SecurityExecutionEvidence.TryReadOwnedChildReport(results, "native-uid-child.trx", out byte[] report)).IsTrue();
            var required = new SecurityControlRegister.RequiredTest("ISLAMU.ReleaseEngineering.Tests." + targetClass,
                targetMethod, node.GetProperty("displayName").GetString()!);
            bool joined = SecurityExecutionEvidence.TryCreateNativeProof(listed.StandardOutput, report,
                [required], exactSelection: true, out byte[]? proof, out string diagnostic);
            await Assert.That(joined).IsTrue();
            await Assert.That(diagnostic).IsEqualTo(string.Empty);
            using JsonDocument publicProof = JsonDocument.Parse(proof!);
            await Assert.That(publicProof.RootElement.GetProperty("counts").GetProperty("passed").GetInt32()).IsEqualTo(1);
            await Assert.That(publicProof.RootElement.GetProperty("nodes")[0].GetProperty("requiredTest").GetInt32()).IsEqualTo(0);
            await Assert.That(Encoding.UTF8.GetString(proof!)).DoesNotContain(uid);
            // The native child and its cleanup have genuinely completed before the local report failure.
            using (FileStream oversized = File.Open(reportPath, FileMode.Open, FileAccess.Write, FileShare.None))
                oversized.SetLength(16_777_217);
            await Assert.That(SecurityExecutionEvidence.TryReadOwnedChildReport(results, "native-uid-child.trx", out byte[] rejected)).IsFalse();
            await Assert.That(rejected.Length).IsEqualTo(0);
            await Assert.That(executed.CleanupSucceeded).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
        await Assert.That(Directory.Exists(root)).IsFalse();
    }
}

public sealed class SecurityExecutionEvidencePublicationTests
{
    [Test]
    [Arguments("valid")]
    [Arguments("readback-false")]
    [Arguments("readback-io-failure")]
    public async Task FailedPublicationReadbackCannotLeaveAnAdmissibleDesignatedPacket(string variant)
    {
        const string candidate = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        string root = Directory.CreateTempSubdirectory("public-proof-move-").FullName;
        try
        {
            string stage = Path.Combine(root, "synthetic-stage");
            string destination = Path.Combine(root, "public-output");
            SecurityExecutionEvidenceFixture.Write(stage, candidate);
            await Assert.That(SecurityExecutionEvidence.TryValidate(stage, candidate, out _, out _)).IsTrue();
            Exception? failure = null;
            try
            {
                SecurityExecutionEvidence.PublishFreshPublicDirectory(stage, destination, directory =>
                {
                    if (variant == "readback-io-failure")
                    {
                        using FileStream missing = File.OpenRead(Path.Combine(directory, "absent-readback-file"));
                        return missing.Length == 0;
                    }
                    return variant == "valid" && SecurityExecutionEvidence.TryValidate(directory, candidate, out _, out _);
                });
            }
            catch (Exception exception) { failure = exception; }
            if (variant == "valid")
            {
                await Assert.That(Directory.Exists(stage)).IsFalse();
                await Assert.That(Directory.Exists(destination)).IsTrue();
                await Assert.That(failure).IsNull();
                await Assert.That(SecurityExecutionEvidence.TryValidate(destination, candidate, out _, out _)).IsTrue();
            }
            else
            {
                await Assert.That(failure).IsNotNull();
                if (variant == "readback-io-failure") await Assert.That(failure is FileNotFoundException).IsTrue();
                await Assert.That(File.Exists(Path.Combine(destination, SecurityExecutionEvidence.PacketFileName))).IsFalse();
                await Assert.That(SecurityExecutionEvidence.TryValidate(destination, candidate, out _, out _)).IsFalse();
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

public sealed class SecurityExecutionEvidenceResourceSnapshotTests
{
    [Test]
    [Arguments("valid")]
    [Arguments("additional-owned-instance")]
    [Arguments("foreign-malformed-identity")]
    [Arguments("short-id")]
    [Arguments("uppercase-id")]
    [Arguments("duplicate-id")]
    [Arguments("unknown-resource")]
    [Arguments("unadmitted-image")]
    [Arguments("retargeted-image")]
    [Arguments("missing-owner")]
    [Arguments("missing-image-id")]
    [Arguments("unknown-state")]
    [Arguments("duplicate-json-property")]
    [Arguments("preexisting-id")]
    public async Task OnlyFullObservedOwnedIdentitiesBindToPrelaunchCachedImages(string variant)
    {
        string owner = new('a', 32);
        string image = "sha256:" + new string('b', 64);
        var admitted = new Dictionary<string, string>(StringComparer.Ordinal) { ["cached:test"] = image };
        var initialContainerIds = new HashSet<string>(StringComparer.Ordinal) { new string('f', 64) };
        JsonObject Resource(char id, string kind) => new()
        {
            ["Id"] = new string(id, 64), ["ImageID"] = image, ["Image"] = "cached:test", ["State"] = "running",
            ["Labels"] = new JsonObject { ["islamu.assurance.owner"] = owner, ["islamu.assurance.resource"] = kind }
        };
        var snapshot = new JsonArray(Resource('c', "keycloak"), Resource('d', "cerbos"));
        byte[] Bytes() => Encoding.UTF8.GetBytes(snapshot.ToJsonString());
        await Assert.That(SecurityExecutionEvidence.TryReadOwnedResourceSnapshot(Bytes(), owner, admitted, initialContainerIds,
            out var baseline, out _)).IsTrue();
        await Assert.That(baseline!.Count).IsEqualTo(2);
        await Assert.That(baseline.All(resource => resource.Running)).IsTrue();
        string ExpectedIdentities(bool additional) => new string('c', 64) + "|" + image + "|keycloak|True\n" +
            new string('d', 64) + "|" + image + "|cerbos|True" +
            (additional ? "\n" + new string('e', 64) + "|" + image + "|cerbos|True" : string.Empty);
        string ActualIdentities(IReadOnlyList<SecurityExecutionEvidence.ObservedOwnedResource> resources) =>
            string.Join('\n', resources.OrderBy(resource => resource.ContainerId, StringComparer.Ordinal)
                .Select(resource => resource.ContainerId + "|" + resource.ImageId + "|" + resource.Resource + "|" + resource.Running));
        await Assert.That(ActualIdentities(baseline)).IsEqualTo(ExpectedIdentities(false));

        switch (variant)
        {
            case "additional-owned-instance": snapshot.Add(Resource('e', "cerbos")); break;
            case "foreign-malformed-identity":
                JsonObject foreign = Resource('e', "cerbos");
                foreign["Id"] = "foreign-short";
                foreign["Labels"]!["islamu.assurance.owner"] = new string('f', 32);
                snapshot.Add(foreign);
                break;
            case "short-id": snapshot[0]!["Id"] = "short"; break;
            case "uppercase-id": snapshot[0]!["Id"] = new string('C', 64); break;
            case "duplicate-id": snapshot[1]!["Id"] = new string('c', 64); break;
            case "unknown-resource": snapshot[0]!["Labels"]!["islamu.assurance.resource"] = "other"; break;
            case "unadmitted-image": snapshot[0]!["ImageID"] = "sha256:" + new string('e', 64); break;
            case "retargeted-image": snapshot[0]!["Image"] = "unadmitted:test"; break;
            case "missing-owner": snapshot[0]!["Labels"]!.AsObject().Remove("islamu.assurance.owner"); break;
            case "missing-image-id": snapshot[0]!.AsObject().Remove("ImageID"); break;
            case "unknown-state": snapshot[0]!["State"] = "unknown"; break;
            case "preexisting-id": initialContainerIds.Add(new string('c', 64)); break;
        }
        byte[] bytes = variant == "duplicate-json-property"
            ? Encoding.UTF8.GetBytes(snapshot.ToJsonString().Replace("\"Id\":", "\"Id\":\"duplicate\",\"Id\":", StringComparison.Ordinal))
            : Bytes();
        bool accepted = SecurityExecutionEvidence.TryReadOwnedResourceSnapshot(bytes, owner, admitted, initialContainerIds,
            out var observed, out string diagnostic);
        bool valid = variant is "valid" or "additional-owned-instance" or "foreign-malformed-identity";
        await Assert.That(accepted).IsEqualTo(valid);
        if (valid)
        {
            await Assert.That(observed!.Count).IsEqualTo(variant == "additional-owned-instance" ? 3 : 2);
            await Assert.That(ActualIdentities(observed)).IsEqualTo(ExpectedIdentities(variant == "additional-owned-instance"));
            await Assert.That(diagnostic).IsEqualTo(string.Empty);
        }
        else
        {
            await Assert.That(observed is null).IsTrue();
            await Assert.That(diagnostic).IsEqualTo("owned_resource_snapshot_invalid");
        }
    }
}

public sealed class SecurityExecutionEvidenceApiContentTests
{
    [Test]
    [Arguments("valid")]
    [Arguments("stale-realm")]
    [Arguments("stale-policy")]
    [Arguments("missing-realm")]
    [Arguments("missing-policy")]
    [Arguments("extra-policy")]
    [Arguments("linked-policy")]
    public async Task CopiedPrerequisitesMustEqualTheExactCandidateContent(string variant)
    {
        using var source = new SecurityExecutionEvidenceCandidateTests.SourceFixture();
        string realm = Path.Combine(source.Root, "docker", "keycloak", "ISLAMU-realm.test.json");
        string policy = Path.Combine(source.Root, "cerbos", "policies", "policy.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(realm)!);
        Directory.CreateDirectory(Path.GetDirectoryName(policy)!);
        File.WriteAllBytes(realm, RandomNumberGenerator.GetBytes(32));
        File.WriteAllBytes(policy, RandomNumberGenerator.GetBytes(32));
        source.Git("add", "--", "docker", "cerbos");
        source.Git("-c", "user.name=Native Control", "-c", "user.email=native-control@invalid.example",
            "commit", "--quiet", "-m", "Native copied input control");
        string candidate = source.Git("rev-parse", "HEAD").Trim();
        File.AppendAllText(Path.Combine(source.Root, ".git", "info", "exclude"), "\n/output/\n");
        string output = Path.Combine(source.Root, "output");
        string copiedRealm = Path.Combine(output, "TestAssets", "ISLAMU-realm.test.json");
        string copiedPolicy = Path.Combine(output, "TestAssets", "cerbos", "policies", "policy.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(copiedPolicy)!);
        File.Copy(realm, copiedRealm);
        File.Copy(policy, copiedPolicy);

        await Assert.That(SecurityExecutionEvidence.TryCaptureApiPrerequisiteInputs(source.Root, candidate, output,
            out var baseline, out _)).IsTrue();
        await Assert.That(baseline!.FileCount).IsEqualTo(2);
        await Assert.That(baseline.PrivateManifestSha256)
            .IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(baseline.PrivateManifest)));
        JsonArray files = JsonNode.Parse(baseline.PrivateManifest)!["files"]!.AsArray();
        await Assert.That(files.Count).IsEqualTo(2);
        foreach (var expected in new[]
        {
            (Source: "docker/keycloak/ISLAMU-realm.test.json", Output: "TestAssets/ISLAMU-realm.test.json"),
            (Source: "cerbos/policies/policy.yaml", Output: "TestAssets/cerbos/policies/policy.yaml")
        })
        {
            JsonNode file = files.Single(item => item!["sourcePath"]!.GetValue<string>() == expected.Source)!;
            await Assert.That(file["outputPath"]!.GetValue<string>()).IsEqualTo(expected.Output);
            await Assert.That(file["gitBlobOid"]!.GetValue<string>())
                .IsEqualTo(source.Git("rev-parse", candidate + ":" + expected.Source).Trim());
            await Assert.That(file["sha256"]!.GetValue<string>()).IsEqualTo(Convert.ToHexStringLower(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(output, expected.Output)))));
            await Assert.That(file["bytes"]!.GetValue<long>()).IsEqualTo(32L);
        }

        switch (variant)
        {
            case "stale-realm":
            case "stale-policy":
                string changed = variant == "stale-realm" ? copiedRealm : copiedPolicy;
                DateTime timestamp = File.GetLastWriteTimeUtc(changed);
                byte[] bytes = File.ReadAllBytes(changed);
                bytes[0] ^= 1;
                File.WriteAllBytes(changed, bytes);
                File.SetLastWriteTimeUtc(changed, timestamp);
                break;
            case "missing-realm": File.Delete(copiedRealm); break;
            case "missing-policy": File.Delete(copiedPolicy); break;
            case "extra-policy":
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(copiedPolicy)!, "extra.yaml"), RandomNumberGenerator.GetBytes(32));
                break;
            case "linked-policy":
                File.Delete(copiedPolicy);
                File.CreateSymbolicLink(copiedPolicy, policy);
                break;
        }
        bool accepted = SecurityExecutionEvidence.TryCaptureApiPrerequisiteInputs(source.Root, candidate, output,
            out var captured, out string diagnostic);
        await Assert.That(accepted).IsEqualTo(variant == "valid");
        if (variant == "valid")
        {
            await Assert.That(captured!.PrivateManifestSha256).IsEqualTo(baseline.PrivateManifestSha256);
            await Assert.That(diagnostic).IsEqualTo(string.Empty);
        }
        else
        {
            await Assert.That(captured is null).IsTrue();
            await Assert.That(diagnostic).IsEqualTo("api_prerequisite_inputs_invalid");
        }
    }
}

public sealed class SecurityExecutionEvidenceExecutionTests
{
    [Test]
    public async Task NativeOpaqueUidJoinsByExactTupleWithoutBecomingTheTrxGuid()
    {
        using var fixture = new NativeFixture();
        byte[] discovery = Encoding.UTF8.GetBytes(fixture.Discovery.ToJsonString());
        byte[] report = fixture.ReportBytes();

        bool accepted = SecurityExecutionEvidence.TryCreateNativeProof(discovery, report,
            fixture.RequiredTests, exactSelection: true, out byte[]? proof, out string diagnostic);

        await Assert.That(accepted).IsTrue();
        await Assert.That(diagnostic).IsEqualTo(string.Empty);
        JsonNode retained = JsonNode.Parse(proof!)!;
        await Assert.That(retained["nodes"]!.AsArray().Count).IsEqualTo(2);
        await Assert.That(retained["privateDiscoverySha256"]!.GetValue<string>()).IsEqualTo(Digest(discovery));
        await Assert.That(retained["privateTrxSha256"]!.GetValue<string>()).IsEqualTo(Digest(report));
        await Assert.That(Encoding.UTF8.GetString(proof!)).DoesNotContain("opaque native uid");
    }

    [Test]
    [Arguments("uid")]
    [Arguments("location")]
    [Arguments("traits")]
    [Arguments("properties")]
    public async Task PrivateDiscoveryDataNeverBecomesPublicIdentity(string field)
    {
        using var fixture = new NativeFixture();
        string canary = "private-identity-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        JsonNode test = fixture.Discovery["tests"]![0]!;
        switch (field)
        {
            case "uid": test["uid"] = canary; break;
            case "location": test["location"] = new JsonObject { ["file"] = canary }; break;
            default: test[field] = new JsonObject { ["private-value"] = canary }; break;
        }

        bool accepted = SecurityExecutionEvidence.TryCreateNativeProof(
            Encoding.UTF8.GetBytes(fixture.Discovery.ToJsonString()), fixture.ReportBytes(),
            fixture.RequiredTests, exactSelection: true, out byte[]? proof, out _);

        await Assert.That(accepted).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(proof!)).DoesNotContain(canary);
    }

    [Test]
    public async Task ExtraApiIdentityIsPrivateWhileRegisteredWitnessReferencesRemainComplete()
    {
        using var fixture = new NativeFixture();
        string canary = "private-identity-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        fixture.AppendTest(new(canary + ".Class", canary + "Method", canary + "(argument)"), canary);

        bool accepted = SecurityExecutionEvidence.TryCreateNativeProof(
            Encoding.UTF8.GetBytes(fixture.Discovery.ToJsonString()), fixture.ReportBytes(),
            fixture.RequiredTests, exactSelection: false, out byte[]? proof, out _);

        await Assert.That(accepted).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(proof!)).DoesNotContain(canary);
        var nodes = JsonNode.Parse(proof!)!["nodes"]!.AsArray();
        await Assert.That(nodes.Count).IsEqualTo(3);
        await Assert.That(nodes.Count(node => node!["requiredTest"] is null)).IsEqualTo(1);
    }

    [Test]
    public async Task PublicProofValidatesClosedReferencesWithoutPretendingToReparsePrivateInputs()
    {
        using var fixture = new NativeFixture();
        bool produced = SecurityExecutionEvidence.TryCreateNativeProof(
            Encoding.UTF8.GetBytes(fixture.Discovery.ToJsonString()), fixture.ReportBytes(),
            fixture.RequiredTests, exactSelection: true, out byte[]? proof, out _);
        await Assert.That(produced).IsTrue();
        bool accepted = SecurityExecutionEvidence.TryValidateNativeProof(proof!, fixture.RequiredTests,
            exactSelection: true, out string diagnostic);
        await Assert.That(accepted).IsTrue();
        await Assert.That(diagnostic).IsEqualTo(string.Empty);
    }

    [Test]
    [Arguments("raw-uid")]
    [Arguments("raw-type")]
    [Arguments("raw-method")]
    [Arguments("raw-display")]
    [Arguments("duplicate-node")]
    [Arguments("missing-node")]
    [Arguments("unknown-required-reference")]
    [Arguments("reused-required-reference")]
    [Arguments("missing-required-reference")]
    [Arguments("raw-execution-identity")]
    [Arguments("failed-node")]
    [Arguments("skipped-count")]
    [Arguments("wrong-count")]
    [Arguments("invalid-private-digest")]
    [Arguments("unknown-field")]
    public async Task PublicProjectionRejectsDataBearingFieldsAndIncompleteReferenceGraphs(string variant)
    {
        using var fixture = new NativeFixture();
        bool produced = SecurityExecutionEvidence.TryCreateNativeProof(
            Encoding.UTF8.GetBytes(fixture.Discovery.ToJsonString()), fixture.ReportBytes(),
            fixture.RequiredTests, exactSelection: true, out byte[]? proof, out _);
        await Assert.That(produced).IsTrue();
        var projection = JsonNode.Parse(proof!)!.AsObject();
        var nodes = projection["nodes"]!.AsArray();
        string canary = "private-identity-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        switch (variant)
        {
            case "raw-uid": nodes[0]!["uid"] = canary; break;
            case "raw-type": nodes[0]!["className"] = canary; break;
            case "raw-method": nodes[0]!["methodName"] = canary; break;
            case "raw-display": nodes[0]!["displayName"] = canary; break;
            case "duplicate-node": nodes[1]!["node"] = nodes[0]!["node"]!.GetValue<int>(); break;
            case "missing-node": nodes.RemoveAt(1); break;
            case "unknown-required-reference": nodes[0]!["requiredTest"] = 999; break;
            case "reused-required-reference": nodes[1]!["requiredTest"] = nodes[0]!["requiredTest"]!.GetValue<int>(); break;
            case "missing-required-reference": nodes[0]!["requiredTest"] = null; break;
            case "raw-execution-identity": nodes[0]!["executionId"] = canary; break;
            case "failed-node": nodes[0]!["outcome"] = "failed"; break;
            case "skipped-count": projection["counts"]!["skipped"] = 1; break;
            case "wrong-count": projection["counts"]!["selected"] = 3; break;
            case "invalid-private-digest": projection["privateDiscoverySha256"] = canary; break;
            case "unknown-field": projection["unknown"] = canary; break;
        }

        bool accepted = SecurityExecutionEvidence.TryValidateNativeProof(
            Encoding.UTF8.GetBytes(projection.ToJsonString()), fixture.RequiredTests, exactSelection: true,
            out string diagnostic);
        await Assert.That(accepted).IsFalse();
        await Assert.That(diagnostic).IsNotEqualTo(string.Empty);
        await Assert.That(diagnostic).DoesNotContain(canary);
    }

    [Test]
    [Arguments("duplicate-json-key")]
    [Arguments("malformed-json")]
    [Arguments("malformed-trx")]
    [Arguments("empty-requirements")]
    public async Task MalformedPrivateInputsCannotProduceAnyPublicProof(string variant)
    {
        using var fixture = new NativeFixture();
        byte[] discovery = Encoding.UTF8.GetBytes(fixture.Discovery.ToJsonString());
        byte[] report = fixture.ReportBytes();
        IReadOnlyList<SecurityControlRegister.RequiredTest> requirements = fixture.RequiredTests;
        switch (variant)
        {
            case "duplicate-json-key": discovery = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"schemaVersion\":1,\"tests\":[]}"); break;
            case "malformed-json": discovery = Encoding.UTF8.GetBytes("{"); break;
            case "malformed-trx": report = Encoding.UTF8.GetBytes("<"); break;
            case "empty-requirements": requirements = []; break;
        }
        bool accepted = SecurityExecutionEvidence.TryCreateNativeProof(discovery, report, requirements,
            exactSelection: true, out byte[]? proof, out string diagnostic);
        await Assert.That(accepted).IsFalse();
        await Assert.That(proof is null).IsTrue();
        await Assert.That(diagnostic).IsNotEqualTo(string.Empty);
    }

    [Test]
    [Arguments("missing-type")]
    [Arguments("missing-uid")]
    [Arguments("empty-uid")]
    [Arguments("duplicate-uid")]
    [Arguments("duplicate-tuple")]
    [Arguments("missing-result")]
    [Arguments("extra-result")]
    [Arguments("case-changed-type")]
    [Arguments("case-changed-method")]
    [Arguments("case-changed-display")]
    [Arguments("whitespace-changed-display")]
    [Arguments("different-argument")]
    [Arguments("wrong-schema")]
    [Arguments("empty-discovery")]
    [Arguments("failed-result")]
    [Arguments("skipped-result")]
    [Arguments("missing-required-variant")]
    [Arguments("owned-extra-variant")]
    [Arguments("canary-type")]
    [Arguments("canary-method")]
    [Arguments("canary-display")]
    public async Task DiscoveryAndExecutionMustHaveOneOrdinalUnambiguousIdentityJoin(string variant)
    {
        using var fixture = new NativeFixture();
        JsonArray tests = fixture.Discovery["tests"]!.AsArray();
        JsonNode first = tests[0]!;
        string canary = "private-identity-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        switch (variant)
        {
            case "missing-type": first.AsObject().Remove("type"); break;
            case "missing-uid": first.AsObject().Remove("uid"); break;
            case "empty-uid": first["uid"] = ""; break;
            case "duplicate-uid": tests[1]!["uid"] = first["uid"]!.GetValue<string>(); break;
            case "duplicate-tuple":
                tests[1]!["type"] = first["type"]!.DeepClone();
                tests[1]!["displayName"] = first["displayName"]!.GetValue<string>();
                break;
            case "missing-result": tests.RemoveAt(1); break;
            case "extra-result":
                fixture.AppendTest(new("Synthetic.Extra", "Extra", "Extra"), "another opaque uid");
                tests.RemoveAt(tests.Count - 1);
                break;
            case "case-changed-type": first["type"]!["typeName"] = "ownedprofile"; break;
            case "case-changed-method": first["type"]!["methodName"] = "syntheticwitness"; break;
            case "case-changed-display": first["displayName"] = first["displayName"]!.GetValue<string>().ToUpperInvariant(); break;
            case "whitespace-changed-display": first["displayName"] = first["displayName"]!.GetValue<string>() + " "; break;
            case "different-argument": first["displayName"] = "SyntheticWitness(another argument)"; break;
            case "wrong-schema": fixture.Discovery["schemaVersion"] = 999; break;
            case "empty-discovery": fixture.Discovery["tests"] = new JsonArray(); break;
            case "failed-result": fixture.Results[0].SetAttributeValue("outcome", "Failed"); break;
            case "skipped-result": fixture.Results[0].SetAttributeValue("outcome", "NotExecuted"); break;
            case "missing-required-variant":
                fixture.RemoveLastTest();
                break;
            case "owned-extra-variant": fixture.AppendTest(new("Synthetic.Extra", "Extra", "Extra"), "another opaque uid"); break;
            case "canary-type": first["type"]!["typeName"] = canary; break;
            case "canary-method": first["type"]!["methodName"] = canary; break;
            case "canary-display": first["displayName"] = canary; break;
        }

        bool accepted = SecurityExecutionEvidence.TryCreateNativeProof(
            Encoding.UTF8.GetBytes(fixture.Discovery.ToJsonString()), fixture.ReportBytes(),
            fixture.RequiredTests, exactSelection: true, out byte[]? proof, out string diagnostic);

        await Assert.That(accepted).IsFalse();
        await Assert.That(proof is null).IsTrue();
        await Assert.That(diagnostic).IsNotEqualTo(string.Empty);
        await Assert.That(diagnostic).DoesNotContain(canary);
    }

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal sealed class NativeFixture : IDisposable
    {
        private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        private readonly string root = Path.Combine(Path.GetTempPath(), "native-proof-" + Guid.NewGuid().ToString("N"));
        internal XDocument Report { get; }
        internal JsonObject Discovery { get; }
        internal SecurityControlRegister.RequiredTest[] RequiredTests { get; }
        internal string RegisterPath => Path.Combine(root, "register.json");
        internal XElement[] Results => Report.Root!.Element(Ns + "Results")!.Elements().ToArray();

        internal NativeFixture(string laneId = "owned-runtime", [CallerFilePath] string sourcePath = "")
        {
            Directory.CreateDirectory(root);
            string shipped = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", "..", "..",
                "security", "security-control-register.json"));
            string registerPath = RegisterPath;
            File.WriteAllText(registerPath, SecurityExecutionEvidenceFixture.OwnedRegister(shipped).ToJsonString());
            var registration = SecurityControlRegister.ReadExecutionRequirements(registerPath);
            SecurityExecutionEvidenceFixture.Write(root, new string('a', 40), registerPath: registerPath);
            Report = XDocument.Load(Path.Combine(root, "security-execution-reports", laneId + ".trx"));
            RequiredTests = registration[laneId].Tests.ToArray();
            Discovery = new JsonObject { ["schemaVersion"] = 1, ["tests"] = new JsonArray() };
            int index = 0;
            foreach (var definition in Report.Root!.Element(Ns + "TestDefinitions")!.Elements())
            {
                var method = definition.Element(Ns + "TestMethod")!;
                AddDiscovery(new((string)method.Attribute("className")!, (string)method.Attribute("name")!,
                    (string)definition.Attribute("name")!), "opaque native uid " + index++);
            }
        }

        internal void AppendTest(SecurityControlRegister.RequiredTest identity, string uid)
        {
            string testId = Guid.NewGuid().ToString("D");
            string executionId = Guid.NewGuid().ToString("D");
            Report.Root!.Element(Ns + "Results")!.Add(new XElement(Ns + "UnitTestResult",
                new XAttribute("testId", testId), new XAttribute("executionId", executionId),
                new XAttribute("testName", identity.TestName), new XAttribute("outcome", "Passed")));
            Report.Root.Element(Ns + "TestDefinitions")!.Add(new XElement(Ns + "UnitTest",
                new XAttribute("id", testId), new XAttribute("name", identity.TestName),
                new XElement(Ns + "Execution", new XAttribute("id", executionId)),
                new XElement(Ns + "TestMethod", new XAttribute("className", identity.ClassName),
                    new XAttribute("name", identity.MethodName))));
            AddDiscovery(identity, uid);
            UpdateCounts();
        }

        internal void RemoveLastTest()
        {
            Report.Root!.Element(Ns + "Results")!.Elements().Last().Remove();
            Report.Root.Element(Ns + "TestDefinitions")!.Elements().Last().Remove();
            Discovery["tests"]!.AsArray().RemoveAt(Discovery["tests"]!.AsArray().Count - 1);
            UpdateCounts();
        }

        private void UpdateCounts()
        {
            var counters = Report.Root!.Element(Ns + "ResultSummary")!.Element(Ns + "Counters")!;
            foreach (string field in new[] { "total", "executed", "passed" }) counters.SetAttributeValue(field, Results.Length);
        }

        private void AddDiscovery(SecurityControlRegister.RequiredTest identity, string uid)
        {
            int split = identity.ClassName.LastIndexOf('.');
            Discovery["tests"]!.AsArray().Add(new JsonObject
            {
                ["uid"] = uid, ["displayName"] = identity.TestName,
                ["type"] = new JsonObject
                {
                    ["namespace"] = identity.ClassName[..split], ["typeName"] = identity.ClassName[(split + 1)..],
                    ["methodName"] = identity.MethodName
                }
            });
        }

        internal byte[] ReportBytes()
        {
            using var stream = new MemoryStream();
            Report.Save(stream);
            return stream.ToArray();
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}

public sealed class SecurityExecutionEvidenceInvocationProofTests
{
    private const string LoadLane = "owned-load-correctness";
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    [Test]
    [Arguments("owned-load-control")]
    [Arguments("owned-load-correctness")]
    public async Task SeparateLoadUidsKeepGlobalRegisteredReferencesAndPrivateWholeArtifactDigests(string laneId)
    {
        using var fixture = new SecurityExecutionEvidenceExecutionTests.NativeFixture(laneId);
        byte[] discovery = Encoding.UTF8.GetBytes(fixture.Discovery.ToJsonString());
        var invocations = SingleUidInputs(fixture);
        bool accepted = SecurityExecutionEvidence.TryCreateNativeLaneProofs(discovery, invocations,
            laneId, fixture.RegisterPath, out var proofs, out string diagnostic);
        await Assert.That(accepted).IsTrue();
        await Assert.That(diagnostic).IsEqualTo(string.Empty);
        await Assert.That(proofs!.Count).IsEqualTo(2);
        var references = new HashSet<int>();
        for (int index = 0; index < proofs.Count; index++)
        {
            JsonNode proof = JsonNode.Parse(proofs[index])!;
            await Assert.That(proof["nodes"]!.AsArray().Count).IsEqualTo(1);
            await Assert.That(proof["nodes"]![0]!["node"]!.GetValue<int>()).IsEqualTo(0);
            await Assert.That(references.Add(proof["nodes"]![0]!["requiredTest"]!.GetValue<int>())).IsTrue();
            await Assert.That(proof["privateDiscoverySha256"]!.GetValue<string>()).IsEqualTo(Digest(discovery));
            await Assert.That(proof["privateTrxSha256"]!.GetValue<string>()).IsEqualTo(Digest(invocations[index].ReportBytes));
            foreach (string uid in Uids(fixture)) await Assert.That(Encoding.UTF8.GetString(proofs[index])).DoesNotContain(uid);
        }
        await Assert.That(references.SetEquals([0, 1])).IsTrue();
    }

    [Test]
    public async Task RuntimeSingletonsKeepExactCoverageWhileOtherOrdinaryLanesRemainBatched()
    {
        using var fixture = new SecurityExecutionEvidenceExecutionTests.NativeFixture();
        byte[] runtimeDiscovery = Encoding.UTF8.GetBytes(fixture.Discovery.ToJsonString());
        var runtimeInvocations = SingleUidInputs(fixture);
        bool accepted = SecurityExecutionEvidence.TryCreateNativeLaneProofs(
            runtimeDiscovery, runtimeInvocations,
            "owned-runtime", fixture.RegisterPath, out var proofs, out _);
        await Assert.That(accepted).IsTrue();
        var runtimeProofs = proofs ?? throw new InvalidDataException("runtime_proofs_missing");
        await Assert.That(runtimeProofs.Count).IsEqualTo(2);
        var references = new HashSet<int>();
        for (int index = 0; index < runtimeProofs.Count; index++)
        {
            JsonNode proof = JsonNode.Parse(runtimeProofs[index])!;
            await Assert.That(proof["nodes"]!.AsArray().Count).IsEqualTo(1);
            await Assert.That(proof["nodes"]![0]!["node"]!.GetValue<int>()).IsEqualTo(0);
            await Assert.That(references.Add(proof["nodes"]![0]!["requiredTest"]!.GetValue<int>())).IsTrue();
            await Assert.That(proof["privateDiscoverySha256"]!.GetValue<string>()).IsEqualTo(Digest(runtimeDiscovery));
            await Assert.That(proof["privateTrxSha256"]!.GetValue<string>()).IsEqualTo(Digest(runtimeInvocations[index].ReportBytes));
            foreach (string uid in Uids(fixture))
                await Assert.That(Encoding.UTF8.GetString(runtimeProofs[index])).DoesNotContain(uid);
        }
        await Assert.That(references.SetEquals([0, 1])).IsTrue();
        await Assert.That(SecurityExecutionEvidence.TryValidateNativeLaneProofs(runtimeProofs, "owned-runtime",
            fixture.RegisterPath, out _)).IsTrue();
        await Assert.That(SecurityExecutionEvidence.TryCreateNativeLaneProofs(runtimeDiscovery,
            [new SecurityExecutionEvidence.NativeInvocationInput(Uids(fixture), fixture.ReportBytes())],
            "owned-runtime", fixture.RegisterPath, out _, out _)).IsFalse();
        await Assert.That(SecurityExecutionEvidence.TryCreateNativeProof(runtimeDiscovery, fixture.ReportBytes(),
            fixture.RequiredTests, true, out var runtimeBatch, out _)).IsTrue();
        byte[] completeRuntimeProof = runtimeBatch ?? throw new InvalidDataException("runtime_batch_proof_missing");
        await Assert.That(SecurityExecutionEvidence.TryValidateNativeLaneProofs([completeRuntimeProof], "owned-runtime",
            fixture.RegisterPath, out _)).IsFalse();
        foreach (string lane in SecurityControlRegister.ExecutionRequirements.Keys.Where(lane =>
            lane is not ("owned-runtime" or "owned-load-control" or "owned-load-correctness")))
        {
            using var batched = new SecurityExecutionEvidenceExecutionTests.NativeFixture(lane);
            byte[] discovery = Encoding.UTF8.GetBytes(batched.Discovery.ToJsonString());
            await Assert.That(SecurityExecutionEvidence.TryCreateNativeLaneProofs(discovery,
                [new SecurityExecutionEvidence.NativeInvocationInput(Uids(batched), batched.ReportBytes())],
                lane, batched.RegisterPath, out var complete, out _)).IsTrue();
            var completeProofs = complete ?? throw new InvalidDataException("ordinary_batch_proof_missing");
            await Assert.That(completeProofs.Count).IsEqualTo(1);
            await Assert.That(JsonNode.Parse(completeProofs[0])!["nodes"]!.AsArray().Count).IsEqualTo(batched.RequiredTests.Length);
            await Assert.That(SecurityExecutionEvidence.TryValidateNativeLaneProofs(completeProofs,
                lane, batched.RegisterPath, out _)).IsTrue();
            if (batched.RequiredTests.Length > 1)
            {
                await Assert.That(SecurityExecutionEvidence.TryCreateNativeLaneProofs(discovery,
                    SingleUidInputs(batched), lane, batched.RegisterPath, out _, out _)).IsFalse();
                await Assert.That(SecurityExecutionEvidence.TryValidateNativeLaneProofs(PublicProofs(batched),
                    lane, batched.RegisterPath, out _)).IsFalse();
            }
        }
    }

    [Test]
    public async Task PublicLoadProofsCloseOneCompleteDisjointRegisteredUnion()
    {
        using var fixture = new SecurityExecutionEvidenceExecutionTests.NativeFixture(LoadLane);
        bool accepted = SecurityExecutionEvidence.TryValidateNativeLaneProofs(PublicProofs(fixture),
            LoadLane, fixture.RegisterPath, out string diagnostic);
        await Assert.That(accepted).IsTrue();
        await Assert.That(diagnostic).IsEqualTo(string.Empty);
    }

    [Test]
    [Arguments("missing-invocation")]
    [Arguments("reused-selected-uid")]
    [Arguments("unknown-selected-uid")]
    [Arguments("combined-load-selection")]
    [Arguments("wrong-selected-result")]
    [Arguments("reused-native-run")]
    [Arguments("reused-native-execution")]
    [Arguments("failed-result")]
    [Arguments("unregistered-discovery")]
    [Arguments("invalid-full-register")]
    public async Task IncompleteOrReusedPrivateInvocationsCannotCoverTheRegisteredLane(string variant)
    {
        using var fixture = new SecurityExecutionEvidenceExecutionTests.NativeFixture(LoadLane);
        List<SecurityExecutionEvidence.NativeInvocationInput> inputs = SingleUidInputs(fixture).ToList();
        string[] uids = Uids(fixture);
        switch (variant)
        {
            case "missing-invocation": inputs.RemoveAt(1); break;
            case "reused-selected-uid": inputs[1] = new([uids[0]], OneReport(fixture, 0)); break;
            case "unknown-selected-uid": inputs[1] = new(["unobserved opaque uid"], inputs[1].ReportBytes); break;
            case "combined-load-selection": inputs = [new(uids, fixture.ReportBytes())]; break;
            case "wrong-selected-result": inputs[1] = new([uids[1]], OneReport(fixture, 0)); break;
            case "reused-native-run":
            case "reused-native-execution":
            case "failed-result":
                XDocument first = ReadXml(inputs[0].ReportBytes);
                XDocument second = ReadXml(inputs[1].ReportBytes);
                if (variant == "reused-native-run") second.Root!.SetAttributeValue("id", first.Root!.Attribute("id")!.Value);
                else if (variant == "failed-result")
                    second.Root!.Element(Ns + "Results")!.Elements().Single().SetAttributeValue("outcome", "Failed");
                else
                {
                    string reused = first.Root!.Element(Ns + "Results")!.Elements().Single().Attribute("executionId")!.Value;
                    second.Root!.Element(Ns + "Results")!.Elements().Single().SetAttributeValue("executionId", reused);
                    second.Root.Element(Ns + "TestDefinitions")!.Elements().Single().Element(Ns + "Execution")!.SetAttributeValue("id", reused);
                }
                inputs[1] = new([uids[1]], XmlBytes(second));
                break;
            case "unregistered-discovery": fixture.AppendTest(new("Synthetic.Unregistered", "Witness", "Witness"), "extra opaque uid"); break;
            case "invalid-full-register":
                JsonNode register = JsonNode.Parse(File.ReadAllBytes(fixture.RegisterPath))!;
                register["executionRequirements"]!.AsArray().RemoveAt(2);
                File.WriteAllText(fixture.RegisterPath, register.ToJsonString());
                break;
        }
        bool accepted = SecurityExecutionEvidence.TryCreateNativeLaneProofs(
            Encoding.UTF8.GetBytes(fixture.Discovery.ToJsonString()), inputs, LoadLane, fixture.RegisterPath,
            out var proofs, out string diagnostic);
        await Assert.That(accepted).IsFalse();
        await Assert.That(proofs is null).IsTrue();
        await Assert.That(diagnostic).IsEqualTo("native_lane_projection_invalid");
    }

    [Test]
    [Arguments("missing-proof")]
    [Arguments("duplicate-proof")]
    [Arguments("reused-required-reference")]
    [Arguments("outside-required-reference")]
    [Arguments("public-uid-canary")]
    [Arguments("nonpassing-outcome")]
    [Arguments("wrong-counts")]
    [Arguments("reused-trx-digest")]
    [Arguments("different-discovery-digest")]
    [Arguments("merged-load-proof")]
    public async Task PublicInvocationReferencesCannotHideMissingReusedOrPrivateResults(string variant)
    {
        using var fixture = new SecurityExecutionEvidenceExecutionTests.NativeFixture(LoadLane);
        List<byte[]> proofs = PublicProofs(fixture).ToList();
        JsonNode second = JsonNode.Parse(proofs[1])!;
        string canary = "private-invocation-uid-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        switch (variant)
        {
            case "missing-proof": proofs.RemoveAt(1); break;
            case "duplicate-proof": proofs[1] = proofs[0].ToArray(); break;
            case "reused-required-reference": second["nodes"]![0]!["requiredTest"] = 0; break;
            case "outside-required-reference": second["nodes"]![0]!["requiredTest"] = 2; break;
            case "public-uid-canary": second["nodes"]![0]!["uid"] = canary; break;
            case "nonpassing-outcome": second["nodes"]![0]!["outcome"] = "skipped"; break;
            case "wrong-counts": second["counts"]!["selected"] = 2; break;
            case "reused-trx-digest": second["privateTrxSha256"] = JsonNode.Parse(proofs[0])!["privateTrxSha256"]!.GetValue<string>(); break;
            case "different-discovery-digest": second["privateDiscoverySha256"] = new string('b', 64); break;
            case "merged-load-proof":
                if (!SecurityExecutionEvidence.TryCreateNativeProof(Encoding.UTF8.GetBytes(fixture.Discovery.ToJsonString()),
                        fixture.ReportBytes(), fixture.RequiredTests, true, out byte[]? merged, out _))
                    throw new InvalidDataException("native_invocation_fixture_invalid");
                proofs = [merged!];
                break;
        }
        if (variant is not ("missing-proof" or "duplicate-proof" or "merged-load-proof")) proofs[1] = Encoding.UTF8.GetBytes(second.ToJsonString());
        bool accepted = SecurityExecutionEvidence.TryValidateNativeLaneProofs(proofs, LoadLane,
            fixture.RegisterPath, out string diagnostic);
        await Assert.That(accepted).IsFalse();
        await Assert.That(diagnostic).IsEqualTo("native_lane_proof_invalid");
        await Assert.That(diagnostic.Contains(canary, StringComparison.Ordinal)).IsFalse();
    }

    private static SecurityExecutionEvidence.NativeInvocationInput[] SingleUidInputs(
        SecurityExecutionEvidenceExecutionTests.NativeFixture fixture) =>
        Uids(fixture).Select((uid, index) => new SecurityExecutionEvidence.NativeInvocationInput([uid], OneReport(fixture, index))).ToArray();

    private static string[] Uids(SecurityExecutionEvidenceExecutionTests.NativeFixture fixture) =>
        fixture.Discovery["tests"]!.AsArray().Select(test => test!["uid"]!.GetValue<string>()).ToArray();

    private static byte[] OneReport(SecurityExecutionEvidenceExecutionTests.NativeFixture fixture, int index)
    {
        var report = new XDocument(fixture.Report);
        report.Root!.SetAttributeValue("id", Guid.NewGuid().ToString("D"));
        report.Root.Element(Ns + "Results")!.Elements().Where((_, ordinal) => ordinal != index).Remove();
        report.Root.Element(Ns + "TestDefinitions")!.Elements().Where((_, ordinal) => ordinal != index).Remove();
        XElement counters = report.Root.Element(Ns + "ResultSummary")!.Element(Ns + "Counters")!;
        foreach (XAttribute counter in counters.Attributes()) counter.Value = counter.Name.LocalName is "total" or "executed" or "passed" ? "1" : "0";
        return XmlBytes(report);
    }

    private static byte[] XmlBytes(XDocument report)
    {
        using var stream = new MemoryStream();
        report.Save(stream);
        return stream.ToArray();
    }

    private static XDocument ReadXml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return XDocument.Load(stream);
    }

    private static byte[][] PublicProofs(SecurityExecutionEvidenceExecutionTests.NativeFixture fixture)
    {
        if (!SecurityExecutionEvidence.TryCreateNativeProof(Encoding.UTF8.GetBytes(fixture.Discovery.ToJsonString()),
                fixture.ReportBytes(), fixture.RequiredTests, true, out byte[]? complete, out _))
            throw new InvalidDataException("native_invocation_fixture_invalid");
        JsonNode full = JsonNode.Parse(complete!)!;
        return Enumerable.Range(0, 2).Select(index =>
        {
            JsonNode proof = full.DeepClone();
            JsonNode node = proof["nodes"]![index]!.DeepClone();
            node["node"] = 0;
            proof["nodes"] = new JsonArray(node);
            proof["counts"] = new JsonObject { ["selected"] = 1, ["executed"] = 1, ["passed"] = 1, ["failed"] = 0, ["skipped"] = 0 };
            proof["privateTrxSha256"] = Digest(OneReport(fixture, index));
            return Encoding.UTF8.GetBytes(proof.ToJsonString());
        }).ToArray();
    }

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

public sealed class SecurityExecutionEvidenceTransportTests
{
    [Test]
    [Arguments("opaque uid 'quoted' = value")]
    [Arguments("opaque --report-trx-filename /foreign.trx")]
    public async Task OpaqueUidIsOneLiteralArgumentAndCannotReplaceFixedResultsPaths(string uid)
    {
        string root = Path.Combine(Path.GetTempPath(), "native-transport-" + Guid.NewGuid().ToString("N"));
        string results = Path.Combine(root, "results");
        string? originalTemporaryDirectory = Environment.GetEnvironmentVariable("TMPDIR");
        ProcessStartInfo command = SecurityExecutionEvidence.NativeTestStartInfo(
            typeof(SecurityExecutionEvidenceTransportTests).Assembly.Location, [uid, "second opaque uid"],
            results, root, "security-integration-tests.trx", 60);

        await Assert.That(command.UseShellExecute).IsFalse();
        await Assert.That(command.FileName).IsEqualTo(ReleaseToolPaths.Dotnet);
        await Assert.That(command.ArgumentList.Count(argument => argument == "--filter-uid")).IsEqualTo(1);
        await Assert.That(command.ArgumentList.Contains("--treenode-filter")).IsFalse();
        await Assert.That(command.ArgumentList.Count(argument => argument == uid)).IsEqualTo(1);
        await Assert.That(command.ArgumentList.Count(argument => argument == "--report-trx-filename")).IsEqualTo(1);
        int filename = command.ArgumentList.IndexOf("--report-trx-filename");
        await Assert.That(command.ArgumentList[filename + 1]).IsEqualTo("security-integration-tests.trx");
        int directory = command.ArgumentList.IndexOf("--results-directory");
        await Assert.That(command.ArgumentList[directory + 1]).IsEqualTo(results);
        await Assert.That(command.Environment["TMPDIR"]).IsEqualTo(root);
        await Assert.That(Environment.GetEnvironmentVariable("TMPDIR")).IsEqualTo(originalTemporaryDirectory);
    }

    [Test]
    [Arguments("--report-trx-filename")]
    [Arguments("-x")]
    [Arguments("@response-file")]
    [Arguments("--")]
    [Arguments("-")]
    [Arguments("empty")]
    [Arguments("spaces")]
    [Arguments("leading-space")]
    [Arguments("trailing-space")]
    [Arguments("newline")]
    [Arguments("return")]
    [Arguments("tab")]
    [Arguments("nul")]
    [Arguments("delete")]
    [Arguments("oversized")]
    public async Task CliControlFormsAreRejectedWithoutNormalizingTheNativeIdentity(string variant)
    {
        string uid = variant switch
        {
            "empty" => "", "spaces" => "   ", "leading-space" => " native",
            "trailing-space" => "native ", "newline" => "native\nuid", "return" => "native\ruid",
            "tab" => "native\tuid", "nul" => "native\0uid", "delete" => "native\u007fuid",
            "oversized" => new string('x', 4097), _ => variant
        };
        string root = Path.Combine(Path.GetTempPath(), "native-transport-" + Guid.NewGuid().ToString("N"));
        bool rejected = false;
        try
        {
            _ = SecurityExecutionEvidence.NativeTestStartInfo(typeof(SecurityExecutionEvidenceTransportTests).Assembly.Location,
                [uid], Path.Combine(root, "results"), root, "security-integration-tests.trx", 60);
        }
        catch (InvalidDataException) { rejected = true; }
        await Assert.That(rejected).IsTrue();
    }

    [Test]
    [Arguments("outside-results")]
    [Arguments("absolute-filename")]
    [Arguments("relative-filename")]
    [Arguments("empty-selection")]
    [Arguments("duplicate-selection")]
    [Arguments("argument-budget")]
    [Arguments("infinite-deadline")]
    public async Task NativeSelectionCannotEscapeTheOwnedOutputOrRemoveItsFiniteBound(string variant)
    {
        string root = Path.Combine(Path.GetTempPath(), "native-transport-" + Guid.NewGuid().ToString("N"));
        string results = Path.Combine(root, "results");
        string filename = "security-integration-tests.trx";
        int timeout = 60;
        IReadOnlyList<string> uids = ["opaque native uid"];
        switch (variant)
        {
            case "outside-results": results = Path.Combine(Path.GetTempPath(), "outside-results"); break;
            case "absolute-filename": filename = Path.Combine(Path.GetTempPath(), "outside.trx"); break;
            case "relative-filename": filename = "../outside.trx"; break;
            case "empty-selection": uids = []; break;
            case "duplicate-selection": uids = ["opaque native uid", "opaque native uid"]; break;
            case "argument-budget": uids = Enumerable.Range(0, 600).Select(index => new string('x', 2000) + index).ToArray(); break;
            case "infinite-deadline": timeout = 0; break;
        }
        bool rejected = false;
        try
        {
            _ = SecurityExecutionEvidence.NativeTestStartInfo(typeof(SecurityExecutionEvidenceTransportTests).Assembly.Location,
                uids, results, root, filename, timeout);
        }
        catch (InvalidDataException) { rejected = true; }
        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task OldPassingReportsCannotBeReassignedToANewProducerInvocation()
    {
        string root = Path.Combine(Path.GetTempPath(), "native-stale-reports-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string old = Path.Combine(root, "old");
            SecurityExecutionEvidenceFixture.Write(old, new string('a', 40));
            foreach (var (id, requirement) in SecurityControlRegister.ExecutionRequirements)
            {
                string directory = Path.Combine(root, Path.GetDirectoryName(requirement.Project)!, "TestResults");
                Directory.CreateDirectory(directory);
                File.Copy(Path.Combine(old, "security-execution-reports", id + ".trx"),
                    Path.Combine(directory, requirement.ReportFileName));
            }
            string outputDirectory = Path.Combine(root, "new-output");
            using var output = new StringWriter();
            int code = SecurityExecutionEvidence.Run(
                ["produce-security-execution", outputDirectory, new string('b', 40), "99", "2", "push"], output, root);
            await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
            await Assert.That(File.Exists(Path.Combine(outputDirectory, SecurityExecutionEvidence.PacketFileName))).IsFalse();
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

public sealed class SecurityExecutionEvidenceProcessTests
{
    [Test]
    public async Task LiveObserverReceivesActualOwnedIdentityAndClosesItsResourceBeforeReturn()
    {
        using var fixture = new ProcessFixture();
        ProcessStartInfo command = await fixture.CommandAsync("clean");
        string resource = Path.Combine(command.WorkingDirectory, "observer-resource");
        using var fallback = new FileStream(resource, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.DeleteOnClose);
        var handle = fallback.SafeFileHandle;
        await Assert.That(File.Exists(resource)).IsTrue();
        SecurityExecutionEvidence.OwnedNativeProcessContext? observed = null;
        bool liveIdentityMatched = false;
        var result = await SecurityExecutionEvidence.RunOwnedNativeProcessAsync(command,
            TimeSpan.FromSeconds(10), CancellationToken.None, async (identity, token) =>
            {
                observed = identity;
                string value = File.ReadAllText("/proc/" + identity.Pid + "/stat");
                string[] fields = value[(value.LastIndexOf(')') + 2)..].Split(' ');
                liveIdentityMatched = identity.Pid != Environment.ProcessId && identity.Group == identity.Pid &&
                    identity.Session == identity.Pid && identity.Start > 0 &&
                    fields[2] == identity.Group.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                    fields[3] == identity.Session.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                    fields[19] == identity.Start.ToString(System.Globalization.CultureInfo.InvariantCulture);
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                finally { fallback.Dispose(); }
            });
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(Encoding.UTF8.GetString(result.StandardOutput)).Contains(fixture.Canary);
        await Assert.That(observed is not null).IsTrue();
        await Assert.That(liveIdentityMatched).IsTrue();
        await Assert.That(handle.IsClosed).IsTrue();
        await Assert.That(File.Exists(resource)).IsFalse();
        await Assert.That(result.ObserverSettled).IsTrue();
        await Assert.That(result.ObserverFailures.Count).IsEqualTo(0);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(fixture.OwnedChildrenRemain()).IsFalse();
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(1200, false)]
    [Arguments(0, true)]
    public async Task ObserverAndCancellationCallbackFailuresBothSurviveJoinedNativeCleanup(
        int callbackDelayMilliseconds, bool propagateStopCancellation)
    {
        using var fixture = new ProcessFixture();
        ProcessStartInfo command = await fixture.CommandAsync("clean");
        string resource = Path.Combine(command.WorkingDirectory, "observer-failure-resource");
        using var fallback = new FileStream(resource, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.DeleteOnClose);
        var handle = fallback.SafeFileHandle;
        await Assert.That(File.Exists(resource)).IsTrue();
        Exception primary = new IOException("observer_primary");
        var cleanupFailure = new InvalidOperationException("observer_cancellation_callback");
        var cancellationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = await SecurityExecutionEvidence.RunOwnedNativeProcessAsync(command,
            TimeSpan.FromSeconds(10), CancellationToken.None, async (_, token) =>
            {
                try
                {
                    using var registration = token.Register(() =>
                    {
                        Thread.Sleep(callbackDelayMilliseconds);
                        cancellationObserved.TrySetResult(true);
                        throw cleanupFailure;
                    });
                    await cancellationObserved.Task;
                    if (propagateStopCancellation) primary = new OperationCanceledException(token);
                    throw primary;
                }
                finally { fallback.Dispose(); }
            });
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(Encoding.UTF8.GetString(result.StandardOutput)).Contains(fixture.Canary);
        Exception[] failures = result.ObserverFailures.SelectMany(failure => failure is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions.AsEnumerable() : Enumerable.Repeat(failure, 1)).ToArray();
        await Assert.That(failures.Any(failure => ReferenceEquals(failure, primary))).IsTrue();
        await Assert.That(failures.Any(failure => ReferenceEquals(failure, cleanupFailure))).IsTrue();
        await Assert.That(handle.IsClosed).IsTrue();
        await Assert.That(File.Exists(resource)).IsFalse();
        await Assert.That(result.ObserverSettled).IsTrue();
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.CleanupSucceeded).IsTrue();
        await Assert.That(fixture.OwnedChildrenRemain()).IsFalse();
    }

    [Test]
    public async Task CleanCompiledChildDrainsBothPrivateStreamsAndCompletes()
    {
        using var fixture = new ProcessFixture();
        ProcessStartInfo command = await fixture.CommandAsync("clean");
        var result = await SecurityExecutionEvidence.RunOwnedNativeProcessAsync(command, TimeSpan.FromSeconds(10), CancellationToken.None);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(result.StandardOutput)).Contains(fixture.Canary);
        await Assert.That(Encoding.UTF8.GetString(result.StandardError)).Contains(fixture.Canary);
    }

    [Test]
    public async Task ParentExitZeroWithOwnedChildHoldingPipesIsFailureEvenAfterCleanup()
    {
        using var fixture = new ProcessFixture();
        ProcessStartInfo command = await fixture.CommandAsync("leak");
        var result = await SecurityExecutionEvidence.RunOwnedNativeProcessAsync(command, TimeSpan.FromSeconds(10), CancellationToken.None);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.ResidualOwnedChildren).IsTrue();
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.CleanupSucceeded).IsTrue();
        await Assert.That(fixture.OwnedChildrenRemain()).IsFalse();
    }

    [Test]
    public async Task FiniteDeadlineKillsAndJoinsOnlyItsOwnedCompiledChild()
    {
        using var fixture = new ProcessFixture();
        ProcessStartInfo command = await fixture.CommandAsync("hold");
        var result = await SecurityExecutionEvidence.RunOwnedNativeProcessAsync(command, TimeSpan.FromSeconds(1), CancellationToken.None);
        await Assert.That(result.TimedOut).IsTrue();
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.CleanupSucceeded).IsTrue();
        await Assert.That(fixture.OwnedChildrenRemain()).IsFalse();
    }

    [Test]
    public async Task BoundedOutputCannotTurnAFloodingChildIntoAnAdmittedPass()
    {
        using var fixture = new ProcessFixture();
        ProcessStartInfo command = await fixture.CommandAsync("flood");
        var result = await SecurityExecutionEvidence.RunOwnedNativeProcessAsync(command, TimeSpan.FromSeconds(10), CancellationToken.None);
        await Assert.That(result.OutputLimitExceeded).IsTrue();
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.CleanupSucceeded).IsTrue();
        await Assert.That(result.StandardOutput.Length <= 16_777_216).IsTrue();
        await Assert.That(fixture.OwnedChildrenRemain()).IsFalse();
    }

    [Test]
    public async Task OwnedNativeChild()
    {
        string? mode = Environment.GetEnvironmentVariable("ISLAMU_RELEASE_NATIVE_CHILD_CONTROL");
        if (mode is null) return;
        string root = Environment.GetEnvironmentVariable("ISLAMU_RELEASE_NATIVE_CHILD_ROOT")!;
        string canary = Environment.GetEnvironmentVariable("ISLAMU_RELEASE_NATIVE_CHILD_CANARY")!;
        RecordOwnedPid(root, Environment.ProcessId);
        if (mode == "clean")
        {
            using Stream standardOutput = Console.OpenStandardOutput();
            using Stream standardError = Console.OpenStandardError();
            await standardOutput.WriteAsync(Encoding.UTF8.GetBytes(canary));
            await standardError.WriteAsync(Encoding.UTF8.GetBytes(canary));
            Environment.Exit(0);
        }
        if (mode == "managed-claim")
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            string owner = Path.GetFileName(root)[^32..];
            string context = Path.Combine(root, "islamu-assurance", owner);
            using var claim = new FileStream(Path.Combine(context, ".apphost-claim"),
                FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            string pending = Path.Combine(context, ".managed-claim-ready-" + Environment.ProcessId);
            using (var ready = new FileStream(pending,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                }))
            {
                await ready.WriteAsync(Encoding.ASCII.GetBytes("claim-held"));
                await ready.FlushAsync();
            }
            File.Move(pending, Path.Combine(context, "managed-claim-ready-" + Environment.ProcessId));
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return;
        }
        if (mode is "managed-witness" or "managed-launcher" or "managed-host")
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            string owner = Path.GetFileName(root)[^32..];
            string assurance = Path.Combine(root, "islamu-assurance");
            string context = Path.Combine(assurance, owner);
            if (mode != "managed-host")
            {
                Directory.CreateDirectory(context);
                foreach (string directory in new[] { assurance, context })
                    File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.Read,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            };
            if (mode != "managed-host")
            {
                using (var marker = new FileStream(Path.Combine(context, ".islamu-test-owner"), options))
                    marker.Write(Encoding.ASCII.GetBytes(owner));
                using (var model = new FileStream(Path.Combine(context, "container-model.json"), options))
                    model.Write(JsonSerializer.SerializeToUtf8Bytes(new
                        { Schema = 1, RunId = owner, Containers = Array.Empty<object>(), Projects = Array.Empty<object>() }));
            }
            using var claim = mode == "managed-host" ? null : new FileStream(Path.Combine(context, ".apphost-claim"), new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.ReadWrite,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            });
            string stat = File.ReadAllText("/proc/" + Environment.ProcessId + "/stat");
            string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            ulong start = ulong.Parse(fields[19], System.Globalization.CultureInfo.InvariantCulture);
            string unpublished = Path.Combine(context, ".managed-root-control");
            using var record = mode == "managed-launcher" ? null : new FileStream(unpublished, options);
            if (record is not null)
            {
                await record.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Schema = 1, RunId = owner, Pid = Environment.ProcessId, Start = start,
                    ContentRoot = Path.GetDirectoryName(typeof(SecurityExecutionEvidenceProcessTests).Assembly.Location),
                    ApplicationName = typeof(SecurityExecutionEvidenceProcessTests).Assembly.GetName().Name,
                    EntryAssembly = typeof(SecurityExecutionEvidenceProcessTests).Assembly.Location,
                    IsWebHost = false, WebRoot = (string?)null, ServedAssets = (object?)null
                }));
                await record.FlushAsync();
                File.Move(unpublished, Path.Combine(context, $"managed-host-root-{Environment.ProcessId}-{start}.json"));
            }
            if (mode == "managed-host")
            {
                await Task.Delay(Timeout.InfiniteTimeSpan);
                return;
            }
            string creationId = Guid.CreateVersion7().ToString("N");
            string creationStem = Path.Combine(context, "api-container-keycloak-" + creationId);
            using var creationDeclaration = mode == "managed-witness"
                ? new FileStream(creationStem + ".declaration-pending", options) : null;
            using var creationReceipt = mode == "managed-witness"
                ? new FileStream(creationStem + ".created-pending", options) : null;
            if (creationDeclaration is not null && creationReceipt is not null)
            {
                byte[] declarationBytes = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Schema = 1, RunId = owner, CreationId = creationId, CreatorPid = Environment.ProcessId,
                    Resource = "keycloak", ImageReference = "example.invalid/creator-control:local"
                });
                await creationDeclaration.WriteAsync(declarationBytes);
                creationDeclaration.Flush(true);
                File.Move(creationStem + ".declaration-pending", creationStem + "-declaration.json");
                await creationReceipt.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Schema = 1, RunId = owner, CreationId = creationId, CreatorPid = Environment.ProcessId,
                    Resource = "keycloak", ImageReference = "example.invalid/creator-control:local",
                    ContainerId = new string('a', 64),
                    DeclarationSha256 = Convert.ToHexStringLower(SHA256.HashData(declarationBytes))
                }));
                creationReceipt.Flush(true);
                File.Move(creationStem + ".created-pending", creationStem + "-created-" + new string('a', 64) + ".json");
            }
            using var hosted = new Process();
            using var secondHosted = new Process();
            using var sibling = new Process();
            if (mode == "managed-launcher")
            {
                sibling.StartInfo = SecurityExecutionEvidence.NativeTestStartInfo(
                    typeof(SecurityExecutionEvidenceProcessTests).Assembly.Location,
                    [TestContext.Current!.Metadata.TestDetails.Identity.TestId], Path.Combine(root, "sibling-results"),
                    root, "managed-sibling.trx", 60);
                sibling.StartInfo.Environment["ISLAMU_RELEASE_NATIVE_CHILD_CONTROL"] = "managed-claim";
                sibling.StartInfo.Environment["ISLAMU_RELEASE_NATIVE_CHILD_ROOT"] = root;
                sibling.StartInfo.Environment["ISLAMU_RELEASE_NATIVE_CHILD_CANARY"] = canary;
                sibling.StartInfo.RedirectStandardOutput = false;
                sibling.StartInfo.RedirectStandardError = false;
                if (!sibling.Start()) throw new InvalidDataException("native_managed_sibling_start_failed");
                RecordOwnedPid(root, sibling.Id);
            }
            string pending = Path.Combine(context, ".managed-lead-control");
            using (var lead = new FileStream(pending, options))
            {
                await lead.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Schema = 1, RunId = owner, Hint = new
                    {
                        Resource = "event-migrationservice", InstanceSha256 = new string('c', 64), Pid = Environment.ProcessId, Start = start,
                        Diagnostic = "managed_launch_inputs_unavailable:process_not_owned"
                    }
                }));
                await lead.FlushAsync();
            }
            File.Move(pending, Path.Combine(context, "managed-control-lead.json"));
            if (mode == "managed-launcher")
            {
                using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                while (!File.Exists(Path.Combine(context, "managed-control-start-host")))
                    await Task.Delay(10, startup.Token);
                hosted.StartInfo = SecurityExecutionEvidence.NativeTestStartInfo(
                    typeof(SecurityExecutionEvidenceProcessTests).Assembly.Location,
                    [TestContext.Current!.Metadata.TestDetails.Identity.TestId], Path.Combine(root, "host-results"),
                    root, "managed-host.trx", 60);
                hosted.StartInfo.Environment["ISLAMU_RELEASE_NATIVE_CHILD_CONTROL"] = "managed-host";
                hosted.StartInfo.Environment["ISLAMU_RELEASE_NATIVE_CHILD_ROOT"] = root;
                hosted.StartInfo.Environment["ISLAMU_RELEASE_NATIVE_CHILD_CANARY"] = canary;
                hosted.StartInfo.RedirectStandardOutput = false;
                hosted.StartInfo.RedirectStandardError = false;
                if (!hosted.Start()) throw new InvalidDataException("native_managed_host_start_failed");
                RecordOwnedPid(root, hosted.Id);
                while (!File.Exists(Path.Combine(context, "managed-control-start-second-host")))
                    await Task.Delay(10);
                secondHosted.StartInfo = SecurityExecutionEvidence.NativeTestStartInfo(
                    typeof(SecurityExecutionEvidenceProcessTests).Assembly.Location,
                    [TestContext.Current!.Metadata.TestDetails.Identity.TestId], Path.Combine(root, "second-host-results"),
                    root, "managed-second-host.trx", 60);
                secondHosted.StartInfo.Environment["ISLAMU_RELEASE_NATIVE_CHILD_CONTROL"] = "managed-host";
                secondHosted.StartInfo.Environment["ISLAMU_RELEASE_NATIVE_CHILD_ROOT"] = root;
                secondHosted.StartInfo.Environment["ISLAMU_RELEASE_NATIVE_CHILD_CANARY"] = canary;
                secondHosted.StartInfo.RedirectStandardOutput = false;
                secondHosted.StartInfo.RedirectStandardError = false;
                if (!secondHosted.Start()) throw new InvalidDataException("native_managed_second_host_start_failed");
                RecordOwnedPid(root, secondHosted.Id);
            }
            if (mode == "managed-witness")
            {
                while (!File.Exists(Path.Combine(context, "managed-control-next-creation"))) await Task.Delay(10);
                if (creationDeclaration is null || creationReceipt is null)
                    throw new InvalidDataException("native_api_initial_creation_not_held");
                var creationCloseFailures = new List<Exception>();
                try { await creationDeclaration.DisposeAsync(); }
                catch (Exception failure) { creationCloseFailures.Add(failure); }
                try { await creationReceipt.DisposeAsync(); }
                catch (Exception failure) { creationCloseFailures.Add(failure); }
                if (creationCloseFailures.Count != 0) throw new AggregateException(creationCloseFailures);
                string nextCreationId = Guid.CreateVersion7().ToString("N");
                string nextCreationStem = Path.Combine(context, "api-container-cerbos-" + nextCreationId);
                using var nextDeclaration = new FileStream(nextCreationStem + ".declaration-pending", options);
                using var nextReceipt = new FileStream(nextCreationStem + ".created-pending", options);
                byte[] nextDeclarationBytes = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Schema = 1, RunId = owner, CreationId = nextCreationId, CreatorPid = Environment.ProcessId,
                    Resource = "cerbos", ImageReference = "example.invalid/creator-control:next"
                });
                await nextDeclaration.WriteAsync(nextDeclarationBytes);
                nextDeclaration.Flush(true);
                File.Move(nextCreationStem + ".declaration-pending", nextCreationStem + "-declaration.json");
                await nextReceipt.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Schema = 1, RunId = owner, CreationId = nextCreationId, CreatorPid = Environment.ProcessId,
                    Resource = "cerbos", ImageReference = "example.invalid/creator-control:next",
                    ContainerId = new string('d', 64),
                    DeclarationSha256 = Convert.ToHexStringLower(SHA256.HashData(nextDeclarationBytes))
                }));
                nextReceipt.Flush(true);
                File.Move(nextCreationStem + ".created-pending", nextCreationStem + "-created-" + new string('d', 64) + ".json");
                string nextReadyPending = Path.Combine(context, ".managed-control-next-creation-ready");
                using (var nextReady = new FileStream(nextReadyPending, options))
                {
                    nextReady.Write(Encoding.ASCII.GetBytes(nextCreationId));
                    nextReady.Flush(true);
                }
                File.Move(nextReadyPending, Path.Combine(context, "managed-control-next-creation-ready"));
                await Task.Delay(Timeout.InfiniteTimeSpan);
                return;
            }
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return;
        }
        if (mode == "leak")
        {
            ProcessStartInfo descendant = SecurityExecutionEvidence.NativeTestStartInfo(
                typeof(SecurityExecutionEvidenceProcessTests).Assembly.Location,
                [TestContext.Current!.Metadata.TestDetails.Identity.TestId], Path.Combine(root, "descendant-results"), root,
                "descendant.trx", 60);
            descendant.Environment["ISLAMU_RELEASE_NATIVE_CHILD_CONTROL"] = "hold";
            descendant.RedirectStandardOutput = false;
            descendant.RedirectStandardError = false;
            using Process child = Process.Start(descendant)!;
            RecordOwnedPid(root, child.Id);
            Environment.Exit(0);
        }
        if (mode == "flood")
        {
            byte[] bytes = new byte[1024 * 1024];
            using Stream stream = Console.OpenStandardOutput();
            for (int index = 0; index < 20; index++) await stream.WriteAsync(bytes);
            Environment.Exit(0);
        }
        if (mode == "fifo-input")
        {
            using var input = new SecurityExecutionEvidenceCompiledInputTests.CompiledFixture();
            input.CreateFifo();
            bool accepted = SecurityExecutionEvidence.TryCaptureCompiledInputs(input.Root, input.Candidate,
                ["entry.dll"], out _, out _);
            Environment.Exit(accepted ? 1 : 0);
        }
        if (mode == "hold") await Task.Delay(Timeout.InfiniteTimeSpan);
        throw new InvalidDataException("unknown_native_child_control");
    }

    private static void RecordOwnedPid(string root, int pid)
    {
        string stat = File.ReadAllText("/proc/" + pid + "/stat");
        string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
        File.WriteAllText(Path.Combine(root, "pid-" + pid),
            pid.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + fields[19]);
    }

    internal sealed class ProcessFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "native-owned-process-" + Guid.NewGuid().ToString("N"));
        internal string Canary { get; } = "private-stream-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

        internal ProcessFixture() => Directory.CreateDirectory(root);

        internal async Task<ProcessStartInfo> CommandAsync(string mode)
        {
            var discovery = new ProcessStartInfo(ReleaseToolPaths.Dotnet)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = root, CreateNoWindow = true
            };
            foreach (string argument in new[] { typeof(SecurityExecutionEvidenceProcessTests).Assembly.Location,
                "--list-tests", "json", "--treenode-filter", "/*/*/SecurityExecutionEvidenceProcessTests/OwnedNativeChild", "--no-ansi" })
                discovery.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = discovery };
            process.Start();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            if (process.ExitCode != 0 || (await error).Length != 0) throw new InvalidDataException("native_child_discovery_failed");
            using JsonDocument document = JsonDocument.Parse(await output);
            JsonElement tests = document.RootElement.GetProperty("tests");
            if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1 || tests.GetArrayLength() != 1)
                throw new InvalidDataException("native_child_discovery_failed");
            string uid = tests[0].GetProperty("uid").GetString()!;
            var command = SecurityExecutionEvidence.NativeTestStartInfo(typeof(SecurityExecutionEvidenceProcessTests).Assembly.Location,
                [uid], Path.Combine(root, "results"), root, "owned-child.trx", 60);
            command.Environment["ISLAMU_RELEASE_NATIVE_CHILD_CONTROL"] = mode;
            command.Environment["ISLAMU_RELEASE_NATIVE_CHILD_ROOT"] = root;
            command.Environment["ISLAMU_RELEASE_NATIVE_CHILD_CANARY"] = Canary;
            return command;
        }

        internal bool OwnedChildrenRemain()
        {
            foreach (string receipt in Directory.EnumerateFiles(root, "pid-*"))
            {
                string[] identity = File.ReadAllText(receipt).Split(' ');
                int pid = int.Parse(identity[0], System.Globalization.CultureInfo.InvariantCulture);
                string stat = "/proc/" + pid + "/stat";
                if (File.Exists(stat))
                {
                    string value = File.ReadAllText(stat);
                    string[] fields = value[(value.LastIndexOf(')') + 2)..].Split(' ');
                    if (fields[19] == identity[1] && fields[0] != "Z") return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            foreach (string receipt in Directory.EnumerateFiles(root, "pid-*"))
            {
                string[] identity = File.ReadAllText(receipt).Split(' ');
                int pid = int.Parse(identity[0], System.Globalization.CultureInfo.InvariantCulture);
                try
                {
                    string value = File.ReadAllText("/proc/" + pid + "/stat");
                    string[] fields = value[(value.LastIndexOf(')') + 2)..].Split(' ');
                    if (fields[19] != identity[1] || fields[0] == "Z") continue;
                    using Process process = Process.GetProcessById(pid);
                    process.Kill(entireProcessTree: true);
                    if (!process.WaitForExit(5000)) throw new InvalidDataException("owned_child_cleanup_failed");
                }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or ArgumentException)
                {
                    // A disappeared or replaced PID is not an owned cleanup target.
                }
            }
            Directory.Delete(root, recursive: true);
        }
    }
}

public sealed class SecurityExecutionEvidenceCandidateTests
{
    [Test]
    public async Task ActualCleanDetachedCheckoutSuppliesItsCommitAndTree()
    {
        using var fixture = new SourceFixture();
        bool accepted = SecurityExecutionEvidence.TryReadCandidateSource(fixture.Root, fixture.Candidate,
            out var identity, out string diagnostic);
        await Assert.That(accepted).IsTrue();
        await Assert.That(identity!.CandidateOid).IsEqualTo(fixture.Candidate);
        await Assert.That(identity.TreeOid).IsEqualTo(fixture.Tree);
        await Assert.That(diagnostic).IsEqualTo(string.Empty);
    }

    [Test]
    [Arguments("wrong-candidate")]
    [Arguments("dirty-worktree")]
    [Arguments("dirty-index")]
    [Arguments("untracked-input")]
    [Arguments("assume-unchanged-input")]
    [Arguments("skip-worktree-input")]
    [Arguments("missing-repository")]
    [Arguments("invalid-candidate")]
    public async Task ClaimedCandidateCannotReplaceTheActualCleanSourceInputs(string variant)
    {
        using var fixture = new SourceFixture();
        string repository = fixture.Root;
        string expected = fixture.Candidate;
        switch (variant)
        {
            case "wrong-candidate": expected = new string('a', 40); break;
            case "dirty-worktree": File.AppendAllText(Path.Combine(repository, "input.txt"), "changed"); break;
            case "dirty-index":
                File.AppendAllText(Path.Combine(repository, "input.txt"), "changed");
                fixture.Git("add", "input.txt");
                break;
            case "untracked-input": File.WriteAllText(Path.Combine(repository, "untracked-input.txt"), "unowned"); break;
            case "assume-unchanged-input":
                fixture.Git("update-index", "--assume-unchanged", "input.txt");
                File.AppendAllText(Path.Combine(repository, "input.txt"), "hidden change");
                break;
            case "skip-worktree-input":
                fixture.Git("update-index", "--skip-worktree", "input.txt");
                File.AppendAllText(Path.Combine(repository, "input.txt"), "hidden change");
                break;
            case "missing-repository": repository = Path.Combine(repository, "missing"); break;
            case "invalid-candidate": expected = "--output=/outside"; break;
        }
        bool accepted = SecurityExecutionEvidence.TryReadCandidateSource(repository, expected,
            out var identity, out string diagnostic);
        await Assert.That(accepted).IsFalse();
        await Assert.That(identity is null).IsTrue();
        await Assert.That(diagnostic).IsNotEqualTo(string.Empty);
    }

    internal sealed class SourceFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "native-source-" + Guid.NewGuid().ToString("N"));
        internal string Candidate { get; }
        internal string Tree { get; }

        internal SourceFixture()
        {
            Directory.CreateDirectory(Root);
            Git("init", "--quiet");
            File.WriteAllText(Path.Combine(Root, "input.txt"), "native candidate input");
            Git("add", "input.txt");
            Git("-c", "user.name=Native Control", "-c", "user.email=native-control@invalid.example",
                "commit", "--quiet", "-m", "Native input control");
            Candidate = Git("rev-parse", "HEAD").Trim();
            Tree = Git("rev-parse", "HEAD^{tree}").Trim();
            Git("checkout", "--quiet", "--detach", Candidate);
        }

        internal string Git(params string[] arguments)
        {
            var info = new ProcessStartInfo(ReleaseToolPaths.Git)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Root, CreateNoWindow = true
            };
            info.Environment.Clear();
            info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            info.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = info };
            process.Start();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            bool finished = false;
            bool joined = false;
            try
            {
                finished = process.WaitForExit(5000);
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                joined = process.WaitForExit(5000);
            }
            if (!joined) throw new InvalidDataException("native_source_fixture_cleanup_failed");
            if (!finished) throw new InvalidDataException("native_source_fixture_timeout");
            if (process.ExitCode != 0) throw new InvalidDataException("native_source_fixture_failed");
            _ = stderr.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            return stdout.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

public sealed class SecurityExecutionEvidenceServedAssetTests
{
    [Test]
    public async Task ReferencedTrackedGeneratedAndExactLockedPackageBytesFormOnePrivateInputClosure()
    {
        using var fixture = new ServedAssetFixture();
        bool accepted = fixture.Capture(out var snapshot, out string diagnostic);
        await Assert.That(accepted).IsTrue();
        await Assert.That(diagnostic).IsEqualTo(string.Empty);
        await Assert.That(snapshot!.FileCount).IsEqualTo(5);
        await Assert.That(snapshot.PrivateManifestSha256)
            .IsEqualTo(Convert.ToHexString(SHA256.HashData(snapshot.PrivateManifest)).ToLowerInvariant());
        JsonNode manifest = JsonNode.Parse(snapshot.PrivateManifest)!;
        JsonArray files = manifest["files"]!.AsArray();
        await Assert.That(files.Count).IsEqualTo(5);
        foreach (var expected in new[]
        {
            (Root: 0, Path: fixture.TrackedAsset), (Root: 0, Path: fixture.GeneratedAsset),
            (Root: 0, Path: fixture.RuntimePath), (Root: 0, Path: fixture.EndpointsPath),
            (Root: 1, Path: fixture.PackageAsset)
        })
        {
            string relative = Path.GetRelativePath(expected.Root == 0 ? fixture.CandidateRoot : fixture.PackageRoot, expected.Path);
            JsonNode file = files.Single(item => item!["root"]!.GetValue<int>() == expected.Root &&
                item["path"]!.GetValue<string>() == relative)!;
            string path = expected.Path;
            byte[] bytes = File.ReadAllBytes(path);
            await Assert.That(file["sha256"]!.GetValue<string>())
                .IsEqualTo(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            await Assert.That(file["bytes"]!.GetValue<long>()).IsEqualTo(bytes.LongLength);
        }
        JsonNode tracked = files.Single(file => file!["path"]!.GetValue<string>() == "src/Fixture/wwwroot/tracked.bin")!;
        await Assert.That(tracked["gitBlobOid"]!.GetValue<string>())
            .IsEqualTo(fixture.Source.Git("rev-parse", fixture.Candidate + ":src/Fixture/wwwroot/tracked.bin").Trim());
    }

    [Test]
    [Arguments("generated")]
    [Arguments("locked-package")]
    public async Task IgnoredLocalServedBytesCannotHideBehindTheirOriginalSizeAndTimestamp(string kind)
    {
        using var fixture = new ServedAssetFixture();
        bool first = fixture.Capture(out var before, out _);
        await Assert.That(first).IsTrue();
        string path = kind == "generated" ? fixture.GeneratedAsset : fixture.PackageAsset;
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(32));
        File.SetLastWriteTimeUtc(path, timestamp);
        bool second = fixture.Capture(out var after, out _);
        await Assert.That(second).IsTrue();
        await Assert.That(after!.PrivateManifestSha256).IsNotEqualTo(before!.PrivateManifestSha256);
    }

    [Test]
    [Arguments("missing-paired-manifest")]
    [Arguments("malformed-runtime-manifest")]
    [Arguments("duplicate-runtime-key")]
    [Arguments("unknown-runtime-field")]
    [Arguments("invalid-content-index")]
    [Arguments("outside-subpath")]
    [Arguments("outside-content-root")]
    [Arguments("linked-asset")]
    [Arguments("untracked-source-asset")]
    [Arguments("tracked-source-byte-drift")]
    [Arguments("unsupported-pattern")]
    [Arguments("unsupported-endpoints-version")]
    [Arguments("unrepresented-endpoint-asset")]
    [Arguments("missing-generated-asset")]
    public async Task UnresolvedOrUnownedServedInputsCannotAcquireCandidateIdentity(string variant)
    {
        using var fixture = new ServedAssetFixture();
        bool saveRuntime = true;
        bool saveEndpoints = true;
        switch (variant)
        {
            case "missing-paired-manifest": File.Delete(fixture.EndpointsPath); saveEndpoints = false; break;
            case "malformed-runtime-manifest": File.WriteAllText(fixture.RuntimePath, "{"); saveRuntime = false; break;
            case "duplicate-runtime-key":
                File.WriteAllText(fixture.RuntimePath, "{\"ContentRoots\":[]," + fixture.Runtime.ToJsonString()[1..]);
                saveRuntime = false;
                break;
            case "unknown-runtime-field": fixture.Runtime["unregistered-private-field"] = "private-value-must-not-be-admitted"; break;
            case "invalid-content-index": fixture.Runtime["Root"]!["Children"]!["tracked.bin"]!["Asset"]!["ContentRootIndex"] = 3; break;
            case "outside-subpath": fixture.Runtime["Root"]!["Children"]!["tracked.bin"]!["Asset"]!["SubPath"] = "../unowned.bin"; break;
            case "outside-content-root": fixture.Runtime["ContentRoots"]![2] = Path.GetDirectoryName(fixture.PackageRoot); break;
            case "linked-asset":
                File.Delete(fixture.GeneratedAsset);
                File.CreateSymbolicLink(fixture.GeneratedAsset, fixture.PackageAsset);
                break;
            case "untracked-source-asset":
                File.WriteAllText(Path.Combine(fixture.CandidateRoot, ".git", "info", "exclude"), "/src/Fixture/wwwroot/ignored.bin\n");
                File.WriteAllBytes(Path.Combine(fixture.CandidateRoot, "src/Fixture/wwwroot/ignored.bin"), RandomNumberGenerator.GetBytes(32));
                break;
            case "tracked-source-byte-drift":
                DateTime timestamp = File.GetLastWriteTimeUtc(fixture.TrackedAsset);
                File.WriteAllBytes(fixture.TrackedAsset, RandomNumberGenerator.GetBytes(32));
                File.SetLastWriteTimeUtc(fixture.TrackedAsset, timestamp);
                break;
            case "unsupported-pattern": fixture.Runtime["Root"]!["Patterns"]![0]!["Pattern"] = "unsupported-[pattern"; break;
            case "unsupported-endpoints-version": fixture.EndpointManifest["Version"] = 2; break;
            case "unrepresented-endpoint-asset": fixture.EndpointManifest["Endpoints"]![0]!["AssetFile"] = "unrepresented.bin"; break;
            case "missing-generated-asset": File.Delete(fixture.GeneratedAsset); break;
        }
        if (saveRuntime) fixture.SaveRuntime();
        if (saveEndpoints) fixture.SaveEndpoints();
        bool accepted = fixture.Capture(out var snapshot, out string diagnostic);
        await Assert.That(accepted).IsFalse();
        await Assert.That(snapshot is null).IsTrue();
        await Assert.That(diagnostic).IsEqualTo("local_served_assets_invalid");
    }

    private sealed class ServedAssetFixture : IDisposable
    {
        internal SecurityExecutionEvidenceCandidateTests.SourceFixture Source { get; } = new();
        internal string CandidateRoot => Source.Root;
        internal string Candidate { get; }
        internal string PackageRoot { get; } = Path.Combine(Path.GetTempPath(), "native-asset-package-" + Guid.NewGuid().ToString("N"), "1.0.0", "staticwebassets");
        internal string GeneratedRoot => Path.Combine(CandidateRoot, "src/Fixture/obj/Release/net10.0/served");
        internal string OutputRoot => Path.Combine(CandidateRoot, "src/Fixture/bin/Release/net10.0");
        internal string TrackedAsset => Path.Combine(CandidateRoot, "src/Fixture/wwwroot/tracked.bin");
        internal string GeneratedAsset => Path.Combine(GeneratedRoot, "generated.bin");
        internal string PackageAsset => Path.Combine(PackageRoot, "locked.bin");
        internal string RuntimePath => Path.Combine(OutputRoot, "Fixture.staticwebassets.runtime.json");
        internal string EndpointsPath => Path.Combine(OutputRoot, "Fixture.staticwebassets.endpoints.json");
        internal JsonObject Runtime { get; }
        internal JsonObject EndpointManifest { get; }

        internal ServedAssetFixture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TrackedAsset)!);
            File.WriteAllBytes(TrackedAsset, RandomNumberGenerator.GetBytes(32));
            File.WriteAllText(Path.Combine(CandidateRoot, ".gitignore"), "**/obj/\n**/bin/\n");
            Source.Git("add", ".gitignore", "src/Fixture/wwwroot/tracked.bin");
            Source.Git("-c", "user.name=Native Control", "-c", "user.email=native-control@invalid.example",
                "commit", "--quiet", "-m", "Native served input control");
            Candidate = Source.Git("rev-parse", "HEAD").Trim();
            Directory.CreateDirectory(GeneratedRoot);
            Directory.CreateDirectory(OutputRoot);
            Directory.CreateDirectory(PackageRoot);
            File.WriteAllBytes(GeneratedAsset, RandomNumberGenerator.GetBytes(32));
            File.WriteAllBytes(PackageAsset, RandomNumberGenerator.GetBytes(32));
            Runtime = new JsonObject
            {
                ["ContentRoots"] = new JsonArray(Path.GetDirectoryName(TrackedAsset)!, GeneratedRoot, PackageRoot),
                ["Root"] = new JsonObject
                {
                    ["Asset"] = null,
                    ["Children"] = new JsonObject
                    {
                        ["tracked.bin"] = Asset(0, "tracked.bin"),
                        ["generated.bin"] = Asset(1, "generated.bin"),
                        ["_content"] = new JsonObject
                        {
                            ["Asset"] = null, ["Patterns"] = null,
                            ["Children"] = new JsonObject { ["locked.bin"] = Asset(2, "locked.bin") }
                        }
                    },
                    ["Patterns"] = new JsonArray(new JsonObject
                    {
                        ["ContentRootIndex"] = 0, ["Pattern"] = "**", ["Depth"] = 0
                    })
                }
            };
            EndpointManifest = new JsonObject
            {
                ["Version"] = 1, ["ManifestType"] = "Build",
                ["Endpoints"] = new JsonArray(Endpoint("tracked.bin"), Endpoint("generated.bin"), Endpoint("_content/locked.bin"))
            };
            SaveRuntime();
            SaveEndpoints();
        }

        private static JsonObject Asset(int root, string path) => new()
        {
            ["Asset"] = new JsonObject { ["ContentRootIndex"] = root, ["SubPath"] = path },
            ["Children"] = null, ["Patterns"] = null
        };

        private static JsonObject Endpoint(string file) => new()
        {
            ["AssetFile"] = file, ["Route"] = file, ["EndpointProperties"] = new JsonArray(),
            ["ResponseHeaders"] = new JsonArray(), ["Selectors"] = new JsonArray()
        };

        internal bool Capture(out SecurityExecutionEvidence.LocalServedAssetSnapshot? snapshot, out string diagnostic) =>
            SecurityExecutionEvidence.TryCaptureLocalServedAssets(CandidateRoot, Candidate, [RuntimePath],
                [GeneratedRoot, OutputRoot], [PackageRoot], out snapshot, out diagnostic);
        internal void SaveRuntime() => File.WriteAllText(RuntimePath, Runtime.ToJsonString());
        internal void SaveEndpoints() => File.WriteAllText(EndpointsPath, EndpointManifest.ToJsonString());

        public void Dispose()
        {
            Source.Dispose();
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(PackageRoot)!)!, recursive: true);
        }
    }
}

public sealed class SecurityExecutionEvidenceProjectClosureTests
{
    [Test]
    public async Task ActualGeneratedDepsBindsBothOwnedManagedRevisionsAndItsCompleteBytes()
    {
        using var fixture = new ProjectClosureFixture();
        bool accepted = SecurityExecutionEvidence.TryCaptureProjectManagedInputs(fixture.Root, fixture.Candidate,
            [fixture.EntryName], out var snapshot, out string diagnostic);
        await Assert.That(accepted).IsTrue();
        await Assert.That(diagnostic).IsEqualTo(string.Empty);
        await AssertClosureAsync(snapshot!, fixture);
    }

    [Test]
    public async Task CompileOnlyFrameworkReferencesRemainDistinctFromManagedProjectRuntimeInputs()
    {
        using var fixture = new ProjectClosureFixture();
        fixture.Libraries["System.Runtime.Reference/10.0.0.0"] = new JsonObject
        {
            ["type"] = "referenceassembly", ["serviceable"] = false, ["sha512"] = string.Empty
        };
        fixture.Target["System.Runtime.Reference/10.0.0.0"] = new JsonObject
        {
            ["compile"] = new JsonObject { ["System.Runtime.Reference.dll"] = new JsonObject() },
            ["compileOnly"] = true
        };
        fixture.SaveDeps();
        bool accepted = SecurityExecutionEvidence.TryCaptureProjectManagedInputs(fixture.Root, fixture.Candidate,
            [fixture.EntryName], out var snapshot, out _);
        await Assert.That(accepted).IsTrue();
        await AssertClosureAsync(snapshot!, fixture);
    }

    private static async Task AssertClosureAsync(SecurityExecutionEvidence.CompiledInputSnapshot snapshot,
        ProjectClosureFixture fixture)
    {
        JsonNode manifest = JsonNode.Parse(snapshot.PrivateManifest)!;
        JsonArray managed = manifest["managedEntries"]!.AsArray();
        await Assert.That(managed.Count).IsEqualTo(2);
        foreach (string name in new[] { fixture.EntryName, fixture.DependencyName })
        {
            JsonNode entry = managed.Single(node => node!["path"]!.GetValue<string>() == name)!;
            await Assert.That(entry["informationalVersion"]!.GetValue<string>()
                .EndsWith("+" + fixture.Candidate, StringComparison.Ordinal)).IsTrue();
        }
        JsonArray files = manifest["files"]!.AsArray();
        await Assert.That(files.Count).IsEqualTo(4);
        foreach (string name in new[] { fixture.EntryName, fixture.DependencyName, Path.GetFileName(fixture.DepsPath), "payload.bin" })
        {
            JsonNode file = files.Single(item => item!["path"]!.GetValue<string>() == name)!;
            byte[] bytes = File.ReadAllBytes(Path.Combine(fixture.Root, name));
            await Assert.That(file["sha256"]!.GetValue<string>())
                .IsEqualTo(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            await Assert.That(file["bytes"]!.GetValue<long>()).IsEqualTo(bytes.LongLength);
        }
    }

    [Test]
    [Arguments("missing-deps")]
    [Arguments("malformed-deps")]
    [Arguments("duplicate-root-key")]
    [Arguments("missing-selected-target")]
    [Arguments("missing-library")]
    [Arguments("missing-project-target")]
    [Arguments("unknown-library-kind")]
    [Arguments("missing-project-runtime")]
    [Arguments("outside-project-runtime")]
    [Arguments("missing-project-dll")]
    [Arguments("linked-project-dll")]
    [Arguments("wrong-transitive-revision")]
    [Arguments("ambiguous-project-owner")]
    [Arguments("selected-entry-not-project")]
    public async Task UnprovedTransitiveProjectClosureCannotAcquireCandidateIdentity(string variant)
    {
        using var fixture = new ProjectClosureFixture();
        bool save = true;
        switch (variant)
        {
            case "missing-deps": File.Delete(fixture.DepsPath); save = false; break;
            case "malformed-deps": File.WriteAllText(fixture.DepsPath, "{"); save = false; break;
            case "duplicate-root-key":
                File.WriteAllText(fixture.DepsPath, "{\"libraries\":{}," + fixture.Deps.ToJsonString()[1..]);
                save = false;
                break;
            case "missing-selected-target": fixture.Deps["runtimeTarget"]!["name"] = "unselected-target"; break;
            case "missing-library": fixture.Libraries.Remove(fixture.DependencyLibrary); break;
            case "missing-project-target": fixture.Target.Remove(fixture.DependencyLibrary); break;
            case "unknown-library-kind": fixture.Libraries[fixture.DependencyLibrary]!["type"] = "unproved-library-kind"; break;
            case "missing-project-runtime": fixture.Target[fixture.DependencyLibrary]!.AsObject().Remove("runtime"); break;
            case "outside-project-runtime":
                fixture.Target[fixture.DependencyLibrary]!["runtime"] = new JsonObject
                {
                    ["../" + fixture.DependencyName] = new JsonObject()
                };
                break;
            case "missing-project-dll": File.Delete(Path.Combine(fixture.Root, fixture.DependencyName)); break;
            case "linked-project-dll":
                File.Delete(Path.Combine(fixture.Root, fixture.DependencyName));
                File.CreateSymbolicLink(Path.Combine(fixture.Root, fixture.DependencyName),
                    typeof(SecurityExecutionEvidence).Assembly.Location);
                break;
            case "wrong-transitive-revision":
                File.Copy(typeof(object).Assembly.Location, Path.Combine(fixture.Root, fixture.DependencyName), overwrite: true);
                break;
            case "ambiguous-project-owner":
                fixture.Libraries["Ambiguous.Owned.Project/1.0.0"] = new JsonObject
                {
                    ["type"] = "project", ["serviceable"] = false, ["sha512"] = string.Empty
                };
                fixture.Target["Ambiguous.Owned.Project/1.0.0"] = new JsonObject
                {
                    ["runtime"] = new JsonObject { [fixture.DependencyName] = new JsonObject() }
                };
                break;
            case "selected-entry-not-project": fixture.Libraries[fixture.EntryLibrary]!["type"] = "package"; break;
        }
        if (save) fixture.SaveDeps();
        bool accepted = SecurityExecutionEvidence.TryCaptureProjectManagedInputs(fixture.Root, fixture.Candidate,
            [fixture.EntryName], out var snapshot, out string diagnostic);
        await Assert.That(accepted).IsFalse();
        await Assert.That(snapshot is null).IsTrue();
        await Assert.That(diagnostic).IsEqualTo("managed_project_closure_invalid");
    }

    private sealed class ProjectClosureFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "native-project-closure-" + Guid.NewGuid().ToString("N"));
        internal string EntryName { get; } = Path.GetFileName(typeof(SecurityExecutionEvidenceProjectClosureTests).Assembly.Location);
        internal string DependencyName { get; } = Path.GetFileName(typeof(SecurityExecutionEvidence).Assembly.Location);
        internal string Candidate { get; }
        internal string DepsPath => Path.Combine(Root, Path.ChangeExtension(EntryName, ".deps.json"));
        internal JsonObject Deps { get; }
        internal JsonObject Libraries => Deps["libraries"]!.AsObject();
        internal JsonObject Target => Deps["targets"]![Deps["runtimeTarget"]!["name"]!.GetValue<string>()]!.AsObject();
        internal string EntryLibrary { get; }
        internal string DependencyLibrary { get; }

        internal ProjectClosureFixture()
        {
            string entryPath = typeof(SecurityExecutionEvidenceProjectClosureTests).Assembly.Location;
            string dependencyPath = typeof(SecurityExecutionEvidence).Assembly.Location;
            string version = typeof(SecurityExecutionEvidence).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? throw new InvalidDataException("owned_project_revision_missing");
            int marker = version.LastIndexOf('+');
            Candidate = marker < 0 ? string.Empty : version[(marker + 1)..];
            if (Candidate.Length is not (40 or 64) || Candidate.Any(character =>
                    character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
                throw new InvalidDataException("owned_project_revision_missing");
            Deps = JsonNode.Parse(File.ReadAllBytes(Path.ChangeExtension(entryPath, ".deps.json")))!.AsObject();
            EntryLibrary = ProjectLibraryFor(EntryName);
            DependencyLibrary = ProjectLibraryFor(DependencyName);
            Directory.CreateDirectory(Root);
            File.Copy(entryPath, Path.Combine(Root, EntryName));
            File.Copy(dependencyPath, Path.Combine(Root, DependencyName));
            File.WriteAllBytes(Path.Combine(Root, "payload.bin"), RandomNumberGenerator.GetBytes(32));
            SaveDeps();
        }

        private string ProjectLibraryFor(string assembly) => Libraries.Single(library =>
            library.Value!["type"]!.GetValue<string>() == "project" &&
            Target[library.Key]?["runtime"] is JsonObject runtime && runtime.ContainsKey(assembly)).Key;

        internal void SaveDeps() => File.WriteAllText(DepsPath, Deps.ToJsonString());
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

public sealed class SecurityExecutionEvidenceCompiledInputTests
{
    [Test]
    public async Task FifoIsRejectedWithoutBlockingAnOwnedChildRead()
    {
        using var fixture = new SecurityExecutionEvidenceProcessTests.ProcessFixture();
        ProcessStartInfo command = await fixture.CommandAsync("fifo-input");
        var result = await SecurityExecutionEvidence.RunOwnedNativeProcessAsync(command, TimeSpan.FromSeconds(3), CancellationToken.None);
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.CleanupSucceeded).IsTrue();
        await Assert.That(fixture.OwnedChildrenRemain()).IsFalse();
    }

    [Test]
    public async Task ActualManagedRevisionAndEveryRegularInputByteEnterThePrivateManifest()
    {
        using var fixture = new CompiledFixture();
        bool accepted = SecurityExecutionEvidence.TryCaptureCompiledInputs(fixture.Root, fixture.Candidate,
            ["entry.dll"], out var snapshot, out string diagnostic);
        await Assert.That(accepted).IsTrue();
        await Assert.That(snapshot!.FileCount).IsEqualTo(2);
        await Assert.That(snapshot.PrivateManifestSha256).IsEqualTo(
            Convert.ToHexString(SHA256.HashData(snapshot.PrivateManifest)).ToLowerInvariant());
        await AssertManifestBytesAsync(snapshot, fixture);
        await Assert.That(diagnostic).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SameSizeAndTimestampCannotHideChangedCandidateInputBytes()
    {
        using var fixture = new CompiledFixture();
        bool first = SecurityExecutionEvidence.TryCaptureCompiledInputs(fixture.Root, fixture.Candidate,
            ["entry.dll"], out var before, out _);
        await Assert.That(first).IsTrue();
        await AssertManifestBytesAsync(before!, fixture);
        string payload = Path.Combine(fixture.Root, "payload.bin");
        DateTime timestamp = File.GetLastWriteTimeUtc(payload);
        File.WriteAllBytes(payload, Enumerable.Repeat((byte)2, 32).ToArray());
        File.SetLastWriteTimeUtc(payload, timestamp);
        bool second = SecurityExecutionEvidence.TryCaptureCompiledInputs(fixture.Root, fixture.Candidate,
            ["entry.dll"], out var after, out _);
        await Assert.That(second).IsTrue();
        await AssertManifestBytesAsync(after!, fixture);
        await Assert.That(after!.PrivateManifestSha256).IsNotEqualTo(before!.PrivateManifestSha256);
    }

    private static async Task AssertManifestBytesAsync(SecurityExecutionEvidence.CompiledInputSnapshot snapshot, CompiledFixture fixture)
    {
        JsonArray files = JsonNode.Parse(snapshot.PrivateManifest)!["files"]!.AsArray();
        await Assert.That(files.Count).IsEqualTo(2);
        foreach (string name in new[] { "entry.dll", "payload.bin" })
        {
            JsonNode file = files.Single(entry => entry!["path"]!.GetValue<string>() == name)!;
            byte[] actual = File.ReadAllBytes(Path.Combine(fixture.Root, name));
            await Assert.That(file["sha256"]!.GetValue<string>()).IsEqualTo(Convert.ToHexString(SHA256.HashData(actual)).ToLowerInvariant());
            await Assert.That(file["bytes"]!.GetValue<long>()).IsEqualTo(actual.LongLength);
        }
    }

    [Test]
    [Arguments("wrong-revision")]
    [Arguments("missing-entry")]
    [Arguments("bad-managed-image")]
    [Arguments("truncated-managed-image")]
    [Arguments("linked-file")]
    [Arguments("linked-directory")]
    [Arguments("root-link")]
    [Arguments("duplicate-entry")]
    [Arguments("outside-entry")]
    [Arguments("empty-entries")]
    public async Task UnprovedManagedInputsCannotAcquireACandidateIdentity(string variant)
    {
        using var fixture = new CompiledFixture();
        string root = fixture.Root;
        string expected = fixture.Candidate;
        IReadOnlyList<string> entries = ["entry.dll"];
        switch (variant)
        {
            case "wrong-revision": expected = new string('a', 40); break;
            case "missing-entry": File.Delete(Path.Combine(root, "entry.dll")); break;
            case "bad-managed-image": File.WriteAllText(Path.Combine(root, "entry.dll"), "not a managed image"); break;
            case "truncated-managed-image":
                using (var stream = File.OpenWrite(Path.Combine(root, "entry.dll"))) stream.SetLength(8);
                break;
            case "linked-file": File.CreateSymbolicLink(Path.Combine(root, "link.bin"), Path.Combine(fixture.ExternalRoot, "payload.bin")); break;
            case "linked-directory": Directory.CreateSymbolicLink(Path.Combine(root, "linked-directory"), fixture.ExternalRoot); break;
            case "root-link":
                string link = Path.Combine(root, "alias");
                Directory.CreateSymbolicLink(link, root);
                root = link;
                break;
            case "duplicate-entry": entries = ["entry.dll", "entry.dll"]; break;
            case "outside-entry": entries = ["../entry.dll"]; break;
            case "empty-entries": entries = []; break;
        }
        bool accepted = SecurityExecutionEvidence.TryCaptureCompiledInputs(root, expected, entries,
            out var snapshot, out string diagnostic);
        await Assert.That(accepted).IsFalse();
        await Assert.That(snapshot is null).IsTrue();
        await Assert.That(diagnostic).IsNotEqualTo(string.Empty);
    }

    internal sealed class CompiledFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "native-compiled-input-" + Guid.NewGuid().ToString("N"));
        internal string ExternalRoot { get; } = Path.Combine(Path.GetTempPath(), "native-compiled-external-" + Guid.NewGuid().ToString("N"));
        internal string Candidate { get; }

        internal CompiledFixture()
        {
            string version = typeof(SecurityExecutionEvidence).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? throw new InvalidDataException("owned_compiled_revision_missing");
            int marker = version.LastIndexOf('+');
            Candidate = marker < 0 ? string.Empty : version[(marker + 1)..];
            if (Candidate.Length is not (40 or 64) || Candidate.Any(character =>
                    character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
                throw new InvalidDataException("owned_compiled_revision_missing");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(ExternalRoot);
            File.Copy(typeof(SecurityExecutionEvidence).Assembly.Location, Path.Combine(Root, "entry.dll"));
            File.WriteAllBytes(Path.Combine(Root, "payload.bin"), Enumerable.Repeat((byte)1, 32).ToArray());
            File.WriteAllBytes(Path.Combine(ExternalRoot, "payload.bin"), Enumerable.Repeat((byte)3, 32).ToArray());
        }

        internal void CreateFifo()
        {
            var info = new ProcessStartInfo("/usr/bin/mkfifo")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            };
            info.ArgumentList.Add("--");
            info.ArgumentList.Add(Path.Combine(Root, "input.pipe"));
            using var process = new Process { StartInfo = info };
            process.Start();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            bool finished = false;
            bool joined = false;
            try { finished = process.WaitForExit(5000); }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                joined = process.WaitForExit(5000);
            }
            if (!finished || !joined || process.ExitCode != 0) throw new InvalidDataException("native_fifo_fixture_failed");
            _ = output.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
            _ = error.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
            Directory.Delete(ExternalRoot, recursive: true);
        }
    }
}
