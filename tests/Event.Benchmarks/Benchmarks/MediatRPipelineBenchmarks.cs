using BenchmarkDotNet.Attributes;
using Event.Benchmarks.Configuration;
using Explore.Application.Behaviors;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Benchmarks.Benchmarks;

[Config(typeof(ExploreBenchmarkConfig))]
public class MediatRPipelineBenchmarks
{
    private PerformanceBehavior<MockRequest, int> _performanceBehavior = null!;
    private MockRequest _request = null!;
    private RequestHandlerDelegate<int> _next = null!;
    private Func<MockRequest, Task<int>> _directHandler = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _performanceBehavior = new PerformanceBehavior<MockRequest, int>(
            NullLogger<PerformanceBehavior<MockRequest, int>>.Instance);

        _request = new MockRequest { Value = 42 };
        _next = static _ => Task.FromResult(42);
        _directHandler = static request => Task.FromResult(request.Value);
    }

    [Benchmark]
    public Task<int> PerformanceBehavior_Overhead()
    {
        return _performanceBehavior.Handle(_request, _next, CancellationToken.None);
    }

    [Benchmark(Baseline = true)]
    public Task<int> DirectHandler_NoOverhead()
    {
        return _directHandler(_request);
    }

    public sealed class MockRequest : IRequest<int>
    {
        public int Value { get; set; }
    }
}
