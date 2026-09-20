using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Features.EventAgendaItems.Requests.Commands;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using Explore.Application.Responses;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventAgendaItemOperationTests
{
    [Test]
    [Arguments(typeof(CreateEventAgendaItemCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateEventAgendaItemCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(DeleteEventAgendaItemCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(GetEventAgendaItemsByEventRequest), typeof(IQuery<List<EventAgendaItemListDto>>))]
    [Arguments(typeof(GetEventAgendaItemDetailRequest), typeof(IQuery<EventAgendaItemDto>))]
    [Arguments(typeof(GetManagedEventAgendaItemsByEventRequest), typeof(IQuery<List<EventAgendaItemListDto>>))]
    [Arguments(typeof(GetManagedEventAgendaItemDetailRequest), typeof(IQuery<EventAgendaItemDto>))]
    public async Task Requests_ExposeOnlyTheirExactNativeOperation(Type request, Type port)
    {
        await Assert.That(request.GetInterfaces()).Contains(port);
        await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        await Assert.That(request.GetInterfaces().Count(type => type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(ICommand<>)
                || type.GetGenericTypeDefinition() == typeof(IQuery<>)))).IsEqualTo(1);
    }
}
