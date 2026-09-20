using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

public abstract class SettingsCapabilityControllerBase : ControllerBase
{
    private protected static readonly ApiValidationProblemDescriptor SettingsValidationProblem = new(
        "settings",
        "Settings validation failed",
        "Settings update failed.");

    protected ActionResult<BaseCommandResponse<Guid>> HandleCommandResponse(BaseCommandResponse<Guid> response)
    {
        if (response.IsSuccess) return Ok(response);

        if (response.FailureCode == FailureCodes.AdminRequired)
        {
            return this.ToForbiddenProblem(detail: response.Message);
        }

        return response.FailureCode == CommandResponseResultMapper.VisitorAccessAccountRequiredConflict
            ? this.MapCommandResponse(response)
            : this.ToCommandValidationProblem(response, SettingsValidationProblem);
    }
}
