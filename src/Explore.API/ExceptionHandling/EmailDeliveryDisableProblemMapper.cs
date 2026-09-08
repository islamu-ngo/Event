
using Explore.Application.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.ExceptionHandling;

internal static class EmailDeliveryDisableProblemMapper
{
    private static readonly CommandFailurePolicy Policy = CommandFailurePolicy
        .ValidatedBy(new ApiValidationProblemDescriptor(
            "emailDelivery", "Email delivery disable validation failed", "Email delivery disable failed."))
        .Forbidden("Forbidden", "Current administrator authority is required for the selected scope.",
            FailureCodes.AdminRequired)
        .NotFound(new ApiNotFoundProblemDescriptor(
            "Email delivery scope not found", "Email delivery scope was not found."), FailureCodes.NotFound)
        .Conflict("Email delivery confirmation conflict", "Email delivery disable failed.",
            FailureCodes.ConcurrencyConflict);

    public static ActionResult ToEmailDeliveryDisableProblem<T>(
        this ControllerBase controller, BaseCommandResponse<T> response) => Policy.Map(controller, response);
}
