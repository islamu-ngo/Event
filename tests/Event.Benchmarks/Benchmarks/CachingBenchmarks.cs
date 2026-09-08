using System.Collections.Concurrent;
using System.Collections.Frozen;
using BenchmarkDotNet.Attributes;
using Event.Benchmarks.Configuration;

namespace Event.Benchmarks.Benchmarks;

[Config(typeof(ExploreBenchmarkConfig))]
public class CachingBenchmarks
{
    private FrozenDictionary<int, string> _frozenDictionary = null!;
    private Dictionary<int, string> _dictionary = null!;
    private ConcurrentDictionary<int, string> _concurrentDictionary = null!;
    private int _lookupKey;

    [Params(100, 1000, 10000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _dictionary = new Dictionary<int, string>(Size);
        _concurrentDictionary = new ConcurrentDictionary<int, string>();

        for (var i = 0; i < Size; i++)
        {
            var value = $"value-{i}";
            _dictionary[i] = value;
            _concurrentDictionary[i] = value;
        }

        _frozenDictionary = _dictionary.ToFrozenDictionary();
        _lookupKey = Size / 2;
    }

    [Benchmark(Baseline = true)]
    public bool FrozenDictionary_Lookup()
    {
        return _frozenDictionary.TryGetValue(_lookupKey, out _);
    }

    [Benchmark]
    public bool Dictionary_Lookup()
    {
        return _dictionary.TryGetValue(_lookupKey, out _);
    }

    [Benchmark]
    public bool ConcurrentDictionary_Lookup()
    {
        return _concurrentDictionary.TryGetValue(_lookupKey, out _);
    }

    [Benchmark]
    public int FrozenDictionary_Enumerate()
    {
        var length = 0;
        foreach (var pair in _frozenDictionary)
        {
            length += pair.Value.Length;
        }

        return length;
    }

    [Benchmark]
    public int Dictionary_Enumerate()
    {
        var length = 0;
        foreach (var pair in _dictionary)
        {
            length += pair.Value.Length;
        }

        return length;
    }
}
