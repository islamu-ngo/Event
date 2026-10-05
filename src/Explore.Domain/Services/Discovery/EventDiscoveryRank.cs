namespace Explore.Domain.Services.Discovery;

/// <summary>Portable discovery order: invariant-uppercase UTF-16 title units, then source kind and source ID.</summary>
public static class EventDiscoveryRank
{
    public const int ContractVersion = 1;
    public const int EventTitleMaximumLength = 200;
    public const int ProjectionNameMaximumLength = 240;
    public const int EventTitleKeyMaximumLength = EventTitleMaximumLength * 4;
    public const int ProjectionTitleKeyMaximumLength = ProjectionNameMaximumLength * 4;
    public const int SourceKeyLength = 32;

    public static string TitleKey(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        string upper = title.ToUpperInvariant();
        return string.Create(checked(upper.Length * 4), upper, static (buffer, value) =>
        {
            const string digits = "0123456789ABCDEF";
            for (int index = 0; index < value.Length; index++)
            {
                char unit = value[index];
                int offset = index * 4;
                buffer[offset] = digits[unit >> 12];
                buffer[offset + 1] = digits[(unit >> 8) & 15];
                buffer[offset + 2] = digits[(unit >> 4) & 15];
                buffer[offset + 3] = digits[unit & 15];
            }
        });
    }

    public static string SourceKey(Guid sourceId) => sourceId.ToString("N");
}
