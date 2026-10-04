namespace Explore.Domain;

/// <summary>A bounded mutation acknowledgement, never proof of provider absence.</summary>
public enum StorageRetirementAdmission
{
    NotFound,
    InUse,
    RetentionBlocked,
    InvalidTarget,
    Pending
}
