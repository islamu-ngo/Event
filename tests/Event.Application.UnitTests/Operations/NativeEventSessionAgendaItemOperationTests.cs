using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Commands;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Queries;
using Explore.Application.Responses;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventSessionAgendaItemOperationTests
{
    [Test]
    [Arguments(typeof(CreateEventSessionAgendaItemCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateEventSessionAgendaItemCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(DeleteEventSessionAgendaItemCommand), typeof(ICommand<bool>))]
    [Arguments(typeof(GetAgendaItemsBySessionRequest), typeof(IQuery<List<EventSessionAgendaItemListDto>>))]
    [Arguments(typeof(GetEventSessionAgendaItemDetailsRequest), typeof(IQuery<EventSessionAgendaItemDto?>))]
    [Arguments(typeof(GetEventSessionAgendaItemListRequest), typeof(IQuery<PaginatedResult<EventSessionAgendaItemListDto>>))]
    [Arguments(typeof(GetManagedAgendaItemsBySessionRequest), typeof(IQuery<List<EventSessionAgendaItemListDto>?>))]
    public async Task Requests_ExposeOnlyTheirExactNativeOperation(Type request, Type port)
    {
        await Assert.That(request.GetInterfaces()).Contains(port);
        await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        await Assert.That(request.GetInterfaces().Count(type => type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(ICommand<>)
                || type.GetGenericTypeDefinition() == typeof(IQuery<>)))).IsEqualTo(1);
    }
}
