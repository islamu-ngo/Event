using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Commands;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Queries;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Commands;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Queries;
using Explore.Application.Responses;

namespace Event.Application.UnitTests.Features.EventCustomPropertyProjections;

public sealed class NativeProjectionContractTests
{
    [Test]
    [Arguments(typeof(GetEventSessionCustomPropertyProjectionStatusQuery), typeof(IQuery<BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>>))]
    [Arguments(typeof(GetEventSessionCustomPropertyProjectionsForSessionQuery), typeof(IQuery<BaseCommandResponse<IReadOnlyList<EventSessionCustomPropertyProjectionDto>>>))]
    [Arguments(typeof(RebuildEventSessionCustomPropertyProjectionCommand), typeof(ICommand<BaseCommandResponse<RebuildProjectionResponseDto>>))]
    [Arguments(typeof(RebuildSingleEventSessionCustomPropertyProjectionCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(GetEventCustomPropertyProjectionStatusQuery), typeof(IQuery<BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>>))]
    [Arguments(typeof(GetCustomPropertyProjectionDirtyScopesQuery), typeof(IQuery<PaginatedResult<ProjectionDirtyScopeDto>>))]
    [Arguments(typeof(GetEventCustomPropertyProjectionsForEventQuery), typeof(IQuery<BaseCommandResponse<IReadOnlyList<EventCustomPropertyProjectionDto>>>))]
    [Arguments(typeof(RebuildEventCustomPropertyProjectionCommand), typeof(ICommand<BaseCommandResponse<RebuildProjectionResponseDto>>))]
    [Arguments(typeof(RebuildSingleEventCustomPropertyProjectionCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(DrainCustomPropertyProjectionDirtyScopesCommand), typeof(ICommand<BaseCommandResponse<DrainDirtyScopesResponseDto>>))]
    public async Task OperationsHaveOneNativeShape(Type request, Type shape)
    {
        await Assert.That(shape.IsAssignableFrom(request)).IsTrue();
    }
}
