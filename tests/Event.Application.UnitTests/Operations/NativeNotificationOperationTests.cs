using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Notifications.Requests.Commands;
using Explore.Application.Features.Notifications.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeNotificationOperationTests
{
    [Test]
    public async Task NotificationCapabilityRegistersExactlyThirteenCommandsAndEightQueries()
    {
        Type[] commands =
        [
            typeof(ArchiveNotificationCommand), typeof(DeleteNotificationCommand),
            typeof(MarkAllNotificationsAsReadCommand), typeof(MarkNotificationAsReadCommand),
            typeof(SetCurrentUserNotificationPreferenceMuteCommand), typeof(SetGroupNotificationPreferenceMuteCommand),
            typeof(SetOrganizationNotificationPreferenceMuteCommand), typeof(SnoozeNotificationCommand),
            typeof(SubscribeCurrentUserWebPushSubscriptionCommand), typeof(UnsubscribeCurrentUserWebPushSubscriptionCommand),
            typeof(UpdateCurrentUserNotificationPreferenceMatrixCommand), typeof(UpdateGroupNotificationPreferenceMatrixCommand),
            typeof(UpdateOrganizationNotificationPreferenceMatrixCommand)
        ];
        Type[] queries =
        [
            typeof(GetCurrentUserNotificationPreferenceMatrixQuery), typeof(GetCurrentUserWebPushSubscriptionQuery),
            typeof(GetGroupNotificationPreferenceMatrixQuery), typeof(GetNotificationByIdQuery),
            typeof(GetOrganizationNotificationPreferenceMatrixQuery), typeof(GetUnreadCountQuery),
            typeof(GetUserNotificationsQuery), typeof(GetWebPushPublicConfigurationQuery)
        ];
        var services = new ServiceCollection();
        services.AddNativeOperations(typeof(ArchiveNotificationCommand).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("Explore.Application.Features.Notifications.", StringComparison.Ordinal) == true));
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Select(descriptor => descriptor.ServiceType).ToArray();
        await Assert.That(ports.Where(type => type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
            .Select(type => type.GetGenericArguments()[0])).IsEquivalentTo(commands);
        await Assert.That(ports.Where(type => type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .Select(type => type.GetGenericArguments()[0])).IsEquivalentTo(queries);
        foreach (var request in commands.Concat(queries))
        {
            await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        }
    }
}
