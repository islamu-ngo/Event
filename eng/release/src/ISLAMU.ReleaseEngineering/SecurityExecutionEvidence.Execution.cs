using System.Security.Cryptography;
using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace ISLAMU.ReleaseEngineering;

public static partial class SecurityExecutionEvidence
{
    internal sealed record ObservedOwnedResource(string ContainerId, string ImageId, string Resource, bool Running);

    internal static bool TryReadOwnedResourceSnapshot(byte[] bytes, string owner,
        IReadOnlyDictionary<string, string> admittedImages, IReadOnlySet<string> initialContainerIds,
        out IReadOnlyList<ObservedOwnedResource>? resources,
        out string diagnostic)
    {
        resources = null;
        diagnostic = "owned_resource_snapshot_invalid";
        try
        {
            if (bytes.Length is < 1 or > MaximumReportBytes || owner.Length != 32 || !LowerHex(owner) ||
                admittedImages.Count is < 1 or > 65_536 || initialContainerIds.Count > 65_536 ||
                initialContainerIds.Any(id => id.Length != 64 || !LowerHex(id))) return false;
            var initial = new HashSet<string>(initialContainerIds, StringComparer.Ordinal);
            var images = admittedImages.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            if (images.Any(item => !BoundedIdentity(item.Key, 1024) || !FullLocalImageId(item.Value))) return false;
            using JsonDocument document = JsonDocument.Parse(bytes, DocumentOptions);
            JsonElement inventory = document.RootElement;
            if (inventory.ValueKind != JsonValueKind.Array || inventory.GetArrayLength() > 65_536 ||
                !UniqueProperties(inventory)) return false;
            var observed = new List<ObservedOwnedResource>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement item in inventory.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) return false;
                if (!item.TryGetProperty("Labels", out JsonElement labels) || labels.ValueKind == JsonValueKind.Null)
                    continue;
                if (labels.ValueKind != JsonValueKind.Object) return false;
                if (!labels.TryGetProperty("islamu.assurance.owner", out JsonElement selectedOwner))
                {
                    if (labels.TryGetProperty("islamu.assurance.resource", out _)) return false;
                    continue;
                }
                if (selectedOwner.ValueKind != JsonValueKind.String) return false;
                if (!string.Equals(selectedOwner.GetString(), owner, StringComparison.Ordinal)) continue;
                string id = Text(item, "Id");
                string image = Text(item, "ImageID");
                string reference = Text(item, "Image");
                string resource = Text(labels, "islamu.assurance.resource");
                string state = Text(item, "State");
                if (id.Length != 64 || !LowerHex(id) || initial.Contains(id) || !ids.Add(id) ||
                    !FullLocalImageId(image) || !images.TryGetValue(reference, out string? admitted) || image != admitted ||
                    resource is not ("keycloak" or "cerbos") ||
                    state is not ("created" or "restarting" or "running" or "removing" or "paused" or "exited" or "dead") ||
                    observed.Count >= 1024) return false;
                observed.Add(new(id, image, resource, state == "running"));
            }
            resources = observed.AsReadOnly();
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or
            FormatException or OverflowException or KeyNotFoundException)
        {
            return false;
        }
    }

    private static bool FullLocalImageId(string value) => value.Length == 71 &&
        value.StartsWith("sha256:", StringComparison.Ordinal) && LowerHex(value[7..]);

    internal static bool TryCaptureApiPrerequisiteInputs(string repositoryRoot, string candidate, string outputDirectory,
        out CompiledInputSnapshot? snapshot, out string diagnostic)
    {
        snapshot = null;
        diagnostic = "api_prerequisite_inputs_invalid";
        try
        {
            string repository = InputDirectory(repositoryRoot);
            string output = InputDirectory(outputDirectory);
            if (!TryReadCandidateSource(repository, candidate, out _, out _)) return false;
            Dictionary<string, string> tracked = ReadTrackedInputBlobs(repository, candidate);
            const string realm = "docker/keycloak/ISLAMU-realm.test.json";
            const string policies = "cerbos/policies/";
            if (!tracked.TryGetValue(realm, out string? realmBlob)) return false;
            var expected = new List<(string Source, string Output, string Blob)>
            {
                (realm, "TestAssets/ISLAMU-realm.test.json", realmBlob)
            };
            expected.AddRange(tracked.Where(item => item.Key.StartsWith(policies, StringComparison.Ordinal))
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => (item.Key, "TestAssets/" + item.Key, item.Value)));
            if (expected.Count is < 2 or > 16_384) return false;

            HashSet<string> Inventory(string directory, string relativeRoot)
            {
                var files = new HashSet<string>(StringComparer.Ordinal);
                var pending = new Stack<(string Path, int Depth)>();
                pending.Push((InputDirectory(directory), 0));
                int entries = 0;
                while (pending.TryPop(out var item))
                {
                    if (++entries > 65_536 || item.Depth > 64 || !NoInputLinks(item.Path))
                        throw new InvalidDataException("api_prerequisite_inputs_invalid");
                    FileAttributes attributes = File.GetAttributes(item.Path);
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        foreach (string child in Directory.EnumerateFileSystemEntries(item.Path))
                            pending.Push((child, item.Depth + 1));
                    }
                    else if (!files.Add(Path.GetRelativePath(relativeRoot, item.Path)))
                        throw new InvalidDataException("api_prerequisite_inputs_invalid");
                }
                return files;
            }

            if (!Inventory(Path.Combine(repository, "cerbos", "policies"), repository)
                    .SetEquals(expected.Skip(1).Select(item => item.Source)) ||
                !Inventory(Path.Combine(output, "TestAssets", "cerbos", "policies"), output)
                    .SetEquals(expected.Skip(1).Select(item => item.Output))) return false;
            string[] paths = expected.SelectMany(item => new[]
                { Path.Combine(repository, item.Source), Path.Combine(output, item.Output) }).ToArray();
            if (paths.Any(path => !NoInputLinks(path)) ||
                !AreRegularCandidateInputsAsync(paths).GetAwaiter().GetResult()) return false;
            var files = new List<object>();
            var captured = new List<(string FullPath, CompiledInputFile Input)>();
            long total = 0;
            foreach (var item in expected)
            {
                string path = Path.Combine(output, item.Output);
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                long size = stream.Length;
                if (size > MaximumReportBytes || (total = checked(total + size)) > MaximumReportBytes) return false;
                string hash = Convert.ToHexStringLower(SHA256.HashData(stream));
                if (stream.Length != size || !NoInputLinks(path)) return false;
                files.Add(new { sourcePath = item.Source, outputPath = item.Output, gitBlobOid = item.Blob, bytes = size, sha256 = hash });
                captured.Add((path, new(item.Output, size, hash)));
            }
            foreach (var batch in expected.Chunk(32))
            {
                string[] inputs = batch.SelectMany(item => new[]
                    { Path.Combine(repository, item.Source), Path.Combine(output, item.Output) }).ToArray();
                string[] arguments = ["hash-object", "--no-filters", "--", .. inputs];
                string[] hashes = ReadCandidateGitAsync(repository, arguments)
                    .GetAwaiter().GetResult().Split('\n');
                if (hashes.Length != inputs.Length + 1 || hashes[^1].Length != 0) return false;
                for (int index = 0; index < batch.Length; index++)
                    if (hashes[index * 2] != batch[index].Blob || hashes[index * 2 + 1] != batch[index].Blob) return false;
            }
            if (paths.Any(path => !NoInputLinks(path)) ||
                captured.Any(item => !CapturedRegularFileStillMatches(item.FullPath, item.Input)) ||
                !TryReadCandidateSource(repository, candidate, out _, out _)) return false;
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, candidateOid = candidate, files });
            if (manifest.Length > MaximumReportBytes) return false;
            snapshot = new(manifest, Convert.ToHexStringLower(SHA256.HashData(manifest)), files.Count);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or Win32Exception or TimeoutException or OperationCanceledException or
            NotSupportedException or OverflowException or FormatException)
        {
            return false;
        }
    }

    internal sealed record NativeInvocationInput(IReadOnlyList<string> SelectedUids, byte[] ReportBytes);
    internal sealed record NativeLaneInput(byte[] DiscoveryBytes, IReadOnlyList<NativeInvocationInput> Invocations);

    internal static bool TryCreateNativeRunProofs(IReadOnlyDictionary<string, NativeLaneInput> lanes,
        string? registerPath, out IReadOnlyDictionary<string, IReadOnlyList<byte[]>>? proofs, out string diagnostic)
    {
        proofs = null;
        diagnostic = "native_run_projection_invalid";
        try
        {
            var requirements = registerPath is null ? SecurityControlRegister.ExecutionRequirements
                : SecurityControlRegister.ReadExecutionRequirements(registerPath);
            if (!requirements.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(lanes.Keys)) return false;
            var runs = new HashSet<Guid>();
            var executions = new HashSet<Guid>();
            var result = new Dictionary<string, IReadOnlyList<byte[]>>(StringComparer.Ordinal);
            foreach ((string id, NativeLaneInput lane) in lanes)
            {
                foreach (NativeInvocationInput invocation in lane.Invocations)
                {
                    if (invocation.ReportBytes.Length is < 1 or > MaximumReportBytes ||
                        !TryReadReport(invocation.ReportBytes, out ExecutionReport report) || !runs.Add(report.RunId) ||
                        executions.Overlaps(report.Executions)) return false;
                    executions.UnionWith(report.Executions);
                }
                if (!TryCreateNativeLaneProofs(lane.DiscoveryBytes, lane.Invocations, id, registerPath,
                        out var laneProofs, out _)) return false;
                result.Add(id, laneProofs!);
            }
            proofs = result;
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            JsonException or XmlException or InvalidOperationException or FormatException or OverflowException or ArgumentException or
            KeyNotFoundException)
        {
            return false;
        }
    }

    internal static bool TryCreateNativeLaneProofs(byte[] discoveryBytes, IReadOnlyList<NativeInvocationInput> invocations,
        string laneId, string? registerPath, out IReadOnlyList<byte[]>? proofs, out string diagnostic)
    {
        proofs = null;
        diagnostic = "native_lane_projection_invalid";
        try
        {
            var requirements = registerPath is null ? SecurityControlRegister.ExecutionRequirements
                : SecurityControlRegister.ReadExecutionRequirements(registerPath);
            if (!requirements.TryGetValue(laneId, out var requirement) ||
                !TryReadNativeDiscovery(discoveryBytes, out var discovered)) return false;
            bool load = laneId is "owned-load-control" or "owned-load-correctness";
            bool owned = laneId.StartsWith("owned-", StringComparison.Ordinal);
            var allUids = discovered.ToDictionary(node => node.Uid, StringComparer.Ordinal);
            var identities = discovered.Select(node => node.Identity).ToHashSet();
            if ((owned ? !requirement.Tests.SetEquals(identities) : !requirement.Tests.IsSubsetOf(identities)) ||
                (load ? invocations.Count != discovered.Count : invocations.Count != 1)) return false;
            var registered = requirement.Tests.OrderBy(test => test.ClassName, StringComparer.Ordinal)
                .ThenBy(test => test.MethodName, StringComparer.Ordinal).ThenBy(test => test.TestName, StringComparer.Ordinal)
                .Select((test, index) => (test, index)).ToDictionary(item => item.test, item => item.index);
            var selected = new HashSet<string>(StringComparer.Ordinal);
            var runs = new HashSet<Guid>();
            var executions = new HashSet<Guid>();
            var projections = new List<byte[]>();
            string discoveryDigest = Convert.ToHexString(SHA256.HashData(discoveryBytes)).ToLowerInvariant();
            long reportBytes = 0;
            foreach (NativeInvocationInput invocation in invocations)
            {
                if (invocation.SelectedUids.Count is < 1 or > 65_536 || (load && invocation.SelectedUids.Count != 1) ||
                    invocation.ReportBytes.Length is < 1 or > MaximumReportBytes ||
                    (reportBytes = checked(reportBytes + invocation.ReportBytes.LongLength)) > MaximumReportBytes ||
                    !TryReadReport(invocation.ReportBytes, out ExecutionReport report) || !runs.Add(report.RunId) ||
                    executions.Overlaps(report.Executions)) return false;
                executions.UnionWith(report.Executions);
                var nodes = new List<NativeDiscoveredTest>();
                foreach (string uid in invocation.SelectedUids)
                {
                    if (!allUids.TryGetValue(uid, out NativeDiscoveredTest? node) || !selected.Add(uid)) return false;
                    nodes.Add(node);
                }
                if (!report.Tests.SetEquals(nodes.Select(node => node.Identity)) || report.Counts.Selected != nodes.Count) return false;
                byte[] projection = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schemaVersion = 1,
                    privateDiscoverySha256 = discoveryDigest,
                    privateTrxSha256 = Convert.ToHexString(SHA256.HashData(invocation.ReportBytes)).ToLowerInvariant(),
                    counts = new
                    {
                        selected = report.Counts.Selected, executed = report.Counts.Executed, passed = report.Counts.Passed,
                        failed = report.Counts.Failed, skipped = report.Counts.Skipped
                    },
                    nodes = nodes.Select((node, ordinal) => new
                    {
                        node = ordinal, requiredTest = registered.TryGetValue(node.Identity, out int index) ? (int?)index : null,
                        outcome = "passed"
                    })
                });
                projections.Add(projection);
            }
            if (!selected.SetEquals(allUids.Keys) ||
                !TryValidateNativeLaneProofs(projections, laneId, registerPath, out _)) return false;
            proofs = projections;
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            JsonException or XmlException or InvalidOperationException or FormatException or OverflowException or ArgumentException or
            KeyNotFoundException)
        {
            return false;
        }
    }

    internal static bool TryValidateNativeLaneProofs(IReadOnlyList<byte[]> proofs, string laneId, string? registerPath,
        out string diagnostic)
    {
        diagnostic = "native_lane_proof_invalid";
        try
        {
            var requirements = registerPath is null ? SecurityControlRegister.ExecutionRequirements
                : SecurityControlRegister.ReadExecutionRequirements(registerPath);
            if (!requirements.TryGetValue(laneId, out var requirement)) return false;
            bool load = laneId is "owned-load-control" or "owned-load-correctness";
            bool owned = laneId.StartsWith("owned-", StringComparison.Ordinal);
            if ((load ? proofs.Count != requirement.Tests.Count : proofs.Count != 1) ||
                proofs.Sum(proof => (long)proof.Length) > MaximumReportBytes) return false;
            var references = new HashSet<int>();
            var reports = new HashSet<string>(StringComparer.Ordinal);
            string? discovery = null;
            foreach (byte[] bytes in proofs)
            {
                if (bytes.Length is < 1 or > MaximumReportBytes) return false;
                using JsonDocument document = JsonDocument.Parse(bytes, DocumentOptions);
                JsonElement proof = document.RootElement;
                if (!UniqueProperties(proof) || !ExactFields(proof, "schemaVersion", "privateDiscoverySha256", "privateTrxSha256", "counts", "nodes") ||
                    proof.GetProperty("schemaVersion").GetInt32() != 1) return false;
                string discoveryHash = Text(proof, "privateDiscoverySha256");
                string reportHash = Text(proof, "privateTrxSha256");
                if (discoveryHash.Length != 64 || !LowerHex(discoveryHash) || reportHash.Length != 64 || !LowerHex(reportHash) ||
                    !reports.Add(reportHash) || (discovery is not null && discovery != discoveryHash)) return false;
                discovery = discoveryHash;
                JsonElement countsElement = proof.GetProperty("counts");
                JsonElement nodes = proof.GetProperty("nodes");
                if (!ExactFields(countsElement, "selected", "executed", "passed", "failed", "skipped") ||
                    nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() is < 1 or > 65_536 ||
                    (load && nodes.GetArrayLength() != 1)) return false;
                ExecutionCounts counts = JsonCounts(countsElement);
                if (counts.Selected != nodes.GetArrayLength() || counts.Executed != counts.Selected || counts.Passed != counts.Selected ||
                    counts.Failed != 0 || counts.Skipped != 0) return false;
                int ordinal = 0;
                foreach (JsonElement node in nodes.EnumerateArray())
                {
                    if (!ExactFields(node, "node", "requiredTest", "outcome") ||
                        node.GetProperty("node").GetInt32() != ordinal++ || Text(node, "outcome") != "passed") return false;
                    JsonElement reference = node.GetProperty("requiredTest");
                    if (reference.ValueKind == JsonValueKind.Null)
                    {
                        if (owned) return false;
                    }
                    else
                    {
                        int index = reference.GetInt32();
                        if (index < 0 || index >= requirement.Tests.Count || !references.Add(index)) return false;
                    }
                }
            }
            if (references.Count != requirement.Tests.Count) return false;
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException or KeyNotFoundException)
        {
            return false;
        }
    }

    internal static bool TryCaptureLocalServedAssets(string repositoryRoot, string candidate,
        IReadOnlyList<string> runtimeManifests, IReadOnlyList<string> generatedRoots,
        IReadOnlyList<string> lockedPackageRoots, out LocalServedAssetSnapshot? snapshot, out string diagnostic)
    {
        snapshot = null;
        diagnostic = "local_served_assets_invalid";
        try
        {
            if (!OperatingSystem.IsLinux() || runtimeManifests.Count is < 1 or > 64 ||
                generatedRoots.Count is < 1 or > 128 || lockedPackageRoots.Count > 128) return false;
            string repository = InputDirectory(repositoryRoot);
            string[] generated = generatedRoots.Select(InputDirectory).Distinct(StringComparer.Ordinal).ToArray();
            string[] packages = lockedPackageRoots.Select(InputDirectory).Order(StringComparer.Ordinal).ToArray();
            if (generated.Length != generatedRoots.Count || packages.Distinct(StringComparer.Ordinal).Count() != packages.Length ||
                generated.Any(path => !InputWithin(path, repository) ||
                    !Path.GetRelativePath(repository, path).Split('/').Any(segment => segment is "bin" or "obj")) ||
                packages.Any(path => path == repository || InputWithin(path, repository) || InputWithin(repository, path)) ||
                packages.Any(path => packages.Any(other => path != other && InputWithin(path, other))) ||
                !TryReadCandidateSource(repository, candidate, out _, out _)) return false;
            Dictionary<string, string> tracked = ReadTrackedInputBlobs(repository, candidate);
            var inputs = new Dictionary<string, LocalAssetWork>(StringComparer.Ordinal);
            var metadata = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long metadataBytes = 0;

            void Register(string path)
            {
                string full = Path.GetFullPath(path);
                if (inputs.ContainsKey(full)) return;
                if (inputs.Count >= 65_536 || !NoInputLinks(full)) throw new InvalidDataException("local_input_invalid");
                int root = 0;
                string relative;
                string? blob = null;
                if (InputWithin(full, repository))
                {
                    relative = Path.GetRelativePath(repository, full);
                    if (!generated.Any(directory => InputWithin(full, directory)) && !tracked.TryGetValue(relative, out blob))
                        throw new InvalidDataException("local_input_untracked");
                }
                else
                {
                    int package = Array.FindIndex(packages, directory => InputWithin(full, directory));
                    if (package < 0) throw new InvalidDataException("local_input_outside");
                    root = package + 1;
                    relative = Path.GetRelativePath(packages[package], full);
                }
                if (!SafeInputRelativePath(relative)) throw new InvalidDataException("local_input_outside");
                inputs.Add(full, new(root, relative, full, blob));
            }

            byte[] ReadManifest(string path)
            {
                Register(path);
                if (!generated.Any(directory => InputWithin(path, directory)) || metadata.ContainsKey(path) ||
                    !AreRegularCandidateInputsAsync([path]).GetAwaiter().GetResult()) throw new InvalidDataException("local_manifest_invalid");
                byte[] bytes = ReadBoundedFile(path, MaximumReportBytes);
                metadataBytes = checked(metadataBytes + bytes.LongLength);
                if (metadataBytes > MaximumReportBytes) throw new InvalidDataException("local_manifest_oversized");
                metadata.Add(path, bytes);
                return bytes;
            }

            var manifests = new HashSet<string>(StringComparer.Ordinal);
            foreach (string declared in runtimeManifests)
            {
                if (!Path.IsPathFullyQualified(declared)) return false;
                string path = Path.GetFullPath(declared);
                const string runtimeSuffix = ".staticwebassets.runtime.json";
                if (!path.EndsWith(runtimeSuffix, StringComparison.Ordinal) || !manifests.Add(path)) return false;
                string endpointsPath = path[..^runtimeSuffix.Length] + ".staticwebassets.endpoints.json";
                using var runtime = JsonDocument.Parse(ReadManifest(path), new JsonDocumentOptions { MaxDepth = 64 });
                using var endpoints = JsonDocument.Parse(ReadManifest(endpointsPath), new JsonDocumentOptions { MaxDepth = 64 });
                JsonElement runtimeRoot = runtime.RootElement;
                if (!UniqueInputProperties(runtimeRoot) || !ExactFields(runtimeRoot, "ContentRoots", "Root") ||
                    runtimeRoot.GetProperty("ContentRoots").ValueKind != JsonValueKind.Array) return false;
                JsonElement[] roots = runtimeRoot.GetProperty("ContentRoots").EnumerateArray().ToArray();
                if (roots.Length is < 1 or > 256 || roots.Any(root => root.ValueKind != JsonValueKind.String)) return false;
                string[] contentRoots = roots.Select(root => InputDirectory(root.GetString()!)).ToArray();
                if (contentRoots.Distinct(StringComparer.Ordinal).Count() != contentRoots.Length ||
                    contentRoots.Any(root => root != repository && !InputWithin(root, repository) &&
                        !packages.Any(package => root == package || InputWithin(root, package)))) return false;
                HashSet<string> logicalAssets = CollectLocalRuntimeAssets(runtimeRoot.GetProperty("Root"), contentRoots, Register);
                if (!ValidLocalAssetEndpoints(endpoints.RootElement, logicalAssets)) return false;
            }

            LocalAssetWork[] work = inputs.Values.OrderBy(input => input.FullPath, StringComparer.Ordinal).ToArray();
            if (!AreRegularCandidateInputsAsync(work.Select(input => input.FullPath).ToArray()).GetAwaiter().GetResult()) return false;
            var files = new List<object>();
            long total = 0;
            foreach (LocalAssetWork input in work)
            {
                using var stream = new FileStream(input.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                long size = stream.Length;
                if (size > 1_073_741_824 || (total = checked(total + size)) > 17_179_869_184) return false;
                string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                if (stream.Length != size || (File.GetAttributes(input.FullPath) & FileAttributes.ReparsePoint) != 0) return false;
                if (metadata.TryGetValue(input.FullPath, out byte[]? parsed) &&
                    (parsed.LongLength != size || !string.Equals(Convert.ToHexString(SHA256.HashData(parsed)), hash,
                        StringComparison.OrdinalIgnoreCase))) return false;
                files.Add(new { root = input.Root, path = input.Relative, bytes = size, sha256 = hash, gitBlobOid = input.GitBlobOid });
            }
            foreach (LocalAssetWork[] batch in work.Where(input => input.GitBlobOid is not null).Chunk(64))
            {
                string[] arguments = ["hash-object", "--no-filters", "--", .. batch.Select(input => input.FullPath)];
                string[] hashes = ReadCandidateGitAsync(repository, arguments).GetAwaiter().GetResult().Split('\n');
                if (hashes.Length != batch.Length + 1 || hashes[^1].Length != 0) return false;
                for (int index = 0; index < batch.Length; index++) if (hashes[index] != batch[index].GitBlobOid) return false;
            }
            if (!TryReadCandidateSource(repository, candidate, out _, out _)) return false;
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1, candidateOid = candidate,
                roots = new[] { new { kind = "candidate", path = repository } }
                    .Concat(packages.Select(path => new { kind = "locked-package", path })),
                files
            });
            if (manifest.Length > MaximumReportBytes) return false;
            snapshot = new(manifest, Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant(), files.Count);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or OverflowException or NotSupportedException or JsonException or
            Win32Exception or TimeoutException or OperationCanceledException or KeyNotFoundException or FormatException)
        {
            return false;
        }
    }

    internal sealed record LocalServedAssetSnapshot(byte[] PrivateManifest, string PrivateManifestSha256, int FileCount);
    private sealed record LocalAssetWork(int Root, string Relative, string FullPath, string? GitBlobOid);

    private static string InputDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("input_directory_invalid");
        string full = Path.GetFullPath(path).TrimEnd('/');
        if (full.Length == 0 || !Directory.Exists(full) || !NoInputLinks(full)) throw new InvalidDataException("input_directory_invalid");
        return full;
    }

    private static bool InputWithin(string path, string directory) => path.StartsWith(directory + "/", StringComparison.Ordinal);

    private static bool NoInputLinks(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }

    private static Dictionary<string, string> ReadTrackedInputBlobs(string repository, string candidate)
    {
        string output = ReadCandidateGitAsync(repository, "ls-tree", "-r", "-z", "--full-tree", candidate).GetAwaiter().GetResult();
        var tracked = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string row in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = row.IndexOf('\t', StringComparison.Ordinal);
            if (tab < 0 || tracked.Count >= 100_000) throw new InvalidDataException("tracked_input_invalid");
            string[] identity = row[..tab].Split(' ');
            if (identity.Length != 3) throw new InvalidDataException("tracked_input_invalid");
            if (identity[0] is not ("100644" or "100755") || identity[1] != "blob") continue;
            string relative = row[(tab + 1)..];
            if (!SafeInputRelativePath(relative) || identity[2].Length != candidate.Length || !LowerHex(identity[2]) ||
                !tracked.TryAdd(relative, identity[2])) throw new InvalidDataException("tracked_input_invalid");
        }
        return tracked;
    }

    private static HashSet<string> CollectLocalRuntimeAssets(JsonElement root, string[] contentRoots, Action<string> register)
    {
        var logical = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new Stack<(JsonElement Value, string Url, int Depth)>();
        var patterns = new List<(string Url, int Root)>();
        nodes.Push((root, string.Empty, 0));
        int visited = 0;
        while (nodes.TryPop(out var node))
        {
            if (++visited > 100_000 || node.Depth > 64 || !ExactFields(node.Value, "Asset", "Children", "Patterns"))
                throw new InvalidDataException("runtime_asset_node_invalid");
            JsonElement asset = node.Value.GetProperty("Asset");
            if (asset.ValueKind != JsonValueKind.Null)
            {
                if (!ExactFields(asset, "ContentRootIndex", "SubPath")) throw new InvalidDataException("runtime_asset_invalid");
                int index = asset.GetProperty("ContentRootIndex").GetInt32();
                string? relative = asset.GetProperty("SubPath").GetString();
                if (index < 0 || index >= contentRoots.Length || relative is null || !SafeInputRelativePath(relative) ||
                    !SafeInputRelativePath(node.Url)) throw new InvalidDataException("runtime_asset_invalid");
                register(Path.Combine(contentRoots[index], relative));
                logical.Add(node.Url);
            }
            JsonElement children = node.Value.GetProperty("Children");
            if (children.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty child in children.EnumerateObject())
                {
                    if (!SafeInputRelativePath(child.Name) || child.Name.Contains('/', StringComparison.Ordinal))
                        throw new InvalidDataException("runtime_asset_child_invalid");
                    string url = node.Url.Length == 0 ? child.Name : node.Url + "/" + child.Name;
                    if (!SafeInputRelativePath(url)) throw new InvalidDataException("runtime_asset_child_invalid");
                    nodes.Push((child.Value, url, node.Depth + 1));
                }
            }
            else if (children.ValueKind != JsonValueKind.Null) throw new InvalidDataException("runtime_asset_children_invalid");
            JsonElement declaredPatterns = node.Value.GetProperty("Patterns");
            if (declaredPatterns.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement pattern in declaredPatterns.EnumerateArray())
                {
                    if (!ExactFields(pattern, "ContentRootIndex", "Depth", "Pattern") || pattern.GetProperty("Depth").GetInt32() != 0 ||
                        pattern.GetProperty("Pattern").GetString() != "**" || patterns.Count >= 256)
                        throw new InvalidDataException("runtime_asset_pattern_invalid");
                    int index = pattern.GetProperty("ContentRootIndex").GetInt32();
                    if (index < 0 || index >= contentRoots.Length) throw new InvalidDataException("runtime_asset_pattern_invalid");
                    patterns.Add((node.Url, index));
                }
            }
            else if (declaredPatterns.ValueKind != JsonValueKind.Null) throw new InvalidDataException("runtime_asset_patterns_invalid");
        }
        int inventory = 0;
        foreach (var pattern in patterns)
        {
            string directory = contentRoots[pattern.Root];
            var pending = new Stack<(string Path, int Depth)>();
            pending.Push((directory, 0));
            while (pending.TryPop(out var item))
            {
                if (++inventory > 100_000 || item.Depth > 64 || !NoInputLinks(item.Path)) throw new InvalidDataException("runtime_asset_inventory_invalid");
                if ((File.GetAttributes(item.Path) & FileAttributes.Directory) != 0)
                {
                    foreach (string child in Directory.EnumerateFileSystemEntries(item.Path)) pending.Push((child, item.Depth + 1));
                }
                else
                {
                    string relative = Path.GetRelativePath(directory, item.Path);
                    string url = pattern.Url.Length == 0 ? relative : pattern.Url + "/" + relative;
                    if (!SafeInputRelativePath(url)) throw new InvalidDataException("runtime_asset_pattern_invalid");
                    register(item.Path);
                    logical.Add(url);
                }
            }
        }
        return logical;
    }

    private static bool ValidLocalAssetEndpoints(JsonElement root, IReadOnlySet<string> logical)
    {
        if (!UniqueInputProperties(root) || !ExactFields(root, "Version", "ManifestType", "Endpoints") ||
            root.GetProperty("Version").GetInt32() != 1 || root.GetProperty("ManifestType").GetString() != "Build" ||
            root.GetProperty("Endpoints").ValueKind != JsonValueKind.Array) return false;
        JsonElement[] endpoints = root.GetProperty("Endpoints").EnumerateArray().ToArray();
        if (endpoints.Length is < 1 or > 65_536) return false;
        foreach (JsonElement endpoint in endpoints)
        {
            if (!ExactFields(endpoint, "AssetFile", "Route", "EndpointProperties", "ResponseHeaders", "Selectors") ||
                endpoint.GetProperty("AssetFile").ValueKind != JsonValueKind.String || endpoint.GetProperty("Route").ValueKind != JsonValueKind.String ||
                !logical.Contains(endpoint.GetProperty("AssetFile").GetString()!) ||
                !SafeInputRelativePath(endpoint.GetProperty("Route").GetString()!)) return false;
            foreach (string property in new[] { "EndpointProperties", "ResponseHeaders", "Selectors" })
            {
                JsonElement values = endpoint.GetProperty(property);
                if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 64) return false;
                foreach (JsonElement value in values.EnumerateArray())
                {
                    bool selector = property == "Selectors";
                    if (!(selector ? ExactFields(value, "Name", "Value", "Quality") : ExactFields(value, "Name", "Value")) ||
                        value.EnumerateObject().Any(field => field.Value.ValueKind != JsonValueKind.String || field.Value.GetString()!.Length > 4096)) return false;
                }
            }
        }
        return true;
    }

    internal static bool TryCaptureProjectManagedInputs(string directory, string candidate,
        IReadOnlyList<string> selectedAssemblies, out CompiledInputSnapshot? snapshot, out string diagnostic)
    {
        snapshot = null;
        diagnostic = "managed_project_closure_invalid";
        try
        {
            directory = InputDirectory(directory);
            if (!TryCaptureCompiledInputs(directory, candidate, selectedAssemblies, out var captured, out _)) return false;
            using var inventory = JsonDocument.Parse(captured!.PrivateManifest);
            var files = inventory.RootElement.GetProperty("files").EnumerateArray().ToDictionary(
                file => file.GetProperty("path").GetString()!,
                file => new CompiledInputFile(file.GetProperty("path").GetString()!,
                    file.GetProperty("bytes").GetInt64(), file.GetProperty("sha256").GetString()!), StringComparer.Ordinal);
            var projectOwners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string entry in selectedAssemblies)
            {
                string depsPath = Path.ChangeExtension(entry, ".deps.json");
                byte[] bytes = ReadCapturedInputBytes(directory, depsPath, files);
                using var deps = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
                JsonElement root = deps.RootElement;
                if (!UniqueInputProperties(root) ||
                    !ExactFields(root, "runtimeTarget", "compilationOptions", "targets", "libraries")) return false;
                JsonElement runtimeTarget = root.GetProperty("runtimeTarget");
                JsonElement targets = root.GetProperty("targets");
                JsonElement libraries = root.GetProperty("libraries");
                if (!ExactFields(runtimeTarget, "name", "signature") ||
                    runtimeTarget.GetProperty("name").ValueKind != JsonValueKind.String ||
                    runtimeTarget.GetProperty("signature").ValueKind != JsonValueKind.String ||
                    targets.ValueKind != JsonValueKind.Object || libraries.ValueKind != JsonValueKind.Object ||
                    root.GetProperty("compilationOptions").ValueKind != JsonValueKind.Object) return false;
                string targetName = runtimeTarget.GetProperty("name").GetString()!;
                if (!BoundedIdentity(targetName, 512) || !targets.TryGetProperty(targetName, out JsonElement target) ||
                    target.ValueKind != JsonValueKind.Object) return false;
                JsonProperty[] declarations = libraries.EnumerateObject().ToArray();
                if (declarations.Length is < 1 or > 16_384 || target.EnumerateObject().Count() != declarations.Length) return false;
                var entryProjects = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty library in declarations)
                {
                    if (!BoundedIdentity(library.Name, 1024) || library.Value.ValueKind != JsonValueKind.Object ||
                        !library.Value.TryGetProperty("type", out JsonElement kind) || kind.ValueKind != JsonValueKind.String ||
                        !target.TryGetProperty(library.Name, out JsonElement assets) || assets.ValueKind != JsonValueKind.Object) return false;
                    switch (kind.GetString())
                    {
                        case "package": break;
                        case "referenceassembly":
                            if (!assets.TryGetProperty("compileOnly", out JsonElement compileOnly) || compileOnly.ValueKind != JsonValueKind.True ||
                                assets.TryGetProperty("runtime", out _) || assets.TryGetProperty("runtimeTargets", out _)) return false;
                            break;
                        case "project":
                            if (!assets.TryGetProperty("runtime", out JsonElement runtime) || runtime.ValueKind != JsonValueKind.Object ||
                                assets.TryGetProperty("runtimeTargets", out _)) return false;
                            JsonProperty[] runtimeFiles = runtime.EnumerateObject().ToArray();
                            if (runtimeFiles.Length is < 1 or > 128) return false;
                            foreach (JsonProperty asset in runtimeFiles)
                            {
                                string relative = asset.Name;
                                if (!SafeInputRelativePath(relative) || !relative.EndsWith(".dll", StringComparison.Ordinal) ||
                                    !files.ContainsKey(relative) || asset.Value.ValueKind != JsonValueKind.Object) return false;
                                if (projectOwners.TryGetValue(relative, out string? owner) && owner != library.Name) return false;
                                projectOwners[relative] = library.Name;
                                entryProjects.Add(relative);
                            }
                            break;
                        default: return false;
                    }
                }
                if (!entryProjects.Contains(entry)) return false;
            }
            if (projectOwners.Count is < 1 or > 128) return false;
            var managed = new List<object>();
            foreach (string relative in projectOwners.Keys.Order(StringComparer.Ordinal))
            {
                string path = Path.Combine(directory, relative);
                if (!AreRegularCandidateInputsAsync([path]).GetAwaiter().GetResult() ||
                    (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
                string version = ReadManagedInformationalVersion(path);
                if (!version.EndsWith("+" + candidate, StringComparison.Ordinal) ||
                    !CapturedRegularFileStillMatches(path, files[relative])) return false;
                managed.Add(new { path = relative, informationalVersion = version });
            }
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1, candidateOid = candidate,
                files = inventory.RootElement.GetProperty("files"), managedEntries = managed
            });
            if (manifest.Length > MaximumReportBytes) return false;
            snapshot = new(manifest, Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant(), captured.FileCount);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or BadImageFormatException or OverflowException or NotSupportedException or
            JsonException or Win32Exception or TimeoutException or OperationCanceledException or KeyNotFoundException)
        {
            return false;
        }
    }

    private static byte[] ReadCapturedInputBytes(string directory, string relative,
        IReadOnlyDictionary<string, CompiledInputFile> files)
    {
        if (!files.TryGetValue(relative, out CompiledInputFile? input) || input.Bytes > MaximumReportBytes)
            throw new InvalidDataException("captured_input_missing");
        string path = Path.Combine(directory, relative);
        if (!AreRegularCandidateInputsAsync([path]).GetAwaiter().GetResult() ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("captured_input_invalid");
        byte[] bytes = ReadBoundedFile(path, MaximumReportBytes);
        if (bytes.LongLength != input.Bytes || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), input.Sha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("captured_input_changed");
        return bytes;
    }

    private static bool CapturedRegularFileStillMatches(string path, CompiledInputFile input)
    {
        if (!AreRegularCandidateInputsAsync([path]).GetAwaiter().GetResult() ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != input.Bytes) return false;
        string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return hash == input.Sha256 && stream.Length == input.Bytes &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    }

    private static bool UniqueInputProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
                if (!names.Add(property.Name) || !UniqueInputProperties(property.Value)) return false;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray()) if (!UniqueInputProperties(item)) return false;
        }
        return true;
    }

    internal static bool TryCaptureCompiledInputs(string directory, string candidate,
        IReadOnlyList<string> entryAssemblies, out CompiledInputSnapshot? snapshot, out string diagnostic)
    {
        snapshot = null;
        diagnostic = "compiled_inputs_invalid";
        try
        {
            if (!OperatingSystem.IsLinux() || candidate.Length is not (40 or 64) || !LowerHex(candidate) ||
                !Path.IsPathFullyQualified(directory) || !Directory.Exists(directory) ||
                entryAssemblies.Count is < 1 or > 128 || entryAssemblies.Distinct(StringComparer.Ordinal).Count() != entryAssemblies.Count ||
                entryAssemblies.Any(entry => !SafeInputRelativePath(entry) || !entry.EndsWith(".dll", StringComparison.Ordinal))) return false;
            var files = new Dictionary<string, CompiledInputFile>(StringComparer.Ordinal);
            var regularInputs = new List<(string Path, string Relative)>();
            var pending = new Stack<(string Path, int Depth)>();
            pending.Push((directory, 0));
            int inventory = 0;
            long total = 0;
            while (pending.TryPop(out var item))
            {
                FileAttributes attributes = File.GetAttributes(item.Path);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || item.Depth > 64 || ++inventory > 100_000) return false;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    foreach (string child in Directory.EnumerateFileSystemEntries(item.Path)) pending.Push((child, item.Depth + 1));
                    continue;
                }
                string relative = Path.GetRelativePath(directory, item.Path);
                if (!SafeInputRelativePath(relative) || regularInputs.Count >= 65_536) return false;
                regularInputs.Add((item.Path, relative));
            }
            if (!AreRegularCandidateInputsAsync(regularInputs.Select(input => input.Path).ToArray())
                    .GetAwaiter().GetResult()) return false;
            foreach (var input in regularInputs)
            {
                using var stream = new FileStream(input.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                long size = stream.Length;
                if (size > 1_073_741_824 || (total = checked(total + size)) > 17_179_869_184) return false;
                string digest = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                if (stream.Length != size || (File.GetAttributes(input.Path) & FileAttributes.ReparsePoint) != 0 ||
                    !files.TryAdd(input.Relative, new(input.Relative, size, digest))) return false;
            }
            var managed = new List<object>();
            foreach (string entry in entryAssemblies.Order(StringComparer.Ordinal))
            {
                if (!files.ContainsKey(entry)) return false;
                string version = ReadManagedInformationalVersion(Path.Combine(directory, entry));
                if (!version.EndsWith("+" + candidate, StringComparison.Ordinal)) return false;
                managed.Add(new { path = entry, informationalVersion = version });
            }
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1, candidateOid = candidate,
                files = files.Values.OrderBy(file => file.Path, StringComparer.Ordinal)
                    .Select(file => new { path = file.Path, bytes = file.Bytes, sha256 = file.Sha256 }),
                managedEntries = managed
            });
            if (manifest.Length > MaximumReportBytes) return false;
            snapshot = new(manifest, Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant(), files.Count);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or BadImageFormatException or OverflowException or NotSupportedException or
            Win32Exception or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    internal sealed record CompiledInputSnapshot(byte[] PrivateManifest, string PrivateManifestSha256, int FileCount);
    private sealed record CompiledInputFile(string Path, long Bytes, string Sha256);

    private static bool SafeInputRelativePath(string path) => path.Length is > 0 and <= 4096 &&
        !Path.IsPathRooted(path) && !path.Contains('\\', StringComparison.Ordinal) &&
        path.Split('/').All(part => part.Length > 0 && part is not ("." or ".."));

    private static async Task<bool> AreRegularCandidateInputsAsync(IReadOnlyList<string> paths)
    {
        foreach (string[] batch in paths.Chunk(64))
        {
            var info = new ProcessStartInfo("/usr/bin/stat")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true
            };
            info.Environment.Clear();
            info.Environment["LC_ALL"] = "C";
            info.ArgumentList.Add("--format=%f");
            info.ArgumentList.Add("--");
            foreach (string path in batch) info.ArgumentList.Add(path);
            using var process = new Process { StartInfo = info };
            using var overflow = new CancellationTokenSource();
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(overflow.Token);
            using var drain = new CancellationTokenSource();
            stop.CancelAfter(TimeSpan.FromSeconds(5));
            process.Start();
            Task<NativeOutputCapture> stdout = CaptureNativeOutputAsync(process.StandardOutput.BaseStream,
                4096, overflow, drain.Token);
            Task<NativeOutputCapture> stderr = CaptureNativeOutputAsync(process.StandardError.BaseStream,
                65_536, overflow, drain.Token);
            try
            {
                await process.WaitForExitAsync(stop.Token).ConfigureAwait(false);
                NativeOutputCapture[] streams = await Task.WhenAll(stdout, stderr)
                    .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
                if (process.ExitCode != 0 || streams.Any(stream => !stream.Complete || stream.Exceeded) ||
                    streams[1].Bytes.Length != 0) return false;
                string[] modes = new UTF8Encoding(false, true).GetString(streams[0].Bytes).Split('\n');
                if (modes.Length != batch.Length + 1 || modes[^1].Length != 0) return false;
                foreach (string mode in modes[..^1])
                {
                    if (mode.Length is < 1 or > 8 || mode.Any(character => !char.IsAsciiHexDigit(character)) ||
                        !uint.TryParse(mode, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint bits) ||
                        (bits & 0xf000) != 0x8000) return false;
                }
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
                await drain.CancelAsync().ConfigureAwait(false);
                _ = await Task.WhenAll(stdout, stderr)
                    .WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false);
            }
        }
        return true;
    }

    private static string ReadManagedInformationalVersion(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var image = new PEReader(stream);
        if (!image.HasMetadata) throw new InvalidDataException("managed_revision_missing");
        MetadataReader reader = image.GetMetadataReader();
        if (!reader.IsAssembly) throw new InvalidDataException("managed_revision_missing");
        string? version = null;
        foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            EntityHandle declaringType;
            string constructor;
            if (attribute.Constructor.Kind == HandleKind.MemberReference)
            {
                MemberReference member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                declaringType = member.Parent;
                constructor = reader.GetString(member.Name);
            }
            else if (attribute.Constructor.Kind == HandleKind.MethodDefinition)
            {
                MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                declaringType = method.GetDeclaringType();
                constructor = reader.GetString(method.Name);
            }
            else continue;
            string name;
            string namespaceName;
            if (declaringType.Kind == HandleKind.TypeReference)
            {
                TypeReference type = reader.GetTypeReference((TypeReferenceHandle)declaringType);
                name = reader.GetString(type.Name);
                namespaceName = reader.GetString(type.Namespace);
            }
            else if (declaringType.Kind == HandleKind.TypeDefinition)
            {
                TypeDefinition type = reader.GetTypeDefinition((TypeDefinitionHandle)declaringType);
                name = reader.GetString(type.Name);
                namespaceName = reader.GetString(type.Namespace);
            }
            else continue;
            if (constructor != ".ctor" || namespaceName != "System.Reflection" || name != "AssemblyInformationalVersionAttribute") continue;
            if (version is not null) throw new InvalidDataException("managed_revision_ambiguous");
            BlobReader blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) throw new InvalidDataException("managed_revision_invalid");
            version = blob.ReadSerializedString();
            if (version is null || version.Length is < 1 or > 512 || blob.ReadUInt16() != 0 || blob.RemainingBytes != 0)
                throw new InvalidDataException("managed_revision_invalid");
        }
        return version ?? throw new InvalidDataException("managed_revision_missing");
    }

    internal static bool TryReadCandidateSource(string repositoryRoot, string expectedCandidate,
        out CandidateSourceIdentity? identity, out string diagnostic)
    {
        identity = null;
        diagnostic = "candidate_source_invalid";
        try
        {
            if (!OperatingSystem.IsLinux() || expectedCandidate.Length is not (40 or 64) ||
                !LowerHex(expectedCandidate) || !Path.IsPathFullyQualified(repositoryRoot) ||
                !Directory.Exists(repositoryRoot)) return false;
            string head = ReadCandidateGitAsync(repositoryRoot, "rev-parse", "--verify", "HEAD")
                .GetAwaiter().GetResult().TrimEnd('\n');
            if (head != expectedCandidate) return false;
            string tree = ReadCandidateGitAsync(repositoryRoot, "rev-parse", "--verify", expectedCandidate + "^{tree}")
                .GetAwaiter().GetResult().TrimEnd('\n');
            if (tree.Length != expectedCandidate.Length || !LowerHex(tree) ||
                ReadCandidateGitAsync(repositoryRoot, "status", "--porcelain=v1", "-z", "--untracked-files=all",
                    "--ignore-submodules=none").GetAwaiter().GetResult().Length != 0) return false;
            string[] entries = ReadCandidateGitAsync(repositoryRoot, "ls-files", "-v", "-z")
                .GetAwaiter().GetResult().Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (entries.Length == 0 || entries.Any(entry => !entry.StartsWith("H ", StringComparison.Ordinal))) return false;
            if (ReadCandidateGitAsync(repositoryRoot, "rev-parse", "--verify", "HEAD")
                    .GetAwaiter().GetResult().TrimEnd('\n') != expectedCandidate) return false;
            identity = new(expectedCandidate, tree);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or
            ArgumentException or Win32Exception or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal sealed record CandidateSourceIdentity(string CandidateOid, string TreeOid);

    private static async Task<string> ReadCandidateGitAsync(string root, params string[] arguments)
    {
        var info = new ProcessStartInfo(ReleaseToolPaths.Git)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = root
        };
        info.Environment.Clear();
        foreach (var (name, value) in new[]
        {
            ("GIT_CONFIG_NOSYSTEM", "1"), ("GIT_CONFIG_GLOBAL", "/dev/null"), ("GIT_OPTIONAL_LOCKS", "0"),
            ("GIT_NO_REPLACE_OBJECTS", "1"), ("GIT_NO_LAZY_FETCH", "1"), ("GIT_TERMINAL_PROMPT", "0"), ("LC_ALL", "C")
        }) info.Environment[name] = value;
        foreach (string argument in new[]
        {
            "--no-replace-objects", "-c", "core.fsmonitor=false", "-c", "core.untrackedCache=false",
            "-c", "core.trustctime=true", "-c", "core.checkStat=default", "-C", root
        }) info.ArgumentList.Add(argument);
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        using var overflow = new CancellationTokenSource();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(overflow.Token);
        using var drain = new CancellationTokenSource();
        stop.CancelAfter(TimeSpan.FromSeconds(5));
        process.Start();
        Task<NativeOutputCapture> stdout = CaptureNativeOutputAsync(process.StandardOutput.BaseStream,
            MaximumReportBytes, overflow, drain.Token);
        Task<NativeOutputCapture> stderr = CaptureNativeOutputAsync(process.StandardError.BaseStream,
            65_536, overflow, drain.Token);
        try
        {
            await process.WaitForExitAsync(stop.Token).ConfigureAwait(false);
            NativeOutputCapture[] streams = await Task.WhenAll(stdout, stderr)
                .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
            if (process.ExitCode != 0 || streams.Any(stream => !stream.Complete || stream.Exceeded) || streams[1].Bytes.Length != 0)
                throw new InvalidDataException("candidate_git_unavailable");
            return new UTF8Encoding(false, true).GetString(streams[0].Bytes);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
            await drain.CancelAsync().ConfigureAwait(false);
            _ = await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false);
        }
    }

    internal static async Task<OwnedNativeProcessResult> RunOwnedNativeProcessAsync(ProcessStartInfo command,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || command.FileName != ReleaseToolPaths.Dotnet || command.UseShellExecute ||
            command.Arguments.Length != 0 || command.ArgumentList.Count == 0 ||
            !command.RedirectStandardOutput || !command.RedirectStandardError || timeout <= TimeSpan.Zero ||
            timeout > TimeSpan.FromDays(1)) throw new InvalidDataException("native_process_invalid");
        var isolated = new ProcessStartInfo("/usr/bin/setsid")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = command.WorkingDirectory
        };
        isolated.Environment.Clear();
        foreach (var (name, value) in command.Environment) isolated.Environment[name] = value;
        isolated.ArgumentList.Add(command.FileName);
        foreach (string argument in command.ArgumentList) isolated.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = isolated };
        using var overflow = new CancellationTokenSource();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, overflow.Token);
        using var drain = new CancellationTokenSource();
        stop.CancelAfter(timeout);
        Task<NativeOutputCapture>? stdout = null;
        Task<NativeOutputCapture>? stderr = null;
        NativeOutputCapture output = new([], false, false);
        NativeOutputCapture error = new([], false, false);
        NativeProcessIdentity? leader = null;
        bool started = false;
        bool residual = false;
        bool cleanup = false;
        bool timedOut = false;
        bool cancelled = false;
        bool invocationFailed = false;
        try
        {
            process.Start();
            started = true;
            stdout = CaptureNativeOutputAsync(process.StandardOutput.BaseStream, MaximumReportBytes, overflow, drain.Token);
            stderr = CaptureNativeOutputAsync(process.StandardError.BaseStream, 1_048_576, overflow, drain.Token);
            long startedAt = Stopwatch.GetTimestamp();
            while (!process.HasExited && Stopwatch.GetElapsedTime(startedAt) < TimeSpan.FromSeconds(1))
            {
                NativeProcessIdentity? observed = ReadNativeProcessIdentity(process.Id);
                if (observed is not null && observed.Group == process.Id && observed.Session == process.Id)
                {
                    leader = observed;
                    break;
                }
                await Task.Delay(1, stop.Token).ConfigureAwait(false);
            }
            if (leader is null) throw new InvalidDataException("native_process_custody_missing");
            await process.WaitForExitAsync(stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested && !overflow.IsCancellationRequested;
            cancelled = cancellationToken.IsCancellationRequested;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or Win32Exception or InvalidOperationException or
            ArgumentException or UnauthorizedAccessException)
        {
            invocationFailed = true;
        }
        finally
        {
            if (started)
            {
                try
                {
                    if (leader is not null)
                    {
                        residual = process.HasExited && NativeGroupHasLiveMembers(process.Id, leader);
                        if (!process.HasExited || NativeGroupHasLiveMembers(process.Id, leader))
                        {
                            await SignalNativeGroupAsync(process.Id, leader, "-INT").ConfigureAwait(false);
                            if (!await WaitNativeGroupAsync(process.Id, leader, TimeSpan.FromSeconds(2)).ConfigureAwait(false))
                                await SignalNativeGroupAsync(process.Id, leader, "-KILL").ConfigureAwait(false);
                        }
                        await process.WaitForExitAsync(CancellationToken.None)
                            .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
                        cleanup = await WaitNativeGroupAsync(process.Id, leader, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    else
                    {
                        // Without a witnessed session/group, only the directly started process tree is an owned target.
                        if (!process.HasExited) process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(CancellationToken.None)
                            .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
                    }
                    if (stdout is not null && stderr is not null)
                    {
                        NativeOutputCapture[] streams = await Task.WhenAll(stdout, stderr)
                            .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
                        output = streams[0];
                        error = streams[1];
                        cleanup &= output.Complete && error.Complete;
                    }
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or Win32Exception or InvalidOperationException or
                    TimeoutException or UnauthorizedAccessException)
                {
                    cleanup = false;
                }
                finally
                {
                    await drain.CancelAsync().ConfigureAwait(false);
                    try
                    {
                        if (stdout is not null) output = await stdout.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false);
                        if (stderr is not null) error = await stderr.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (TimeoutException) { cleanup = false; }
                }
            }
        }
        return new OwnedNativeProcessResult(started && process.HasExited ? process.ExitCode : -1,
            timedOut, cancelled, residual, output.Exceeded || error.Exceeded, cleanup && !invocationFailed, output.Bytes, error.Bytes);
    }

    private static async Task<NativeOutputCapture> CaptureNativeOutputAsync(Stream stream, int maximum,
        CancellationTokenSource overflow, CancellationToken cancellationToken)
    {
        using var retained = new MemoryStream();
        byte[] buffer = new byte[8192];
        bool exceeded = false;
        try
        {
            while (true)
            {
                int count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0) return new(retained.ToArray(), true, exceeded);
                int available = checked(maximum - (int)retained.Length);
                retained.Write(buffer, 0, Math.Min(available, count));
                if (count > available && !exceeded)
                {
                    exceeded = true;
                    await overflow.CancelAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            return new(retained.ToArray(), false, exceeded);
        }
    }

    private static NativeProcessIdentity? ReadNativeProcessIdentity(int pid)
    {
        try
        {
            string stat = File.ReadAllText("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/stat");
            string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            if (fields.Length < 20 || fields[0].Length != 1 ||
                !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int group) ||
                !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out int session) ||
                !ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out ulong start))
                throw new InvalidDataException("native_process_identity_invalid");
            return new(group, session, start, fields[0][0]);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static bool NativeGroupHasLiveMembers(int group, NativeProcessIdentity leader)
    {
        NativeProcessIdentity? current = ReadNativeProcessIdentity(group);
        if (current is not null && current.Start != leader.Start)
            throw new InvalidDataException("native_process_identity_replaced");
        foreach (string directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out int pid)) continue;
            NativeProcessIdentity? member = ReadNativeProcessIdentity(pid);
            if (member is null || member.Group != group || member.State is 'Z' or 'X') continue;
            if (member.Session != group) throw new InvalidDataException("native_process_group_foreign");
            return true;
        }
        return false;
    }

    private static async Task<bool> WaitNativeGroupAsync(int group, NativeProcessIdentity leader, TimeSpan timeout)
    {
        long started = Stopwatch.GetTimestamp();
        do
        {
            if (!NativeGroupHasLiveMembers(group, leader)) return true;
            await Task.Delay(20).ConfigureAwait(false);
        } while (Stopwatch.GetElapsedTime(started) < timeout);
        return !NativeGroupHasLiveMembers(group, leader);
    }

    private static async Task SignalNativeGroupAsync(int group, NativeProcessIdentity leader, string signal)
    {
        if (!NativeGroupHasLiveMembers(group, leader)) return;
        using var process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/kill")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        } };
        process.StartInfo.ArgumentList.Add(signal);
        process.StartInfo.ArgumentList.Add("--");
        process.StartInfo.ArgumentList.Add("-" + group.ToString(CultureInfo.InvariantCulture));
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
        finally
        {
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        _ = await output.ConfigureAwait(false);
        _ = await error.ConfigureAwait(false);
        if (process.ExitCode != 0 && NativeGroupHasLiveMembers(group, leader))
            throw new InvalidDataException("native_process_signal_failed");
    }

    private sealed record NativeProcessIdentity(int Group, int Session, ulong Start, char State);
    private sealed record NativeOutputCapture(byte[] Bytes, bool Complete, bool Exceeded);

    internal sealed record OwnedNativeProcessResult(int ExitCode, bool TimedOut, bool Cancelled,
        bool ResidualOwnedChildren, bool OutputLimitExceeded, bool CleanupSucceeded,
        byte[] StandardOutput, byte[] StandardError)
    {
        internal bool Succeeded => ExitCode == 0 && !TimedOut && !Cancelled && !ResidualOwnedChildren &&
            !OutputLimitExceeded && CleanupSucceeded;
    }

    internal static ProcessStartInfo NativeTestStartInfo(string assemblyPath, IReadOnlyList<string> selectedUids,
        string resultsDirectory, string temporaryDirectory, string reportFileName, int timeoutSeconds)
    {
        if (!OperatingSystem.IsLinux() || !Path.IsPathFullyQualified(assemblyPath) ||
            !Path.IsPathFullyQualified(temporaryDirectory) || !Path.IsPathFullyQualified(resultsDirectory) ||
            selectedUids.Count is < 1 or > 65_536 || selectedUids.Distinct(StringComparer.Ordinal).Count() != selectedUids.Count ||
            selectedUids.Any(uid => !SafeUidTransport(uid)) ||
            selectedUids.Sum(uid => (long)Encoding.UTF8.GetByteCount(uid) + 1) > 1_048_576 ||
            timeoutSeconds is < 1 or > 86_400 || reportFileName.Length is < 1 or > 128 ||
            reportFileName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '.')) ||
            !reportFileName.EndsWith(".trx", StringComparison.Ordinal))
            throw new InvalidDataException("native_transport_invalid");
        string relativeResults = Path.GetRelativePath(temporaryDirectory, resultsDirectory);
        if (relativeResults == "." || Path.IsPathRooted(relativeResults) || relativeResults == ".." ||
            relativeResults.StartsWith("../", StringComparison.Ordinal))
            throw new InvalidDataException("native_transport_invalid");

        var info = new ProcessStartInfo(ReleaseToolPaths.Dotnet)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = temporaryDirectory
        };
        info.Environment["TMPDIR"] = temporaryDirectory;
        info.ArgumentList.Add(assemblyPath);
        info.ArgumentList.Add("--filter-uid");
        foreach (string uid in selectedUids) info.ArgumentList.Add(uid);
        foreach (string argument in new[]
        {
            "--timeout", timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "s",
            "--maximum-parallel-tests", "1", "--minimum-expected-tests",
            selectedUids.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), "--progress", "off",
            "--no-ansi", "--disable-logo", "--show-stdout", "None", "--show-stderr", "None",
            "--results-directory", resultsDirectory, "--report-trx", "--report-trx-filename", reportFileName
        }) info.ArgumentList.Add(argument);
        return info;
    }

    private static bool SafeUidTransport(string uid) => BoundedIdentity(uid, 4096) &&
        uid[0] is not ('-' or '@') && !char.IsWhiteSpace(uid[0]) && !char.IsWhiteSpace(uid[^1]) &&
        !uid.Any(char.IsControl);

    internal static bool TryValidateNativeProof(byte[] bytes,
        IReadOnlyList<SecurityControlRegister.RequiredTest> requiredTests, bool exactSelection,
        out string diagnostic)
    {
        diagnostic = "native_identity_proof_invalid";
        try
        {
            if (bytes.Length is < 1 or > MaximumReportBytes || !ValidRequiredIdentities(requiredTests)) return false;
            using JsonDocument document = JsonDocument.Parse(bytes, DocumentOptions);
            JsonElement proof = document.RootElement;
            if (!UniqueProperties(proof) || !ExactFields(proof, "schemaVersion", "privateDiscoverySha256",
                    "privateTrxSha256", "counts", "nodes") || proof.GetProperty("schemaVersion").GetInt32() != 1)
                return false;
            foreach (string field in new[] { "privateDiscoverySha256", "privateTrxSha256" })
            {
                string digest = Text(proof, field);
                if (digest.Length != 64 || !LowerHex(digest)) return false;
            }

            JsonElement countsElement = proof.GetProperty("counts");
            if (!ExactFields(countsElement, "selected", "executed", "passed", "failed", "skipped")) return false;
            ExecutionCounts counts = JsonCounts(countsElement);
            JsonElement nodes = proof.GetProperty("nodes");
            if (nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() is < 1 or > 65_536 ||
                counts.Selected != nodes.GetArrayLength() || counts.Executed != counts.Selected ||
                counts.Passed != counts.Selected || counts.Failed != 0 || counts.Skipped != 0) return false;
            var references = new HashSet<int>();
            int ordinal = 0;
            foreach (JsonElement node in nodes.EnumerateArray())
            {
                if (!ExactFields(node, "node", "requiredTest", "outcome") ||
                    node.GetProperty("node").GetInt32() != ordinal++ || Text(node, "outcome") != "passed") return false;
                JsonElement reference = node.GetProperty("requiredTest");
                if (reference.ValueKind == JsonValueKind.Null)
                {
                    if (exactSelection) return false;
                }
                else
                {
                    int index = reference.GetInt32();
                    if (index < 0 || index >= requiredTests.Count || !references.Add(index)) return false;
                }
            }

            if (references.Count != requiredTests.Count) return false;
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            FormatException or OverflowException or ArgumentException or KeyNotFoundException)
        {
            return false;
        }
    }

    internal static bool TryCreateNativeProof(byte[] discoveryBytes, byte[] reportBytes,
        IReadOnlyList<SecurityControlRegister.RequiredTest> requiredTests, bool exactSelection,
        out byte[]? publicProof, out string diagnostic)
    {
        publicProof = null;
        diagnostic = "native_identity_join_invalid";
        try
        {
            if (!ValidRequiredIdentities(requiredTests) || reportBytes.Length is < 1 or > MaximumReportBytes ||
                !TryReadNativeDiscovery(discoveryBytes, out var discovered) ||
                !TryReadReport(reportBytes, out ExecutionReport report)) return false;
            var identities = discovered.Select(node => node.Identity).ToHashSet();
            if (!identities.SetEquals(report.Tests) ||
                (exactSelection ? !identities.SetEquals(requiredTests) : requiredTests.Any(test => !identities.Contains(test))))
                return false;

            var registered = requiredTests.OrderBy(test => test.ClassName, StringComparer.Ordinal)
                .ThenBy(test => test.MethodName, StringComparer.Ordinal).ThenBy(test => test.TestName, StringComparer.Ordinal)
                .Select((test, index) => (test, index)).ToDictionary(entry => entry.test, entry => entry.index);
            byte[] projection = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                privateDiscoverySha256 = Convert.ToHexString(SHA256.HashData(discoveryBytes)).ToLowerInvariant(),
                privateTrxSha256 = Convert.ToHexString(SHA256.HashData(reportBytes)).ToLowerInvariant(),
                counts = new
                {
                    selected = report.Counts.Selected, executed = report.Counts.Executed,
                    passed = report.Counts.Passed, failed = report.Counts.Failed, skipped = report.Counts.Skipped
                },
                nodes = discovered.Select((node, ordinal) => new
                {
                    node = ordinal,
                    requiredTest = registered.TryGetValue(node.Identity, out int reference) ? (int?)reference : null,
                    outcome = "passed"
                })
            });
            if (!TryValidateNativeProof(projection, requiredTests, exactSelection, out _)) return false;
            publicProof = projection;
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or XmlException or InvalidOperationException or
            InvalidDataException or FormatException or OverflowException or ArgumentException or KeyNotFoundException)
        {
            return false;
        }
    }

    private static bool TryReadNativeDiscovery(byte[] bytes, out List<NativeDiscoveredTest> discovered)
    {
        discovered = [];
        if (bytes.Length is < 1 or > MaximumReportBytes) return false;
        _ = new UTF8Encoding(false, true).GetString(bytes);
        using JsonDocument document = JsonDocument.Parse(bytes, DocumentOptions);
        JsonElement root = document.RootElement;
        if (!UniqueProperties(root) || root.GetProperty("schemaVersion").GetInt32() != 1) return false;
        JsonElement nodes = root.GetProperty("tests");
        if (nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() is < 1 or > 65_536) return false;
        var uids = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<SecurityControlRegister.RequiredTest>();
        foreach (JsonElement node in nodes.EnumerateArray())
        {
            string uid = Text(node, "uid");
            string display = Text(node, "displayName");
            JsonElement type = node.GetProperty("type");
            string namespaceName = Text(type, "namespace");
            string typeName = Text(type, "typeName");
            string method = Text(type, "methodName");
            if (!BoundedIdentity(uid, 4096) || !BoundedIdentity(display, 16_384) ||
                !BoundedIdentity(namespaceName, 2048) || !BoundedIdentity(typeName, 2048) ||
                !BoundedIdentity(method, 2048) || !uids.Add(uid)) return false;
            var identity = new SecurityControlRegister.RequiredTest(namespaceName + "." + typeName, method, display);
            if (!identities.Add(identity)) return false;
            discovered.Add(new NativeDiscoveredTest(uid, identity));
        }

        return true;
    }

    private static bool BoundedIdentity(string value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;

    private static bool ValidRequiredIdentities(IReadOnlyList<SecurityControlRegister.RequiredTest> tests) =>
        tests.Count is >= 1 and <= 1024 && tests.Distinct().Count() == tests.Count &&
        tests.All(test => BoundedIdentity(test.ClassName, 4097) && BoundedIdentity(test.MethodName, 2048) &&
            BoundedIdentity(test.TestName, 16_384));

    private static bool ExactFields(JsonElement value, params string[] names) =>
        value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == names.Length &&
        value.EnumerateObject().All(property => names.Contains(property.Name, StringComparer.Ordinal));

    private sealed record NativeDiscoveredTest(string Uid, SecurityControlRegister.RequiredTest Identity);
}
