using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

/// <summary>
/// Turns provider-neutral package status into something an operator can act on.
/// <para>
/// The mapping to a recovery action lives here rather than in the caller because there is exactly one
/// right next step per issue code, and leaving that to each consumer is how "unknown status" ends up
/// rendered as a shrug in one UI and an outage page in another.
/// </para>
/// </summary>
public sealed class GetAuthorizationPolicyPackageStatusQueryHandler(
    IPolicyPackageService policyPackageService,
    IAuthorizationProviderConfigurationService providerConfigurationService)
    : IRequestHandler<GetAuthorizationPolicyPackageStatusQuery, AuthorizationPolicyPackageStatusDto>
{
    public async Task<AuthorizationPolicyPackageStatusDto> Handle(
        GetAuthorizationPolicyPackageStatusQuery request,
        CancellationToken cancellationToken)
    {
        var configuration = await providerConfigurationService.ReadConfigurationAsync();
        var status = await policyPackageService.GetStatusAsync(cancellationToken);

        return new AuthorizationPolicyPackageStatusDto(
            Provider: configuration.Provider,
            PackageId: status.PackageId,
            PackageContentHash: status.ContentHash,
            ObservedRevision: status.ObservedRevision,
            RevisionCertain: status.IsHealthy && status.ObservedRevision is not null,
            IsHealthy: status.IsHealthy,
            IssueCode: status.IssueCode.ToString(),
            Message: status.Message,
            Warnings: status.Warnings,
            RecoveryAction: DescribeRecoveryAction(status),
            CheckedAt: status.CheckedAt);
    }

    private static string DescribeRecoveryAction(PolicyPackageStatusResult status) => status.IssueCode switch
    {
        PolicyPackageIssueCode.None when status.ObservedRevision is null =>
            "Grant the Admin API credentials permission to read policies so the store revision becomes observable. "
            + "Until then an in-place policy edit is invisible to this diagnostic.",

        PolicyPackageIssueCode.None =>
            "No action required. Record the observed revision; a change to it that nobody published is drift.",

        PolicyPackageIssueCode.PackageMismatch =>
            "Re-publish the policy package (POST authz-provider/sync), then re-check this status.",

        PolicyPackageIssueCode.PackageStatusUnknown =>
            "Restore Cerbos Admin API reachability, then re-check the explicit package status. Runtime decisions continue through the gRPC PDP.",

        PolicyPackageIssueCode.AdminApiNotConfigured =>
            "Configure the Cerbos Admin API endpoint and credentials, or publish the package out of band "
            + "and download it from authz-provider/package for manual installation.",

        PolicyPackageIssueCode.AdminApiUnavailable =>
            "The Cerbos Admin API is configured but not responding. Check network reachability and credentials.",

        PolicyPackageIssueCode.PackageUnavailable =>
            "Mount or bundle the policy package directory into this API deployment and restart.",

        _ => "Inspect server logs for the authorization policy package status details."
    };
}
