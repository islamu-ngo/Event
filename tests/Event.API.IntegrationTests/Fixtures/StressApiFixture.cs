// Used for timing-sensitive scenarios: rate limiting enforcement, timeout handling, auth conflicts.

using Explore.Application.Features.Events.OpenGraph;
using Explore.Application.Features.Events.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Stress test fixture: PostgreSQL-backed with rate limiting enabled.
/// For timing-sensitive scenarios requiring real middleware enforcement.
/// </summary>
public class StressApiFixture : PostgreSqlApiFixtureBase
{
    private readonly GatedOpenGraphImageRenderer _openGraphRenderer = new();

    protected override Dictionary<string, string?> GetAdditionalConfiguration() => new()
    {
        ["Testing:HostProfile"] = TestHostProfile.Stress,
        ["RateLimiting:DisableInTesting"] = "false",
        // Low thresholds to trigger 429 in tests without excessive request volume.
        // Global policy exempts loopback IPs, so only Authenticated and Write are testable.
        ["RateLimiting:Write:PermitLimit"] = "3",
        ["RateLimiting:Write:WindowSeconds"] = "60",
        ["RateLimiting:Authenticated:PermitLimit"] = "5",
        ["RateLimiting:Authenticated:WindowSeconds"] = "60",
        ["RateLimiting:SetupSecret:PermitLimit"] = "2",
        ["RateLimiting:SetupSecret:WindowSeconds"] = "60",
        ["RateLimiting:EventOpenGraphImage:ConcurrencyLimit"] = "1",
    };

    protected override void ConfigureAdditionalTestServices(IServiceCollection services)
    {
        _openGraphRenderer.Reset();
        services.RemoveAll<IEventOpenGraphImageRenderer>();
        services.AddSingleton<IEventOpenGraphImageRenderer>(_openGraphRenderer);
    }

    public Task WaitForFirstOpenGraphRenderAsync()
        => _openGraphRenderer.FirstRenderStarted.Task;

    public void ReleaseFirstOpenGraphRender()
        => _openGraphRenderer.ReleaseFirstRender();

    private sealed class GatedOpenGraphImageRenderer : IEventOpenGraphImageRenderer
    {
        private static readonly EventOpenGraphImageRenderResult Result =
            new(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "\"stress\"");

        private TaskCompletionSource<bool> _firstRenderStarted = CreateSource();
        private TaskCompletionSource<bool> _releaseFirstRender = CreateSource();
        private int _renderCount;

        public TaskCompletionSource<bool> FirstRenderStarted => _firstRenderStarted;

        public void Reset()
        {
            _firstRenderStarted = CreateSource();
            _releaseFirstRender = CreateSource();
            _renderCount = 0;
        }

        public void ReleaseFirstRender()
            => _releaseFirstRender.TrySetResult(true);

        public async Task<EventOpenGraphImageRenderResult> RenderAsync(
            EventOpenGraphImageRenderRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            if (Interlocked.Increment(ref _renderCount) == 1)
            {
                _firstRenderStarted.TrySetResult(true);
                await _releaseFirstRender.Task.WaitAsync(cancellationToken);
            }

            return Result;
        }

        private static TaskCompletionSource<bool> CreateSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
