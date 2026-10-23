using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Services.Moderation;

/// <summary>
/// Parses the single native activation inventory and compares complete, current runtime evidence.
/// The composition owner supplies schemas/moderation-capabilities.json; there is no built-in fallback
/// matrix, plugin discovery or filesystem search. Source-support metadata is deliberately not a permit.
/// </summary>
public sealed class ModerationCapabilityRegistry
{
    private const int MaximumInventoryBytes = 512 * 1024;
    private readonly FrozenDictionary<(ModerationCapability Capability, ModerationNativeMode Mode),
        ImmutableArray<string>> _required;

    private ModerationCapabilityRegistry(
        long revision,
        string fingerprint,
        FrozenDictionary<string, ModerationConsumerInventoryEntry> consumers,
        FrozenDictionary<ModerationNativeMode, ModerationModeInventoryEntry> modes,
        FrozenDictionary<ModerationCapability, string> closures,
        FrozenDictionary<(ModerationCapability, ModerationNativeMode), ImmutableArray<string>> required)
        => (Revision, Fingerprint, Consumers, Modes, EffectClosures, _required) =
            (revision, fingerprint, consumers, modes, closures, required);

    public long Revision { get; }
    public string Fingerprint { get; }
    public FrozenDictionary<string, ModerationConsumerInventoryEntry> Consumers { get; }
    public FrozenDictionary<ModerationNativeMode, ModerationModeInventoryEntry> Modes { get; }
    public FrozenDictionary<ModerationCapability, string> EffectClosures { get; }

    /// <summary>Reads the shipped single inventory, never a working-directory substitute or fallback.</summary>
    public static string ReadInventory()
    {
        using var resource = typeof(ModerationCapabilityRegistry).Assembly
            .GetManifestResourceStream("Explore.Application.Moderation.Capabilities")
            ?? throw new InvalidOperationException("The native moderation activation inventory is not packaged.");
        using var reader = new StreamReader(resource);
        return reader.ReadToEnd();
    }

