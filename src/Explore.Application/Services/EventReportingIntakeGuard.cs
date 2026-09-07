using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventReporting;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Services;

public sealed class EventReportingIntakeGuard(
    IHierarchicalSettingsResolver settingsResolver,
    ILogger<EventReportingIntakeGuard> logger) : IEventReportingIntakeGuard
{
    private const string IntakeEnabledReasonCode = "event_reporting_intake_enabled";
    private const string TenantUnresolvedMessage = "Tenant context could not be resolved.";
    private const string IntakeDisabledMessage = "Event reporting intake is disabled for this tenant.";
    private const string IntakeEnabledMessage = "Event reporting intake is enabled.";

    public async Task<EventReportingIntakeDecision> ResolveAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return new EventReportingIntakeDecision(
                TenantResolved: false,
                IntakeEnabled: false,
                ReasonCode: EventReportFailureCodes.TenantUnresolved,
                Message: TenantUnresolvedMessage);
        }

        try
        {
            ReportingIntakeSettingGroup settings = await settingsResolver.ResolveGroupAsync<ReportingIntakeSettingGroup>(
                new SettingContext(TenantId: tenantId),
                cancellationToken);
            return settings.IntakeEnabled
                ? new EventReportingIntakeDecision(true, true, IntakeEnabledReasonCode, IntakeEnabledMessage)
                : new EventReportingIntakeDecision(
                    true,
                    false,
                    EventReportFailureCodes.IntakeDisabled,
                    IntakeDisabledMessage);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Event reporting intake resolution failed for tenant {TenantId}.", tenantId);
            return new EventReportingIntakeDecision(
                TenantResolved: true,
                IntakeEnabled: false,
                EventReportFailureCodes.IntakeDisabled,
                IntakeDisabledMessage);
        }
    }
}
