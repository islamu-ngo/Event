namespace Explore.Blazor.Client.Tests.Common;

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

internal static class TestTime
{
    internal static readonly DateTimeOffset UtcNow =
        new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
}
