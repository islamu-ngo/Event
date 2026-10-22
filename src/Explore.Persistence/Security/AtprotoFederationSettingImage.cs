using Explore.Domain;

namespace Explore.Persistence.Security;

/// <summary>
/// Immutable complete row image for independent source and persisted-postimage
/// comparisons. An image is data only; possessing or constructing one never
/// issues an acquired source or authorizes a write.
/// </summary>
internal sealed record AtprotoFederationSettingImage(
    Guid Id,
    Guid? TenantId,
    string Key,
    string Value,
    bool IsLocked,
    DateTime CreatedAt,
    Guid? CreatedBy,
    DateTime? UpdatedAt,
    Guid? UpdatedBy)
{
    internal static AtprotoFederationSettingImage Capture(AtprotoFederationInstanceSetting row) =>
        new(row.Id, null, row.SettingKey, row.Value, row.IsLocked,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);

    internal static AtprotoFederationSettingImage Capture(AtprotoFederationTenantSetting row) =>
        new(row.Id, row.TenantId, row.SettingKey, row.Value, row.IsLocked,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);

    /// <summary>
    /// Predicts the audit-bearing value update from the original source rather
    /// than learning the expected result from the database after the write.
    /// Updating a value preserves the target's own lock, even when already locked.
    /// </summary>
    internal AtprotoFederationSettingImage WithValue(string value, DateTime occurredAt, Guid actorId) =>
        this with { Value = value, UpdatedAt = occurredAt, UpdatedBy = actorId };

    internal AtprotoFederationSettingImage WithLock(bool isLocked, DateTime occurredAt, Guid actorId) =>
        this with { IsLocked = isLocked, UpdatedAt = occurredAt, UpdatedBy = actorId };
}
