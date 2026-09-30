namespace ISLAMU.ReleaseEngineering;

internal static class ReleaseChangePresentation
{
    public static int Category(ReleaseContextChange change) => change.Breaking ? 0 : change.Type switch
    {
        "feat" => 1,
        "fix" => 2,
        "perf" => 3,
        _ => 4,
    };

    public static string Heading(int category) => category switch
    {
        0 => "Breaking Changes",
        1 => "Features",
        2 => "Bug Fixes",
        3 => "Performance",
        4 => "Other Improvements",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };
}
