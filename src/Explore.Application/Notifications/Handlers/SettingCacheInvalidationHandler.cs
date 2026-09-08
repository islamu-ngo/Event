using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using MediatR;

namespace Explore.Application.Notifications.Handlers;

public sealed class SettingCacheInvalidationHandler : INotificationHandler<SettingChangedNotification>
{
    private readonly IHierarchicalSettingsResolver _resolver;
    private readonly IEnumerable<IAtprotoDiscoveryCacheInvalidator> _atprotoDiscoveryCacheInvalidators;
    private readonly IEnumerable<IEventReportingOutputCacheInvalidator> _eventReportingOutputCacheInvalidators;

    public SettingCacheInvalidationHandler(
        IHierarchicalSettingsResolver resolver,
        IEnumerable<IAtprotoDiscoveryCacheInvalidator> atprotoDiscoveryCacheInvalidators,
        IEnumerable<IEventReportingOutputCacheInvalidator> eventReportingOutputCacheInvalidators)
    {
        _resolver = resolver;
        _atprotoDiscoveryCacheInvalidators = atprotoDiscoveryCacheInvalidators;
        _eventReportingOutputCacheInvalidators = eventReportingOutputCacheInvalidators;
    }

    public async Task Handle(SettingChangedNotification notification, CancellationToken cancellationToken)
    {
        _resolver.InvalidateCache(SettingScope.Instance);

        if (notification.TenantId.HasValue)
        {
            _resolver.InvalidateCache(SettingScope.Tenant, notification.TenantId.Value);
        }

        if (string.Equals(
                notification.Key,
                GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
                StringComparison.Ordinal))
        {
            foreach (IAtprotoDiscoveryCacheInvalidator invalidator in _atprotoDiscoveryCacheInvalidators)
            {
                await invalidator.InvalidateAsync(cancellationToken);
            }
        }

        if (string.Equals(
                notification.Key,
                GovernanceSettingKeys.EventReporting.IntakeEnabled,
                StringComparison.Ordinal))
        {
            foreach (IEventReportingOutputCacheInvalidator invalidator in _eventReportingOutputCacheInvalidators)
            {
                await invalidator.InvalidateAsync(cancellationToken);
            }
        }
    }
}
