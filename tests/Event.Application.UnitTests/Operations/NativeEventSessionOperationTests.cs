using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Application.Responses;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventSessionOperationTests
{
    [Test]
    [Arguments(typeof(CreateEventSessionCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(CreateDraftEventSessionCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ScheduleEventSessionCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(PublishEventSessionCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ArchiveEventSessionCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(CancelEventSessionCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(CompleteEventSessionCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateEventSessionCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(DeleteEventSessionCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(GetEventSessionAuthorizationContextRequest), typeof(IQuery<EventSessionAuthorizationContextDto?>))]
    [Arguments(typeof(GetEventSessionCreateContextRequest), typeof(IQuery<EventSessionCreateContextDto?>))]
    [Arguments(typeof(GetEventSessionDetailsRequest), typeof(IQuery<EventSessionDto?>))]
    [Arguments(typeof(GetEventSessionListRequest), typeof(IQuery<PaginatedResult<EventSessionListDto>>))]
    [Arguments(typeof(GetManagedEventSessionDetailsRequest), typeof(IQuery<EventSessionDto?>))]
    [Arguments(typeof(GetManagedSessionsByEventRequest), typeof(IQuery<List<EventSessionListDto>>))]
    [Arguments(typeof(GetSessionsByEventRequest), typeof(IQuery<List<EventSessionListDto>>))]
    public async Task Requests_ExposeOnlyTheirExactNativeOperation(Type request, Type port)
    {
        await Assert.That(request.GetInterfaces()).Contains(port);
        await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        await Assert.That(request.GetInterfaces().Count(type => type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(ICommand<>)
                || type.GetGenericTypeDefinition() == typeof(IQuery<>)))).IsEqualTo(1);
    }
}
