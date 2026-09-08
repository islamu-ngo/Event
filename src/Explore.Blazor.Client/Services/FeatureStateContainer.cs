namespace Explore.Blazor.Client.Services;

public class FeatureStateContainer
{
    private Dictionary<string, bool> _flags = new();

    public bool IsEnabled(string flagKey) =>
        _flags.TryGetValue(flagKey, out var enabled) && enabled;

    public void SetFlags(Dictionary<string, bool> flags) =>
        _flags = flags ?? new();

    public IReadOnlyDictionary<string, bool> All => _flags;
}
