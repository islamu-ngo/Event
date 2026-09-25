using System.Collections.Immutable;
using Explore.Domain.Enums;

namespace Explore.Domain.ValueObjects;

public sealed class EventResourceGovernancePolicy : IEquatable<EventResourceGovernancePolicy>
{
    public const long DefaultMaxUploadBytes = 10_485_760;
    public const int DefaultAuditRetentionDays = 30;
    public const int MaximumAuditRetentionDays = 90;
    public const int DefaultMaxActiveResources = 500;
    public const int MaximumActiveResources = 500;

    public const string PdfMediaType = "application/pdf";
    public const string WordDocumentMediaType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string PowerPointPresentationMediaType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    private static readonly ImmutableHashSet<string> SupportedFileTypes =
        ImmutableHashSet.Create(StringComparer.Ordinal, PdfMediaType, WordDocumentMediaType, PowerPointPresentationMediaType);

    private readonly ImmutableSortedSet<EventResourceDeliveryTypeEnum> _enabledDeliveryTypes;
    private readonly ImmutableSortedSet<EventResourceAudienceKindEnum> _enabledAudiences;
    private readonly ImmutableSortedSet<string> _permittedFileTypes;
    private readonly ImmutableSortedSet<string> _externalOrigins;

    private EventResourceGovernancePolicy(
        ImmutableSortedSet<EventResourceDeliveryTypeEnum> enabledDeliveryTypes,
        ImmutableSortedSet<EventResourceAudienceKindEnum> enabledAudiences,
        ImmutableSortedSet<string> permittedFileTypes,
        long maxUploadBytes,
        bool allowUnscannedDocuments,
        ImmutableSortedSet<string> externalOrigins,
        int auditRetentionDays,
        int maxActiveResources)
    {
        _enabledDeliveryTypes = enabledDeliveryTypes;
        _enabledAudiences = enabledAudiences;
        _permittedFileTypes = permittedFileTypes;
        MaxUploadBytes = maxUploadBytes;
        AllowUnscannedDocuments = allowUnscannedDocuments;
        _externalOrigins = externalOrigins;
        AuditRetentionDays = auditRetentionDays;
        MaxActiveResources = maxActiveResources;
    }

    public IReadOnlySet<EventResourceDeliveryTypeEnum> EnabledDeliveryTypes => _enabledDeliveryTypes;
    public IReadOnlySet<EventResourceAudienceKindEnum> EnabledAudiences => _enabledAudiences;
    public IReadOnlySet<string> PermittedFileTypes => _permittedFileTypes;
    public long MaxUploadBytes { get; }
    public bool AllowUnscannedDocuments { get; }
    public IReadOnlySet<string> ExternalOrigins => _externalOrigins;
    public int AuditRetentionDays { get; }
    public int MaxActiveResources { get; }

    public static EventResourceGovernancePolicy Default(long storageMaxUploadBytes) =>
        Create(
            Enum.GetValues<EventResourceDeliveryTypeEnum>(),
            Enum.GetValues<EventResourceAudienceKindEnum>(),
            [PdfMediaType, WordDocumentMediaType, PowerPointPresentationMediaType],
            DefaultMaxUploadBytes,
            allowUnscannedDocuments: false,
            externalOrigins: [],
            DefaultAuditRetentionDays,
            DefaultMaxActiveResources,
            storageMaxUploadBytes);

    public static EventResourceGovernancePolicy Create(
        IEnumerable<EventResourceDeliveryTypeEnum> enabledDeliveryTypes,
        IEnumerable<EventResourceAudienceKindEnum> enabledAudiences,
        IEnumerable<string> permittedFileTypes,
        long maxUploadBytes,
        bool allowUnscannedDocuments,
        IEnumerable<string> externalOrigins,
        int auditRetentionDays,
        int maxActiveResources,
        long storageMaxUploadBytes)
    {
        ArgumentNullException.ThrowIfNull(enabledDeliveryTypes);
        ArgumentNullException.ThrowIfNull(enabledAudiences);
        ArgumentNullException.ThrowIfNull(permittedFileTypes);
        ArgumentNullException.ThrowIfNull(externalOrigins);

        var deliveryTypes = enabledDeliveryTypes.ToImmutableSortedSet();
        if (deliveryTypes.Any(value => !Enum.IsDefined(value)))
            throw new ArgumentOutOfRangeException(nameof(enabledDeliveryTypes), "Delivery types must be implemented closed values.");

        var audiences = enabledAudiences.ToImmutableSortedSet();
        if (audiences.Any(value => !Enum.IsDefined(value)))
            throw new ArgumentOutOfRangeException(nameof(enabledAudiences), "Audience kinds must be implemented closed values.");

        var fileTypes = SnapshotStrings(permittedFileTypes, nameof(permittedFileTypes));
        if (!fileTypes.IsSubsetOf(SupportedFileTypes))
            throw new ArgumentException("File types must be members of the closed v1 MIME set.", nameof(permittedFileTypes));

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxUploadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(storageMaxUploadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(auditRetentionDays);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(auditRetentionDays, MaximumAuditRetentionDays);
        ArgumentOutOfRangeException.ThrowIfNegative(maxActiveResources);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxActiveResources, MaximumActiveResources);

        var origins = SnapshotStrings(externalOrigins, nameof(externalOrigins));
        if (origins.Any(origin => !IsCanonicalHttpsOrigin(origin)))
            throw new ArgumentException("External origins must be canonical HTTPS origin authorities.", nameof(externalOrigins));

        return new(
            deliveryTypes,
            audiences,
            fileTypes,
            Math.Min(maxUploadBytes, storageMaxUploadBytes),
            allowUnscannedDocuments,
            origins,
            auditRetentionDays,
            maxActiveResources);
    }

