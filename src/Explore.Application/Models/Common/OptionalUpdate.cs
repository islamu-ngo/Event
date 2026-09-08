namespace Explore.Application.Models.Common;

public readonly record struct OptionalUpdate<T>(bool HasValue, T? Value)
{
    public static OptionalUpdate<T> Unspecified() => default;

    public static OptionalUpdate<T> Set(T? value) => new(true, value);
}
