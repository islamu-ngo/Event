using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.DTOs.ManagedProviderProvisioning.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;
using Explore.Application.Management;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Explore.Application.Features.ManagedProviderProvisioning;

/// <summary>
/// Coordinates one original human request on its preselected Global physical owner.
/// All provisioning observations and effects belong to the closed DATA compiler; the
/// coordinator neither resolves ordinary repositories nor borrows worker authority.
/// </summary>
public sealed class HumanManagedProviderProvisioningCoordinator(
    IHumanCommitScopePreparation humanScope,
    IHumanManagedProviderProvisioningPreparation preparation,
    PrivacyIdentityFenceOperation identityFence,
    IOriginalOwnerSettlementBoundary owner,
    ISettingMutationLock mutationLock,
    IOptions<ManagedControlPlaneOptions> managedOptions,
    Func<IEventDiscoveryEnrollmentGate> enrollmentGate)
{
    public async Task<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>> ExecuteAsync(
        EnsureManagedProviderClientProvisionedCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ManagementRequest is not null)
            throw new InvalidOperationException("Managed workers do not consume human provisioning authority.");
        var dto = command.ProvisioningDto;
        if (dto.ActivateTenant && dto.DirectoryOperatorIdentity is null)
            return BaseCommandResponse.Failure<ManagedProviderClientProvisioningResultDto>(
                "tenant_directory_operator_identity_incomplete", "Tenant directory operator identity is not ready.");
        var validation = await new ManagedProviderClientProvisioningDtoValidator().ValidateAsync(dto, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<ManagedProviderClientProvisioningResultDto>(
                validation.Errors.Select(error => error.ErrorMessage),
                "Managed provider client provisioning failed due to validation errors.");

        ProviderAccountKey account = AdministratorAccount(dto);
        // Capture deployment scalars once. Persisted mode/counts are read by DATA
        // under the original physical owner and the existing deployment-capacity lock.
        bool capacityEnforced = managedOptions.Value.Enabled;
        int maximumTenants = managedOptions.Value.MaximumTenantCount;
        try
        {
            await humanScope.PrepareAsync(cancellationToken);
            var subject = humanScope.Subject
                ?? throw new UnauthorizedAccessException("Original provisioning administrator authority is required.");
            await using var enrollment = await enrollmentGate().AcquireAsync(cancellationToken);
            var prepared = await preparation.PrepareAsync(command, subject.PrincipalId, cancellationToken);
            return await identityFence.ExecuteSettledEnrollmentAsync(account, command, owner, async token =>
            {
                var data = await prepared.AcquireAsync(command, token);
                if (data.Refusal is { } refusal)
                    return OriginalOwnerDecision<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>>
                        .RollBack(Refuse(refusal));
                if (data.AdministratorAccount != account)
                    throw new UnauthorizedAccessException("The original provisioning target account changed.");
                if (account.ProviderKind != AuthenticationProviderKind.Local)
                    await identityFence.EnsureSubjectMayEnrollAsync(data.AdministratorUserId, token);

                // Replay never reserves or consumes another active-tenant slot. DATA
                // validates the complete existing customer graph before returning it.
                if (!data.IsReplay && dto.ActivateTenant && capacityEnforced)
                    return await mutationLock.ExecuteAsync(GovernanceSettingKeys.Deployment.Mode, async capacityToken =>
                    {
                        var capacity = await data.ReadActivationCapacityAsync(capacityToken);
                        int maximum = capacity.DeploymentMode == DeploymentMode.SingleTenant ? 1 : maximumTenants;
                        if (maximum == 0)
                            return OriginalOwnerDecision<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>>
                                .RollBack(Refuse(new("tenant_provisioning_capacity_not_configured",
                                    "Tenant activation capacity validation failed.",
                                    "Managed tenant provisioning capacity is not configured.")));
                        if (Math.Max(0, maximum - capacity.ActiveTenants - capacity.ReservedTenants) == 0)
                            return OriginalOwnerDecision<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>>
                                .RollBack(Refuse(new("tenant_provisioning_capacity_exhausted",
                                    "Tenant activation capacity validation failed.",
                                    "This Event instance has no available tenant capacity.")));
                        return await Apply(capacityToken);
                    }, token);
                return await Apply(token);

                async Task<OriginalOwnerDecision<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>>> Apply(
                    CancellationToken applyToken)
                {
                    var rows = await data.ApplyAsync(applyToken);
                    return OriginalOwnerDecision<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>>.Commit(
                        BaseCommandResponse.Success(new ManagedProviderClientProvisioningResultDto
                        {
                            TenantId = rows.TenantId,
                            UserId = rows.UserId,
                            TenantUserId = rows.TenantUserId,
                            TenantUserProfileId = rows.TenantUserProfileId,
                            UserActorId = rows.UserActorId,
                            UserExternalLoginId = rows.UserExternalLoginId,
                            TenantUserRoleGrantId = rows.TenantUserRoleGrantId,
                            OrganizerId = rows.OrganizerId,
                            OrganizerActorId = rows.OrganizerActorId,
                            OrganizerKind = rows.OrganizerKind switch
                            {
                                null => null,
                                ManagedProviderOrganizerKind.Organization => ManagedProviderOrganizerKindDto.Organization,
                                ManagedProviderOrganizerKind.Group => ManagedProviderOrganizerKindDto.Group,
                                _ => throw new InvalidOperationException("Unknown provisioned organizer kind.")
                            },
                            OrganizerMembershipId = rows.OrganizerMembershipId
                        }, rows.AlreadyProvisioned
                            ? "Managed provider client already provisioned."
                            : "Managed provider client provisioned successfully."));
                }
            }, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            // The physical boundary propagates this only after acknowledged rollback.
            // Unknown commit/rollback/disposal outcomes have a different exception type
            // and must never become an authorization response or a retry opportunity.
            return Refuse(new("authorization_denied", "Current instance administrator authority is required.",
                "Current instance administrator authority is required."));
        }
        catch (TenantDirectoryOperatorIdentityReadinessException exception)
        {
            return BaseCommandResponse.Failure<ManagedProviderClientProvisioningResultDto>(
                exception.FailureCode, exception.Message, exception.ReasonCodes);
        }
    }

    private static BaseCommandResponse<ManagedProviderClientProvisioningResultDto> Refuse(
        HumanManagedProviderProvisioningRefusal refusal) =>
        refusal.FailureCode is null
            ? BaseCommandResponse.Validation<ManagedProviderClientProvisioningResultDto>([refusal.Error], refusal.Message)
            : BaseCommandResponse.Failure<ManagedProviderClientProvisioningResultDto>(
                refusal.FailureCode, refusal.Message, [refusal.Error]);

    private static ProviderAccountKey AdministratorAccount(ManagedProviderClientProvisioningDto dto)
    {
        if (dto.LocalIdentity is { } local)
            return new(AuthenticationProviderKind.Local, local.LocalSubjectId.ToString("D"));
        var external = dto.ExternalAdmin!;
        string authority = external.IdentityProvider.Trim();
        if (authority.Equals("atproto", StringComparison.OrdinalIgnoreCase))
            return PlatformIdentityPrincipalExtensions.CreateAtprotoAccountKey(
                Explore.Domain.ValueObjects.AtprotoDid.Parse(external.Subject));
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var issuer)
            || issuer.Scheme is not ("https" or "http"))
            throw new InvalidOperationException("Managed external identity requires an absolute OIDC issuer authority.");
        return PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(authority, external.Subject);
    }
}
