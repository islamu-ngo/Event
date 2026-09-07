using Explore.API.ExceptionHandling;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

/// <summary>
/// Instance settings are writable in two situations that look different but mean the same thing: an
/// authenticated instance administrator is changing configuration, or the instance is still in first-run setup
/// and the caller holds the setup secret. Every settings surface needs that same rule, and a surface that
/// implemented it slightly differently would become a way to configure an instance without being its admin.
/// <para>
/// Keeping the rule — and the failure mapping that turns an admin-required result into a 403 rather than a
/// validation error — on one base class is what makes the family's split into capability controllers safe.
/// </para>
/// </summary>
public abstract class InstanceSettingsControllerBase(
    IAdminContext adminContext,
    ISetupSecretProvider setupSecretProvider) : EventControllerBase
{
    private const string SetupSecretHeader = "X-Setup-Secret";

    /// <summary>Exposed for surfaces that must assert instance-admin authority for a specific user id.</summary>
    protected IAdminContext AdminContext { get; } = adminContext;

    private static readonly ApiValidationProblemDescriptor InstanceSettingsValidationProblem = new(
        "instanceSettings",
        "Instance settings validation failed",
        "Instance settings update failed.");

    /// <summary>
    /// True for an instance administrator, or for a setup-secret holder while setup mode is still active.
    /// The secret is read from the request header only after <see cref="ISetupSecretProvider.IsSetupModeActive"/>
    /// confirms setup is live, so a stale header cannot grant authority on a configured instance.
    /// </summary>
    protected async Task<bool> IsInstanceAdminOrSetupAuthenticated(CancellationToken cancellationToken)
    {
        if (await AdminContext.IsInstanceAdminAsync(cancellationToken))
        {
            return true;
        }

        var setupSecret = Request.Headers.TryGetValue(SetupSecretHeader, out var value)
            ? value.ToString()
            : null;
        SetupSecretValidationOutcome validation =
            await setupSecretProvider.ValidateSecretAsync(setupSecret, cancellationToken);
        return validation == SetupSecretValidationOutcome.Accepted;
    }

    /// <summary>True when the request authenticated through the setup-secret scheme itself.</summary>
    protected bool IsSetupSecretAuthenticated()
        => User.Identities.Any(identity =>
            identity.IsAuthenticated
            && string.Equals(
                identity.AuthenticationType,
                ApiAuthenticationSchemeNames.SetupSecret,
                StringComparison.Ordinal));

    /// <summary>
    /// Maps a settings command result. An admin-required failure becomes a 403 rather than a validation
    /// problem, because the caller's request was well-formed — they simply lack the authority to make it.
    /// </summary>
    protected ActionResult<BaseCommandResponse<Guid>> HandleCommandResponse(BaseCommandResponse<Guid> response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.IsSuccess)
        {
            return Ok(response);
        }

        return response.FailureCode == FailureCodes.AdminRequired
            ? this.ToForbiddenProblem(detail: response.Message)
            : this.ToCommandValidationProblem(response, InstanceSettingsValidationProblem);
    }
}
