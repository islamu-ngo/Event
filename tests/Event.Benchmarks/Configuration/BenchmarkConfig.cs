using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Validators;

namespace Event.Benchmarks.Configuration;

public sealed class ExploreBenchmarkConfig : ManualConfig
{
    public ExploreBenchmarkConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddDiagnoser(ThreadingDiagnoser.Default);
        AddDiagnoser(ExceptionDiagnoser.Default);

        AddExporter(MarkdownExporter.GitHub);
        AddExporter(HtmlExporter.Default);
        AddExporter(JsonExporter.Full);

        AddValidator(ExecutionValidator.FailOnError);
        AddValidator(JitOptimizationsValidator.FailOnError);

        AddJob(
            Job.Default
                .WithRuntime(CoreRuntime.Core10_0)
                .WithMaxRelativeError(0.01));
    }
}
