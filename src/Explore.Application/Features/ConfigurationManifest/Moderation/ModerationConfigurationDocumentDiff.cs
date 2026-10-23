using System.Collections.Immutable;
using System.Text.Json;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace Explore.Application.Features.ConfigurationManifest.Moderation;

public enum ModerationConfigurationChangeKind { Added, Changed, Removed }

public sealed record ModerationConfigurationChange(
    string Section, ModerationConfigurationReference? Reference,
    ModerationConfigurationChangeKind Kind, bool Conflict);

public sealed record ModerationConfigurationDiff(ImmutableArray<ModerationConfigurationChange> Changes)
{
    public bool HasConflicts => Changes.Any(change => change.Conflict);
}

public sealed record ModerationConfigurationMerge(
    ModerationConfigurationDocument? Document, ModerationConfigurationDiff Diff);

/// <summary>
/// Compares a retained preset baseline, local definitions and incoming definitions without
/// applying them. Local-only edits survive; divergent edits to one complete definition conflict.
/// Neither the baseline nor this comparison establishes destination authority or import eligibility.
/// </summary>
public static class ModerationConfigurationDocumentDiff
{
    public static ModerationConfigurationDiff Initial(ModerationConfigurationDocument incoming)
    {
        ModerationConfigurationDocumentValidator.ValidateShape(incoming);
        var changes = ImmutableArray.CreateBuilder<ModerationConfigurationChange>();
        foreach (var section in new[] { "catalogueRevision", "modes", "supportedProfiles" })
            changes.Add(new(section, null, ModerationConfigurationChangeKind.Added, false));
        Add("classes", incoming.Classes.Select(value => value.Reference));
        Add("reasons", incoming.Reasons.Select(value => value.Reference));
        Add("profiles", incoming.Profiles.Select(value => value.Rule));
        return new(changes.ToImmutable());

        void Add(string section, IEnumerable<ModerationConfigurationReference> references)
        {
            foreach (var reference in references.OrderBy(value => value.Code, StringComparer.Ordinal)
                         .ThenBy(value => value.Revision))
                changes.Add(new(section, reference, ModerationConfigurationChangeKind.Added, false));
        }
    }

    /// <summary>
    /// Preserves local-only definitions and applies complete nonconflicting incoming changes.
    /// Any conflict withholds the whole document. The destination must still validate the merged
    /// policy and qualify its retained baseline, current mapping and publication authority.
    /// </summary>
    public static ModerationConfigurationMerge Merge(
        ModerationConfigurationDocument baseline,
        ModerationConfigurationDocument local,
        ModerationConfigurationDocument incoming)
    {
        var diff = Compare(baseline, local, incoming);
        if (diff.HasConflicts)
            return new(null, diff);

        var merged = new ModerationConfigurationDocument(local.SchemaVersion,
            Changed("catalogueRevision") ? incoming.CatalogueRevision : local.CatalogueRevision,
            Changed("modes") ? incoming.Modes : local.Modes,
            Definitions("classes", local.Classes, incoming.Classes, item => item.Reference),
            Definitions("reasons", local.Reasons, incoming.Reasons, item => item.Reference),
            Definitions("profiles", local.Profiles, incoming.Profiles, item => item.Rule),
            Changed("supportedProfiles") ? incoming.SupportedProfiles : local.SupportedProfiles);
        return new(merged, diff);

        bool Changed(string section) => diff.Changes.Any(change => change.Section == section);

        IReadOnlyList<T> Definitions<T>(string section, IReadOnlyList<T> current,
            IReadOnlyList<T> proposed, Func<T, ModerationConfigurationReference> reference)
        {
            var definitions = current.ToDictionary(reference);
            foreach (var change in diff.Changes.Where(change => change.Section == section))
            {
                var key = change.Reference!;
                if (change.Kind == ModerationConfigurationChangeKind.Removed)
                    definitions.Remove(key);
                else
                    definitions[key] = proposed.Single(item => reference(item) == key);
            }
            return definitions.OrderBy(item => item.Key.Code, StringComparer.Ordinal)
                .ThenBy(item => item.Key.Revision).Select(item => item.Value).ToArray();
        }
    }

