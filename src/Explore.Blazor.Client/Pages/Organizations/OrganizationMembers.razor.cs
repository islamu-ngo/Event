using Blazouter.Services;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Pages.Organizations.Dialogs;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Organizations;

public partial class OrganizationMembers
{
    [Inject] protected IOrganizationMemberService MemberService { get; set; } = null!;
    [Inject] protected IOrganizationService OrganizationService { get; set; } = null!;
    [Inject] protected IDialogService DialogService { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;
    [Inject] protected RouterStateService RouterState { get; set; } = null!;
    [Inject] private IAccessibilityFocusService AccessibilityFocusService { get; set; } = default!;

    private Guid Id { get; set; }
    private List<OrganizationMemberDto> Members = new();
    private bool _loading = true;
    private bool _canCreateMembers;
    private string? _errorMessage;

    private string _searchString = "";
    private int? _roleFilter;

    private IEnumerable<OrganizationMemberDto> FilteredMembers =>
        Members
            .Where(x => string.IsNullOrWhiteSpace(_searchString) ||
                        (x.UserFullName?.Contains(_searchString, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (x.UserEmail?.Contains(_searchString, StringComparison.OrdinalIgnoreCase) ?? false))
            .Where(x => _roleFilter == null || x.RoleId == _roleFilter);

    private bool HasAnyMemberActions => Members.Any(member => CanEditMember(member) || CanDeleteMember(member));

    protected override async Task OnInitializedAsync()
    {
        var idStr = RouterState.GetParam("id");
        if (Guid.TryParse(idStr, out var id))
        {
            Id = id;
        }

        await LoadMembers();
    }

    private static bool CanEditMember(OrganizationMemberDto member) =>
        member.HasHalLink("edit");

    private static bool CanDeleteMember(OrganizationMemberDto member) =>
        member.HasHalLink("delete");

    private async Task LoadMembers()
    {
        _loading = true;
        _errorMessage = null;
        try
        {
            var result = await MemberService.GetMembersWithAffordancesAsync(Id);
            Members = result.Members.ToList();
            _canCreateMembers = result.CanCreate;
        }
        catch (Exception ex)
        {
            _errorMessage = $"Error loading members: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }

    private Color GetRoleColor(int role)
    {
        return RoleHelper.GetRoleColor(role);
    }

    private string GetRoleName(int role)
    {
        return RoleHelper.GetRoleName(role);
    }

    private async Task OpenInviteDialog()
    {
        if (!_canCreateMembers)
        {
            return;
        }

        var parameters = new DialogParameters { ["OrganizationId"] = Id };
        await AccessibilityFocusService.SaveFocusAsync();
        var dialog = await InviteMemberDialog.ShowAsync(DialogService, "Invite Member", parameters);
        var result = await dialog.Result;
        await AccessibilityFocusService.RestoreFocusAsync();

        if (!result.Canceled)
        {
            await LoadMembers();
        }
    }

    private async Task OpenEditRoleDialog(OrganizationMemberDto member)
    {
        if (!CanEditMember(member))
        {
            return;
        }

        var parameters = new DialogParameters { ["Member"] = member, ["OrganizationId"] = Id };
        await AccessibilityFocusService.SaveFocusAsync();
        var dialog = await EditMemberRoleDialog.ShowAsync(DialogService, "Edit Role", parameters);
        var result = await dialog.Result;
        await AccessibilityFocusService.RestoreFocusAsync();

        if (!result.Canceled)
        {
            await LoadMembers();
        }
    }

    private async Task RemoveMember(OrganizationMemberDto member)
    {
        if (!CanDeleteMember(member))
        {
            return;
        }

        await AccessibilityFocusService.SaveFocusAsync();
        bool? result = await DialogService.ShowMessageBoxAsync(
            "Remove Member",
            $"Are you sure you want to remove {member.UserEmail} from the organization?",
            yesText: "Remove", cancelText: "Cancel");
        await AccessibilityFocusService.RestoreFocusAsync();

        if (result == true)
        {
            try
            {
                if (!member.Id.HasValue)
                {
                    _errorMessage = "Member ID is missing";
                    return;
                }
                await MemberService.DeleteMemberAsync(member.Id.Value);
                await LoadMembers();
            }
            catch (Exception ex)
            {
                _errorMessage = $"Error removing member: {ex.Message}";
            }
        }
    }
}
