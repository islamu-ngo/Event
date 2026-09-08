using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Explore.Infrastructure.Localization.Resilience;

internal static class TmsResiliencePipelineConfigurator
{
    internal static void Configure(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        Func<Polly.Retry.RetryDelayGeneratorArguments<HttpResponseMessage>, ValueTask<TimeSpan?>>? delayGenerator)
    {
        builder
            .AddTimeout(TimeSpan.FromSeconds(10))
            .AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                UseJitter = true,
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is HttpRequestException ||
                    (args.Outcome.Result is
                    {
                        StatusCode: HttpStatusCode.TooManyRequests
                            or HttpStatusCode.InternalServerError
                            or HttpStatusCode.BadGateway
                            or HttpStatusCode.ServiceUnavailable
                    })),
                DelayGenerator = delayGenerator
            })
            .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            .AddTimeout(TimeSpan.FromSeconds(30));
    }
}
