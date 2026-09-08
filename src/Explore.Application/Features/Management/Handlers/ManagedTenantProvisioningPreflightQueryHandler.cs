using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Management;
using Explore.Application.DTOs.Management.Validators;
using Explore.Application.Features.Management.Requests.Queries;
using Explore.Application.Management;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;

namespace Explore.Application.Features.Management.Handlers.Queries;

public sealed class GetManagedTenantProvisioningPreflightQueryHandler(
    IOptions<ManagedControlPlaneOptions> options,
    IDeploymentModeProvider deploymentModeProvider,
    IManagedControlPlaneRegistrationRepository registrationRepository,
    TenantActivationCapacityPolicy capacityPolicy,
    ManagedTenantProvisioningPreflight preflight)
    : IRequestHandler<GetManagedTenantProvisioningPreflightQuery,
        ManagementTenantProvisioningPreflightDto>
{
    public async Task<ManagementTenantProvisioningPreflightDto> Handle(
        GetManagedTenantProvisioningPreflightQuery query,
        CancellationToken cancellationToken)
    {
        var validator = new ManagementTenantProvisioningRequestValidator();
        await validator.ValidateAndThrowAsync(query.Request, cancellationToken);

        ManagementTenantProvisioningRequestDto request =
            ManagedTenantProvisioningRequestCodec.Normalize(query.Request);
        string requestHash = ManagedTenantProvisioningRequestCodec.ComputeHash(request);

        if (!options.Value.Enabled)
        {
            return Blocked(
                query.ManagedInstanceId,
                request,
                requestHash,
                DeploymentMode.SingleTenant,
                "Disabled",
                null,
                new ManagementTenantProvisioningBlockerDto(
                    "managed_mode_disabled",
                    "Managed mode is disabled on this Event instance."));
        }

        DeploymentMode deploymentMode = await deploymentModeProvider.GetCurrentModeAsync(cancellationToken);
        if (deploymentMode != DeploymentMode.MultiTenant)
        {
            return Blocked(
                query.ManagedInstanceId,
                request,
                requestHash,
                deploymentMode,
                "NotAssessed",
                null,
                new ManagementTenantProvisioningBlockerDto(
                    "tenant_provisioning_requires_multi_tenant",
                    "Managed tenant provisioning is unavailable in SingleTenant mode."));
        }

        ManagedControlPlaneRegistration? registration =
            await registrationRepository.GetCurrentAsync(cancellationToken);
        ManagementTenantProvisioningBlockerDto? registrationBlocker =
            ManagedTenantProvisioningRegistrationPolicy.Evaluate(
                registration,
                query.ManagedInstanceId,
                deploymentMode);
        Guid? eventInstanceId = registration?.ManagedInstanceId == query.ManagedInstanceId
            ? registration.EventInstanceId
            : null;
        string registrationState = registration?.Status.ToString() ?? "Unregistered";
        if (registrationBlocker is not null)
        {
            return Blocked(
                query.ManagedInstanceId,
                request,
                requestHash,
                deploymentMode,
                registrationState,
                eventInstanceId,
                registrationBlocker);
        }

        var blockers = new List<ManagementTenantProvisioningBlockerDto>();
        ManagementTenantProvisioningCapacityDto? capacity = null;
        if (options.Value.MaximumTenantCount <= 0)
        {
            blockers.Add(new ManagementTenantProvisioningBlockerDto(
                "tenant_provisioning_capacity_not_configured",
                "Managed tenant provisioning capacity is not configured."));
        }
        else
        {
            TenantActivationCapacityAssessment capacityAssessment = await capacityPolicy.EvaluateAsync(
                requireMultiTenant: true,
                knownPersistedMode: deploymentMode,
                cancellationToken: cancellationToken);
            capacity = new ManagementTenantProvisioningCapacityDto(
                capacityAssessment.Maximum,
                capacityAssessment.Active,
                capacityAssessment.Reserved,
                capacityAssessment.Available,
                capacityAssessment.Allowed,
                capacityAssessment.FailureCode);
            if (!capacityAssessment.Allowed)
            {
                blockers.Add(new ManagementTenantProvisioningBlockerDto(
                    capacityAssessment.FailureCode!,
                    capacityAssessment.Error!));
            }
        }

        ManagedTenantProvisioningPreflightResult policy = await preflight.EvaluateAsync(
            request,
            requireProvisionablePlan: true,
            cancellationToken);
        if (!policy.Success)
        {
            blockers.Add(new ManagementTenantProvisioningBlockerDto(
                policy.FailureCode!,
                policy.Error!));
        }

        ManagedTenantProvisioningResolvedBootstrap? resolved = policy.Resolved;
        return CreateAssessment(
            query.ManagedInstanceId,
            eventInstanceId,
            registrationState,
            deploymentMode,
            requestHash,
            request,
            blockers,
            capacity,
            resolved);
    }

    private static ManagementTenantProvisioningPreflightDto Blocked(
        Guid managedInstanceId,
        ManagementTenantProvisioningRequestDto request,
        string requestHash,
        DeploymentMode deploymentMode,
        string registrationState,
        Guid? eventInstanceId,
        ManagementTenantProvisioningBlockerDto blocker) =>
        CreateAssessment(
            managedInstanceId,
            eventInstanceId,
            registrationState,
            deploymentMode,
            requestHash,
            request,
            [blocker],
            null,
            null);

    private static ManagementTenantProvisioningPreflightDto CreateAssessment(
        Guid managedInstanceId,
        Guid? eventInstanceId,
        string registrationState,
        DeploymentMode deploymentMode,
        string requestHash,
        ManagementTenantProvisioningRequestDto request,
        IReadOnlyList<ManagementTenantProvisioningBlockerDto> blockers,
        ManagementTenantProvisioningCapacityDto? capacity,
        ManagedTenantProvisioningResolvedBootstrap? resolved)
    {
        ManagementTenantPlanDto? plan = resolved is null
            ? null
            : new ManagementTenantPlanDto
            {
                Key = resolved.Plan.Key,
                VersionId = resolved.PlanVersion.Id,
                Quotas = resolved.PlanVersion.Quotas
                    .Select(quota => new ManagementTenantQuotaDto(quota.QuotaKey, quota.Limit))
                    .OrderBy(quota => quota.Key, StringComparer.Ordinal)
                    .ToArray()
            };
        ManagementTenantBrandingIntentDto? branding = resolved is null
            ? null
            : new ManagementTenantBrandingIntentDto
            {
                DisplayName = resolved.Branding.DisplayName,
                LogoUrl = resolved.Branding.LogoUrl,
                FaviconUrl = resolved.Branding.FaviconUrl,
                CustomCssUrl = resolved.Branding.CustomCssUrl
            };

        return new ManagementTenantProvisioningPreflightDto(
            ManagementTenantProvisioningRequestDto.CurrentSchemaVersion,
            ManagedControlPlaneContract.ManagementApiVersion,
            ManagementVersionResolver.EventVersion,
            managedInstanceId,
            eventInstanceId,
            registrationState,
            deploymentMode,
            requestHash,
            request.TenantSlug,
            blockers.Count == 0,
            true,
            blockers,
            capacity,
            plan,
            resolved?.Modules.Select(module => module.ModuleKey)
                .Order(StringComparer.Ordinal)
                .ToArray() ?? [],
            resolved is null ? null : request.Domain,
            branding,
            resolved?.Settings
                .Select(setting => new ManagementTenantResolvedSettingDto(
                    setting.SettingKey,
                    setting.Value,
                    setting.IsLocked))
                .OrderBy(setting => setting.Key, StringComparer.Ordinal)
                .ToArray() ?? [],
            request.Callback?.CorrelationId,
            DateTime.UtcNow);
    }
}
