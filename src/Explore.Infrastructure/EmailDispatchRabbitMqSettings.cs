namespace Explore.Infrastructure;

public sealed class EmailDispatchRabbitMqSettings
{
    public const string SectionName = "EmailDispatchRabbitMq";

    public bool Enabled { get; set; }

    public string ConnectionStringName { get; set; } = "messaging";

    public string? ConnectionString { get; set; }

    public string ExchangeName { get; set; } = "explore.email-dispatch";

    public string DispatchQueueName { get; set; } = "explore.email-dispatch.dispatch";

    public string DispatchRoutingKey { get; set; } = "email-dispatch.dispatch";

    public string DeadLetterExchangeName { get; set; } = "explore.email-dispatch.dlx";

    public string DeadLetterQueueName { get; set; } = "explore.email-dispatch.dead-letter";

    public string DeadLetterRoutingKey { get; set; } = "email-dispatch.dead-letter";

    public string ParkingQueueName { get; set; } = "explore.email-dispatch.parking";

    public string ParkingRoutingKey { get; set; } = "email-dispatch.parking";

    public string ClientProvidedName { get; set; } = "explore-email-dispatch-rabbitmq";

    public string ConsumerId { get; set; } = "explore-email-dispatch-rabbitmq-consumer";

    public bool DeadLetterReplayEnabled { get; set; }

    public string DeadLetterReplayConsumerId { get; set; } = "explore-email-dispatch-dlq-replay";

    public ushort PrefetchCount { get; set; } = 10;

    public ushort DeadLetterReplayPrefetchCount { get; set; } = 5;

    public int PublishTimeoutSeconds { get; set; } = 15;

    public int PublisherPollingIntervalSeconds { get; set; } = 5;

    public int PublisherBatchSize { get; set; } = 100;

    public int PublisherRetryDelaySeconds { get; set; } = 30;
}
