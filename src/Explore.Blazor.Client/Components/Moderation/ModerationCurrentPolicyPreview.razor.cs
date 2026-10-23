using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Components.Moderation;

/// <summary>Read-only retained authoring presentation; selection is declaration, never deployed support.</summary>
public partial class ModerationCurrentPolicyPreview : ComponentBase
{
    [Parameter, EditorRequired] public HalResourceOfModerationCurrentPolicyView? Policy { get; set; }
    [Parameter] public bool ShowAdvanced { get; set; }

    private static bool SameRule(ModerationRuleReference left, ModerationRuleReference right) =>
        left.OwnerId == right.OwnerId && left.Code == right.Code && left.Revision == right.Revision;
}
