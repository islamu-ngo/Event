using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Forms;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Organizations;

public partial class CreateOrganization
{
    private const string GenericSubmitFailureMessage = "Organization could not be submitted. Please try again.";

    [Inject] protected NavigationManager NavigationManager { get; set; } = null!;
    [Inject] protected IOrganizationService OrganizationService { get; set; } = null!;
    [Inject] protected IImageStorageService ImageStorageService { get; set; } = null!;
    [Inject] protected ILogger<CreateOrganization> Logger { get; set; } = null!;
    [Inject] protected ISnackbar Snackbar { get; set; } = null!;

    private CreateOrganizationDto organization = new();
    private bool acceptTerms = false;
    private bool confirmInformation = false;
    private string logoPreview = string.Empty;

    private ImageUpload? _imageUpload;
    private FileUploadData? _selectedLogoData;
    private bool _isUploadingLogo = false;
    private Guid? _uploadedLogoStorageObjectId = null;
    private string? _logoUploadError;
    private AppearanceSettings _appearance = new();

    private bool submitSuccess = false;
    private EditContext _editContext = default!;
    private FormSubmitState _submitState = new();
    private ServerValidationErrorStore _errorStore = new();

    protected override async Task OnInitializedAsync()
    {
        organization = CreateEmptyOrganization();
        _editContext = new EditContext(organization);
        _errorStore.Init(_editContext);
        await base.OnInitializedAsync();
    }