    /// <summary>Rejects unknown/duplicate fields, incomplete mode/effect coverage and dangling consumer IDs.</summary>
    public static ModerationCapabilityRegistry Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > MaximumInventoryBytes)
            throw new JsonException("Moderation inventory exceeds the bounded native contract size.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        Shape(root, "formatVersion", "revision", "contract", "modes", "consumers", "effects");
        if (root.GetProperty("formatVersion").ValueKind != JsonValueKind.Number
            || !root.GetProperty("formatVersion").TryGetInt32(out int version) || version != 1
            || root.GetProperty("revision").ValueKind != JsonValueKind.Number
            || !root.GetProperty("revision").TryGetInt64(out long revision) || revision < 1)
            throw new JsonException("Unsupported moderation inventory format or revision.");
        _ = Text(root, "contract");

        var modes = new Dictionary<ModerationNativeMode, ModerationModeInventoryEntry>();
        foreach (var element in Items(root, "modes"))
        {
            Shape(element, "mode", "supportState", "notes");
            var mode = Named<ModerationNativeMode>(element, "mode");
            if (!modes.TryAdd(mode, new(mode, Named<ModerationSourceSupport>(element, "supportState"),
                    Text(element, "notes"))))
                throw new JsonException("Duplicate moderation mode.");
        }
        if (modes.Count != Enum.GetValues<ModerationNativeMode>().Length)
            throw new JsonException("Every native mode must be explicitly inventoried.");

        var consumers = new Dictionary<string, ModerationConsumerInventoryEntry>(StringComparer.Ordinal);
        foreach (var element in Items(root, "consumers"))
        {
            Shape(element, "id", "subjectResolver", "targetResolver", "authorizationOwner", "entryCheckpoint",
                "terminalCheckpoint", "writerFence", "freshnessClass", "owningPacket", "conformanceTest",
                "supportState", "supportNotes", "sourcePaths", "aliases");
            string id = Code(element, "id");
            var contract = new ModerationConsumerContract(id, Text(element, "subjectResolver"),
                Text(element, "targetResolver"), Text(element, "authorizationOwner"),
                Text(element, "entryCheckpoint"), Text(element, "terminalCheckpoint"), Text(element, "writerFence"),
                Named<ModerationConsumerFreshness>(element, "freshnessClass"), Text(element, "owningPacket"),
                Code(element, "conformanceTest"));
            var entry = new ModerationConsumerInventoryEntry(contract,
                Named<ModerationSourceSupport>(element, "supportState"), Text(element, "supportNotes"),
                Strings(element, "sourcePaths"), Strings(element, "aliases"));
            if (!consumers.TryAdd(id, entry))
                throw new JsonException("Duplicate moderation consumer.");
        }

        var required = new Dictionary<(ModerationCapability, ModerationNativeMode), ImmutableArray<string>>();
        var closures = new Dictionary<ModerationCapability, string>();
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in Items(root, "effects"))
        {
            Shape(effect, "capability", "closure", "modes");
            var capability = Named<ModerationCapability>(effect, "capability");
            if (!closures.TryAdd(capability, Text(effect, "closure")))
                throw new JsonException("Duplicate moderation effect.");
            foreach (var modeElement in Items(effect, "modes"))
            {
                Shape(modeElement, "mode", "requiredConsumers");
                var mode = Named<ModerationNativeMode>(modeElement, "mode");
                var ids = Strings(modeElement, "requiredConsumers");
                if (ids.Any(id => !consumers.ContainsKey(id)))
                    throw new JsonException("An effect references an unknown native consumer.");
                if (!required.TryAdd((capability, mode), ids))
                    throw new JsonException("Duplicate moderation effect/mode.");
                referenced.UnionWith(ids);
            }
        }
        if (closures.Count != Enum.GetValues<ModerationCapability>().Length
            || required.Count != closures.Count * modes.Count)
            throw new JsonException("Every capability requires an explicit nonempty set for every native mode.");
        if (!referenced.SetEquals(consumers.Keys))
            throw new JsonException("Unreferenced consumers cannot enter the finite activation inventory.");

        return new(revision, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))),
            consumers.ToFrozenDictionary(StringComparer.Ordinal), modes.ToFrozenDictionary(),
            closures.ToFrozenDictionary(), required.ToFrozenDictionary());
    }

    public ImmutableArray<string> RequiredConsumers(ModerationCapability capability, ModerationNativeMode mode) =>
        _required.TryGetValue((capability, mode), out var required)
            ? required : throw new ArgumentException("Unknown native moderation effect/mode.");

    /// <summary>
    /// Compares exact sets, never intersects or silently filters registrations. Current sources are
    /// independent authority observations for this publication. Call inside the native writer's
    /// qualification/fence lifetime; a successful return cannot protect a subsequent unfenced write.
    /// </summary>
    public ModerationInventoryMatch Match(
        ModerationActivationBinding binding,
        IEnumerable<ModerationCapabilityRegistration> registrations,
        IEnumerable<ModerationCapabilitySourceObservation> currentSources,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(currentSources);
        if (!ValidBinding(binding))
            return new([], [new(ModerationInventoryMismatch.InvalidBinding)], []);
        var required = RequiredConsumers(binding.Capability, binding.Mode);
        if (observedAt.Offset != TimeSpan.Zero || observedAt == DateTimeOffset.MinValue
            || observedAt == DateTimeOffset.MaxValue)
            return new(required, [new(ModerationInventoryMismatch.InvalidObservationTime)], []);

        var issues = ImmutableArray.CreateBuilder<ModerationInventoryIssue>();
        var expected = required.ToFrozenSet(StringComparer.Ordinal);
        var supplied = new Dictionary<string, ModerationCapabilityRegistration>(StringComparer.Ordinal);
        var sources = new Dictionary<string, ModerationCapabilitySourceObservation>(StringComparer.Ordinal);
        var sourceIds = new HashSet<Guid>();
        foreach (var source in currentSources)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.ConsumerId))
            {
                issues.Add(new(ModerationInventoryMismatch.InvalidSource));
                continue;
            }
            if (!sources.TryAdd(source.ConsumerId, source))
                issues.Add(new(ModerationInventoryMismatch.DuplicateCurrentSource, source.ConsumerId));
            if (!expected.Contains(source.ConsumerId))
                issues.Add(new(ModerationInventoryMismatch.ExtraCurrentSource, source.ConsumerId));
            if (!sourceIds.Add(source.SourceId))
                issues.Add(new(ModerationInventoryMismatch.ReusedSourceIdentity, source.ConsumerId));
            if (!Version7(source.SourceId) || source.Revision < 1 || !Generation(source.WriterGeneration))
                issues.Add(new(ModerationInventoryMismatch.InvalidSource, source.ConsumerId));
            if (source.Binding != binding)
                issues.Add(new(ModerationInventoryMismatch.BindingMismatch, source.ConsumerId));
            if (source.ObservedAt != observedAt)
                issues.Add(new(ModerationInventoryMismatch.StaleObservation, source.ConsumerId));
            if (source.ValidUntil.Offset != TimeSpan.Zero || source.ValidUntil <= observedAt
                || source.ValidUntil == DateTimeOffset.MaxValue)
                issues.Add(new(ModerationInventoryMismatch.ExpiredSourceAuthority, source.ConsumerId));
        }

        foreach (var registration in registrations)
        {
            if (registration?.Contract is not { } contract || string.IsNullOrWhiteSpace(contract.Id))
            {
                issues.Add(new(ModerationInventoryMismatch.InvalidSource));
                continue;
            }
            string id = contract.Id;
            if (!supplied.TryAdd(id, registration))
                issues.Add(new(ModerationInventoryMismatch.DuplicateConsumer, id));
            if (!Consumers.TryGetValue(id, out var inventory))
                issues.Add(new(ModerationInventoryMismatch.UnknownConsumer, id));
            else if (inventory.Contract != contract)
                issues.Add(new(ModerationInventoryMismatch.ContractMismatch, id));
            if (!expected.Contains(id))
                issues.Add(new(ModerationInventoryMismatch.ExtraConsumer, id));
            if (registration.Binding != binding)
                issues.Add(new(ModerationInventoryMismatch.BindingMismatch, id));
            if (!string.Equals(registration.InventoryFingerprint, Fingerprint, StringComparison.Ordinal))
                issues.Add(new(ModerationInventoryMismatch.InventoryMismatch, id));
            if (registration.Conformance != ModerationNativeConformance.Passed)
                issues.Add(new(ModerationInventoryMismatch.ConformanceNotPassed, id));
            if (registration.VerifiedAt != observedAt)
                issues.Add(new(ModerationInventoryMismatch.StaleObservation, id));
            if (registration.ValidUntil.Offset != TimeSpan.Zero || registration.ValidUntil <= observedAt
                || registration.ValidUntil == DateTimeOffset.MaxValue)
                issues.Add(new(ModerationInventoryMismatch.ExpiredConformanceAuthority, id));
            if (!Version7(registration.SourceId) || registration.SourceRevision < 1
                || !Generation(registration.WriterGeneration))
                issues.Add(new(ModerationInventoryMismatch.InvalidSource, id));
            if (sources.TryGetValue(id, out var source)
                && (registration.SourceId != source.SourceId || registration.SourceRevision != source.Revision
                    || !string.Equals(registration.WriterGeneration, source.WriterGeneration, StringComparison.Ordinal)))
                issues.Add(new(ModerationInventoryMismatch.SourceMismatch, id));
        }
        foreach (string id in required)
        {
            if (!supplied.ContainsKey(id)) issues.Add(new(ModerationInventoryMismatch.MissingConsumer, id));
            if (!sources.ContainsKey(id)) issues.Add(new(ModerationInventoryMismatch.MissingCurrentSource, id));
        }
        return new(required, issues.ToImmutable(),
            issues.Count == 0 ? required.Select(id => supplied[id]).ToImmutableArray() : []);
    }

    private static bool ValidBinding(ModerationActivationBinding? binding) =>
        binding is { Jurisdiction: not null, Rule: not null }
        && Version7(binding.PolicyId) && Version7(binding.PublicationId) && binding.PolicyRevision > 0
        && binding.Rule.OwnerId == binding.Jurisdiction.Id
        && Enum.IsDefined(binding.Capability) && Enum.IsDefined(binding.Mode);

    private static bool Version7(Guid value) =>
        value != Guid.Empty && value.Version == 7 && (value.ToByteArray()[8] & 0xc0) == 0x80;

    private static bool Generation(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && value == value.Trim()
        && !value.Any(char.IsControl);

    private static void Shape(JsonElement element, params string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException("Native inventory entries must be objects.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!fields.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw new JsonException("Unknown or duplicate native inventory field.");
        if (seen.Count != fields.Length)
            throw new JsonException("A required native inventory field is missing.");
    }

    private static string Text(JsonElement element, string field)
    {
        var property = element.GetProperty(field);
        if (property.ValueKind != JsonValueKind.String || property.GetString() is not { } value
            || string.IsNullOrWhiteSpace(value) || value.Length > 4096 || value != value.Trim()
            || value.Any(char.IsControl))
            throw new JsonException("Native inventory text must be nonempty, bounded and canonical.");
        return value;
    }

    private static string Code(JsonElement element, string field)
    {
        string value = Text(element, field);
        if (value.Length > 100 || value.Any(character =>
                character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')))
            throw new JsonException("Native inventory codes must be bounded lowercase stable identifiers.");
        return value;
    }

    private static T Named<T>(JsonElement element, string field) where T : struct, Enum
    {
        string name = Text(element, field);
        if (!Enum.TryParse<T>(name, out var value) || !Enum.IsDefined(value) || Enum.GetName(value) != name)
            throw new JsonException("Unknown or noncanonical native inventory enum.");
        return value;
    }

    private static JsonElement.ArrayEnumerator Items(JsonElement element, string field)
    {
        var array = element.GetProperty(field);
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is < 1 or > 128)
            throw new JsonException("Native inventory lists must be nonempty and bounded.");
        return array.EnumerateArray();
    }

    private static ImmutableArray<string> Strings(JsonElement element, string field)
    {
        var values = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Items(element, field))
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } value
                || string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value != value.Trim()
                || value.Any(char.IsControl) || !seen.Add(value))
                throw new JsonException("Native inventory lists require unique bounded canonical strings.");
            values.Add(value);
        }
        return values.ToImmutable();
    }
}
