using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/managed-provider-provisioning")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
public class ManagedProviderProvisioningController : ControllerBase
{
    private static readonly ApiValidationProblemDescriptor ProvisioningValidationProblem = new(
        "managedProviderProvisioning",
        "Managed provider provisioning validation failed",
        "Managed provider client provisioning failed.");

    private readonly IMediator _mediator;
    private readonly IAdminContext _adminContext;

    public ManagedProviderProvisioningController(IMediator mediator, IAdminContext adminContext)
    {
        _mediator = mediator;
        _adminContext = adminContext;
    }

    [HttpPost("clients:ensure", Name = RouteNames.EnsureManagedProviderClientProvisioned)]
    [EndpointSummary("Ensure managed provider client provisioning")]
    [EndpointDescription("Creates or rehydrates a provider-customer tenant, external admin user, tenant-admin membership, and optional organizer actor. Requires instance administrator authority.")]
    [ProducesResponseType(typeof(BaseCommandResponse<ManagedProviderClientProvisioningResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>>> EnsureClientProvisioned(
        [FromBody] ManagedProviderClientProvisioningDto provisioningDto,
        CancellationToken cancellationToken = default)
    {
        if (!await _adminContext.IsInstanceAdminAsync(cancellationToken))
        {
            return this.ToForbiddenProblem(detail: "Instance administrator authority is required to provision managed provider clients.");
        }

        var response = await _mediator.Send(
            new EnsureManagedProviderClientProvisionedCommand { ProvisioningDto = provisioningDto },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, ProvisioningValidationProblem);
        }

        return Ok(response);
    }
}
