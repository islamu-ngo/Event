using System.Globalization;
using Explore.Domain;

namespace Explore.Infrastructure.Webhooks;

internal static class SvixWebhookOwnershipMetadata
{
    private const string ConsumerIdKey = "islamu.consumer_id";
    private const string OwnerIdKey = "islamu.owner_id";
    private const string OwnerKindIdKey = "islamu.owner_kind_id";
    private const string TenantIdKey = "islamu.tenant_id";

    public static bool Matches(
        IReadOnlyDictionary<string, string> metadata,
        WebhookOwnershipScope ownership,
        Guid consumerId) =>
        HasGuid(metadata, ConsumerIdKey, consumerId) &&
        HasGuid(metadata, OwnerIdKey, ownership.OwnerId) &&
        metadata.TryGetValue(OwnerKindIdKey, out var ownerKindId) &&
        string.Equals(
            ownerKindId,
            ((int)ownership.Kind).ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal) &&
        (ownership.TenantId is { } tenantId
            ? HasGuid(metadata, TenantIdKey, tenantId)
            : !metadata.ContainsKey(TenantIdKey));

    private static bool HasGuid(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        Guid expectedValue) =>
        metadata.TryGetValue(key, out var value) &&
        string.Equals(value, expectedValue.ToString("D"), StringComparison.OrdinalIgnoreCase);
}
