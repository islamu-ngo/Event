using Explore.API.ExceptionHandling;
using Explore.Application.Contracts.Services;
using Explore.Application.Onboarding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Explore.API.Filters;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class SetupSecretRequiredAttribute : TypeFilterAttribute
{
    public SetupSecretRequiredAttribute(bool requireIncomplete = false) : base(typeof(SetupSecretRequiredFilter))
    {
        Arguments = [requireIncomplete];
    }

    internal sealed class SetupSecretRequiredFilter : IAsyncActionFilter
    {
        private readonly ISetupSecretProvider _setupSecretProvider;
        private readonly IInstanceBootstrapAuditLogger _bootstrapAuditLogger;

        public SetupSecretRequiredFilter(
            ISetupSecretProvider setupSecretProvider,
            IInstanceBootstrapAuditLogger bootstrapAuditLogger,
            bool requireIncomplete)
        {
            _setupSecretProvider = setupSecretProvider;
            _bootstrapAuditLogger = bootstrapAuditLogger;
            _ = requireIncomplete;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var secret = context.HttpContext.Request.Headers["X-Setup-Secret"].FirstOrDefault();
            SetupSecretValidationOutcome validation = await _setupSecretProvider.ValidateSecretAsync(
                secret,
                context.HttpContext.RequestAborted);
            if (validation == SetupSecretValidationOutcome.SetupCompleted)
            {
                LogAudit(
                    context,
                    InstanceBootstrapAuditEventType.SetupModeInactive,
                    "inactive",
                    ApiProblemCodes.SetupAlreadyCompleted);

                context.Result = ApiProblemFactory.ToProblemResult(
                    ApiProblemFactory.CreateGoneProblem(
                        context.HttpContext,
                        "Setup already completed",
                        "Setup mode is no longer active for this instance.",
                        ApiProblemCodes.SetupAlreadyCompleted));
                return;
            }

            if (validation != SetupSecretValidationOutcome.Accepted)
            {
                LogAudit(
                    context,
                    InstanceBootstrapAuditEventType.SetupSecretRejected,
                    "rejected",
                    "invalid_setup_secret");

                context.Result = ApiProblemFactory.ToProblemResult(
                    ApiProblemFactory.CreateForbiddenProblem(
                        context.HttpContext,
                        "Invalid setup secret",
                        "A valid setup secret is required for this bootstrap operation."));
                return;
            }

            LogAudit(context, InstanceBootstrapAuditEventType.SetupSecretAccepted, "accepted");
            await next();
        }

        private void LogAudit(
            ActionExecutingContext context,
            InstanceBootstrapAuditEventType eventType,
            string outcome,
            string? failureCode = null)
        {
            _bootstrapAuditLogger.Log(new InstanceBootstrapAuditEvent(
                eventType,
                Operation: "setup_secret_gate",
                Outcome: outcome,
                RouteName: ResolveRouteName(context),
                TraceId: context.HttpContext.TraceIdentifier,
                FailureCode: failureCode));
        }

        private static string? ResolveRouteName(ActionExecutingContext context)
            => context.ActionDescriptor.AttributeRouteInfo?.Name
               ?? context.ActionDescriptor.DisplayName
               ?? context.HttpContext.Request.Path.Value;
    }
}
