namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

/// <summary>
/// Strongly-typed group for all S3/object-storage settings.
/// </summary>
public class StorageSettingGroup : ISettingGroup
{
    public string? Endpoint { get; private set; }
    public string? PublicEndpoint { get; private set; }
    public string? BucketName { get; private set; }
    public string Region { get; private set; } = "fsn1";
    public bool ForcePathStyle { get; private set; } = true;
    public int UploadUrlExpirationMinutes { get; private set; } = 60;

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Storage.Endpoint,
        GovernanceSettingKeys.Storage.PublicEndpoint,
        GovernanceSettingKeys.Storage.BucketName,
        GovernanceSettingKeys.Storage.Region,
        GovernanceSettingKeys.Storage.ForcePathStyle,
        GovernanceSettingKeys.Storage.UploadUrlExpirationMinutes
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Storage.Endpoint, out var ep))
            Endpoint = SettingValueSerializer.DeserializeString(ep.Value);
        if (settings.TryGetValue(GovernanceSettingKeys.Storage.PublicEndpoint, out var pub))
            PublicEndpoint = SettingValueSerializer.DeserializeString(pub.Value);
        if (settings.TryGetValue(GovernanceSettingKeys.Storage.BucketName, out var bucket))
            BucketName = SettingValueSerializer.DeserializeString(bucket.Value);
        if (settings.TryGetValue(GovernanceSettingKeys.Storage.Region, out var region))
            Region = SettingValueSerializer.Deserialize(region.Value, "fsn1");
        if (settings.TryGetValue(GovernanceSettingKeys.Storage.ForcePathStyle, out var fps))
            ForcePathStyle = SettingValueSerializer.Deserialize(fps.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.Storage.UploadUrlExpirationMinutes, out var exp))
            UploadUrlExpirationMinutes = SettingValueSerializer.Deserialize(exp.Value, 60);
    }
}
