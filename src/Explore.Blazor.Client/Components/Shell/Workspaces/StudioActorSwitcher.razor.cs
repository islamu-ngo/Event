using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Services.Shell;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Components.Shell.Workspaces;

public partial class StudioActorSwitcher
{
    [Inject]
    private IUiShellContextService ShellContextService { get; set; } = null!;

    [Inject]
    private UiShellState ShellState { get; set; } = null!;

    private IReadOnlyList<ManagedActorDto> _actors = [];
    private Guid? _pinnedActorId;
    private bool _isLoading = true;

    private bool HasAuthorizedPinnedActor =>
        _pinnedActorId.HasValue && _actors.Any(actor => actor.ActorId == _pinnedActorId);

    private bool CanSwitchActor => _actors.Count > 1 && !HasAuthorizedPinnedActor;

    private string ActiveActorName =>
        ShellState.ActiveActor?.DisplayName
        ?? ShellState.ActiveActor?.ActorType
        ?? "Studio actor";

    private string ActiveActorIcon =>
        string.Equals(ShellState.ActiveActor?.ActorType, "Group", StringComparison.OrdinalIgnoreCase)
            ? Icons.Material.Filled.Groups
            : Icons.Material.Filled.Business;

    protected override async Task OnInitializedAsync()
    {
        var context = await ShellContextService.GetCachedContextAsync();
        _actors = context?.ManagedActors?
            .Where(actor => actor.ActorId is { } actorId && actorId != Guid.Empty)
            .DistinctBy(actor => actor.ActorId)
            .ToList()
            ?? [];
        _pinnedActorId = context?.PinnedActorId;
        ShellState.ReconcileActiveActors(_actors, _pinnedActorId);
        _isLoading = false;
    }

    private void OnActorChanged(ChangeEventArgs args)
    {
        if (Guid.TryParse(args.Value?.ToString(), out var actorId))
        {
            ShellState.TrySetActiveActor(actorId, _actors);
        }
    }

    private static string FormatActorOption(ManagedActorDto actor)
    {
        var name = actor.DisplayName ?? actor.ActorType ?? "Studio actor";
        return string.IsNullOrWhiteSpace(actor.ActorType) ? name : $"{actor.ActorType} · {name}";
    }
}
