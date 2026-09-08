using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Components.Shell.Workspaces;

public partial class EventsWorkspaceNavigation : ComponentBase, IDisposable, IWorkspaceNavigationProvider
{
    [Inject] protected IPublicExperienceService PublicExperienceService { get; set; } = null!;
    [Inject] protected TenantNavLinksState TenantNavLinksState { get; set; } = null!;

    [Parameter] public string AriaLabel { get; set; } = "Events workspace navigation";
    [Parameter] public string DataTestId { get; set; } = "events-workspace-navigation";

    [Parameter]
    public string? BrandDisplayName
    {
        get => _parameterBrandDisplayName;
        set => _parameterBrandDisplayName = value;
    }

    [Parameter]
    public string? BrandLogoUrl
    {
        get => _parameterBrandLogoUrl;
        set => _parameterBrandLogoUrl = value;
    }

    [Parameter]
    public bool? ShowCommunityGuidelinesLink
    {
        get => _parameterShowCommunityGuidelinesLink;
        set => _parameterShowCommunityGuidelinesLink = value;
    }

    [Parameter]
    public IReadOnlyList<TenantNavigationLinkDto>? TenantLinks
    {
        get => _parameterTenantLinks;
        set => _parameterTenantLinks = value;
    }

    private string? _brandDisplayName;
    private string? _brandLogoUrl;
    private bool _showCommunityGuidelinesLink = true;
    private PublicExperienceShellDto? _shell;
    private string? _parameterBrandDisplayName;
    private string? _parameterBrandLogoUrl;
    private bool? _parameterShowCommunityGuidelinesLink;
    private IReadOnlyList<TenantNavigationLinkDto>? _parameterTenantLinks;

    private string? ResolvedBrandDisplayName => _parameterBrandDisplayName ?? _brandDisplayName;
    private string? ResolvedBrandLogoUrl => _parameterBrandLogoUrl ?? _brandLogoUrl;
    private bool ResolvedShowCommunityGuidelinesLink => _parameterShowCommunityGuidelinesLink ?? _showCommunityGuidelinesLink;
    private IReadOnlyList<TenantNavigationLinkDto> ResolvedTenantLinks => _parameterTenantLinks
        ?? BuildTenantLinks(_shell?.Navigation?.Links)
        ?? TenantNavLinksState.Links;
    private string ResolvedEventCatalogLabel => string.IsNullOrWhiteSpace(_shell?.EventCatalog?.Label)
        ? "Events"
        : _shell.EventCatalog.Label;
    private string ResolvedEventCatalogUrl => string.IsNullOrWhiteSpace(_shell?.EventCatalog?.Url)
        ? "/events"
        : _shell.EventCatalog.Url;
    private bool IsOrganizationCentric => _shell?.Mode == PublicExperienceMode.OrganizationCentric;

    protected override async Task OnInitializedAsync()
    {
        TenantNavLinksState.OnChange += StateHasChanged;

        try
        {
            var shellTask = PublicExperienceService.GetCachedShellAsync();
            _shell = shellTask is null ? null : await shellTask;
            if (!string.IsNullOrWhiteSpace(_shell?.Home?.BrandDisplayName))
            {
                _brandDisplayName = _shell.Home.BrandDisplayName;
            }

            if (!string.IsNullOrWhiteSpace(_shell?.Home?.BrandLogoUrl))
            {
                _brandLogoUrl = _shell.Home.BrandLogoUrl;
            }

            var settingsTask = PublicExperienceService.GetCachedSettingsAsync();
            var settings = settingsTask is null ? null : await settingsTask;
            if (settings is null)
            {
                return;
            }

            _showCommunityGuidelinesLink = settings.AllowUserSubmittedEvents == true
                || settings.AllowOrganizationSubmittedEvents == true
                || settings.AllowGroupSubmittedEvents == true;
            _brandDisplayName = string.IsNullOrWhiteSpace(_brandDisplayName)
                ? settings.BrandDisplayName
                : _brandDisplayName;
            _brandLogoUrl = string.IsNullOrWhiteSpace(_brandLogoUrl)
                ? settings.BrandLogoUrl
                : _brandLogoUrl;
        }
        catch
        {
            // Fallback to defaults
        }
    }

    private static IReadOnlyList<TenantNavigationLinkDto>? BuildTenantLinks(IEnumerable<PublicExperienceNavigationLinkDto>? links)
    {
        if (links is null)
        {
            return null;
        }

        var result = links
            .Where(link => !string.IsNullOrWhiteSpace(link.Label) && !string.IsNullOrWhiteSpace(link.Url))
            .OrderBy(link => link.SortOrder)
            .ThenBy(link => link.Label, StringComparer.OrdinalIgnoreCase)
            .Select(link => new TenantNavigationLinkDto
            {
                Id = Guid.CreateVersion7(),
                Label = link.Label!,
                Url = link.Url!,
                Order = link.SortOrder ?? 0,
                OpenInNewTab = false
            })
            .ToList();

        return result.Count == 0 ? null : result;
    }

    public void Dispose()
    {
        TenantNavLinksState.OnChange -= StateHasChanged;
    }
}