    private async Task HandleSubmit()
    {
        if (_submitState.IsSubmitting)
        {
            return;
        }

        if (!ValidateOrganizationForm(displaySubmitError: true))
        {
            return;
        }

        _submitState.Start();

        try
        {
            var createdOrganization = await OrganizationService.CreateOrganizationAsync(organization);

            if (createdOrganization != null)
            {
                _submitState.Complete();
                submitSuccess = true;
                Logger.LogInformation("Organization successfully created with ID: {OrganizationId}", createdOrganization.Id);
                await Task.Delay(1000);
                NavigationManager.NavigateTo("/organization/success");
            }
            else
            {
                _submitState.Fail(GenericSubmitFailureMessage);
            }
        }
        catch (ApiException ex)
        {
            if (_errorStore.HandleApiError(ex))
            {
                _submitState.Fail("Please fix the validation errors below.");
            }
            else
            {
                Logger.LogError(ex, "API error during organization creation. StatusCode={StatusCode}", ex.StatusCode);
                _submitState.Fail(GenericSubmitFailureMessage);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception during organization creation");
            _submitState.Fail(GenericSubmitFailureMessage);
        }
    }

    private async Task OnLogoFileDataSelected(FileUploadData? fileData)
    {
        _selectedLogoData = fileData;
        _logoUploadError = null;

        if (fileData == null)
        {
            _uploadedLogoStorageObjectId = null;
            organization.ProfilePictureId = null;
            return;
        }

        _isUploadingLogo = true;
        // Required: non-UI thread callback
        await InvokeAsync(StateHasChanged);

        try
        {
            var uploadResult = await ImageStorageService.UploadAndCreateRecordFromBytesAsync(fileData);

            if (uploadResult?.Success == true)
            {
                _uploadedLogoStorageObjectId = uploadResult.StorageObjectId;
                organization.ProfilePictureId = uploadResult.StorageObjectId;
                _logoUploadError = null;
            }
            else
            {
                _logoUploadError = ImageUploadClientPolicy.ToUserSafeUploadError(uploadResult?.ErrorMessage);
                await ClearLogoUploadState();
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                "Exception during logo upload. FailureType={FailureType}",
                ImageUploadClientPolicy.GetFailureType(ex));
            _logoUploadError = ImageUploadClientPolicy.GenericUploadFailureMessage;
            await ClearLogoUploadState();
        }
        finally
        {
            _isUploadingLogo = false;
            // Required: non-UI thread callback
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ClearLogoUploadState()
    {
        try
        {
            if (_imageUpload != null)
            {
                await _imageUpload.RemoveImage();
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Unable to clear organization logo upload UI state.");
        }

        _selectedLogoData = null;
        _uploadedLogoStorageObjectId = null;
        organization.ProfilePictureId = null;
    }

    private void OnLogoPreviewUrlChanged(string? value)
    {
        logoPreview = value ?? string.Empty;
    }

    private Task OnPreviewStepInteraction(StepperInteractionEventArgs args)
    {
        if (args.Action != StepAction.Complete)
            return Task.CompletedTask;

        var errors = GetValidationErrorsForStep(args.StepIndex);

        if (errors.Count > 0)
        {
            args.Cancel = true;
            _errorStore.DisplayErrors(errors);
            Snackbar.Add(string.Join(" ", errors.SelectMany(error => error.Value)), Severity.Warning);
        }

        return Task.CompletedTask;
    }

    private bool CanSubmit()
    {
        return acceptTerms &&
               confirmInformation &&
               !string.IsNullOrWhiteSpace(organization.FullName) &&
               !string.IsNullOrWhiteSpace(organization.Email) &&
               !string.IsNullOrWhiteSpace(organization.Address) &&
               !string.IsNullOrWhiteSpace(organization.City) &&
               !string.IsNullOrWhiteSpace(organization.Country) &&
               organization.Postcode > 0 &&
               !_isUploadingLogo;
    }

    private bool ValidateOrganizationForm(bool displaySubmitError)
    {
        var errors = GetValidationErrors();
        if (errors.Count == 0)
        {
            _errorStore.ClearErrors();
            _submitState.Reset();
            return true;
        }

        _errorStore.DisplayErrors(errors);
        if (displaySubmitError)
        {
            _submitState.Fail("Please fix the validation errors below.");
        }

        return false;
    }

    private Dictionary<string, ICollection<string>> GetValidationErrorsForStep(int stepIndex)
    {
        var allErrors = GetValidationErrors();

        return stepIndex switch
        {
            0 => allErrors
                .Where(error => error.Key is nameof(CreateOrganizationDto.FullName) or nameof(CreateOrganizationDto.WebsiteUrl))
                .ToDictionary(error => error.Key, error => error.Value),
            1 => allErrors
                .Where(error => error.Key is nameof(CreateOrganizationDto.Email)
                    or nameof(CreateOrganizationDto.Address)
                    or nameof(CreateOrganizationDto.Postcode)
                    or nameof(CreateOrganizationDto.City)
                    or nameof(CreateOrganizationDto.Country))
                .ToDictionary(error => error.Key, error => error.Value),
            _ => new Dictionary<string, ICollection<string>>()
        };
    }

    private Dictionary<string, ICollection<string>> GetValidationErrors()
    {
        var errors = new Dictionary<string, ICollection<string>>();

        AddRequiredError(errors, nameof(CreateOrganizationDto.FullName), organization.FullName, "Organization name is required.");
        AddRequiredError(errors, nameof(CreateOrganizationDto.Email), organization.Email, "Contact email is required.");
        AddRequiredError(errors, nameof(CreateOrganizationDto.Address), organization.Address, "Street address is required.");
        AddRequiredError(errors, nameof(CreateOrganizationDto.City), organization.City, "City is required.");
        AddRequiredError(errors, nameof(CreateOrganizationDto.Country), organization.Country, "Country is required.");

        if (!string.IsNullOrWhiteSpace(organization.Email) && !IsLikelyEmailAddress(organization.Email))
        {
            AddError(errors, nameof(CreateOrganizationDto.Email), "Enter a valid contact email.");
        }

        if (!string.IsNullOrWhiteSpace(organization.WebsiteUrl) && !IsHttpUrl(organization.WebsiteUrl))
        {
            AddError(errors, nameof(CreateOrganizationDto.WebsiteUrl), "Website URL must start with http:// or https://.");
        }

        if (organization.Postcode is null or <= 0)
        {
            AddError(errors, nameof(CreateOrganizationDto.Postcode), "Postal code is required.");
        }

        if (!acceptTerms)
        {
            AddError(errors, string.Empty, "Accept the terms and conditions.");
        }

        if (!confirmInformation)
        {
            AddError(errors, string.Empty, "Confirm that the information is accurate.");
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

    private static CreateOrganizationDto CreateEmptyOrganization() =>
        new()
        {
            FullName = string.Empty,
            Email = string.Empty,
            Address = string.Empty,
            City = string.Empty,
            Country = string.Empty
        };
}
