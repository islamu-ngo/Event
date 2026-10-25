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
    internal sealed record ConfiguredContainerDeclaration(string Name, string Image,
        IReadOnlyList<ConfiguredMountDeclaration> Mounts);
    internal sealed record ConfiguredMountDeclaration(string Type, string Source, string Target, bool ReadOnly);
    internal sealed record ConfiguredProjectDeclaration(string Name, string ProjectPath);

    internal static bool TryReadConfiguredContainerDeclarations(byte[] bytes, string runId,
        out IReadOnlyList<ConfiguredContainerDeclaration>? declarations) =>
        TryReadConfiguredContainerDeclarations(bytes, runId, out declarations, out _);

    internal static bool TryReadConfiguredContainerDeclarations(byte[] bytes, string runId,
        out IReadOnlyList<ConfiguredContainerDeclaration>? declarations,
        out IReadOnlyList<ConfiguredProjectDeclaration>? projects)
    {
        declarations = null;
        projects = null;
        try
        {
            if (bytes.Length is < 1 or > 262144 || runId.Length != 32 || !LowerHex(runId)) return false;
            using JsonDocument document = JsonDocument.Parse(bytes, DocumentOptions);
            JsonElement root = document.RootElement;
            if (!UniqueProperties(root) || !ExactFields(root, "Schema", "RunId", "Containers", "Projects")
                || root.GetProperty("Schema").GetInt32() != 1 || Text(root, "RunId") != runId) return false;
            JsonElement items = root.GetProperty("Containers");
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 64) return false;
            var names = new HashSet<string>(StringComparer.Ordinal);
            var resources = new List<ConfiguredContainerDeclaration>();
            foreach (JsonElement item in items.EnumerateArray())
            {
                if (!ExactFields(item, "Name", "Image", "PullPolicy", "Mounts")) return false;
                string name = Text(item, "Name"), image = Text(item, "Image");
                if (name.Length is < 1 or > 128 || !name.All(character => char.IsAsciiLetterOrDigit(character)
                        || character is '-' or '_') || !names.Add(name)
                    || image.Length is < 1 or > 2048 || image.Any(character => !char.IsAsciiLetterOrDigit(character)
                        && character is not ('/' or '.' or '_' or ':' or '@' or '-'))
                    || image.Split('/').Any(part => part is "" or "." or "..") || Text(item, "PullPolicy") != "Never") return false;
                int at = image.IndexOf('@', StringComparison.Ordinal);
                if (at >= 0 && (at == 0 || image.Length - at != 72
                    || !image[(at + 1)..].StartsWith("sha256:", StringComparison.Ordinal)
                    || !LowerHex(image[(at + 8)..]))) return false;
                JsonElement mounts = item.GetProperty("Mounts");
                if (mounts.ValueKind != JsonValueKind.Array || mounts.GetArrayLength() > 32) return false;
                var targets = new HashSet<string>(StringComparer.Ordinal);
                var projected = new List<ConfiguredMountDeclaration>();
                foreach (JsonElement mount in mounts.EnumerateArray())
                {
                    if (!ExactFields(mount, "Type", "Source", "Target", "ReadOnly")) return false;
                    string type = Text(mount, "Type"), source = Text(mount, "Source"), target = Text(mount, "Target");
                    if (!HintAbsolutePath(target) || target.Length > 4096
                        || target.Length > 1 && target.EndsWith(Path.DirectorySeparatorChar) || !targets.Add(target)
                        || source.Length is < 1 or > 4096 || source.Any(char.IsControl)
                        || mount.GetProperty("ReadOnly").ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                    bool readOnly = mount.GetProperty("ReadOnly").GetBoolean();
                    if (type == "Volume")
                    {
                        if (!source.StartsWith("islamu-test-" + runId + "-", StringComparison.Ordinal)
                            || source.Any(character => !char.IsAsciiLetterOrDigit(character)
                                && character is not ('-' or '_' or '.'))) return false;
                    }
                    else if (type != "BindMount" || !readOnly || !HintAbsolutePath(source)) return false;
                    projected.Add(new(type, source, target, readOnly));
                }
                resources.Add(new(name, image, projected.OrderBy(mount => mount.Target, StringComparer.Ordinal).ToArray()));
            }
            JsonElement projectItems = root.GetProperty("Projects");
            if (projectItems.ValueKind != JsonValueKind.Array || projectItems.GetArrayLength() > 64) return false;
            var selectedProjects = new List<ConfiguredProjectDeclaration>();
            foreach (JsonElement item in projectItems.EnumerateArray())
            {
                if (!ExactFields(item, "Name", "ProjectPath")) return false;
                string name = Text(item, "Name"), path = Text(item, "ProjectPath");
                if (name.Length is < 1 or > 128 || !name.All(character => char.IsAsciiLetterOrDigit(character)
                        || character is '-' or '_') || !names.Add(name)
                    || path.Length is < 1 or > 4096 || !HintAbsolutePath(path)) return false;
                selectedProjects.Add(new(name, path));
            }
            declarations = resources.OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray();
            projects = selectedProjects.OrderBy(project => project.Name, StringComparer.Ordinal).ToArray();
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException or KeyNotFoundException or NotSupportedException)
        {
            return false;
        }
    }

    internal sealed record NativeFixtureModelSnapshot(string RunId, string Root, byte[] Model,
        string ModelSha256, IReadOnlyList<ConfiguredContainerDeclaration> Containers,
        IReadOnlyList<ConfiguredProjectDeclaration> Projects);

    internal sealed record NativeCreatedContainer(string RunId, string CreationId, int CreatorPid,
        string Resource, string ImageReference, string ContainerId, string DeclarationSha256);
    internal sealed record NativeContainerCreationSnapshot(IReadOnlyList<NativeCreatedContainer> Created,
        IReadOnlySet<string> PendingCreationIds, byte[] PrivateManifest);

    internal static async Task<NativeContainerCreationSnapshot?> CaptureLiveNativeContainerCreationsAsync(
        OwnedNativeProcessContext invocation, string invocationTemporary, string runId, string kind,
        CancellationToken cancellationToken, string? creationId = null)
    {
        try
        {
            if (!OperatingSystem.IsLinux() || kind is not ("api" or "direct") || runId.Length != 32 || !LowerHex(runId)
                || invocation.Pid <= 0 || invocation.Start == 0 || invocation.Group != invocation.Pid
                || invocation.Session != invocation.Pid) return null;
            if (creationId is not null && (creationId.Length != 32 || !LowerHex(creationId) || creationId[12] != '7'
                || creationId[16] is not ('8' or '9' or 'a' or 'b'))) return null;
            string temporary = InputDirectory(invocationTemporary);
            string root = InputDirectory(Path.Combine(temporary, "islamu-assurance", runId));
            ManagedProcessIdentity? creator = ReadManagedProcessIdentity(invocation.Pid);
            if (creator is null || creator.Start != invocation.Start || creator.Group != invocation.Group
                || creator.Session != invocation.Session || creator.State is 'Z' or 'X') return null;
            string marker = Path.Combine(root, ".islamu-test-owner");
            LiveFileIdentity rootIdentity = await ReadLiveMetadataAsync(root, false, cancellationToken).ConfigureAwait(false);
            LiveFileIdentity markerIdentity = await ReadLiveMetadataAsync(marker, false, cancellationToken).ConfigureAwait(false);
            if ((rootIdentity.Bits & 0xf000) != 0x4000 || (rootIdentity.Bits & 0xfff) != 0x1c0
                || rootIdentity.User != creator.User || !PrivateLiveFile(markerIdentity, creator.User, 32)
                || !NoInputLinks(marker) || Encoding.ASCII.GetString(ReadBoundedFile(marker, 32)) != runId) return null;
            string prefix = kind + "-container-";
            string pattern = prefix + (creationId is null ? "*" : "*-" + creationId + "-*") + ".json";
            string[] names = Directory.EnumerateFiles(root, pattern).Take(129).Order(StringComparer.Ordinal).ToArray();
            if (names.Length > 128) return null;
            string[] declarations = names.Where(path => path.EndsWith("-declaration.json", StringComparison.Ordinal)).ToArray();
            if (declarations.Length > 64 || creationId is not null && declarations.Length != 1) return null;
            var consumed = new HashSet<string>(StringComparer.Ordinal);
            var creationIds = new HashSet<string>(StringComparer.Ordinal);
            var containerIds = new HashSet<string>(StringComparer.Ordinal);
            var pending = new HashSet<string>(StringComparer.Ordinal);
            var created = new List<NativeCreatedContainer>();
            var brackets = new List<(string Path, LiveFileIdentity Identity, byte[] Bytes)>();
            foreach (string declaration in declarations)
            {
                LiveFileIdentity identity = await ReadLiveMetadataAsync(declaration, false, cancellationToken).ConfigureAwait(false);
                if (!NoInputLinks(declaration) || !PrivateLiveFile(identity, creator.User, identity.Bytes)
                    || identity.Bytes is < 1 or > 16384
                    || !await ProcessHoldsLiveFileAsync(creator.Pid, declaration, identity, cancellationToken).ConfigureAwait(false)) return null;
                byte[] bytes = ReadBoundedFile(declaration, 16384);
                using JsonDocument document = JsonDocument.Parse(bytes, DocumentOptions);
                JsonElement value = document.RootElement;
                if (!UniqueProperties(value) || !ExactFields(value, "Schema", "RunId", "CreationId", "CreatorPid", "Resource", "ImageReference")
                    || value.GetProperty("Schema").GetInt32() != 1 || Text(value, "RunId") != runId
                    || value.GetProperty("CreatorPid").GetInt32() != creator.Pid) return null;
                string creation = Text(value, "CreationId"), resource = Text(value, "Resource"), imageReference = Text(value, "ImageReference");
                if (creation.Length != 32 || !LowerHex(creation) || creation[12] != '7'
                    || creation[16] is not ('8' or '9' or 'a' or 'b') || !creationIds.Add(creation)
                    || creationId is not null && creation != creationId
                    || (kind == "api" ? resource is not ("keycloak" or "cerbos")
                        : resource is not ("docker-wait" or "foreign-volume-named" or "foreign-volume-bind"))
                    || imageReference.Length is < 1 or > 2048 || imageReference.Any(character => !char.IsAsciiLetterOrDigit(character)
                        && character is not ('/' or '.' or '_' or ':' or '@' or '-'))
                    || imageReference.Split('/').Any(part => part is "" or "." or "..")) return null;
                string stem = Path.Combine(root, prefix + resource + "-" + creation);
                if (declaration != stem + "-declaration.json") return null;
                consumed.Add(declaration);
                brackets.Add((declaration, identity, bytes));
                string digest = DigestBytes(bytes);
                string[] receipts = names.Where(path => path.StartsWith(stem + "-created-", StringComparison.Ordinal)).ToArray();
                if (receipts.Length == 0) { pending.Add(creation); continue; }
                if (receipts.Length != 1) return null;
                string receipt = receipts[0];
                LiveFileIdentity receiptIdentity = await ReadLiveMetadataAsync(receipt, false, cancellationToken).ConfigureAwait(false);
                if (!NoInputLinks(receipt) || !PrivateLiveFile(receiptIdentity, creator.User, receiptIdentity.Bytes)
                    || receiptIdentity.Bytes is < 1 or > 16384
                    || !await ProcessHoldsLiveFileAsync(creator.Pid, receipt, receiptIdentity, cancellationToken).ConfigureAwait(false)) return null;
                byte[] receiptBytes = ReadBoundedFile(receipt, 16384);
                using JsonDocument receiptDocument = JsonDocument.Parse(receiptBytes, DocumentOptions);
                JsonElement actual = receiptDocument.RootElement;
                if (!UniqueProperties(actual) || !ExactFields(actual, "Schema", "RunId", "CreationId", "CreatorPid", "Resource", "ImageReference",
                        "ContainerId", "DeclarationSha256") || actual.GetProperty("Schema").GetInt32() != 1
                    || Text(actual, "RunId") != runId || Text(actual, "CreationId") != creation
                    || actual.GetProperty("CreatorPid").GetInt32() != creator.Pid || Text(actual, "Resource") != resource
                    || Text(actual, "ImageReference") != imageReference || Text(actual, "DeclarationSha256") != digest) return null;
                string id = Text(actual, "ContainerId");
                if (id.Length != 64 || !LowerHex(id) || !containerIds.Add(id) || receipt != stem + "-created-" + id + ".json") return null;
                consumed.Add(receipt);
                brackets.Add((receipt, receiptIdentity, receiptBytes));
                created.Add(new(runId, creation, creator.Pid, resource, imageReference, id, digest));
            }
            if (!consumed.SetEquals(names)) return null;
            foreach (var bracket in brackets)
                if (!NoInputLinks(bracket.Path)
                    || await ReadLiveMetadataAsync(bracket.Path, false, cancellationToken).ConfigureAwait(false) != bracket.Identity
                    || !ReadBoundedFile(bracket.Path, 16384).AsSpan().SequenceEqual(bracket.Bytes)
                    || !await ProcessHoldsLiveFileAsync(creator.Pid, bracket.Path, bracket.Identity, cancellationToken).ConfigureAwait(false)) return null;
            if (!NoInputLinks(root) || !NoInputLinks(marker)
                || await ReadLiveMetadataAsync(root, false, cancellationToken).ConfigureAwait(false) != rootIdentity
                || await ReadLiveMetadataAsync(marker, false, cancellationToken).ConfigureAwait(false) != markerIdentity
                || Encoding.ASCII.GetString(ReadBoundedFile(marker, 32)) != runId
                || !SameLiveProcess(ReadManagedProcessIdentity(creator.Pid), creator)
                || !names.SequenceEqual(Directory.EnumerateFiles(root, pattern).Take(129).Order(StringComparer.Ordinal), StringComparer.Ordinal))
                return null;
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1, runId, kind, creator, invocation, created,
                pendingCreationIds = pending.Order(StringComparer.Ordinal),
                records = brackets.Select(item => new { path = item.Path, sha256 = DigestBytes(item.Bytes),
                    item.Identity.Device, item.Identity.Inode, liveDescriptorMatched = true }),
                creatorAuthentication = "not-attested", assertionAuthority = "reviewed-native-source-and-operator"
            });
            return new(created, pending, manifest);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException
            or InvalidDataException or InvalidOperationException or ArgumentException or OverflowException
            or FormatException or KeyNotFoundException or NotSupportedException)
        { return null; }
    }

    internal sealed record InitialNativeResources(IReadOnlySet<string> ContainerIds, IReadOnlySet<string> VolumeNames,
        IReadOnlySet<string> ImageIds);
    internal sealed record ConfiguredResourceInputs(string Owner, IReadOnlyList<ConfiguredContainerDeclaration> Containers,
        IReadOnlyDictionary<string, string> Images, InitialNativeResources Initial,
        IReadOnlyDictionary<string, IReadOnlySet<string>> ImageVolumeTargets,
        IReadOnlyDictionary<string, string> VolumeMountpoints);
    internal sealed record ObservedContainerMount(string Type, string Name, string Source, string Destination, bool ReadWrite);
    internal sealed record ConfiguredOwnedResource(ObservedOwnedResource Container, IReadOnlyList<ObservedContainerMount> Mounts);

    internal static bool TryReadApiCreatedResources(byte[] bytes, string owner, NativeContainerCreationSnapshot creations,
        IReadOnlyDictionary<string, string> imageIds, InitialNativeResources initial,
        out IReadOnlyList<ObservedOwnedResource>? resources)
    {
        resources = null;
        try
        {
            if (bytes.Length is < 1 or > MaximumReportBytes || owner.Length != 32 || !LowerHex(owner)
                || creations.Created.Count > 64 || creations.PendingCreationIds.Count > 64 || imageIds.Count > 65_536
                || initial.ContainerIds.Count > 65_536 || initial.ImageIds.Count > 65_536
                || initial.ContainerIds.Any(id => id.Length != 64 || !LowerHex(id))
                || initial.ImageIds.Any(id => !FullLocalImageId(id))
                || imageIds.Any(item => !BoundedIdentity(item.Key, 2048) || !FullLocalImageId(item.Value))) return false;
            var pairs = new Dictionary<string, NativeCreatedContainer>(StringComparer.Ordinal);
            var creationIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (NativeCreatedContainer pair in creations.Created)
            {
                if (pair.RunId != owner || pair.CreatorPid <= 0 || pair.Resource is not ("keycloak" or "cerbos")
                    || pair.CreationId.Length != 32 || !LowerHex(pair.CreationId) || pair.CreationId[12] != '7'
                    || pair.CreationId[16] is not ('8' or '9' or 'a' or 'b') || !creationIds.Add(pair.CreationId)
                    || pair.ContainerId.Length != 64 || !LowerHex(pair.ContainerId) || initial.ContainerIds.Contains(pair.ContainerId)
                    || pair.DeclarationSha256.Length != 64 || !LowerHex(pair.DeclarationSha256)
                    || !BoundedIdentity(pair.ImageReference, 2048)
                    || !imageIds.TryGetValue(pair.ImageReference, out string? selectedImage)
                    || !initial.ImageIds.Contains(selectedImage) || !pairs.TryAdd(pair.ContainerId, pair)) return false;
            }
            if (creations.PendingCreationIds.Any(id => id.Length != 32 || !LowerHex(id) || id[12] != '7'
                    || id[16] is not ('8' or '9' or 'a' or 'b') || creationIds.Contains(id))) return false;
            using JsonDocument document = JsonDocument.Parse(bytes, DocumentOptions);
            JsonElement inventory = document.RootElement;
            if (inventory.ValueKind != JsonValueKind.Array || inventory.GetArrayLength() > 65_536
                || !UniqueProperties(inventory)) return false;
            var allIds = new HashSet<string>(StringComparer.Ordinal);
            var observed = new List<ObservedOwnedResource>();
            foreach (JsonElement item in inventory.EnumerateArray())
            {
                string id = Text(item, "Id");
                if (id.Length != 64 || !LowerHex(id) || !allIds.Add(id)
                    || !item.TryGetProperty("Labels", out JsonElement labels)
                    || labels.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)) return false;
                bool hasPair = pairs.TryGetValue(id, out NativeCreatedContainer? pair);
                if (labels.ValueKind == JsonValueKind.Null)
                {
                    if (hasPair) return false;
                    continue;
                }
                if (!labels.TryGetProperty("islamu.assurance.owner", out JsonElement selectedOwner))
                {
                    if (hasPair || labels.TryGetProperty("islamu.assurance.resource", out _)) return false;
                    continue;
                }
                if (selectedOwner.ValueKind != JsonValueKind.String) return false;
                if (selectedOwner.GetString() != owner)
                {
                    if (hasPair) return false;
                    continue;
                }
                if (!hasPair || pair is null || Text(labels, "islamu.assurance.resource") != pair.Resource
                    || Text(item, "Image") != pair.ImageReference || Text(item, "ImageID") != imageIds[pair.ImageReference]
                    || !item.TryGetProperty("Mounts", out JsonElement mounts) || mounts.ValueKind != JsonValueKind.Array
                    || mounts.GetArrayLength() != 0) return false;
                string state = Text(item, "State");
                if (state is not ("created" or "restarting" or "running" or "removing" or "paused" or "exited" or "dead")) return false;
                observed.Add(new(id, imageIds[pair.ImageReference], pair.Resource, state == "running"));
            }
            resources = observed.AsReadOnly();
            return true;
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or ArgumentException
            or FormatException or OverflowException or KeyNotFoundException or NotSupportedException)
        { return false; }
    }

    internal static bool TryReadConfiguredOwnedResources(byte[] bytes, ConfiguredResourceInputs inputs,
        out IReadOnlyList<ConfiguredOwnedResource>? resources)
    {
        resources = null;
        try
        {
            if (bytes.Length is < 1 or > MaximumReportBytes || inputs.Owner.Length != 32 || !LowerHex(inputs.Owner)
                || inputs.Containers.Count > 64 || inputs.Images.Count > 65_536
                || inputs.Initial.ContainerIds.Count > 65_536 || inputs.Initial.VolumeNames.Count > 65_536
                || inputs.Initial.ImageIds.Count > 65_536 || inputs.VolumeMountpoints.Count > 65_536
                || inputs.ImageVolumeTargets.Count > 65_536
                || inputs.Initial.ContainerIds.Any(id => id.Length != 64 || !LowerHex(id))
                || inputs.Initial.ImageIds.Any(id => !FullLocalImageId(id))
                || inputs.Images.Any(item => !BoundedIdentity(item.Key, 2048) || !FullLocalImageId(item.Value))
                || inputs.VolumeMountpoints.Any(item => !RuntimeVolumeName(item.Key) || !CanonicalObservedMountPath(item.Value))
                || inputs.Initial.VolumeNames.Any(name => !RuntimeVolumeName(name))
                || inputs.ImageVolumeTargets.Any(item => !FullLocalImageId(item.Key) || item.Value.Count > 64
                    || item.Value.Any(target => !CanonicalObservedMountPath(target)))) return false;
            var declarations = inputs.Containers.ToDictionary(item => item.Name, StringComparer.Ordinal);
            var images = inputs.Images.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            var mountpoints = inputs.VolumeMountpoints.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            var implicitTargets = inputs.ImageVolumeTargets.ToDictionary(item => item.Key,
                item => item.Value.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
            var volumeNames = inputs.Containers.SelectMany(item => item.Mounts)
                .Where(mount => mount.Type == "Volume").Select(mount => mount.Source)
                .Concat(mountpoints.Keys).ToHashSet(StringComparer.Ordinal);
            using JsonDocument document = JsonDocument.Parse(bytes, DocumentOptions);
            JsonElement inventory = document.RootElement;
            if (inventory.ValueKind != JsonValueKind.Array || inventory.GetArrayLength() > 65_536
                || !UniqueProperties(inventory)) return false;
            var allIds = new HashSet<string>(StringComparer.Ordinal);
            var observed = new List<ConfiguredOwnedResource>();
            var foreignMounts = new List<ObservedContainerMount>();
            foreach (JsonElement item in inventory.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) return false;
                string id = Text(item, "Id");
                if (id.Length != 64 || !LowerHex(id) || !allIds.Add(id)
                    || !TryReadRuntimeMounts(item, out var mounts)) return false;
                string owner = string.Empty;
                if (item.TryGetProperty("Labels", out JsonElement labels) && labels.ValueKind != JsonValueKind.Null)
                {
                    if (labels.ValueKind != JsonValueKind.Object) return false;
                    if (labels.TryGetProperty("islamu.assurance.owner", out JsonElement label))
                    {
                        if (label.ValueKind != JsonValueKind.String) return false;
                        owner = label.GetString()!;
                    }
                    else if (labels.TryGetProperty("islamu.assurance.resource", out _)) return false;
                }
                if (owner != inputs.Owner)
                {
                    foreignMounts.AddRange(mounts);
                    continue;
                }
                string resource = Text(labels, "islamu.assurance.resource"), reference = Text(item, "Image");
                string image = Text(item, "ImageID"), state = Text(item, "State");
                if (inputs.Initial.ContainerIds.Contains(id) || !declarations.TryGetValue(resource, out var declaration)
                    || reference != declaration.Image || !FullLocalImageId(image)
                    || !images.TryGetValue(declaration.Image, out string? selectedImage) || selectedImage != image
                    || !inputs.Initial.ImageIds.Contains(image) || observed.Count >= 1024
                    || state is not ("created" or "restarting" or "running" or "removing" or "paused" or "exited" or "dead"))
                    return false;
                var expectedMounts = declaration.Mounts.ToDictionary(mount => mount.Target, StringComparer.Ordinal);
                var seenTargets = mounts.Select(mount => mount.Destination).ToHashSet(StringComparer.Ordinal);
                implicitTargets.TryGetValue(image, out var imageTargets);
                foreach (ObservedContainerMount mount in mounts)
                {
                    if (expectedMounts.TryGetValue(mount.Destination, out var expected))
                    {
                        if (mount.ReadWrite == expected.ReadOnly) return false;
                        if (expected.Type == "Volume")
                        {
                            if (mount.Type != "volume" || mount.Name != expected.Source) return false;
                        }
                        else if (expected.Type != "BindMount" || !expected.ReadOnly || mount.Type != "bind"
                            || mount.Name.Length != 0 || mount.Source != expected.Source) return false;
                    }
                    else if (mount.Type != "volume" || imageTargets is null || !imageTargets.Contains(mount.Destination))
                        return false;
                    if (mount.Type == "volume")
                    {
                        if (inputs.Initial.VolumeNames.Contains(mount.Name)
                            || !mountpoints.TryGetValue(mount.Name, out string? mountpoint)
                            || mount.Source != mountpoint) return false;
                        volumeNames.Add(mount.Name);
                    }
                }
                if (expectedMounts.Keys.Any(target => !seenTargets.Contains(target))
                    || imageTargets is not null && imageTargets.Any(target => !seenTargets.Contains(target))) return false;
                observed.Add(new(new(id, image, resource, state == "running"), mounts));
            }
            if (foreignMounts.Any(mount => mount.Type == "volume" && volumeNames.Contains(mount.Name)
                || mount.Type == "bind" && mountpoints.Values.Any(path => ObservedMountPathsOverlap(mount.Source, path))))
                return false;
            resources = observed.AsReadOnly();
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException or KeyNotFoundException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool RuntimeVolumeName(string name) => name.Length is >= 1 and <= 255
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool CanonicalObservedMountPath(string path) => HintAbsolutePath(path)
        && (path.Length == 1 || !path.EndsWith('/'));

    private static bool ObservedMountPathsOverlap(string left, string right) => left == right || left == "/" || right == "/"
        || InputWithin(left, right) || InputWithin(right, left);

    private static bool TryReadRuntimeMounts(JsonElement container, out IReadOnlyList<ObservedContainerMount> result)
    {
        result = [];
        if (!container.TryGetProperty("Mounts", out JsonElement mounts) || mounts.ValueKind != JsonValueKind.Array
            || mounts.GetArrayLength() > 128) return false;
        var targets = new HashSet<string>(StringComparer.Ordinal);
        var observed = new List<ObservedContainerMount>();
        foreach (JsonElement mount in mounts.EnumerateArray())
        {
            string type = Text(mount, "Type"), source = Text(mount, "Source"), target = Text(mount, "Destination");
            if (type is not ("volume" or "bind" or "tmpfs" or "npipe") || source.Length > 8192
                || source.Any(char.IsControl) || !CanonicalObservedMountPath(target) || !targets.Add(target)
                || !mount.TryGetProperty("RW", out JsonElement writable)
                || writable.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            string name = mount.TryGetProperty("Name", out _) ? Text(mount, "Name") : string.Empty;
            if (type == "volume" && (!RuntimeVolumeName(name) || !CanonicalObservedMountPath(source))
                || type == "bind" && !CanonicalObservedMountPath(source)
                || type != "volume" && name.Length != 0) return false;
            observed.Add(new(type, name, source, target, writable.GetBoolean()));
        }
        result = observed.AsReadOnly();
        return true;
    }

    internal static async Task<NativeFixtureModelSnapshot?> CaptureLiveFixtureModelAsync(OwnedNativeProcessContext invocation,
        string invocationTemporary, string fixtureRoot, string runId, CancellationToken cancellationToken)
    {
        try
        {
            if (!OperatingSystem.IsLinux() || invocation.Pid <= 0 || invocation.Group != invocation.Pid
                || invocation.Session != invocation.Pid || invocation.Start == 0 || runId.Length != 32 || !LowerHex(runId)) return null;
            string temporary = InputDirectory(invocationTemporary), root = InputDirectory(fixtureRoot);
            if (root != Path.Combine(temporary, "islamu-assurance", runId)) return null;
            ManagedProcessIdentity? owner = ReadManagedProcessIdentity(invocation.Pid);
            if (owner is null || owner.Start != invocation.Start || owner.Group != invocation.Group
                || owner.Session != invocation.Session || owner.State is 'Z' or 'X') return null;
            string marker = Path.Combine(root, ".islamu-test-owner"), claim = Path.Combine(root, ".apphost-claim");
            string modelPath = Path.Combine(root, "container-model.json");
            if (!File.Exists(marker) || !File.Exists(claim) || !File.Exists(modelPath)) return null;
            LiveFileIdentity directory = await ReadLiveMetadataAsync(root, false, cancellationToken).ConfigureAwait(false);
            LiveFileIdentity markerIdentity = await ReadLiveMetadataAsync(marker, false, cancellationToken).ConfigureAwait(false);
            LiveFileIdentity claimIdentity = await ReadLiveMetadataAsync(claim, false, cancellationToken).ConfigureAwait(false);
            LiveFileIdentity modelIdentity = await ReadLiveMetadataAsync(modelPath, false, cancellationToken).ConfigureAwait(false);
            if ((directory.Bits & 0xf000) != 0x4000 || (directory.Bits & 0xfff) != 0x1c0 || directory.User != owner.User
                || !PrivateLiveFile(markerIdentity, owner.User, 32) || !NoInputLinks(marker) || !NoInputLinks(claim)
                || !NoInputLinks(modelPath) || Encoding.ASCII.GetString(ReadBoundedFile(marker, 32)) != runId
                || (claimIdentity.Bits & 0xf000) != 0x8000 || claimIdentity.User != owner.User || claimIdentity.Links != 1
                || claimIdentity.Bytes != 0 || !PrivateLiveFile(modelIdentity, owner.User, modelIdentity.Bytes)
                || modelIdentity.Bytes is < 1 or > 262144
                || !await NativeOwnerHoldsClaimAsync(invocation, owner.User, claim, claimIdentity, cancellationToken).ConfigureAwait(false)) return null;
            byte[] model = ReadBoundedFile(modelPath, 262144);
            if (!TryReadConfiguredContainerDeclarations(model, runId, out var containers, out var projects)) return null;
            if (!NoInputLinks(root) || !NoInputLinks(marker) || !NoInputLinks(claim) || !NoInputLinks(modelPath)
                || await ReadLiveMetadataAsync(root, false, cancellationToken).ConfigureAwait(false) != directory
                || await ReadLiveMetadataAsync(marker, false, cancellationToken).ConfigureAwait(false) != markerIdentity
                || await ReadLiveMetadataAsync(claim, false, cancellationToken).ConfigureAwait(false) != claimIdentity
                || await ReadLiveMetadataAsync(modelPath, false, cancellationToken).ConfigureAwait(false) != modelIdentity
                || Encoding.ASCII.GetString(ReadBoundedFile(marker, 32)) != runId
                || !ReadBoundedFile(modelPath, 262144).AsSpan().SequenceEqual(model)
                || !await NativeOwnerHoldsClaimAsync(invocation, owner.User, claim, claimIdentity, cancellationToken).ConfigureAwait(false)
                || !SameLiveProcess(ReadManagedProcessIdentity(invocation.Pid), owner)) return null;
            return new(runId, root, model, DigestBytes(model), containers!, projects!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or InvalidOperationException or FormatException or OverflowException
            or KeyNotFoundException or NotSupportedException)
        {
            return null;
        }
    }

    internal static async Task<CompiledInputSnapshot?> CaptureLiveManagedInputsAsync(OwnedNativeProcessContext invocation,
        string invocationTemporary, string fixtureRoot, NativeManagedLaunchHint hint, string expectedEntry,
        string candidate, CancellationToken cancellationToken)
    {
        CompiledInputSnapshot? direct = await CaptureLiveManagedHostInputsAsync(invocation, invocationTemporary,
            fixtureRoot, hint, expectedEntry, candidate, cancellationToken).ConfigureAwait(false);
        if (direct is not null || !OperatingSystem.IsLinux() || hint.Inputs.HasValue) return direct;
        try
        {
            NativeFixtureModelSnapshot? model = await CaptureLiveFixtureModelAsync(invocation, invocationTemporary,
                fixtureRoot, hint.RunId, cancellationToken).ConfigureAwait(false);
            ManagedProcessIdentity? owner = ReadManagedProcessIdentity(invocation.Pid);
            ManagedProcessIdentity? lead = ReadManagedProcessIdentity(hint.Pid);
            if (model is null || owner is null || lead is null || owner.Start != invocation.Start
                || lead.User != owner.User || lead.Start != hint.Start || lead.Start < invocation.Start
                || lead.State is 'Z' or 'X') return null;
            string directName = $"managed-host-root-{lead.Pid}-{lead.Start}.json";
            if (Directory.EnumerateFileSystemEntries(model.Root, directName).Any()) return null;
            // Five closed managed resources retain at most 64 incarnation records each.
            string[] records = Directory.EnumerateFiles(model.Root, "managed-host-root-*.json")
                .Take(5 * 64 + 1).ToArray();
            if (records.Length > 5 * 64) return null;
            NativeManagedLaunchHint? selected = null;
            List<ManagedProcessIdentity>? selectedChain = null;
            CompiledInputSnapshot? selectedInputs = null;
            foreach (string record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] parts = Path.GetFileNameWithoutExtension(record)["managed-host-root-".Length..].Split('-');
                if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture,
                        out int pid) || pid <= 0 || !ulong.TryParse(parts[1], NumberStyles.None,
                        CultureInfo.InvariantCulture, out ulong start) || start == 0) return null;
                ManagedProcessIdentity? host = ReadManagedProcessIdentity(pid);
                if (host is null || host.Start != start || host.Start < lead.Start) continue;
                List<ManagedProcessIdentity>? chain = ReadLiveHostAncestorChain(host, lead, cancellationToken);
                if (chain is null) continue;
                var hosted = hint with { Pid = pid, Start = start };
                CompiledInputSnapshot? inputs = await CaptureLiveManagedHostInputsAsync(invocation,
                    invocationTemporary, model.Root, hosted, expectedEntry, candidate, cancellationToken).ConfigureAwait(false);
                if (inputs is null) continue;
                if (selected is not null) return null;
                selected = hosted;
                selectedChain = chain;
                selectedInputs = inputs;
            }
            if (selected is null || selectedChain is null || selectedInputs is null) return null;
            CompiledInputSnapshot? after = await CaptureLiveManagedHostInputsAsync(invocation, invocationTemporary,
                model.Root, selected, expectedEntry, candidate, cancellationToken).ConfigureAwait(false);
            NativeFixtureModelSnapshot? modelAfter = await CaptureLiveFixtureModelAsync(invocation, invocationTemporary,
                model.Root, hint.RunId, cancellationToken).ConfigureAwait(false);
            if (after is null || after.PrivateManifestSha256 != selectedInputs.PrivateManifestSha256
                || modelAfter is null || modelAfter.ModelSha256 != model.ModelSha256
                || !SameLiveProcess(ReadManagedProcessIdentity(invocation.Pid), owner)
                || !SameLiveProcess(ReadManagedProcessIdentity(hint.Pid), lead)
                || selectedChain.Any(identity => !SameLiveProcess(ReadManagedProcessIdentity(identity.Pid), identity)))
                return null;
            if (Directory.EnumerateFileSystemEntries(model.Root, directName).Any()
                || !records.ToHashSet(StringComparer.Ordinal).SetEquals(
                    Directory.EnumerateFiles(model.Root, "managed-host-root-*.json").Take(5 * 64 + 1))) return null;
            var manifest = System.Text.Json.Nodes.JsonNode.Parse(after.PrivateManifest)!.AsObject();
            manifest["process"]!["resourceAssociation"] = "fixture-sdk-lead-and-live-host-ancestry";
            manifest["sdkLead"] = JsonSerializer.SerializeToNode(new
            {
                pid = lead.Pid, start = lead.Start, lead.Parent, lead.User, lead.Group, lead.Session,
                hostAncestry = selectedChain.Select(identity => new
                    { identity.Pid, identity.Start, identity.Parent, identity.User, identity.Group, identity.Session })
            });
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
            return bytes.Length > MaximumReportBytes ? null : new(bytes, DigestBytes(bytes), after.FileCount);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or InvalidOperationException or FormatException or OverflowException
            or KeyNotFoundException or NotSupportedException)
        {
            return null;
        }
    }

    private static List<ManagedProcessIdentity>? ReadLiveHostAncestorChain(ManagedProcessIdentity host,
        ManagedProcessIdentity lead, CancellationToken cancellationToken)
    {
        var chain = new List<ManagedProcessIdentity>();
        var seen = new HashSet<int>();
        int pid = host.Pid;
        for (int count = 0; count < 128; count++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(pid)) return null;
            ManagedProcessIdentity? identity = ReadManagedProcessIdentity(pid);
            if (identity is null || identity.User != lead.User || identity.Start < lead.Start
                || identity.State is 'Z' or 'X' || count == 0 && !SameLiveProcess(identity, host)) return null;
            chain.Add(identity);
            if (identity.Pid == lead.Pid) return SameLiveProcess(identity, lead) ? chain : null;
            pid = identity.Parent;
            if (pid <= 0) return null;
        }
        return null;
    }

    private static async Task<CompiledInputSnapshot?> CaptureLiveManagedHostInputsAsync(OwnedNativeProcessContext invocation,
        string invocationTemporary, string fixtureRoot, NativeManagedLaunchHint hint, string expectedEntry,
        string candidate, CancellationToken cancellationToken)
    {
        try
        {
            if (!OperatingSystem.IsLinux() || invocation.Pid <= 0 || invocation.Group != invocation.Pid
                || invocation.Session != invocation.Pid || invocation.Start == 0
                || candidate.Length is not (40 or 64) || !LowerHex(candidate)
                || hint.RunId.Length != 32 || !LowerHex(hint.RunId) || hint.Pid <= 0 || hint.Start == 0
                || hint.Resource is not ("event-migrationservice" or "explore-api" or "explore-blazor"
                    or "event-standalone" or "explore-blazor-peer")
                || hint.InstanceSha256.Length != 64 || !hint.InstanceSha256.All(char.IsAsciiHexDigit)) return null;
            string temporary = InputDirectory(invocationTemporary);
            string root = InputDirectory(fixtureRoot);
            if (root != Path.Combine(temporary, "islamu-assurance", hint.RunId)
                || !HintAbsolutePath(expectedEntry) || !NoInputLinks(expectedEntry)) return null;
            ManagedProcessIdentity? owner = ReadManagedProcessIdentity(invocation.Pid);
            ManagedProcessIdentity? process = ReadManagedProcessIdentity(hint.Pid);
            if (owner is null || process is null || owner.Start != invocation.Start
                || owner.Group != invocation.Group || owner.Session != invocation.Session
                || owner.State is 'Z' or 'X' || process.State is 'Z' or 'X' || process.User != owner.User
                || process.Start != hint.Start || process.Start < invocation.Start) return null;
            LiveFileIdentity rootIdentity = await ReadLiveMetadataAsync(root, false, cancellationToken).ConfigureAwait(false);
            string marker = Path.Combine(root, ".islamu-test-owner");
            if (!File.Exists(marker)) return null;
            LiveFileIdentity markerIdentity = await ReadLiveMetadataAsync(marker, false, cancellationToken).ConfigureAwait(false);
            if ((rootIdentity.Bits & 0xf000) != 0x4000 || (rootIdentity.Bits & 0xfff) != 0x1c0
                || rootIdentity.User != owner.User || !PrivateLiveFile(markerIdentity, owner.User, 32)
                || !NoInputLinks(marker) || Encoding.ASCII.GetString(ReadBoundedFile(marker, 32)) != hint.RunId) return null;
            string claim = Path.Combine(root, ".apphost-claim");
            if (!File.Exists(claim)) return null;
            LiveFileIdentity claimIdentity = await ReadLiveMetadataAsync(claim, false, cancellationToken).ConfigureAwait(false);
            if ((claimIdentity.Bits & 0xf000) != 0x8000 || claimIdentity.User != owner.User
                || claimIdentity.Links != 1 || claimIdentity.Bytes != 0 || !NoInputLinks(claim)
                || !await NativeOwnerHoldsClaimAsync(invocation, owner.User, claim, claimIdentity, cancellationToken).ConfigureAwait(false))
                return null;
            string modelPath = Path.Combine(root, "container-model.json");
            if (!File.Exists(modelPath)) return null;
            LiveFileIdentity modelIdentity = await ReadLiveMetadataAsync(modelPath, false, cancellationToken).ConfigureAwait(false);
            if (!PrivateLiveFile(modelIdentity, owner.User, modelIdentity.Bytes)
                || modelIdentity.Bytes is < 1 or > 262144 || !NoInputLinks(modelPath)) return null;
            byte[] model = ReadBoundedFile(modelPath, 262144);
            if (!TryReadConfiguredContainerDeclarations(model, hint.RunId, out _)) return null;
            string recordPath = Path.Combine(root, $"managed-host-root-{hint.Pid}-{process.Start}.json");
            if (!File.Exists(recordPath)) return null;
            LiveFileIdentity recordIdentity = await ReadLiveMetadataAsync(recordPath, false, cancellationToken).ConfigureAwait(false);
            if (!PrivateLiveFile(recordIdentity, owner.User, recordIdentity.Bytes)
                || recordIdentity.Bytes is < 1 or > 16384 || !NoInputLinks(recordPath)
                || !await ProcessHoldsLiveFileAsync(hint.Pid, recordPath, recordIdentity, cancellationToken).ConfigureAwait(false)) return null;
            byte[] record = ReadBoundedFile(recordPath, 16384);
            using JsonDocument host = JsonDocument.Parse(record, DocumentOptions);
            JsonElement value = host.RootElement;
            if (!UniqueProperties(value) || !ExactFields(value, "Schema", "RunId", "Pid", "Start", "ContentRoot",
                    "ApplicationName", "EntryAssembly", "IsWebHost", "WebRoot", "ServedAssets")
                || value.GetProperty("Schema").GetInt32() != 1 || Text(value, "RunId") != hint.RunId
                || value.GetProperty("Pid").GetInt32() != hint.Pid || value.GetProperty("Start").GetUInt64() != process.Start)
                return null;
            string contentRoot = InputDirectory(Text(value, "ContentRoot"));
            string applicationName = Text(value, "ApplicationName"), recordedEntry = Text(value, "EntryAssembly");
            JsonElement webRootValue = value.GetProperty("WebRoot"), servedAssets = value.GetProperty("ServedAssets");
            if (string.IsNullOrWhiteSpace(applicationName) || applicationName.Length > 512 || applicationName.Any(char.IsControl)
                || !HintAbsolutePath(recordedEntry) || recordedEntry.Length > 4096 || recordedEntry != expectedEntry
                || value.GetProperty("IsWebHost").ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || webRootValue.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)) return null;
            bool isWebHost = value.GetProperty("IsWebHost").GetBoolean();
            string? webRoot = webRootValue.ValueKind == JsonValueKind.Null ? null : webRootValue.GetString();
            if (!string.IsNullOrEmpty(webRoot))
            {
                if (webRoot.Length > 4096 || !HintAbsolutePath(webRoot)) return null;
                for (DirectoryInfo? component = new(webRoot); component is not null; component = component.Parent)
                    if (component.LinkTarget is not null || component.Exists
                        && (component.Attributes & FileAttributes.ReparsePoint) != 0) return null;
            }
            if (!isWebHost && (webRoot is not null || servedAssets.ValueKind != JsonValueKind.Null)) return null;
            if (servedAssets.ValueKind != JsonValueKind.Null)
            {
                if (!isWebHost || !ExactFields(servedAssets, "RuntimeManifestPath", "RuntimeManifestSha256",
                        "EndpointManifestPath", "EndpointManifestSha256")) return null;
                string runtimePath = Text(servedAssets, "RuntimeManifestPath"), endpointPath = Text(servedAssets, "EndpointManifestPath");
                string runtimeHash = Text(servedAssets, "RuntimeManifestSha256"), endpointHash = Text(servedAssets, "EndpointManifestSha256");
                if (!HintAbsolutePath(runtimePath) || !HintAbsolutePath(endpointPath) || runtimePath == endpointPath
                    || runtimePath.Length > 4096 || endpointPath.Length > 4096
                    || runtimeHash.Length != 64 || !LowerHex(runtimeHash) || endpointHash.Length != 64 || !LowerHex(endpointHash)
                    || !NoInputLinks(runtimePath) || !NoInputLinks(endpointPath)
                    || !await AreRegularCandidateInputsAsync([runtimePath, endpointPath]).ConfigureAwait(false)
                    || DigestBytes(ReadBoundedFile(runtimePath, MaximumReportBytes)) != runtimeHash
                    || DigestBytes(ReadBoundedFile(endpointPath, MaximumReportBytes)) != endpointHash) return null;
            }
            string executable = new FileInfo("/proc/" + hint.Pid + "/exe").ResolveLinkTarget(true)?.FullName
                ?? throw new InvalidDataException("managed_executable_unavailable");
            string directory = new DirectoryInfo("/proc/" + hint.Pid + "/cwd").ResolveLinkTarget(true)?.FullName
                ?? throw new InvalidDataException("managed_directory_unavailable");
            if (!HintAbsolutePath(executable) || !NoInputLinks(executable) || !HintAbsolutePath(directory)
                || !NoInputLinks(directory)) return null;
            byte[] command = ReadLiveProcBytes("/proc/" + hint.Pid + "/cmdline", 65536);
            if (command.Length < 2 || command[^1] != 0) return null;
            string[] arguments = new UTF8Encoding(false, true).GetString(command).Split('\0');
            if (arguments[^1].Length != 0 || arguments[..^1].Any(argument => argument.Length == 0)) return null;
            string actualEntry;
            string dotnet = new FileInfo(ReleaseToolPaths.Dotnet).ResolveLinkTarget(true)?.FullName
                ?? Path.GetFullPath(ReleaseToolPaths.Dotnet);
            if (executable == dotnet)
            {
                int slot = arguments.Length > 2 && arguments[1] == "exec" ? 2 : 1;
                if (slot >= arguments.Length - 1 || !arguments[slot].EndsWith(".dll", StringComparison.Ordinal)) return null;
                actualEntry = Path.GetFullPath(arguments[slot], directory);
            }
            else
            {
                actualEntry = Path.ChangeExtension(executable, ".dll");
                if (Path.GetFileNameWithoutExtension(executable) != Path.GetFileNameWithoutExtension(expectedEntry)) return null;
            }
            if (actualEntry != expectedEntry) return null;
            string[] primaryPaths = [expectedEntry, Path.ChangeExtension(expectedEntry, ".deps.json"),
                Path.ChangeExtension(expectedEntry, ".runtimeconfig.json")];
            var primaryIdentities = new List<LiveFileIdentity>();
            foreach (string path in primaryPaths)
            {
                if (!NoInputLinks(path)) return null;
                LiveFileIdentity identity = await ReadLiveMetadataAsync(path, false, cancellationToken).ConfigureAwait(false);
                if ((identity.Bits & 0xf000) != 0x8000 || identity.Bytes is < 1 or > 1_073_741_824) return null;
                primaryIdentities.Add(identity);
            }
            if (!TryCaptureProjectManagedInputs(Path.GetDirectoryName(expectedEntry)!, candidate,
                [Path.GetFileName(expectedEntry)], out var managed, out _)) return null;
            LiveFileIdentity executableIdentity = await ReadLiveMetadataAsync(executable, false, cancellationToken).ConfigureAwait(false);
            if ((executableIdentity.Bits & 0xf000) != 0x8000 || executableIdentity.Bytes is < 1 or > 1_073_741_824) return null;
            string executableDigest = HashLiveExecutable(executable, executableIdentity.Bytes);
            if (hint.Inputs is JsonElement supplied)
            {
                if (!VerifyLiveSuppliedInputs(supplied, process, executable, directory, actualEntry, cancellationToken))
                    return null;
            }
            if (!NoInputLinks(root) || !NoInputLinks(marker) || !NoInputLinks(claim)
                || !NoInputLinks(modelPath) || !NoInputLinks(recordPath)
                || await ReadLiveMetadataAsync(root, false, cancellationToken).ConfigureAwait(false) != rootIdentity
                || await ReadLiveMetadataAsync(marker, false, cancellationToken).ConfigureAwait(false) != markerIdentity
                || await ReadLiveMetadataAsync(claim, false, cancellationToken).ConfigureAwait(false) != claimIdentity
                || await ReadLiveMetadataAsync(modelPath, false, cancellationToken).ConfigureAwait(false) != modelIdentity
                || await ReadLiveMetadataAsync(recordPath, false, cancellationToken).ConfigureAwait(false) != recordIdentity
                || Encoding.ASCII.GetString(ReadBoundedFile(marker, 32)) != hint.RunId
                || !ReadBoundedFile(modelPath, 262144).AsSpan().SequenceEqual(model)
                || !ReadBoundedFile(recordPath, 16384).AsSpan().SequenceEqual(record)
                || HashLiveExecutable(executable, executableIdentity.Bytes) != executableDigest
                || !TryCaptureProjectManagedInputs(Path.GetDirectoryName(expectedEntry)!, candidate,
                    [Path.GetFileName(expectedEntry)], out var afterManaged, out _)
                || afterManaged!.PrivateManifestSha256 != managed!.PrivateManifestSha256
                || !await NativeOwnerHoldsClaimAsync(invocation, owner.User, claim, claimIdentity, cancellationToken).ConfigureAwait(false)
                || !await ProcessHoldsLiveFileAsync(hint.Pid, recordPath, recordIdentity, cancellationToken).ConfigureAwait(false)) return null;
            for (int ordinal = 0; ordinal < primaryPaths.Length; ordinal++)
                if (!NoInputLinks(primaryPaths[ordinal])
                    || await ReadLiveMetadataAsync(primaryPaths[ordinal], false, cancellationToken).ConfigureAwait(false)
                        != primaryIdentities[ordinal]) return null;
            if (await ReadLiveMetadataAsync(executable, false, cancellationToken).ConfigureAwait(false) != executableIdentity
                || await ReadLiveMetadataAsync(root, false, cancellationToken).ConfigureAwait(false) != rootIdentity
                || !ReadBoundedFile(modelPath, 262144).AsSpan().SequenceEqual(model)
                || !ReadBoundedFile(recordPath, 16384).AsSpan().SequenceEqual(record)
                || new FileInfo("/proc/" + hint.Pid + "/exe").ResolveLinkTarget(true)?.FullName != executable
                || new DirectoryInfo("/proc/" + hint.Pid + "/cwd").ResolveLinkTarget(true)?.FullName != directory
                || !ReadLiveProcBytes("/proc/" + hint.Pid + "/cmdline", 65536).AsSpan().SequenceEqual(command)
                || !SameLiveProcess(ReadManagedProcessIdentity(invocation.Pid), owner)
                || !SameLiveProcess(ReadManagedProcessIdentity(hint.Pid), process)
                || hint.Inputs is JsonElement suppliedAfter
                    && !VerifyLiveSuppliedInputs(suppliedAfter, process, executable, directory, actualEntry, cancellationToken)) return null;
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1, candidateOid = candidate,
                process = new { pid = process.Pid, start = process.Start, user = process.User, group = process.Group,
                    session = process.Session, resource = hint.Resource, instanceSha256 = hint.InstanceSha256,
                    resourceAssociation = "fixture-sdk-lead-metadata", executable,
                    executableSha256 = executableDigest, entry = actualEntry, workingDirectory = directory },
                hostRoot = new { contentRoot, applicationName, entryAssembly = recordedEntry, isWebHost, webRoot,
                    servedAssets = servedAssets.Clone(), recordSha256 = DigestBytes(record), device = recordIdentity.Device,
                    inode = recordIdentity.Inode, liveDescriptorMatched = true,
                    assertionAuthority = "reviewed-child-source-and-operator" },
                invocation, fixture = new { runId = hint.RunId, root, claimIdentity.Device, claimIdentity.Inode },
                containerDeclarationSha256 = DigestBytes(model), containerDeclarationKind = "typed-prestart-declaration",
                managed = JsonSerializer.Deserialize<JsonElement>(managed!.PrivateManifest)
            });
            if (manifest.Length > MaximumReportBytes) return null;
            return new(manifest, DigestBytes(manifest), managed.FileCount);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or InvalidOperationException or FormatException or OverflowException
            or KeyNotFoundException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record ManagedProcessIdentity(int Pid, int Parent, uint User, int Group, int Session, ulong Start, char State);
    private sealed record LiveFileIdentity(uint Bits, uint User, ulong Links, ulong Device, ulong Inode,
        long Bytes, string Modified, string Created);

    private static bool SameLiveProcess(ManagedProcessIdentity? current, ManagedProcessIdentity expected) =>
        current is not null && current.State is not ('Z' or 'X') && current.Pid == expected.Pid
        && current.Parent == expected.Parent && current.User == expected.User && current.Group == expected.Group
        && current.Session == expected.Session && current.Start == expected.Start;

    private static bool VerifyLiveSuppliedInputs(JsonElement supplied, ManagedProcessIdentity process,
        string executable, string directory, string entry, CancellationToken cancellationToken)
    {
        if (!ValidManagedHintInputs(supplied, process.Pid, process.Start)
            || Text(supplied, "Entry") != entry || Text(supplied, "Executable") != executable
            || Text(supplied, "WorkingDirectory") != directory) return false;
        var identities = new List<ManagedProcessIdentity>();
        foreach (JsonElement claimed in supplied.GetProperty("Ancestry").EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManagedProcessIdentity? actual = ReadManagedProcessIdentity(claimed.GetProperty("Pid").GetInt32());
            if (actual is null || actual.State is 'Z' or 'X' || Text(claimed, "State") is "Z" or "X"
                || actual.Start != claimed.GetProperty("Start").GetUInt64()
                || actual.Parent != claimed.GetProperty("Parent").GetInt32()
                || actual.User != claimed.GetProperty("User").GetUInt32() || actual.User != process.User
                || actual.Group != claimed.GetProperty("Group").GetInt32()
                || actual.Session != claimed.GetProperty("Session").GetInt32()) return false;
            identities.Add(actual);
        }
        foreach (JsonElement claimed in supplied.GetProperty("Files").EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Text(claimed, "Path");
            if (!NoInputLinks(path)) return false;
            var file = new FileInfo(path);
            long bytes = claimed.GetProperty("Bytes").GetInt64();
            long modified = claimed.GetProperty("LastWriteUtcTicks").GetInt64();
            long created = claimed.GetProperty("CreationUtcTicks").GetInt64();
            if (!file.Exists || file.Length != bytes || file.LastWriteTimeUtc.Ticks != modified
                || file.CreationTimeUtc.Ticks != created
                || !string.Equals(HashLiveExecutable(path, bytes), Text(claimed, "Sha256"), StringComparison.OrdinalIgnoreCase))
                return false;
            file.Refresh();
            if (!file.Exists || !NoInputLinks(path) || file.Length != bytes
                || file.LastWriteTimeUtc.Ticks != modified || file.CreationTimeUtc.Ticks != created) return false;
        }
        return identities.All(identity => SameLiveProcess(ReadManagedProcessIdentity(identity.Pid), identity));
    }

    private static string HashLiveExecutable(string path, long bytes)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length != bytes) throw new InvalidDataException("managed_executable_changed");
        string digest = Convert.ToHexStringLower(SHA256.HashData(input));
        if (input.Length != bytes) throw new InvalidDataException("managed_executable_changed");
        return digest;
    }

    private static ManagedProcessIdentity? ReadManagedProcessIdentity(int pid)
    {
        try
        {
            string stat = new UTF8Encoding(false, true).GetString(ReadLiveProcBytes("/proc/" + pid + "/stat", 16384));
            int end = stat.LastIndexOf(')');
            if (pid <= 0 || end < 2 || !stat.StartsWith(pid + " (", StringComparison.Ordinal)) return null;
            string[] fields = stat[(end + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string status = new UTF8Encoding(false, true).GetString(ReadLiveProcBytes("/proc/" + pid + "/status", 65536));
            string[] user = status.Split('\n').Single(line => line.StartsWith("Uid:", StringComparison.Ordinal))
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 20 || fields[0].Length != 1 || user.Length != 5
                || !uint.TryParse(user[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint uid)
                || user.Skip(2).Any(value => !uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint other) || uid != other)
                || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int parent)
                || !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int group)
                || !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out int session)
                || !ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out ulong start)
                || parent < 0 || group <= 0 || session <= 0 || start == 0) return null;
            return new(pid, parent, uid, group, session, start, fields[0][0]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[] ReadLiveProcBytes(string path, int maximum)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new MemoryStream();
        byte[] buffer = new byte[Math.Min(maximum + 1, 4096)];
        int read;
        while ((read = input.Read(buffer, 0, Math.Min(buffer.Length, maximum + 1 - (int)output.Length))) != 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length > maximum) throw new InvalidDataException("managed_proc_input_too_large");
        }
        return output.ToArray();
    }

    private static bool PrivateLiveFile(LiveFileIdentity identity, uint user, long bytes) =>
        (identity.Bits & 0xf000) == 0x8000 && (identity.Bits & 0xfff) == 0x180
        && identity.User == user && identity.Links == 1 && identity.Bytes == bytes;

    private static async Task<bool> NativeOwnerHoldsClaimAsync(OwnedNativeProcessContext invocation, uint user,
        string path, LiveFileIdentity expected, CancellationToken cancellationToken)
    {
        ManagedProcessIdentity? leader = ReadManagedProcessIdentity(invocation.Pid);
        if (leader is null || leader.User != user || leader.Start != invocation.Start
            || leader.Group != invocation.Group || leader.Session != invocation.Session || leader.State is 'Z' or 'X') return false;
        if (await ProcessHoldsLiveFileAsync(invocation.Pid, path, expected, cancellationToken).ConfigureAwait(false)
            && SameLiveProcess(ReadManagedProcessIdentity(invocation.Pid), leader)) return true;
        string[] processes = Directory.EnumerateDirectories("/proc").Take(65537).ToArray();
        if (processes.Length > 65536) return false;
        foreach (string directory in processes)
        {
            if (!int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out int pid)) continue;
            if (pid == invocation.Pid) continue;
            ManagedProcessIdentity? identity = ReadManagedProcessIdentity(pid);
            if (identity is null || identity.User != user || identity.Group != invocation.Group
                || identity.Session != invocation.Session || identity.State is 'Z' or 'X') continue;
            if (await ProcessHoldsLiveFileAsync(pid, path, expected, cancellationToken).ConfigureAwait(false)
                && SameLiveProcess(ReadManagedProcessIdentity(pid), identity)) return true;
        }
        return false;
    }

    private static async Task<bool> ProcessHoldsLiveFileAsync(int pid, string path, LiveFileIdentity expected,
        CancellationToken cancellationToken)
    {
        string directory = "/proc/" + pid + "/fd";
        string[] descriptors = Directory.EnumerateFileSystemEntries(directory).Take(4097).ToArray();
        if (descriptors.Length > 4096) return false;
        foreach (string descriptor in descriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!int.TryParse(Path.GetFileName(descriptor), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                || number < 0 || new FileInfo(descriptor).LinkTarget != path) continue;
            LiveFileIdentity identity = await ReadLiveMetadataAsync(descriptor, true, cancellationToken).ConfigureAwait(false);
            string text = new UTF8Encoding(false, true).GetString(ReadLiveProcBytes("/proc/" + pid + "/fdinfo/" + number, 16384));
            string[] flags = text.Split('\n').Single(line => line.StartsWith("flags:", StringComparison.Ordinal))
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (flags.Length != 2) return false;
            ulong access = Convert.ToUInt64(flags[1], 8) & 3;
            if (identity == expected && access is 1 or 2) return true;
        }
        return false;
    }

    private static async Task<LiveFileIdentity> ReadLiveMetadataAsync(string path, bool follow, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/stat")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        } };
        process.StartInfo.Environment.Clear();
        process.StartInfo.Environment["LC_ALL"] = "C";
        process.StartInfo.Environment["TZ"] = "UTC";
        if (follow) process.StartInfo.ArgumentList.Add("--dereference");
        foreach (string argument in new[] { "--format=%f|%u|%h|%d|%i|%s|%y|%w", "--", path }) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new InvalidDataException("managed_metadata_unavailable");
        Task<string> output = ReadLiveMetadataPipeAsync(process.StandardOutput, deadline.Token);
        Task<string> error = ReadLiveMetadataPipeAsync(process.StandardError, deadline.Token);
        var failures = new List<Exception>();
        string? value = null;
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            value = await output.WaitAsync(deadline.Token).ConfigureAwait(false);
            if ((await error.WaitAsync(deadline.Token).ConfigureAwait(false)).Length != 0 || process.ExitCode != 0)
                throw new InvalidDataException("managed_metadata_unavailable");
        }
        catch (Exception failure) { failures.Add(failure); }
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception failure) { failures.Add(failure); }
        try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false); }
        catch (Exception failure) { failures.Add(failure); }
        try { await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false); }
        catch (Exception failure) { failures.Add(failure); }
        bool outputComplete = output.IsCompleted, errorComplete = error.IsCompleted;
        foreach (Task<string> task in new[] { outputComplete ? output : null, errorComplete ? error : null }.OfType<Task<string>>())
        {
            try { await task.ConfigureAwait(false); }
            catch (Exception failure)
            {
                if (!failures.Any(existing => ReferenceEquals(existing, failure))) failures.Add(failure);
            }
        }
        if (failures.Count != 0) throw new AggregateException(failures);
        string[] fields = value!.TrimEnd('\n').Split('|');
        if (fields.Length != 8 || !uint.TryParse(fields[0], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint bits)
            || !uint.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint user)
            || !ulong.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out ulong links)
            || !ulong.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out ulong device)
            || !ulong.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out ulong inode)
            || !long.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out long bytes) || bytes < 0
            || fields[6].Length is < 1 or > 64 || fields[7].Length is < 1 or > 64
            || fields.Skip(6).Any(field => field.Any(char.IsControl)))
            throw new InvalidDataException("managed_metadata_invalid");
        return new(bits, user, links, device, inode, bytes, fields[6], fields[7]);
    }

    private static async Task<string> ReadLiveMetadataPipeAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        char[] buffer = new char[513];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0) return new string(buffer, 0, count);
            count += read;
        }
        throw new InvalidDataException("managed_metadata_too_large");
    }

    internal sealed record NativeManagedLaunchHint(string RunId, string Resource, string InstanceSha256,
        int Pid, ulong Start, string Diagnostic, JsonElement? Inputs);

    internal static bool TryReadManagedLaunchHint(byte[] bytes, string fixtureRunId,
        out NativeManagedLaunchHint? hint, out string diagnostic)
    {
        hint = null;
        diagnostic = "managed_launch_hint_invalid";
        try
        {
            if (bytes.Length is < 1 or > 65_536 || fixtureRunId.Length != 32 || !LowerHex(fixtureRunId)) return false;
            using JsonDocument document = JsonDocument.Parse(bytes, DocumentOptions);
            JsonElement root = document.RootElement;
            if (!UniqueProperties(root) || !ExactFields(root, "Schema", "RunId", "Hint") ||
                root.GetProperty("Schema").GetInt32() != 1 || Text(root, "RunId") != fixtureRunId) return false;
            JsonElement value = root.GetProperty("Hint");
            bool hasInputs = value.TryGetProperty("Inputs", out JsonElement inputs);
            if (hasInputs ? !ExactFields(value, "Resource", "InstanceSha256", "Pid", "Start", "Diagnostic", "Inputs")
                : !ExactFields(value, "Resource", "InstanceSha256", "Pid", "Start", "Diagnostic")) return false;
            string resource = Text(value, "Resource");
            string instance = Text(value, "InstanceSha256");
            int pid = value.GetProperty("Pid").GetInt32();
            ulong start = value.GetProperty("Start").GetUInt64();
            string reason = Text(value, "Diagnostic");
            if (resource is not ("event-migrationservice" or "explore-api" or "explore-blazor" or
                    "event-standalone" or "explore-blazor-peer") ||
                instance.Length != 64 || !instance.All(char.IsAsciiHexDigit) || pid <= 0 || start == 0 ||
                reason.Length is < 1 or > 128 || reason.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '.' or ':'))) return false;
            JsonElement? capturedInputs = null;
            if (hasInputs && inputs.ValueKind != JsonValueKind.Null)
            {
                if (!ValidManagedHintInputs(inputs, pid, start)) return false;
                capturedInputs = inputs.Clone();
            }
            hint = new(fixtureRunId, resource, instance, pid, start, reason, capturedInputs);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or InvalidOperationException or
            ArgumentException or FormatException or OverflowException or KeyNotFoundException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool ValidManagedHintInputs(JsonElement inputs, int pid, ulong start)
    {
        if (!ExactFields(inputs, "Ancestry", "Executable", "WorkingDirectory", "Entry", "Files")) return false;
        string executable = Text(inputs, "Executable"), directory = Text(inputs, "WorkingDirectory"), entry = Text(inputs, "Entry");
        if (!HintAbsolutePath(executable) || !HintAbsolutePath(directory) || !HintAbsolutePath(entry)) return false;
        JsonElement ancestry = inputs.GetProperty("Ancestry");
        if (ancestry.ValueKind != JsonValueKind.Array || ancestry.GetArrayLength() is < 1 or > 128) return false;
        if (ancestry[0].GetProperty("Start").GetUInt64() != start) return false;
        var pids = new HashSet<int>();
        int expected = pid;
        foreach (JsonElement identity in ancestry.EnumerateArray())
        {
            if (!ExactFields(identity, "Pid", "Parent", "User", "Group", "Session", "Start", "State")) return false;
            int current = identity.GetProperty("Pid").GetInt32(), parent = identity.GetProperty("Parent").GetInt32();
            string state = Text(identity, "State");
            if (current <= 0 || current != expected || !pids.Add(current) || parent < 0 ||
                !identity.GetProperty("User").TryGetUInt32(out _) || identity.GetProperty("Group").GetInt32() <= 0 ||
                identity.GetProperty("Session").GetInt32() <= 0 || identity.GetProperty("Start").GetUInt64() == 0 ||
                state.Length != 1 || !"RSDTtZXIKWP".Contains(state[0], StringComparison.Ordinal)) return false;
            expected = parent;
        }
        JsonElement files = inputs.GetProperty("Files");
        if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() != 4) return false;
        string[] paths = [executable, entry, Path.ChangeExtension(entry, ".deps.json"), Path.ChangeExtension(entry, ".runtimeconfig.json")];
        if (paths.Distinct(StringComparer.Ordinal).Count() != paths.Length) return false;
        int ordinal = 0;
        foreach (JsonElement file in files.EnumerateArray())
        {
            string digest = Text(file, "Sha256");
            if (!ExactFields(file, "Path", "Bytes", "Sha256", "LastWriteUtcTicks", "CreationUtcTicks") ||
                Text(file, "Path") != paths[ordinal++] || file.GetProperty("Bytes").GetInt64() is < 0 or > 1_073_741_824 ||
                digest.Length != 64 || !digest.All(char.IsAsciiHexDigit) ||
                file.GetProperty("LastWriteUtcTicks").GetInt64() is < 0 or > 3155378975999999999L ||
                file.GetProperty("CreationUtcTicks").GetInt64() is < 0 or > 3155378975999999999L) return false;
        }
        return true;
    }

    private static bool HintAbsolutePath(string path) => path.Length is >= 1 and <= 8192 &&
        !path.Any(char.IsControl) && Path.IsPathFullyQualified(path) && Path.GetFullPath(path) == path;

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
            bool singleton = laneId is "owned-runtime" or "owned-load-control" or "owned-load-correctness";
            bool owned = laneId.StartsWith("owned-", StringComparison.Ordinal);
            var allUids = discovered.ToDictionary(node => node.Uid, StringComparer.Ordinal);
            var identities = discovered.Select(node => node.Identity).ToHashSet();
            if ((owned ? !requirement.Tests.SetEquals(identities) : !requirement.Tests.IsSubsetOf(identities)) ||
                (singleton ? invocations.Count != discovered.Count : invocations.Count != 1)) return false;
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
                if (invocation.SelectedUids.Count is < 1 or > 65_536 || (singleton && invocation.SelectedUids.Count != 1) ||
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
            bool singleton = laneId is "owned-runtime" or "owned-load-control" or "owned-load-correctness";
            bool owned = laneId.StartsWith("owned-", StringComparison.Ordinal);
            if ((singleton ? proofs.Count != requirement.Tests.Count : proofs.Count != 1) ||
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
                    (singleton && nodes.GetArrayLength() != 1)) return false;
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
        TimeSpan timeout, CancellationToken cancellationToken,
        Func<OwnedNativeProcessContext, CancellationToken, Task>? observer = null)
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
        using var observerStop = new CancellationTokenSource();
        Task? observation = null;
        var observerFailures = new List<Exception>();
        bool observerSettled = observer is null;
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
            if (observer is null)
                await process.WaitForExitAsync(stop.Token).ConfigureAwait(false);
            else
            {
                var context = new OwnedNativeProcessContext(process.Id, leader.Group, leader.Session, leader.Start);
                observation = Task.Run(() => observer(context, observerStop.Token), CancellationToken.None);
                Task exit = process.WaitForExitAsync(stop.Token);
                Task completed = await Task.WhenAny(exit, observation).ConfigureAwait(false);
                if (completed != observation || observation.IsCompletedSuccessfully)
                    await exit.ConfigureAwait(false);
            }
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
            try
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
            finally
            {
                if (observation is not null)
                {
                    Task cancellation = observerStop.CancelAsync();
                    try { await cancellation.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception exception) { observerFailures.Add(exception); }
                    try { await observation.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception exception) { observerFailures.Add(exception); }
                    bool observationCompleted = observation.IsCompleted;
                    bool cancellationCompleted = cancellation.IsCompleted;
                    if (observationCompleted)
                    {
                        try { await observation.ConfigureAwait(false); }
                        catch (Exception exception)
                        {
                            if (!observerFailures.Any(failure => ReferenceEquals(failure, exception)))
                                observerFailures.Add(exception);
                        }
                    }
                    if (cancellationCompleted)
                    {
                        try { await cancellation.ConfigureAwait(false); }
                        catch (Exception exception)
                        {
                            if (!observerFailures.Any(failure => ReferenceEquals(failure, exception)))
                                observerFailures.Add(exception);
                        }
                    }
                    observerSettled = observationCompleted && cancellationCompleted;
                }
            }
        }
        return new OwnedNativeProcessResult(started && process.HasExited ? process.ExitCode : -1,
            timedOut, cancelled, residual, output.Exceeded || error.Exceeded, cleanup && !invocationFailed, output.Bytes, error.Bytes)
        {
            ObserverSettled = observerSettled,
            ObserverFailures = observerFailures.AsReadOnly()
        };
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
    internal sealed record OwnedNativeProcessContext(int Pid, int Group, int Session, ulong Start);

    internal sealed record OwnedNativeProcessResult(int ExitCode, bool TimedOut, bool Cancelled,
        bool ResidualOwnedChildren, bool OutputLimitExceeded, bool CleanupSucceeded,
        byte[] StandardOutput, byte[] StandardError)
    {
        internal bool ObserverSettled { get; init; } = true;
        internal IReadOnlyList<Exception> ObserverFailures { get; init; } = [];
        internal bool Succeeded => ExitCode == 0 && !TimedOut && !Cancelled && !ResidualOwnedChildren &&
            !OutputLimitExceeded && CleanupSucceeded && ObserverSettled && ObserverFailures.Count == 0;
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
