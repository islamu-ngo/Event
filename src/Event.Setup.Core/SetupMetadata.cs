namespace ISLAMU.Event.Setup.Core;

using System.Collections.ObjectModel;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed record SetupProfileIdentity
{
    public SetupProfileIdentity(string value) => Value = SetupText.Identifier(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record SetupCapabilityKey
{
    public SetupCapabilityKey(string value) => Value = SetupText.Identifier(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record SetupTopologyKey
{
    public SetupTopologyKey(string value) => Value = SetupText.Identifier(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record PortableSectionKey : IComparable<PortableSectionKey>
{
    public PortableSectionKey(string value) => Value = SetupText.SectionKey(value, nameof(value));

    public string Value { get; }

    public int CompareTo(PortableSectionKey? other) =>
        other is null ? 1 : StringComparer.Ordinal.Compare(Value, other.Value);

    public static bool operator <(PortableSectionKey left, PortableSectionKey right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(PortableSectionKey left, PortableSectionKey right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(PortableSectionKey left, PortableSectionKey right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(PortableSectionKey left, PortableSectionKey right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() => Value;
}

public sealed record SetupProfile
{
    public SetupProfile(
        SetupProfileIdentity identity,
        IEnumerable<SetupCapabilityKey> capabilities,
        IEnumerable<SetupTopologyKey> topology)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Capabilities = SetupSnapshot.OrderedDistinct(
            capabilities, static item => item.Value, nameof(capabilities));
        Topology = SetupSnapshot.OrderedDistinct(
            topology, static item => item.Value, nameof(topology));
    }

    public SetupProfileIdentity Identity { get; }
    public IReadOnlyList<SetupCapabilityKey> Capabilities { get; }
    public IReadOnlyList<SetupTopologyKey> Topology { get; }
}

public enum SetupScope
{
    Instance,
    Tenant
}

public sealed record SetupSelection
{
    public SetupSelection(
        SetupScope scope,
        ConfigurationImportApplyMode applyMode,
        IEnumerable<PortableSectionKey> sections)
    {
        Scope = scope;
        ApplyMode = applyMode;
        Sections = SetupSnapshot.OrderedDistinct(
            sections, static item => item.Value, nameof(sections));
    }

    public SetupScope Scope { get; }
    public ConfigurationImportApplyMode ApplyMode { get; }
    public IReadOnlyList<PortableSectionKey> Sections { get; }
}

internal static class SetupSnapshot
{
    internal static IReadOnlyList<T> OrderedDistinct<T>(
        IEnumerable<T> values,
        Func<T, string> key,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        T[] snapshot = values.ToArray();
        if (snapshot.Any(static item => item is null))
            throw new ArgumentException("Collection items cannot be null.", parameterName);

        return new ReadOnlyCollection<T>(snapshot
            .DistinctBy(key, StringComparer.Ordinal)
            .OrderBy(key, StringComparer.Ordinal)
            .ToArray());
    }
}

internal static class SetupText
{
    internal static string Identifier(string value, string parameterName) =>
        Normalize(value, parameterName, allowDot: false);

    internal static string SectionKey(string value, string parameterName) =>
        Normalize(value, parameterName, allowDot: true);

    private static string Normalize(string value, string parameterName, bool allowDot)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length is < 1 or > 128
            || normalized.Any(character => !IsAllowed(character, allowDot)))
        {
            throw new ArgumentException("Identifier format is invalid.", parameterName);
        }

        return normalized;
    }

    private static bool IsAllowed(char character, bool allowDot) =>
        character is >= 'a' and <= 'z'
        || character is >= '0' and <= '9'
        || character is '-' or '_'
        || allowDot && character == '.';
}
