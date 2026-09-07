using Explore.Infrastructure;
using Explore.Infrastructure.Tests.Fixtures;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.RabbitMQ)]
public sealed class EmailDispatchRabbitMqSettingsValidatorTests
{
    private readonly EmailDispatchRabbitMqSettingsValidator _validator = new();

    [Test]
    public async Task ValidateDefaultSettingsReturnsSuccess()
    {
        var result = _validator.Validate(null, new EmailDispatchRabbitMqSettings());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ValidateBlankExchangeNameReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchRabbitMqSettings
        {
            ExchangeName = " "
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(EmailDispatchRabbitMqSettings.ExchangeName));
    }

    [Test]
    public async Task ValidateInvalidPublishTimeoutReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchRabbitMqSettings
        {
            PublishTimeoutSeconds = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(EmailDispatchRabbitMqSettings.PublishTimeoutSeconds));
    }

    [Test]
    public async Task ValidateInvalidPublisherPollingIntervalReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchRabbitMqSettings
        {
            PublisherPollingIntervalSeconds = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(EmailDispatchRabbitMqSettings.PublisherPollingIntervalSeconds));
    }

    [Test]
    public async Task ValidateInvalidPublisherBatchSizeReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchRabbitMqSettings
        {
            PublisherBatchSize = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(EmailDispatchRabbitMqSettings.PublisherBatchSize));
    }

    [Test]
    public async Task ValidateInvalidPublisherRetryDelayReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchRabbitMqSettings
        {
            PublisherRetryDelaySeconds = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(EmailDispatchRabbitMqSettings.PublisherRetryDelaySeconds));
    }

    [Test]
    public async Task ValidateBlankConnectionStringNameReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchRabbitMqSettings
        {
            ConnectionStringName = string.Empty
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(EmailDispatchRabbitMqSettings.ConnectionStringName));
    }

    [Test]
    public async Task ValidateBlankConsumerIdReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchRabbitMqSettings
        {
            ConsumerId = " "
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(EmailDispatchRabbitMqSettings.ConsumerId));
    }

    [Test]
    public async Task ValidateZeroPrefetchCountReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchRabbitMqSettings
        {
            PrefetchCount = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(EmailDispatchRabbitMqSettings.PrefetchCount));
    }

    [Test]
    public async Task ValidateBlankParkingQueueNameReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchRabbitMqSettings
        {
            ParkingQueueName = " "
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(EmailDispatchRabbitMqSettings.ParkingQueueName));
    }

    [Test]
    public async Task ValidateBlankDeadLetterReplayConsumerIdReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchRabbitMqSettings
        {
            DeadLetterReplayConsumerId = string.Empty
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(EmailDispatchRabbitMqSettings.DeadLetterReplayConsumerId));
    }

    [Test]
    public async Task ValidateZeroDeadLetterReplayPrefetchCountReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchRabbitMqSettings
        {
            DeadLetterReplayPrefetchCount = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(EmailDispatchRabbitMqSettings.DeadLetterReplayPrefetchCount));
    }
}