    public static ModerationConfigurationDiff Compare(
        ModerationConfigurationDocument baseline,
        ModerationConfigurationDocument local,
        ModerationConfigurationDocument incoming)
    {
        using var original = JsonDocument.Parse(ModerationConfigurationDocumentProjection.Serialize(Normalize(baseline)));
        using var current = JsonDocument.Parse(ModerationConfigurationDocumentProjection.Serialize(Normalize(local)));
        using var proposed = JsonDocument.Parse(ModerationConfigurationDocumentProjection.Serialize(Normalize(incoming)));
        var changes = ImmutableArray.CreateBuilder<ModerationConfigurationChange>();

        Add("catalogueRevision", null, original.RootElement.GetProperty("catalogueRevision"),
            current.RootElement.GetProperty("catalogueRevision"), proposed.RootElement.GetProperty("catalogueRevision"));
        foreach (var section in new[] { "modes", "supportedProfiles" })
        {
            var before = Set(original.RootElement.GetProperty(section));
            var now = Set(current.RootElement.GetProperty(section));
            var next = Set(proposed.RootElement.GetProperty(section));
            if (!before.SequenceEqual(next) && !now.SequenceEqual(next))
                changes.Add(new(section, null, ModerationConfigurationChangeKind.Changed,
                    !before.SequenceEqual(now)));
        }
        foreach (var section in new[] { "classes", "reasons", "profiles" })
        {
            var referenceMember = section == "profiles" ? "rule" : "reference";
            var before = Entries(original.RootElement.GetProperty(section), referenceMember);
            var now = Entries(current.RootElement.GetProperty(section), referenceMember);
            var next = Entries(proposed.RootElement.GetProperty(section), referenceMember);
            foreach (var reference in before.Keys.Concat(now.Keys).Concat(next.Keys).Distinct()
                .OrderBy(reference => reference.Code, StringComparer.Ordinal).ThenBy(reference => reference.Revision))
            {
                Add(section, reference,
                    before.TryGetValue(reference, out var oldValue) ? oldValue : null,
                    now.TryGetValue(reference, out var currentValue) ? currentValue : null,
                    next.TryGetValue(reference, out var proposedValue) ? proposedValue : null);
            }
        }
        return new(changes.ToImmutable());

        void Add(string section, ModerationConfigurationReference? reference,
            JsonElement? before, JsonElement? now, JsonElement? next)
        {
            if (Same(before, next) || Same(now, next)) return;
            var kind = next is null ? ModerationConfigurationChangeKind.Removed
                : now is null ? ModerationConfigurationChangeKind.Added : ModerationConfigurationChangeKind.Changed;
            changes.Add(new(section, reference, kind, !Same(before, now)));
        }
    }

    private static bool Same(JsonElement? first, JsonElement? second) =>
        first is null ? second is null : second is not null && JsonElement.DeepEquals(first.Value, second.Value);

    private static ModerationConfigurationDocument Normalize(ModerationConfigurationDocument document)
    {
        ModerationConfigurationDocumentValidator.ValidateShape(document);
        return new(document.SchemaVersion, document.CatalogueRevision, document.Modes,
            document.Classes, document.Reasons,
            document.Profiles.Select(profile => profile with
            {
                ScopeKinds = profile.ScopeKinds.Order().ToImmutableArray(),
                SubjectKinds = profile.SubjectKinds.Order().ToImmutableArray(),
                Effects = profile.Effects.OrderBy(effect => effect.Kind)
                    .ThenBy(effect => effect.Capability).ToImmutableArray(),
                Strategies = profile.Strategies.Order().ToImmutableArray()
            }).ToArray(), document.SupportedProfiles);
    }

    private static string[] Set(JsonElement values) =>
        values.EnumerateArray().Select(value => value.GetRawText()).Order(StringComparer.Ordinal).ToArray();

    private static Dictionary<ModerationConfigurationReference, JsonElement> Entries(
        JsonElement values, string referenceMember)
    {
        var entries = new Dictionary<ModerationConfigurationReference, JsonElement>();
        foreach (var value in values.EnumerateArray())
        {
            var reference = value.GetProperty(referenceMember);
            var key = new ModerationConfigurationReference(reference.GetProperty("code").GetString()!,
                reference.GetProperty("revision").GetInt64());
            if (!entries.TryAdd(key, value))
                throw new ConfigurationPortabilityContractException(
                    ConfigurationPortabilityDiagnosticCodes.ContractInvalid, "$");
        }
        return entries;
    }
}
