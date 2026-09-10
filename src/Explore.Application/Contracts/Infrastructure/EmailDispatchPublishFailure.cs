
namespace Explore.Application.Contracts.Infrastructure;

public enum EmailDispatchPublishFailure
{
    Unknown = 0,
    MandatoryReturn = 1,
    PublisherNack = 2,
    PublishTimeout = 3,
    BrokerPublishFailed = 4,
    PointerPublishException = 5
}

public static class EmailDispatchPublishFailureCodes
{
    public static string ToCode(this EmailDispatchPublishFailure failure) => failure switch
    {
        EmailDispatchPublishFailure.MandatoryReturn => "mandatory_return",
        EmailDispatchPublishFailure.PublisherNack => "publisher_nack",
        EmailDispatchPublishFailure.PublishTimeout => "publish_timeout",
        EmailDispatchPublishFailure.BrokerPublishFailed => "broker_publish_failed",
        EmailDispatchPublishFailure.PointerPublishException => "pointer_publish_exception",
        _ => "unknown"
    };
}
