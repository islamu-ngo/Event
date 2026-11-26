namespace Explore.ServiceDefaults;

/// <summary>Identifies manifest-selected, identity-independent public asset endpoints.</summary>
public sealed class PublicStaticAssetMetadata
{
    public static readonly PublicStaticAssetMetadata Instance = new();

    private PublicStaticAssetMetadata() { }
}
