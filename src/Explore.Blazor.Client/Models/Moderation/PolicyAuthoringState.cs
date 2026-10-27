using System.Text.Json;
using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Models.Moderation;

/// <summary>
/// Mutable local editing custody of a complete detached wire graph. Historical definitions are
/// append-only; installed inventory alignment and support declarations are deliberate edits.
/// This state neither compiles policy nor owns a native grant.
/// </summary>
public sealed class PolicyAuthoringState
{
    private static readonly JsonSerializerOptions Json = EventApiJsonSerializerSettings.Configure(new());

    public PolicyAuthoringState(HalResourceOfModerationCurrentPolicyView current)
    {
        var history = current.Authoring;
        Proposed = new()
        {
            PolicyId = current.PolicyId,
            ExpectedRevision = current.ExpectedRevision,
            PublicationId = Guid.CreateVersion7(),
            Jurisdiction = Copy(current.Jurisdiction),
            CatalogueRevision = history?.CatalogueRevision ?? current.InstalledCatalogueRevision,
            InventoryRevision = history?.InventoryRevision ?? current.InstalledInventoryRevision,
            InventoryFingerprint = history?.InventoryFingerprint ?? current.InstalledInventoryFingerprint,
            RequestedState = ModerationPolicyRequestedState.Draft,
            Modes = history is null ? [] : history.Modes.ToList(),
            Classes = history is null ? [] : history.Classes.Select(Copy).ToList(),
            Reasons = history is null ? [] : history.Reasons.Select(Copy).ToList(),
            Profiles = history is null ? [] : history.Profiles.Select(item => Copy(item.Definition)).ToList(),
            SupportedProfiles = history is null ? [] : history.SelectedProfiles.Select(Copy).ToList()
        };
    }

    public ModerationPolicyProposedState Proposed { get; }
    public bool Changed { get; private set; }
    public void MarkChanged() => Changed = true;

    public static T Copy<T>(T value) where T : class =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)
        ?? throw new JsonException("Incomplete policy authoring graph.");

    public static bool Same(ModerationRuleReference left, ModerationRuleReference right) =>
        left.OwnerId == right.OwnerId && left.Code == right.Code && left.Revision == right.Revision;

    public static string Key(ModerationRuleReference reference) =>
        $"{reference.OwnerId:D}/{reference.Code}/{reference.Revision}";

    public long NextRevision(string code, IEnumerable<ModerationRuleReference> definitions) =>
        checked(definitions.Where(item => item.OwnerId == Proposed.Jurisdiction.Id && item.Code == code)
            .Select(item => item.Revision).DefaultIfEmpty(0).Max() + 1);

    public ModerationRuleReference Reference(string code, long revision) =>
        new() { OwnerId = Proposed.Jurisdiction.Id, Code = code, Revision = revision };

    public bool Selected(ModerationRuleReference rule) => Proposed.SupportedProfiles.Any(item => Same(item, rule));

    public void Select(ModerationRuleReference rule, bool selected)
    {
        Proposed.SupportedProfiles = Proposed.SupportedProfiles.Where(item => !Same(item, rule)).ToList();
        if (selected) Proposed.SupportedProfiles.Add(Copy(rule));
        Changed = true;
    }

    public void AlignInventory(HalResourceOfModerationCurrentPolicyView current)
    {
        Proposed.CatalogueRevision = current.InstalledCatalogueRevision;
        Proposed.InventoryRevision = current.InstalledInventoryRevision;
        Proposed.InventoryFingerprint = current.InstalledInventoryFingerprint;
        Changed = true;
    }

    public override string ToString() => nameof(PolicyAuthoringState);
}
