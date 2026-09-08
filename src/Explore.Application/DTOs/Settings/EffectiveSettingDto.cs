namespace Explore.Application.DTOs.Settings;

using Explore.Application.Contracts.Infrastructure;

/// <summary>
/// A fully resolved setting value with metadata for client-side rendering.
/// Includes source provenance, lock state, and editability for the requesting scope.
/// </summary>
public sealed record EffectiveSettingDto
{
    public required string Key { get; init; }
    public required string Value { get; init; }
    public int SettingValueTypeId { get; init; }
    public required string SettingValueTypeCode { get; init; }
    public required string SettingValueTypeName { get; init; }
    public SettingSource Source { get; init; }
    public bool IsLocked { get; init; }
    public bool IsLockable { get; init; }
    public bool CanEdit { get; init; }
    public string? Reason { get; init; }
    public string? Description { get; init; }
    public string? AllowedValues { get; init; }
}