    public bool IsNonWidening(EventResourceGovernancePolicy proposedTenantPolicy)
    {
        ArgumentNullException.ThrowIfNull(proposedTenantPolicy);

        return (!proposedTenantPolicy.AllowUnscannedDocuments || AllowUnscannedDocuments)
            && proposedTenantPolicy._enabledDeliveryTypes.IsSubsetOf(_enabledDeliveryTypes)
            && proposedTenantPolicy._enabledAudiences.IsSubsetOf(_enabledAudiences)
            && proposedTenantPolicy._permittedFileTypes.IsSubsetOf(_permittedFileTypes)
            && proposedTenantPolicy.MaxUploadBytes <= MaxUploadBytes
            && proposedTenantPolicy._externalOrigins.IsSubsetOf(_externalOrigins)
            && proposedTenantPolicy.AuditRetentionDays <= AuditRetentionDays
            && proposedTenantPolicy.MaxActiveResources <= MaxActiveResources;
    }

    public EventResourceGovernancePolicy Intersect(EventResourceGovernancePolicy tenantPolicy)
    {
        ArgumentNullException.ThrowIfNull(tenantPolicy);

        return new(
            _enabledDeliveryTypes.Intersect(tenantPolicy._enabledDeliveryTypes),
            _enabledAudiences.Intersect(tenantPolicy._enabledAudiences),
            _permittedFileTypes.Intersect(tenantPolicy._permittedFileTypes),
            Math.Min(MaxUploadBytes, tenantPolicy.MaxUploadBytes),
            AllowUnscannedDocuments && tenantPolicy.AllowUnscannedDocuments,
            _externalOrigins.Intersect(tenantPolicy._externalOrigins),
            Math.Min(AuditRetentionDays, tenantPolicy.AuditRetentionDays),
            Math.Min(MaxActiveResources, tenantPolicy.MaxActiveResources));
    }

    public bool AllowsExternalOrigin(string origin) =>
        IsCanonicalHttpsOrigin(origin) && _externalOrigins.Contains(origin);

    public bool Equals(EventResourceGovernancePolicy? other) =>
        other is not null
        && MaxUploadBytes == other.MaxUploadBytes
        && AllowUnscannedDocuments == other.AllowUnscannedDocuments
        && AuditRetentionDays == other.AuditRetentionDays
        && MaxActiveResources == other.MaxActiveResources
        && _enabledDeliveryTypes.SetEquals(other._enabledDeliveryTypes)
        && _enabledAudiences.SetEquals(other._enabledAudiences)
        && _permittedFileTypes.SetEquals(other._permittedFileTypes)
        && _externalOrigins.SetEquals(other._externalOrigins);

    public override bool Equals(object? obj) => Equals(obj as EventResourceGovernancePolicy);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MaxUploadBytes);
        hash.Add(AllowUnscannedDocuments);
        hash.Add(AuditRetentionDays);
        hash.Add(MaxActiveResources);
        AddValues(ref hash, _enabledDeliveryTypes);
        AddValues(ref hash, _enabledAudiences);
        AddValues(ref hash, _permittedFileTypes);
        AddValues(ref hash, _externalOrigins);
        return hash.ToHashCode();
    }

    private static ImmutableSortedSet<string> SnapshotStrings(IEnumerable<string> values, string parameterName)
    {
        var result = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (string? value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Values must be nonempty canonical strings.", parameterName);
            result.Add(value);
        }
        return result.ToImmutable();
    }

    private static bool IsCanonicalHttpsOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)
            || origin.Contains('*', StringComparison.Ordinal)
            || !Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        return string.Equals(origin, uri.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal);
    }

    private static void AddValues<T>(ref HashCode hash, IEnumerable<T> values)
    {
        foreach (T value in values)
            hash.Add(value);
    }
}
