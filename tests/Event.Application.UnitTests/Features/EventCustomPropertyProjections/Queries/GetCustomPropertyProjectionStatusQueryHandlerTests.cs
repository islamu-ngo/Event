using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Features.EventCustomPropertyProjections.Handlers.Queries;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Queries;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Handlers.Queries;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventCustomPropertyProjections.Queries;

public sealed class GetCustomPropertyProjectionStatusQueryHandlerTests
{
    private readonly ICustomPropertyProjectionStatusRepository _statusRepository;
    private readonly ICustomPropertyProjectionDirtyScopeRepository _dirtyScopeRepository;
    private readonly IMapper _mapper;

    public GetCustomPropertyProjectionStatusQueryHandlerTests()
    {
        _statusRepository = Substitute.For<ICustomPropertyProjectionStatusRepository>();
        _dirtyScopeRepository = Substitute.For<ICustomPropertyProjectionDirtyScopeRepository>();
        _mapper = Substitute.For<IMapper>();
    }

    [Test]
    public async Task EventStatus_WithDirtyScopeBacklog_ReturnsActionableSignal()
    {
        var tenantId = Guid.NewGuid();
        var status = CreateStatus(
            IEventCustomPropertyProjectionUpdater.ProjectionName,
            IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            tenantId,
            CustomPropertyProjectionState.Idle);
        var dto = CreateDto(status);

        _statusRepository
            .GetAsync(status.ProjectionName, status.ProjectionVersion, tenantId, Arg.Any<CancellationToken>())
            .Returns(status);
        _dirtyScopeRepository
            .CountPendingAsync(status.ProjectionName, status.ProjectionVersion, tenantId, Arg.Any<CancellationToken>())
            .Returns(7);
        _mapper.Map<ProjectionStatusDto>(status).Returns(dto);

        var handler = new GetEventCustomPropertyProjectionStatusQueryHandler(
            _statusRepository,
            _dirtyScopeRepository,
            _mapper);

        var result = await handler.Handle(
            new GetEventCustomPropertyProjectionStatusQuery { TenantId = tenantId },
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsNotNull();
        await Assert.That(result.Id!.Count).IsEqualTo(1);
        await Assert.That(result.Id![0].PendingDirtyScopeCount).IsEqualTo(7);
        await Assert.That(result.Id![0].RequiresOperatorAction).IsTrue();
        await Assert.That(result.Id![0].OperationalState).IsEqualTo("dirty_backlog_pending");
        await Assert.That(result.Id![0].RecommendedAction).Contains("Drain dirty scopes");
    }

    [Test]
    public async Task SessionStatus_WithStaleRebuild_ReturnsLockInvestigationSignal()
    {
        var tenantId = Guid.NewGuid();
        var status = CreateStatus(
            IEventSessionCustomPropertyProjectionUpdater.ProjectionName,
            IEventSessionCustomPropertyProjectionUpdater.ProjectionVersion,
            tenantId,
            CustomPropertyProjectionState.Rebuilding);
        status.LastRebuildStartedAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var dto = CreateDto(status);

        _statusRepository
            .GetAsync(status.ProjectionName, status.ProjectionVersion, tenantId, Arg.Any<CancellationToken>())
            .Returns(status);
        _dirtyScopeRepository
            .CountPendingAsync(status.ProjectionName, status.ProjectionVersion, tenantId, Arg.Any<CancellationToken>())
            .Returns(0);
        _mapper.Map<ProjectionStatusDto>(status).Returns(dto);

        var handler = new GetEventSessionCustomPropertyProjectionStatusQueryHandler(
            _statusRepository,
            _dirtyScopeRepository,
            _mapper);

        var result = await handler.Handle(
            new GetEventSessionCustomPropertyProjectionStatusQuery { TenantId = tenantId },
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsNotNull();
        await Assert.That(result.Id!.Count).IsEqualTo(1);
        await Assert.That(result.Id![0].PendingDirtyScopeCount).IsEqualTo(0);
        await Assert.That(result.Id![0].RequiresOperatorAction).IsTrue();
        await Assert.That(result.Id![0].OperationalState).IsEqualTo("rebuild_stale");
        await Assert.That(result.Id![0].RecommendedAction).Contains("advisory-lock waits");
    }

    private static CustomPropertyProjectionStatus CreateStatus(
        string projectionName,
        int projectionVersion,
        Guid tenantId,
        CustomPropertyProjectionState state)
    {
        return new CustomPropertyProjectionStatus
        {
            ProjectionName = projectionName,
            ProjectionVersion = projectionVersion,
            TenantId = tenantId,
            State = state,
            LastRebuildStartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            LastRebuildCompletedAt = DateTimeOffset.UtcNow,
            RowsProcessed = 10,
            RowsFailed = 0,
            ConcurrencyStamp = Guid.NewGuid()
        };
    }

    private static ProjectionStatusDto CreateDto(CustomPropertyProjectionStatus status)
    {
        return new ProjectionStatusDto
        {
            ProjectionName = status.ProjectionName,
            ProjectionVersion = status.ProjectionVersion,
            TenantId = status.TenantId,
            State = status.State,
            LastRebuildStartedAt = status.LastRebuildStartedAt,
            LastRebuildCompletedAt = status.LastRebuildCompletedAt,
            RowsProcessed = status.RowsProcessed,
            RowsFailed = status.RowsFailed
        };
    }
}
