namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Forward-only cursor over CLI arguments that eliminates index mutation inside loops.
/// </summary>
internal ref struct CliArgumentReader
{
    private readonly ReadOnlySpan<string> args;
    private int position;

    public CliArgumentReader(ReadOnlySpan<string> args, int startIndex = 1)
    {
        this.args = args;
        position = startIndex;
    }

    public bool TryReadNext(out string token)
    {
        if (position < args.Length)
        {
            token = args[position++];
            return true;
        }

        token = string.Empty;
        return false;
    }

    public bool TryReadOptionValue(string? existingValue, out string value)
    {
        if (existingValue is null && position < args.Length)
        {
            value = args[position++];
            return true;
        }

        value = string.Empty;
        return false;
    }
}
