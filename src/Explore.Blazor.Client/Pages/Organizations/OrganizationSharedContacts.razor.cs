using Blazouter.Services;
using Explore.Blazor.Client.Contracts.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Organizations;

public partial class OrganizationSharedContacts
{
    [Inject] protected IContactShareConsentService ConsentService { get; set; } = null!;
    [Inject] protected RouterStateService RouterState { get; set; } = null!;
    [Inject] protected ISnackbar Snackbar { get; set; } = null!;
    [Inject] protected IBrowserActionInterop BrowserActionInterop { get; set; } = null!;

    private Guid _organizationActorId;
    private List<SharedContactViewModel> _contacts = [];
    private bool _isLoading = true;
    private bool _isLoadingContacts = false;
    private bool _isExporting = false;
    private string? _errorMessage;
    private string _searchEmail = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        var idStr = RouterState.GetParam("id");
        if (!Guid.TryParse(idStr, out var id))
        {
            _errorMessage = "Invalid organization identifier.";
            _isLoading = false;
            return;
        }

        _organizationActorId = id;
        await LoadContactsAsync();
        _isLoading = false;
    }

    private async Task LoadContactsAsync()
    {
        _isLoadingContacts = true;
        try
        {
            _contacts = await ConsentService.GetOrganizationSharedContactsAsync(
                _organizationActorId,
                searchEmail: string.IsNullOrWhiteSpace(_searchEmail) ? null : _searchEmail);
        }
        catch (Exception ex)
        {
            _errorMessage = "Failed to load shared contacts. You may not have permission to view this page.";
        }
        finally
        {
            _isLoadingContacts = false;
        }
    }

    private async Task OnSearchChanged()
    {
        await LoadContactsAsync();
    }

    private async Task ExportAsync(string format)
    {
        _isExporting = true;
        try
        {
            var result = await ConsentService.ExportSharedContactsAsync(_organizationActorId, format);
            if (result.HasValue)
            {
                var (fileBytes, fileName) = result.Value;
                var downloaded = await DownloadFileAsync(
                    fileBytes,
                    fileName,
                    format == "csv" ? "text/csv" : "text/tab-separated-values");

                Snackbar.Add(
                    downloaded ? "Export downloaded." : "Export failed. Please try again.",
                    downloaded ? Severity.Success : Severity.Error);
            }
            else
            {
                Snackbar.Add("Export failed. Please try again.", Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add("An error occurred during export.", Severity.Error);
        }
        finally
        {
            _isExporting = false;
        }
    }

    private async Task<bool> DownloadFileAsync(byte[] fileBytes, string fileName, string contentType)
    {
        var base64 = Convert.ToBase64String(fileBytes);
        return await BrowserActionInterop.DownloadBase64FileAsync(base64, fileName, contentType);
    }

    private static string FormatPurpose(string purposeCode) => purposeCode switch
    {
        "ORGANIZER_FUTURE_COMMUNICATIONS" => "Future events & updates",
        _ => purposeCode
    };
}
