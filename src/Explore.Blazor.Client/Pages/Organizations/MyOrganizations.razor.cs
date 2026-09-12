using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Organizations;

/// <summary>
/// Displays organizations that the current user is a member of.
/// Allows searching, filtering, and navigation to organization management.
/// </summary>
public partial class MyOrganizations : ComponentBase
{
    [Inject] private IOrganizationService OrganizationService { get; set; } = default!;
    [Inject] private IUserService UserService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private ILogger<MyOrganizations> Logger { get; set; } = default!;

    private ICollection<OrganizationListDto>? _organizations;
    private bool _isLoading = true;
    private string? _errorMessage;
    private string _searchString = string.Empty;

    /// <summary>
    /// Filtered organizations based on search string.
    /// </summary>
    private IEnumerable<OrganizationListDto> FilteredOrganizations =>
        _organizations?
            .Where(x => string.IsNullOrWhiteSpace(_searchString) ||
                        x.FullName?.Contains(_searchString, StringComparison.OrdinalIgnoreCase) == true ||
                        x.Email?.Contains(_searchString, StringComparison.OrdinalIgnoreCase) == true)
        ?? Enumerable.Empty<OrganizationListDto>();

    protected override async Task OnInitializedAsync()
    {
        Logger.LogInformation("[MyOrganizations] OnInitializedAsync starting...");
        await LoadOrganizationsAsync();
    }

    /// <summary>
    /// Loads organizations for the current user.
    /// The API extracts user ID from JWT token, so no client-side user ID extraction needed.
    /// </summary>
    private async Task LoadOrganizationsAsync()
    {
        _isLoading = true;
        _errorMessage = null;

        try
        {
            Logger.LogInformation("[MyOrganizations] Loading organizations...");

            // GetMyOrganizationsAsync calls /api/organization/my which extracts user from JWT
            var orgs = await OrganizationService.GetMyOrganizationsAsync();

            if (orgs == null || orgs.Count == 0)
            {
                Logger.LogInformation("[MyOrganizations] No organizations found, attempting user sync...");
                var syncResult = await UserService.SyncUserAsync();

                if (syncResult?.Success == true)
                {
                    await Task.Delay(200); // Brief delay for sync to complete
                    orgs = await OrganizationService.GetMyOrganizationsAsync();
                }
            }

            _organizations = orgs ?? new List<OrganizationListDto>();
            Logger.LogInformation("[MyOrganizations] Loaded {Count} organizations", _organizations.Count);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[MyOrganizations] Error loading organizations");
            _errorMessage = "Unable to load your organizations. Please try again.";
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// Retry loading organizations after an error.
    /// </summary>
    private async Task RetryLoadAsync()
    {
        _errorMessage = null;
        await LoadOrganizationsAsync();
    }

    /// <summary>
    /// Check if the server advertised event creation for this organization.
    /// </summary>
    private static bool CanCreateEvents(OrganizationListDto org) => org.HasHalLink("create-event");

    /// <summary>
    /// Gets a consistent color based on organization name.
    /// </summary>
    private static Color GetOrganizationColor(string? name)
    {
        if (string.IsNullOrEmpty(name)) return Color.Primary;
        var colors = new[] { Color.Primary, Color.Secondary, Color.Tertiary, Color.Info, Color.Success, Color.Warning };
        return colors[Math.Abs(name.GetHashCode()) % colors.Length];
    }

    /// <summary>
    /// Gets the display color for a user's role in the organization.
    /// </summary>
    private static Color GetRoleColor(int roleId)
    {
        return RoleHelper.GetRoleColor(roleId);
    }

    private static Color GetRoleColor(RoleEnum role)
    {
        return RoleHelper.GetRoleColor(role);
    }

    /// <summary>
    /// Gets the display name for a role ID.
    /// </summary>
    private static string GetRoleName(int roleId)
    {
        return RoleHelper.GetRoleName(roleId);
    }

    private static string GetRoleName(RoleEnum role)
    {
        return RoleHelper.GetRoleName(role);
    }

    /// <summary>
    /// Gets initials from organization name for avatar display.
    /// </summary>
    private static string GetInitials(string? name)
    {
        return DisplayHelper.GetInitials(name, "O");
    }
}
