using System.Text;
using BenchmarkDotNet.Attributes;
using Event.Benchmarks.Configuration;

namespace Event.Benchmarks.Benchmarks;

[Config(typeof(ExploreBenchmarkConfig))]
public class StringProcessingBenchmarks
{
    private string _text = string.Empty;
    private string _needle = string.Empty;
    private Guid _guid;

    [Params(10, 100, 1000)]
    public int Length { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        const string seed = "abcdefghijklmnopqrstuvwxyz0123456789";
        var builder = new StringBuilder(Length + seed.Length);

        while (builder.Length < Length + seed.Length)
        {
            builder.Append(seed);
        }

        _text = builder.ToString();
        _needle = "xyz";
        _guid = Guid.NewGuid();
    }

    [Benchmark(Baseline = true)]
    public string String_Substring()
    {
        var segmentLength = Math.Min(20, _text.Length - 5);
        return _text.Substring(5, segmentLength);
    }

    [Benchmark]
    public string Span_Slice()
    {
        var segmentLength = Math.Min(20, _text.Length - 5);
        return _text.AsSpan(5, segmentLength).ToString();
    }

    [Benchmark]
    public string String_Concatenation()
    {
        var result = string.Empty;
        for (var i = 0; i < Length; i++)
        {
            result += "x";
        }

        return result;
    }

    [Benchmark]
    public string StringBuilder_Append()
    {
        var builder = new StringBuilder(Length);
        for (var i = 0; i < Length; i++)
        {
            builder.Append('x');
        }

        return builder.ToString();
    }

    [Benchmark]
    public string String_Interpolation()
    {
        var result = string.Empty;
        for (var i = 0; i < Length; i++)
        {
            result = $"{result}x";
        }

        return result;
    }

    [Benchmark]
    public bool String_Contains()
    {
        return _text.Contains(_needle, StringComparison.Ordinal);
    }

    [Benchmark]
    public bool Span_Contains()
    {
        return _text.AsSpan().Contains(_needle.AsSpan(), StringComparison.Ordinal);
    }

    [Benchmark]
    public string Guid_ToString()
    {
        return _guid.ToString();
    }

    [Benchmark]
    public int Guid_TryFormat_Span()
    {
        Span<char> buffer = stackalloc char[36];
        _guid.TryFormat(buffer, out var charsWritten);
        return charsWritten;
    }
}
