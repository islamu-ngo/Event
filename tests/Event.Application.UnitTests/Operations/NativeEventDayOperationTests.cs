using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventDay;
using Explore.Application.Features.EventDays.Requests.Commands;
using Explore.Application.Features.EventDays.Requests.Queries;
using Explore.Application.Responses;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventDayOperationTests
{
    [Test]
    [Arguments(typeof(CreateEventDayCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateEventDayCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(DeleteEventDayCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(GetEventDaysByEventRequest), typeof(IQuery<List<EventDayListDto>>))]
    [Arguments(typeof(GetManagedEventDaysByEventRequest), typeof(IQuery<List<EventDayListDto>>))]
    [Arguments(typeof(GetEventDayDetailRequest), typeof(IQuery<EventDayDto>))]
    public async Task Requests_ExposeOnlyTheirExactNativeOperation(Type request, Type port)
    {
        await Assert.That(request.GetInterfaces()).Contains(port);
        await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        await Assert.That(request.GetInterfaces().Count(type => type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(ICommand<>)
                || type.GetGenericTypeDefinition() == typeof(IQuery<>)))).IsEqualTo(1);
    }
}
