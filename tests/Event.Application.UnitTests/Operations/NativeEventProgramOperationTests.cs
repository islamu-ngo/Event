using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventProgram;
using Explore.Application.Features.EventPrograms.Handlers.Queries;
using Explore.Application.Features.EventPrograms.Requests.Queries;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventProgramOperationTests
{
    [Test]
    [Arguments(typeof(GetEventProgramSummaryRequest))]
    [Arguments(typeof(GetManagedEventProgramSummaryRequest))]
    public async Task SummaryRequests_ArePureNativeQueriesWithBothSharedHandlerPorts(Type request)
    {
        await Assert.That(request.GetInterfaces()).Contains(typeof(IQuery<EventProgramSummaryDto?>));
        await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        await Assert.That(request.GetInterfaces().Count(type => type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(IQuery<>)
                || type.GetGenericTypeDefinition() == typeof(ICommand<>)))).IsEqualTo(1);
        await Assert.That(typeof(GetEventProgramSummaryRequestHandler).GetInterfaces())
            .Contains(typeof(IQueryHandler<,>).MakeGenericType(request, typeof(EventProgramSummaryDto)));
    }
}
