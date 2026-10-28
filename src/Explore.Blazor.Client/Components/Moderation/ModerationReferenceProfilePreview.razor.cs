using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Components.Moderation;

/// <summary>Read-only reference presentation; display mode cannot alter policy or grant affordances.</summary>
public partial class ModerationReferenceProfilePreview : ComponentBase
{
    [Parameter, EditorRequired] public ModerationReferenceProfile? Profile { get; set; }
    [Parameter] public bool ShowAdvanced { get; set; }

    private static string Label(Enum? value) => ModerationValueLabel.Format(value);
}
