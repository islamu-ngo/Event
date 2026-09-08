using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Appearance;
using Explore.Application.Features.Appearance.Requests.Commands;
using Explore.Application.Features.Appearance.Requests.Queries;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/user/appearance")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public class UserAppearanceController : ControllerBase
{
    private static readonly ApiValidationProblemDescriptor UpdatePreferencesValidationProblem = new(
        "appearance",
        "Appearance preference validation failed",
        "Appearance preference update failed.");

    private readonly IMediator _mediator;
    private readonly IAppearanceResolutionService _resolutionService;

    public UserAppearanceController(IMediator mediator, IAppearanceResolutionService resolutionService)
    {
        _mediator = mediator;
        _resolutionService = resolutionService;
    }

    [HttpGet(Name = RouteNames.GetCurrentUserAppearancePreferences)]
    [EndpointSummary("Get Resolved Appearance")]
    [EndpointDescription("Returns the fully resolved appearance state for the authenticated user, including provenance, capabilities, and effective theme data.")]
    [ProducesResponseType(typeof(ResolvedAppearanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ResolvedAppearanceDto>> GetResolvedAppearance(CancellationToken cancellationToken = default)
    {
        var result = await _resolutionService.ResolveForCurrentUserAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPatch(Name = RouteNames.UpdateCurrentUserAppearancePreferences)]
    [EndpointSummary("Update Appearance Preferences")]
    [EndpointDescription("Updates supplied current-user language or text-direction preferences without changing theme mode or active profile.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdatePreferences(
        [FromBody] UpdateUserAppearancePreferencesDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new UpdateCurrentUserAppearancePreferencesCommand
        {
            Preferences = request
        }, cancellationToken);

        return response.IsSuccess
            ? Ok(response)
            : this.ToCommandValidationProblem(response, UpdatePreferencesValidationProblem);
    }

    [HttpGet("presets", Name = RouteNames.GetAvailableThemes)]
    [EndpointSummary("Get Available Presets")]
    [EndpointDescription("Returns available platform and tenant theme presets for the current tenant.")]
    [ProducesResponseType(typeof(IReadOnlyList<AvailablePresetDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<AvailablePresetDto>>> GetAvailablePresets(CancellationToken cancellationToken = default)
    {
        var presets = await _resolutionService.GetAvailablePresetsAsync(cancellationToken);
        return Ok(presets);
    }

    [HttpGet("profiles", Name = RouteNames.GetUserAppearanceProfiles)]
    [EndpointSummary("Get User Appearance Profiles")]
    [EndpointDescription("Returns the current user's appearance profiles for the current tenant scope.")]
    [ProducesResponseType(typeof(IReadOnlyList<UserAppearanceProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<UserAppearanceProfileDto>>> GetUserProfiles(CancellationToken cancellationToken = default)
    {
        var profiles = await _resolutionService.GetUserProfilesAsync(cancellationToken);
        return Ok(profiles);
    }

    [HttpPost("profiles/from-preset/{presetId:guid}", Name = RouteNames.ClonePresetToProfile)]
    [EndpointSummary("Clone Preset Into User Profile")]
    [EndpointDescription("Clones a theme preset into a user-owned appearance profile. If an existing clone exists, returns it instead.")]
    [ProducesResponseType(typeof(UserAppearanceProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserAppearanceProfileDto>> ClonePreset(
        Guid presetId,
        [FromBody] ClonePresetRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        var profile = await _resolutionService.ClonePresetAsync(presetId, request?.Name, activate: false, cancellationToken);
        return Ok(profile);
    }

    [HttpPost("profiles", Name = RouteNames.CreateCustomAppearanceProfile)]
    [EndpointSummary("Create Custom Appearance Profile")]
    [EndpointDescription("Creates a fully custom user appearance profile from natural + brand color inputs.")]
    [ProducesResponseType(typeof(UserAppearanceProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserAppearanceProfileDto>> CreateCustomProfile(
        [FromBody] CreateCustomProfileRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var profile = await _resolutionService.CreateCustomProfileAsync(request, cancellationToken);
        return Ok(profile);
    }

    [HttpPatch("profiles/{profileId:guid}", Name = RouteNames.UpdateAppearanceProfile)]
    [EndpointSummary("Update User Appearance Profile")]
    [EndpointDescription("Updates supplied metadata or palette groups on a user-owned appearance profile.")]
    [ProducesResponseType(typeof(UserAppearanceProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserAppearanceProfileDto>> UpdateProfile(
        Guid profileId,
        [FromBody] UpdateAppearanceProfileRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var profile = await _resolutionService.UpdateProfileAsync(profileId, request, cancellationToken);
        return Ok(profile);
    }

    [HttpPut("active-profile", Name = RouteNames.SetActiveAppearanceProfile)]
    [EndpointSummary("Set Active Appearance Profile")]
    [EndpointDescription("Sets the active appearance profile for the current user/scope.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActiveProfile(
        [FromBody] SetActiveProfileRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await _resolutionService.SetActiveProfileAsync(request.ProfileId, cancellationToken);
        return Ok();
    }

    [HttpPut("mode", Name = RouteNames.SetAppearanceThemeMode)]
    [EndpointSummary("Set Theme Mode")]
    [EndpointDescription("Sets the theme mode (light/dark/system/lighthighcontrast/darkhighcontrast/custom) without changing the active profile.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SetThemeMode(
        [FromBody] SetThemeModeRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await _resolutionService.SetThemeModeAsync(request.ThemeMode, cancellationToken);
        return Ok();
    }

    [HttpGet("generate-palette", Name = RouteNames.GenerateAppearancePalette)]
    [EndpointSummary("Generate Palette From Colors")]
    [EndpointDescription("Generates a complete 18-token palette from natural and brand color inputs.")]
    [ProducesResponseType(typeof(UiThemePaletteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public ActionResult<UiThemePaletteDto> GeneratePalette(
        [FromQuery] string naturalColor,
        [FromQuery] string brandColor,
        [FromQuery] bool isDark = false)
    {
        var palette = _resolutionService.GeneratePalette(naturalColor, brandColor, isDark);
        return Ok(palette);
    }

    [HttpPut("profiles/{profileId:guid}/archive", Name = RouteNames.ArchiveAppearanceProfile)]
    [EndpointSummary("Archive User Appearance Profile")]
    [EndpointDescription("Archives a user-owned profile, hiding it from the quick switcher without deletion.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveProfile(Guid profileId, CancellationToken cancellationToken = default)
    {
        await _resolutionService.ArchiveProfileAsync(profileId, cancellationToken);
        return Ok();
    }

    [HttpPost("profiles/{profileId:guid}/duplicate", Name = RouteNames.DuplicateAppearanceProfile)]
    [EndpointSummary("Duplicate User Appearance Profile")]
    [EndpointDescription("Duplicates a user-owned profile with an optional name override.")]
    [ProducesResponseType(typeof(UserAppearanceProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserAppearanceProfileDto>> DuplicateProfile(
        Guid profileId,
        [FromBody] ClonePresetRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        var profile = await _resolutionService.DuplicateProfileAsync(profileId, request?.Name, cancellationToken);
        return Ok(profile);
    }
}
