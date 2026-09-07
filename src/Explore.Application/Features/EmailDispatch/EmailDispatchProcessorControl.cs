namespace Explore.Application.Features.EmailDispatch;

public static class EmailDispatchProcessorControl
{
    public const string SettingKey = "email-dispatch.processor";
    public const int MaximumGlobalRateLimitPerMinute = 100000;
}
