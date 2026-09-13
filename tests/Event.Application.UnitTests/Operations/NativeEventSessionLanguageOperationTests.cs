using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Features.EventSessionLanguages.Requests.Commands;
using Explore.Application.Features.EventSessionLanguages.Requests.Queries;
using Explore.Application.Responses;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventSessionLanguageOperationTests
{
    [Test]
    [Arguments(typeof(CreateEventSessionLanguageCommand), typeof(ICommand<BaseCommandResponse<int>>))]
    [Arguments(typeof(UpdateEventSessionLanguageCommand), typeof(ICommand<BaseCommandResponse<int>>))]
    [Arguments(typeof(DeleteEventSessionLanguageCommand), typeof(ICommand<bool>))]
    [Arguments(typeof(GetEventSessionLanguageDetailsQuery), typeof(IQuery<EventSessionLanguageDto>))]
    [Arguments(typeof(GetEventSessionLanguageListQuery), typeof(IQuery<PaginatedResult<EventSessionLanguageListDto>>))]
    [Arguments(typeof(GetLanguagesBySessionQuery), typeof(IQuery<List<EventSessionLanguageListDto>>))]
    [Arguments(typeof(GetManagedLanguagesBySessionQuery), typeof(IQuery<List<EventSessionLanguageListDto>>))]
    public async Task Requests_ExposeOnlyTheirExactNativeOperation(Type request, Type port)
    {
        await Assert.That(request.GetInterfaces()).Contains(port);
        await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        await Assert.That(request.GetInterfaces().Count(type => type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(ICommand<>)
                || type.GetGenericTypeDefinition() == typeof(IQuery<>)))).IsEqualTo(1);
    }
}
