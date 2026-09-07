namespace Explore.Application.DTOs.Settings;

public sealed record UpdateSettingValueDto
{
    public required string Value { get; init; }
}
