using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventReportingOperationTests
{
    private static readonly Type[] Requests =
    [
        // Commands (12)
        typeof(AssignEventReportCommand),
        typeof(DecideEventReportCommand),
        typeof(ExecuteReportDecisionCommand),
        typeof(ProcessCoopDecisionCallbackCommand),
        typeof(RecordOspreySignalCallbackCommand),
        typeof(SubmitEventReportCommand),
        typeof(TestReportingProviderTargetCommand),
        typeof(TriageEventReportCommand),
        typeof(UpdateMyReportCommunicationConsentCommand),
        typeof(UpdateReportingProviderLocksCommand),
        typeof(UpdateReportingRoutingSettingsCommand),
        typeof(UpdateTenantReportingIntakePolicyCommand),

        // Queries (8)
        typeof(GetEventReportOptionsRequest),
        typeof(GetModerationReportDetailRequest),
        typeof(GetModerationReportQueueRequest),
        typeof(GetMyReportRequest),
        typeof(GetMyReportsRequest),
        typeof(GetReportingRoutingStateRequest),
        typeof(GetTenantModerationReportingDashboardRequest),
        typeof(GetTenantReportingIntakePolicyQuery)
    ];

    [Test]
    public async Task Discovery_RegistersAllTwentyEventReportingScopedPortsWithoutLegacyShapes()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations();

        foreach (var request in Requests)
        {
            var shapes = request.GetInterfaces().Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(ICommand<>) || type.GetGenericTypeDefinition() == typeof(IQuery<>))).ToArray();
            await Assert.That(shapes.Length).IsEqualTo(1).Because(request.Name);
            await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse().Because(request.Name);
        }

        var ports = services.Where(descriptor => !descriptor.IsKeyedService &&
            OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType) &&
            Requests.Contains(descriptor.ServiceType.GenericTypeArguments[0])).ToArray();

        await Assert.That(ports.Length).IsEqualTo(20);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped && port.ImplementationFactory is not null)).IsTrue();
    }
}
