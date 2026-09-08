using Blazouter.Services;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Events;
using Explore.Blazor.Client.Components.Forms;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Organizations;

public partial class OrganizationDetails
{
    [Inject] protected IOrganizationService OrganizationService { get; set; } = null!;
    [Inject] protected IEventService EventService { get; set; } = null!;
    [Inject] protected NavigationManager NavigationManager { get; set; } = null!;
    [Inject] protected RouterStateService RouterState { get; set; } = null!;
    [Inject] protected ILogger<OrganizationDetails> Logger { get; set; } = null!;

    private Guid Id { get; set; }

    private OrganizationDto? organization;
    private bool isLoading = true;
    private bool isEditMode = false;
    private bool isSaving = false;
    private bool canEdit = false;
    private string? errorMessage;
    private string? successMessage;

    private ICollection<EventListDto> _orgEvents = new List<EventListDto>();
    private EventPreviewWorkspace? _eventPreviewWorkspace;

    private OrganizationProfileEditModel editModel = new();
    private AppearanceSettings _appearance = new();

    private EditContext _editContext = default!;
    private FormSubmitState _submitState = new();
    private ServerValidationErrorStore _errorStore = new();

    private IEnumerable<EventListDto> UpcomingEvents =>
        _orgEvents.Where(e => e.IsPast != true)
                  .OrderBy(e => e.FirstSessionDate ?? DateTimeOffset.MaxValue);

    private IEnumerable<EventListDto> PastEvents =>
        _orgEvents.Where(e => e.IsPast == true)
                  .OrderByDescending(e => e.FirstSessionDate ?? DateTimeOffset.MinValue);

    protected override async Task OnInitializedAsync()
    {
        // Get route parameter from Blazouter
        var idStr = RouterState.GetParam("id");
        if (Guid.TryParse(idStr, out var id))
        {
            Id = id;
        }

        try
        {
            await LoadOrganization();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading organization");
            errorMessage = "Failed to load organization details.";
            isLoading = false;
        }
    }

    private void InitializeEditModel()
    {
        if (organization != null)
        {
            editModel = new OrganizationProfileEditModel
            {
                FullName = organization.FullName,
                Email = organization.Email,
                WebsiteUrl = organization.WebsiteUrl,
                Country = organization.Country,
                City = organization.City,
                Postcode = int.TryParse(organization.Postcode, out var pc) ? pc : 0,
                Address = organization.Address
            };

            _appearance = new AppearanceSettings
            {
                BackgroundColor = organization.ActorBackgroundColor ?? string.Empty,
                ImageUri = organization.ActorBannerPictureUri ?? string.Empty,
                BackgroundEffect = organization.ActorBackgroundEffect ?? "None"
            };

            _editContext = new EditContext(editModel);
            _errorStore.Init(_editContext);
        }
    }

    private void CheckEditPermissions()
    {
        canEdit = organization?.HasHalLink("edit") ?? false;
    }

