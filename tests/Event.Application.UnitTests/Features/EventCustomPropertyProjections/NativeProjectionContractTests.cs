using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Commands;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Event.Application.UnitTests.Features.EventCustomPropertyProjections;

public sealed class NativeProjectionContractTests
{
    [Test]
    [Arguments(typeof(GetEventCustomPropertyProjectionStatusQuery), typeof(IQuery<BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>>))]
    [Arguments(typeof(GetCustomPropertyProjectionDirtyScopesQuery), typeof(IQuery<PaginatedResult<ProjectionDirtyScopeDto>>))]
    [Arguments(typeof(GetEventCustomPropertyProjectionsForEventQuery), typeof(IQuery<BaseCommandResponse<IReadOnlyList<EventCustomPropertyProjectionDto>>>))]
    [Arguments(typeof(RebuildEventCustomPropertyProjectionCommand), typeof(ICommand<BaseCommandResponse<RebuildProjectionResponseDto>>))]
    [Arguments(typeof(RebuildSingleEventCustomPropertyProjectionCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(DrainCustomPropertyProjectionDirtyScopesCommand), typeof(ICommand<BaseCommandResponse<DrainDirtyScopesResponseDto>>))]
    public async Task OperationsHaveOneNativeShapeAndNoMediatorContract(Type request, Type shape)
    {
        await Assert.That(shape.IsAssignableFrom(request)).IsTrue();
        await Assert.That(typeof(IBaseRequest).IsAssignableFrom(request)).IsFalse();
    }
}
