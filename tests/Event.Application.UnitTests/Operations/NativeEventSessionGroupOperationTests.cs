using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Features.EventSessionGroups.Requests.Commands;
using Explore.Application.Features.EventSessionGroups.Requests.Queries;
using Explore.Application.Responses;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventSessionGroupOperationTests
{
    [Test]
    [Arguments(typeof(CreateEventSessionGroupCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateEventSessionGroupCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(DeleteEventSessionGroupCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(AssignSessionToGroupCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UnassignSessionFromGroupCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(GetEventSessionGroupsByEventRequest), typeof(IQuery<List<EventSessionGroupListDto>>))]
    [Arguments(typeof(GetEventSessionGroupDetailRequest), typeof(IQuery<EventSessionGroupDto?>))]
    [Arguments(typeof(GetManagedEventSessionGroupsByEventRequest), typeof(IQuery<List<EventSessionGroupListDto>>))]
    [Arguments(typeof(GetManagedEventSessionGroupDetailRequest), typeof(IQuery<EventSessionGroupDto?>))]
    [Arguments(typeof(GetEventSessionGroupSessionsRequest), typeof(IQuery<List<EventSessionListDto>>))]
    public async Task Requests_ExposeOnlyTheirExactNativeOperation(Type request, Type port)
    {
        await Assert.That(request.GetInterfaces()).Contains(port);
        await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        await Assert.That(request.GetInterfaces().Count(type => type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(ICommand<>)
                || type.GetGenericTypeDefinition() == typeof(IQuery<>)))).IsEqualTo(1);
    }
}