    private async Task LoadOrganization()
    {
        isLoading = true;
        errorMessage = null;

        try
        {
            Logger.LogDebug("Loading organization {OrganizationId}", Id);
            organization = await OrganizationService.GetOrganizationByIdAsync(Id);

            if (organization != null)
            {
                Logger.LogDebug("Loaded organization: {OrganizationName}", organization.FullName);
                CheckEditPermissions();
                _orgEvents = await EventService.GetPublicEventsByOrganizationAsync(Id);
                InitializeEditModel();
            }
            else
            {
                errorMessage = "Organization not found.";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load organization {OrganizationId}", Id);
            errorMessage = "Organization details could not be loaded. Please try again.";
        }
        finally
        {
            isLoading = false;
        }
    }

    private void ToggleEditMode()
    {
        if (isEditMode && organization != null)
        {
            // Revert changes
            InitializeEditModel();
        }

        isEditMode = !isEditMode;
        _submitState.Reset();
        errorMessage = string.Empty;
        successMessage = string.Empty;
    }

    private async Task SaveChanges()
    {
        if (_submitState.IsSubmitting) return;

        try
        {
            _submitState.Start();
            errorMessage = string.Empty;
            successMessage = string.Empty;

            if (!ValidateOrganizationForm())
            {
                _submitState.Fail("Please fix the validation errors below.");
                return;
            }

            if (organization.ConcurrencyStamp is not { } concurrencyStamp || concurrencyStamp == Guid.Empty)
            {
                _submitState.Fail("Reload the organization before saving changes.");
                return;
            }

            var success = await OrganizationService.UpdateOrganizationAsync(
                Id,
                concurrencyStamp,
                BuildUpdateDto(editModel));

            if (success?.Success == true)
            {
                successMessage = "Organization updated successfully!";
                isEditMode = false;
                await LoadOrganization(); // Reload to show updated data
            }
            else
            {
                _submitState.Fail(success?.Message ?? "Failed to update organization");
            }
        }
        catch (ApiException ex)
        {
            if (!_errorStore.HandleApiError(ex))
            {
                _submitState.Fail("Organization could not be updated. Please try again.");
            }
            else
            {
                _submitState.Fail("Please fix the validation errors below.");
            }
            Logger.LogError(ex, "Error updating organization {OrganizationId}", Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating organization {OrganizationId}", Id);
            _submitState.Fail("Organization could not be updated. Please try again.");
        }
        finally
        {
            if (!_submitState.HasError)
            {
                _submitState.Complete();
            }
        }
    }

    private string? ValidateEmail(string email)
    {
        if (string.IsNullOrEmpty(email))
            return "Email is required";

        if (!IsLikelyEmailAddress(email))
            return "Invalid email format";

        return null;
    }

    private bool ValidateOrganizationForm()
    {
        var errors = GetValidationErrors();
        if (errors.Count == 0)
        {
            _errorStore.ClearErrors();
            return true;
        }

        _errorStore.DisplayErrors(errors);
        return false;
    }

    private Dictionary<string, ICollection<string>> GetValidationErrors()
    {
        var errors = new Dictionary<string, ICollection<string>>();

        AddRequiredError(errors, nameof(OrganizationProfileEditModel.FullName), editModel.FullName, "Organization name is required.");
        AddRequiredError(errors, nameof(OrganizationProfileEditModel.Email), editModel.Email, "Email is required.");
        AddRequiredError(errors, nameof(OrganizationProfileEditModel.Address), editModel.Address, "Address is required.");
        AddRequiredError(errors, nameof(OrganizationProfileEditModel.City), editModel.City, "City is required.");
        AddRequiredError(errors, nameof(OrganizationProfileEditModel.Country), editModel.Country, "Country is required.");

        if (!string.IsNullOrWhiteSpace(editModel.Email) && !IsLikelyEmailAddress(editModel.Email))
        {
            AddError(errors, nameof(OrganizationProfileEditModel.Email), "Enter a valid contact email.");
        }

        if (!string.IsNullOrWhiteSpace(editModel.WebsiteUrl) && !IsHttpUrl(editModel.WebsiteUrl))
        {
            AddError(errors, nameof(OrganizationProfileEditModel.WebsiteUrl), "Website URL must start with http:// or https://.");
        }

        if (editModel.Postcode <= 0)
        {
            AddError(errors, nameof(OrganizationProfileEditModel.Postcode), "Postal code is required.");
        }

        return errors;
    }

    private static void AddRequiredError(
        IDictionary<string, ICollection<string>> errors,
        string fieldName,
        string? value,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, fieldName, message);
        }
    }

    private static void AddError(IDictionary<string, ICollection<string>> errors, string fieldName, string message)
    {
        if (!errors.TryGetValue(fieldName, out var messages))
        {
            messages = new List<string>();
            errors[fieldName] = messages;
        }

        messages.Add(message);
    }

    private static bool IsLikelyEmailAddress(string value)
    {
        var atIndex = value.IndexOf('@', StringComparison.Ordinal);
        return atIndex > 0
            && atIndex < value.Length - 1
            && value.IndexOf('@', atIndex + 1) < 0
            && value[(atIndex + 1)..].Contains('.', StringComparison.Ordinal);
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static UpdateOrganizationDto BuildUpdateDto(OrganizationProfileEditModel model)
    {
        return new UpdateOrganizationDto
        {
            FullName = new UpdateOrganizationFullNameDto { Value = model.FullName },
            Email = new UpdateOrganizationEmailDto { Value = model.Email },
            WebsiteUrl = new UpdateOrganizationWebsiteUrlDto
            {
                Value = new OptionalUpdateOfstring
                {
                    HasValue = true,
                    Value = string.IsNullOrWhiteSpace(model.WebsiteUrl) ? null : model.WebsiteUrl
                }
            },
            Country = new UpdateOrganizationCountryDto { Value = model.Country },
            City = new UpdateOrganizationCityDto { Value = model.City },
            Postcode = new UpdateOrganizationPostcodeDto { Value = model.Postcode },
            Address = new UpdateOrganizationAddressDto { Value = model.Address }
        };
    }

    private sealed class OrganizationProfileEditModel
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? WebsiteUrl { get; set; }
        public string Country { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public int Postcode { get; set; }
        public string Address { get; set; } = string.Empty;
    }

    private Color GetStatusColor(int statusTypeId)
    {
        return statusTypeId switch
        {
            1 => Color.Warning,  // Pending
            2 => Color.Success,  // Approved
            3 => Color.Error,    // Rejected
            _ => Color.Default
        };
    }

    private Task HandleEventSelected(EventListDto evt) =>
        _eventPreviewWorkspace?.SelectEventAsync(evt) ?? Task.CompletedTask;

    private void HandleEventEdit(EventListDto evt)
    {
        _eventPreviewWorkspace?.NavigateToEdit(evt);
    }

    private Task HandleEventDelete(EventListDto evt) =>
        _eventPreviewWorkspace?.OpenDeleteDialogAsync(evt) ?? Task.CompletedTask;

    private Task HandleEventShare(EventListDto evt) =>
        _eventPreviewWorkspace?.ShareEventAsync(evt) ?? Task.CompletedTask;

    private Task HandleEventDeleted(EventListDto evt)
    {
        _orgEvents = _orgEvents.Where(orgEvent => orgEvent.Id != evt.Id).ToList();
        return Task.CompletedTask;
    }
}
