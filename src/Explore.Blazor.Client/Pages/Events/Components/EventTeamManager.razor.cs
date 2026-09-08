using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Pages.Events.Dialogs;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Events.Components;

public partial class EventTeamManager
{
    [Parameter] public Guid EventId { get; set; }

    [Inject] private IEventTeamService EventTeamService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private IAccessibilityFocusService AccessibilityFocusService { get; set; } = default!;
    [Inject] private ILogger<EventTeamManager> Logger { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<EventTeamMemberDto> _members = [];
    private List<EventRolePresetDto> _assignablePresets = [];
    private HalCollectionResourceOfEventTeamMemberDto _team = new();
    private bool _loading = true;
    private string? _errorMessage;

    private string _searchString = "";
    private int? _roleFilter;

    private IEnumerable<EventTeamMemberDto> FilteredMembers =>
        _members
            .Where(x => string.IsNullOrWhiteSpace(_searchString) ||
                        (x.UserFullName?.Contains(_searchString, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (x.UserEmail?.Contains(_searchString, StringComparison.OrdinalIgnoreCase) ?? false))
            .Where(x => _roleFilter == null || x.RoleId == _roleFilter);

    private bool HasAssignEventRoleLink => _team._links?.ContainsKey("assign-event-role") == true;

    private bool CanAssignTeamRole => HasAssignEventRoleLink && _assignablePresets.Count > 0;

    private bool HasAnyMemberActions => _members.Any(m => CanRevokeMember(m));

    protected override async Task OnParametersSetAsync()
    {
        await LoadTeamMembers();
        await LoadAssignablePresets();
    }

    private bool CanRevokeMember(EventTeamMemberDto member)
    {
        return member.HasHalLink("revoke");
    }

    private async Task LoadTeamMembers()
    {
        _loading = true;
        _errorMessage = null;
        try
        {
            _team = await EventTeamService.GetTeamMembersAsync(EventId, includeInactive: false);
            _members = _team.GetItems().ToList();
        }
        catch (Exception ex)
        {
            _errorMessage = $"Error loading team: {ex.Message}";
            Logger.LogError(ex, "Error loading team for event {EventId}", EventId);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task LoadAssignablePresets()
    {
        _assignablePresets = [];

        if (!HasAssignEventRoleLink)
        {
            return;
        }

        try
        {
            var presets = await EventTeamService.GetAssignablePresetsAsync(EventId);
            _assignablePresets = presets.ToList();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error loading assignable event team roles for event {EventId}", EventId);
        }
    }

    private async Task OpenAssignDialog()
    {
        if (!CanAssignTeamRole) return;

        var parameters = new DialogParameters
        {
            ["EventId"] = EventId,
            ["Presets"] = _assignablePresets
        };

        await AccessibilityFocusService.SaveFocusAsync();
        var dialog = await DialogService.ShowAsync<AssignEventTeamRoleDialog>(
            "Assign Team Role",
            parameters,
            DialogOptionsFactory.Medium());
        var result = await dialog.Result;
        await AccessibilityFocusService.RestoreFocusAsync();

        if (!result.Canceled)
        {
            await LoadTeamMembers();
        }
    }

    private async Task RevokeMember(EventTeamMemberDto member)
    {
        if (!CanRevokeMember(member)) return;

        await AccessibilityFocusService.SaveFocusAsync();
        bool? confirmed = await DialogService.ShowMessageBoxAsync(
            "Revoke Role",
            $"Remove {member.UserFullName ?? member.UserEmail} from the event team?",
            yesText: "Revoke",
            cancelText: "Cancel");
        await AccessibilityFocusService.RestoreFocusAsync();

        if (confirmed != true) return;

        try
        {
            if (member.AssignmentId.HasValue)
            {
                var response = await EventTeamService.RevokeAssignmentAsync(EventId, member.AssignmentId.Value);
                if (response?.Success == true)
                {
                    Snackbar.Add("Role revoked successfully", Severity.Success);
                    await LoadTeamMembers();
                }
                else
                {
                    _errorMessage = response?.Message ?? "Error revoking role.";
                }
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"Error revoking role: {ex.Message}";
            Logger.LogError(ex, "Error revoking role for assignment {AssignmentId}", member.AssignmentId);
        }
    }
}
