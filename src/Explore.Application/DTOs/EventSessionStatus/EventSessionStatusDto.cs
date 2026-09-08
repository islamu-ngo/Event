namespace Explore.Application.DTOs.EventSessionStatus;

public sealed record EventSessionStatusDto
{
    public int Id { get; init; }

    /// <summary>Stable code used for i18n via Tolgee; never localized.</summary>
    public required string MasterCode { get; init; }

    /// <summary>Fallback display name used when no localization is available.</summary>
    public required string FullName { get; init; }

    public string? Description { get; init; }
}
