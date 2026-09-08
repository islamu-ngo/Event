using System.Collections.Frozen;
using BenchmarkDotNet.Attributes;
using Event.Benchmarks.Configuration;

namespace Event.Benchmarks.Benchmarks;

[Config(typeof(ExploreBenchmarkConfig))]
public class CollectionBenchmarks
{
    private int[] _array = [];
    private List<int> _list = [];
    private FrozenSet<int> _frozenSet = null!;
    private int _lookupValue;

    [Params(100, 1000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _array = Enumerable.Range(0, Size).ToArray();
        _list = _array.ToList();
        _frozenSet = _array.ToFrozenSet();
        _lookupValue = Size / 2;
    }

    [Benchmark(Baseline = true)]
    public int List_ForEach_Loop()
    {
        var sum = 0;
        for (var i = 0; i < _list.Count; i++)
        {
            sum += _list[i];
        }

        return sum;
    }

    [Benchmark]
    public int Span_ForEach_Loop()
    {
        var sum = 0;
        var span = _array.AsSpan();

        for (var i = 0; i < span.Length; i++)
        {
            sum += span[i];
        }

        return sum;
    }

    [Benchmark]
    public List<int> LinqWhere_ToList()
    {
        return _list.Where(v => (v & 1) == 0).ToList();
    }

    [Benchmark]
    public List<int> ManualLoop_Filter_ToList()
    {
        var result = new List<int>(_list.Count / 2);
        for (var i = 0; i < _list.Count; i++)
        {
            var value = _list[i];
            if ((value & 1) == 0)
            {
                result.Add(value);
            }
        }

        return result;
    }

    [Benchmark]
    public List<int> LinqSelect_ToList()
    {
        return _list.Select(v => v * 2).ToList();
    }

    [Benchmark]
    public List<int> ManualLoop_Project_ToList()
    {
        var result = new List<int>(_list.Count);
        for (var i = 0; i < _list.Count; i++)
        {
            result.Add(_list[i] * 2);
        }

        return result;
    }

    [Benchmark]
    public int Array_IndexLookup()
    {
        return _array[_lookupValue];
    }

    [Benchmark]
    public int List_IndexLookup()
    {
        return _list[_lookupValue];
    }

    [Benchmark]
    public int Span_IndexLookup()
    {
        return _array.AsSpan()[_lookupValue];
    }

    [Benchmark]
    public bool FrozenSet_Contains()
    {
        return _frozenSet.Contains(_lookupValue);
    }
}
