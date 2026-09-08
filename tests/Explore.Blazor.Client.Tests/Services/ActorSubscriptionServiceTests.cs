using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class ActorSubscriptionServiceTests
{
    private readonly IActorSubscriptionClient _apiClient;
    private readonly ActorSubscriptionService _service;

    public ActorSubscriptionServiceTests()
    {
        _apiClient = Substitute.For<IActorSubscriptionClient>();
        var logger = Substitute.For<ILogger<ActorSubscriptionService>>();
        _service = new ActorSubscriptionService(_apiClient, logger);
    }

    [Test]
    public async Task GetSubscriptionAsync_WhenApiReturnsHalResource_MapsSubscriptionDto()
    {
        var targetActorId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var response = new HalResourceOfActorSubscriptionDto
        {
            Id = subscriptionId,
            TargetActorId = targetActorId,
            StatusCode = "ACTIVE"
        };

        _apiClient.GetActorSubscriptionByActorAsync(targetActorId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _service.GetSubscriptionAsync(targetActorId);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(subscriptionId);
        await Assert.That(result.TargetActorId).IsEqualTo(targetActorId);
        await Assert.That(result.StatusCode).IsEqualTo("ACTIVE");
    }

    [Test]
    public async Task GetSubscriptionAsync_WhenApiReturnsNotFound_ReturnsNull()
    {
        _apiClient.GetActorSubscriptionByActorAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Not Found", 404, null, null, null));

        var result = await _service.GetSubscriptionAsync(Guid.NewGuid());

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task SubscribeAsync_WhenApiSucceeds_ReturnsCommandResult()
    {
        var targetActorId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        _apiClient.SubscribeToActorAsync(Arg.Any<SubscribeToActorDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = subscriptionId, Message = "Subscribed" });

        var result = await _service.SubscribeAsync(targetActorId);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.SubscriptionId).IsEqualTo(subscriptionId);
        await _apiClient.Received(1).SubscribeToActorAsync(
            Arg.Is<SubscribeToActorDto>(dto => dto.TargetActorId == targetActorId),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SubscribeAsync_WhenApiFails_ReturnsSafeFailure()
    {
        _apiClient.SubscribeToActorAsync(Arg.Any<SubscribeToActorDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        var result = await _service.SubscribeAsync(Guid.NewGuid());

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Message).Contains("Could not update subscription");
    }

    [Test]
    public async Task UnsubscribeAsync_PassesRouteTargetAndExpectedConcurrencyStamp()
    {
        var targetActorId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        _apiClient.UnsubscribeFromActorAsync(targetActorId, Arg.Any<UnsubscribeFromActorDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = Guid.NewGuid() });

        await _service.UnsubscribeAsync(targetActorId, concurrencyStamp);

        await _apiClient.Received(1).UnsubscribeFromActorAsync(
            targetActorId,
            Arg.Is<UnsubscribeFromActorDto>(dto =>
                dto.TargetActorId == targetActorId && dto.ExpectedConcurrencyStamp == concurrencyStamp),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateNotificationLevelAsync_PassesLevelAndExpectedConcurrencyStamp()
    {
        var targetActorId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        _apiClient.UpdateActorSubscriptionNotificationLevelAsync(targetActorId, Arg.Any<UpdateActorSubscriptionNotificationLevelDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = Guid.NewGuid() });

        await _service.UpdateNotificationLevelAsync(targetActorId, 1, concurrencyStamp);

        await _apiClient.Received(1).UpdateActorSubscriptionNotificationLevelAsync(
            targetActorId,
            Arg.Is<UpdateActorSubscriptionNotificationLevelDto>(dto =>
                dto.NotificationLevel != null &&
                dto.NotificationLevel.Id == 1 &&
                dto.ExpectedConcurrencyStamp == concurrencyStamp),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }
}
