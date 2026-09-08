using Explore.Infrastructure.Tests.Fixtures;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.RabbitMQ)]
[Category(InfrastructureTestCategories.Runtime)]
[Explicit]
[ClassDataSource<RabbitMqContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel("RabbitMqBroker")]
public sealed class RabbitMqContainerFixtureTests(RabbitMqContainerFixture rabbitMq)
{
    [Test]
    [Timeout(180_000)]
    public async Task InitializeAsync_WithRabbitMqContainer_ExposesAmqpAndManagementDiagnostics()
    {
        await Assert.That(rabbitMq.AmqpConnectionString).StartsWith("amqp://");
        await Assert.That(rabbitMq.Host).IsNotEmpty();
        await Assert.That(rabbitMq.AmqpPort).IsGreaterThan(0);
        await Assert.That(rabbitMq.ManagementBaseUrl).StartsWith("http://");

        var overview = await rabbitMq.GetOverviewAsync();

        await Assert.That(overview.RabbitMqVersion).IsNotEmpty();
        await Assert.That(overview.ObjectTotals.Exchanges).IsGreaterThanOrEqualTo(0);
        await Assert.That(overview.ObjectTotals.Queues).IsGreaterThanOrEqualTo(0);
        await Assert.That(overview.QueueTotals.Messages).IsGreaterThanOrEqualTo(0);
    }
}
