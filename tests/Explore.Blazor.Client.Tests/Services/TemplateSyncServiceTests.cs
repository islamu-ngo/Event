using Explore.Blazor.Client.Services.EventSessionTemplateSync;
using Explore.Blazor.Client.Services.EventTemplateSync;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class TemplateSyncServiceTests
{
    private readonly IEventTemplateSyncClient _api = Substitute.For<IEventTemplateSyncClient>();
    private readonly IEventSessionTemplateSyncClient _sessionClient = Substitute.For<IEventSessionTemplateSyncClient>();

    [Test]
    public async Task EventTemplateSyncService_GetDiffAsync_ForwardsEventAndVersion()
    {
        var eventId = Guid.NewGuid();
        var expected = CreateDiff(7);
        _api.GetEventTemplateSyncDiffAsync(
                eventId,
                7,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = new EventTemplateSyncService(_api);

        var result = await service.GetDiffAsync(eventId, 7);

        await Assert.That(result).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task EventTemplateSyncService_ApplySyncAsync_ForwardsGeneratedRequest()
    {
        var eventId = Guid.NewGuid();
        var request = new EventTemplateSyncApplyRequest
        {
            Plan = new TemplateSyncPlanDto { TargetTemplateVersion = 8 },
            BaseProvenanceVersion = 7
        };
        var expected = CreateOutcome(8);
        _api.ApplyEventTemplateSyncAsync(
                eventId,
                request,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = new EventTemplateSyncService(_api);

        var result = await service.ApplySyncAsync(eventId, request);

        await Assert.That(result).IsSameReferenceAs(expected);
        await _api.Received(1).ApplyEventTemplateSyncAsync(
            eventId,
            request,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EventTemplateSyncService_GetHistoryAsync_ForwardsPaging()
    {
        var eventId = Guid.NewGuid();
        var expected = new PaginatedResultOfEventTemplateSyncHistoryItemDto
        {
            PageNumber = 2,
            PageSize = 5,
            TotalCount = 0,
            Items = []
        };
        _api.GetEventTemplateSyncHistoryAsync(
                eventId,
                2,
                5,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = new EventTemplateSyncService(_api);

        var result = await service.GetHistoryAsync(eventId, 2, 5);

        await Assert.That(result).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task EventSessionTemplateSyncService_GetDiffAsync_ForwardsSessionAndVersion()
    {
        var sessionId = Guid.NewGuid();
        var expected = CreateDiff(9);
        _sessionClient.GetEventSessionTemplateSyncDiffAsync(
                sessionId,
                9,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = new EventSessionTemplateSyncService(_sessionClient);

        var result = await service.GetDiffAsync(sessionId, 9);

        await Assert.That(result).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task EventSessionTemplateSyncService_ApplySyncAsync_ForwardsGeneratedRequest()
    {
        var sessionId = Guid.NewGuid();
        var request = new EventSessionTemplateSyncApplyRequest
        {
            Plan = new TemplateSyncPlanDto { TargetTemplateVersion = 9 },
            BaseProvenanceVersion = 8
        };
        var expected = CreateOutcome(9);
        _sessionClient.ApplyEventSessionTemplateSyncAsync(
                sessionId,
                request,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = new EventSessionTemplateSyncService(_sessionClient);

        var result = await service.ApplySyncAsync(sessionId, request);

        await Assert.That(result).IsSameReferenceAs(expected);
        await _sessionClient.Received(1).ApplyEventSessionTemplateSyncAsync(
            sessionId,
            request,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EventSessionTemplateSyncService_GetHistoryAsync_ForwardsPaging()
    {
        var sessionId = Guid.NewGuid();
        var expected = new PaginatedResultOfEventSessionTemplateSyncHistoryItemDto
        {
            PageNumber = 2,
            PageSize = 5,
            TotalCount = 0,
            Items = []
        };
        _sessionClient.GetEventSessionTemplateSyncHistoryAsync(
                sessionId,
                2,
                5,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = new EventSessionTemplateSyncService(_sessionClient);

        var result = await service.GetHistoryAsync(sessionId, 2, 5);

        await Assert.That(result).IsSameReferenceAs(expected);
    }

    private static HalResourceOfTemplateDiffDto CreateDiff(int targetVersion) => new()
    {
        TargetTemplateVersion = targetVersion,
        BaseProvenanceVersion = targetVersion - 1
    };

    private static TemplateSyncOutcomeDto CreateOutcome(int version) => new()
    {
        Applied = [],
        Skipped = [],
        Conflicts = [],
        NewProvenanceVersion = version,
        SyncedAt = TestTime.UtcNow
    };
}
