using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
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
    private static readonly Guid TenantId = Guid.Parse("01920000-0000-7000-8000-000000000005");
    private readonly ICustomPropertyProjectionStatusRepository _statusRepository;
    private readonly ICustomPropertyProjectionDirtyScopeRepository _dirtyScopeRepository;

    public GetCustomPropertyProjectionStatusQueryHandlerTests()
    {
        _statusRepository = Substitute.For<ICustomPropertyProjectionStatusRepository>();
        _dirtyScopeRepository = Substitute.For<ICustomPropertyProjectionDirtyScopeRepository>();
    }

    [Test]
    public async Task EventStatus_WithDirtyScopeBacklog_ReturnsActionableSignal()
    {
        var tenantId = TenantId;
        var status = CreateStatus(
            IEventCustomPropertyProjectionUpdater.ProjectionName,
            IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            tenantId,
            CustomPropertyProjectionState.Idle);

        _statusRepository
            .GetAsync(status.ProjectionName, status.ProjectionVersion, tenantId, Arg.Any<CancellationToken>())
            .Returns(status);
        _dirtyScopeRepository
            .CountPendingAsync(status.ProjectionName, status.ProjectionVersion, tenantId, Arg.Any<CancellationToken>())
            .Returns(7);

        var handler = new GetEventCustomPropertyProjectionStatusQueryHandler(
            _statusRepository,
            _dirtyScopeRepository);

        var result = await handler.QueryAsync(
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
        var tenantId = TenantId;
        var status = CreateStatus(
            IEventSessionCustomPropertyProjectionUpdater.ProjectionName,
            IEventSessionCustomPropertyProjectionUpdater.ProjectionVersion,
            tenantId,
            CustomPropertyProjectionState.Rebuilding);
        status.LastRebuildStartedAt = DateTimeOffset.UnixEpoch;

        _statusRepository
            .GetAsync(status.ProjectionName, status.ProjectionVersion, tenantId, Arg.Any<CancellationToken>())
            .Returns(status);
        _dirtyScopeRepository
            .CountPendingAsync(status.ProjectionName, status.ProjectionVersion, tenantId, Arg.Any<CancellationToken>())
            .Returns(0);

        var handler = new GetEventSessionCustomPropertyProjectionStatusQueryHandler(
            _statusRepository,
            _dirtyScopeRepository);

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
            LastRebuildStartedAt = DateTimeOffset.UnixEpoch,
            LastRebuildCompletedAt = DateTimeOffset.UnixEpoch.AddMinutes(1),
            RowsProcessed = 10,
            RowsFailed = 0,
            ConcurrencyStamp = Guid.Parse("01920000-0000-7000-8000-000000000001")
        };
    }

}
