using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.DTOs.Appearance;
using Explore.Application.Features.Appearance.Requests.Commands;
using Explore.Application.Features.Appearance.Requests.Queries;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/admin/ui-themes")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public class UiThemeAdminController(IMediator mediator) : ControllerBase
{
    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "uiTheme",
        "UI theme validation failed",
        "UI theme creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "uiTheme",
        "UI theme validation failed",
        "UI theme update failed.");

    private static readonly ApiNotFoundProblemDescriptor UiThemeNotFoundProblem = new(
        "UI theme not found",
        "UI theme not found.");

    [HttpGet(Name = RouteNames.GetUiThemeCatalog)]
    [EndpointSummary("Get UI Theme Catalog")]
    [EndpointDescription("Returns the platform or tenant-owned UI theme catalog. Scope is controlled by the isPlatformCatalog query parameter.")]
    [ProducesResponseType(typeof(IReadOnlyList<UiThemeListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<UiThemeListItemDto>>> GetCatalog(
        [FromQuery] bool isPlatformCatalog = false,
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var themes = await mediator.Send(new GetUiThemeCatalogQuery
        {
            IsPlatformCatalog = isPlatformCatalog,
            ActiveOnly = activeOnly,
        }, cancellationToken);

        return Ok(themes);
    }

    [HttpGet("{id:guid}", Name = RouteNames.GetUiThemeDetails)]
    [EndpointSummary("Get UI Theme Details")]
    [EndpointDescription("Returns full UI theme details including light/dark palettes for administrative editing.")]
    [ProducesResponseType(typeof(UiThemeDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UiThemeDetailsDto>> GetDetails(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var theme = await mediator.Send(new GetUiThemeDetailsQuery { Id = id }, cancellationToken);
        if (theme is null)
        {
            return this.ToNotFoundProblem(UiThemeNotFoundProblem);
        }

        return Ok(theme);
    }

    [HttpPost(Name = RouteNames.CreateUiTheme)]
    [EndpointSummary("Create UI Theme")]
    [EndpointDescription("Creates a platform or tenant-owned UI theme. Platform themes require instance admin privileges.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create(
        [FromBody] CreateUiThemeDto dto,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(new CreateUiThemeCommand { UiThemeDto = dto }, cancellationToken);

        if (response.IsSuccess)
        {
            return CreatedAtRoute(RouteNames.GetUiThemeDetails, new { id = response.Id }, response);
        }

        if (response.Errors?.Count > 0)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return this.ToForbiddenProblem(detail: response.Message ?? "UI theme creation is not authorized for the current principal.");
    }

    [HttpPatch("{id:guid}", Name = RouteNames.UpdateUiTheme)]
    [EndpointSummary("Update UI Theme")]
    [EndpointDescription("Updates supplied metadata, state, or palette groups on an existing UI theme. The caller must match the theme's scope authorization.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateUiThemeDto dto,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(new UpdateUiThemeCommand { Id = id, UiThemeDto = dto }, cancellationToken);

        if (response.IsSuccess)
        {
            return Ok(response);
        }

        if (response.Errors?.Count > 0)
        {
            return this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        if (response.FailureCode == FailureCodes.NotFound)
        {
            return this.ToNotFoundProblem(UiThemeNotFoundProblem);
        }

        return this.ToForbiddenProblem(detail: response.Message ?? "UI theme update is not authorized for the current principal.");
    }

    [HttpDelete("{id:guid}", Name = RouteNames.DeleteUiTheme)]
    [EndpointSummary("Delete UI Theme")]
    [EndpointDescription("Deletes a UI theme. The theme must not be marked as default for its scope; promote another theme first.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await mediator.Send(new DeleteUiThemeCommand { Id = id }, cancellationToken);
        if (!deleted)
        {
            return this.ToNotFoundProblem(UiThemeNotFoundProblem);
        }

        return NoContent();
    }
}
