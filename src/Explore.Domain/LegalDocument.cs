namespace Explore.Domain;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Explore.Domain.Interfaces;

public sealed class LegalDocument : IAuditableEntity, IConcurrencyAware
{
    private readonly List<LegalDocumentVersion> _versions = [];
    private readonly List<LegalDocumentPublication> _publications = [];

    private LegalDocument()
    {
    }

    public Guid Id { get; private set; }
    public LegalDocumentScope Scope { get; private set; }
    public Guid? TenantId { get; private set; }
    public string AuthorityKey { get; private set; } = string.Empty;
    public LegalDocumentKind Kind { get; private set; }
    public LegalDocumentOwnerRole OwnerRole { get; private set; }
    public LegalDocumentLifecycleState State { get; private set; }
    public int CurrentVersion { get; private set; }
    public string? AccountableIdentityReference { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public IReadOnlyList<LegalDocumentVersion> Versions => _versions;
    public IReadOnlyList<LegalDocumentPublication> Publications => _publications;

    public static LegalDocument CreateDraft(
        LegalDocumentScope scope,
        Guid? tenantId,
        LegalDocumentKind kind,
        LegalDocumentAudience audience,
        IReadOnlyList<LegalDocumentLocalizedSource> sources,
        LegalDocumentTemplateProvenance? templateProvenance,
        string accountableIdentityReference,
        bool requiresFreshAcceptance,
        DateTime occurredAt)
    {
        string identity = NormalizeIdentityReference(accountableIdentityReference);
        return Create(
            scope,
            tenantId,
            kind,
            audience,
            sources,
            templateProvenance,
            sourceOrigin: null,
            identity,
            requiresFreshAcceptance,
            LegalDocumentLifecycleState.Draft,
            occurredAt);
    }

    public static LegalDocument CreateImportedDraft(
        LegalDocumentScope scope,
        Guid? tenantId,
        LegalDocumentKind kind,
        LegalDocumentAudience audience,
        IReadOnlyList<LegalDocumentLocalizedSource> sources,
        LegalDocumentTemplateProvenance? templateProvenance,
        string sourceOrigin,
        bool requiresFreshAcceptance,
        DateTime occurredAt) =>
        Create(
            scope,
            tenantId,
            kind,
            audience,
            sources,
            templateProvenance,
            NormalizeRequired(sourceOrigin, 200, nameof(sourceOrigin)),
            accountableIdentityReference: null,
            requiresFreshAcceptance,
            LegalDocumentLifecycleState.ReviewRequired,
            occurredAt);

    public void SubmitForReview(DateTime occurredAt)
    {
        EnsureState(LegalDocumentLifecycleState.Draft);
        EnsureUtc(occurredAt, nameof(occurredAt));
        SetState(LegalDocumentLifecycleState.ReviewRequired, occurredAt);
    }

    public void BindAccountableIdentity(
        string accountableIdentityReference,
        DateTime occurredAt)
    {
        if (State is LegalDocumentLifecycleState.Published
            or LegalDocumentLifecycleState.Retired)
        {
            throw new InvalidOperationException(
                "Published legal identity evidence cannot be rewritten.");
        }

        EnsureUtc(occurredAt, nameof(occurredAt));
        AccountableIdentityReference =
            NormalizeIdentityReference(accountableIdentityReference);
        UpdatedAt = occurredAt;
    }

    public void Approve(
        Guid reviewerId,
        string reviewEvidenceReference,
        DateTime occurredAt)
    {
        EnsureState(LegalDocumentLifecycleState.ReviewRequired);
        ArgumentOutOfRangeException.ThrowIfEqual(reviewerId, Guid.Empty);
        EnsureUtc(occurredAt, nameof(occurredAt));
        if (string.IsNullOrWhiteSpace(AccountableIdentityReference))
            throw new InvalidOperationException(
                "Target accountable identity must be bound before approval.");

        LegalDocumentVersion version = Current();
        version.Approve(
            reviewerId,
            NormalizeRequired(
                reviewEvidenceReference,
                200,
                nameof(reviewEvidenceReference)),
            AccountableIdentityReference,
            occurredAt);
        SetState(LegalDocumentLifecycleState.Approved, occurredAt);
    }

    public void Schedule(DateTime effectiveAt, DateTime occurredAt)
    {
        EnsureState(LegalDocumentLifecycleState.Approved);
        EnsureUtc(effectiveAt, nameof(effectiveAt));
        EnsureUtc(occurredAt, nameof(occurredAt));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            effectiveAt,
            occurredAt,
            nameof(effectiveAt));

        Current().Schedule(effectiveAt);
        SetState(LegalDocumentLifecycleState.Scheduled, occurredAt);
    }

    public LegalDocumentPublication Publish(DateTime occurredAt)
    {
        EnsureState(LegalDocumentLifecycleState.Scheduled);
        EnsureUtc(occurredAt, nameof(occurredAt));
        LegalDocumentVersion version = Current();
        if (version.ProposedEffectiveAt is null
            || occurredAt < version.ProposedEffectiveAt)
        {
            throw new InvalidOperationException(
                "Legal document cannot publish before its effective time.");
        }

        if (string.IsNullOrWhiteSpace(AccountableIdentityReference))
            throw new InvalidOperationException(
                "Target accountable identity is required.");

        version.Publish(occurredAt);
        var publication = LegalDocumentPublication.Create(
            this,
            version,
            LegalDocumentLifecycleState.Published,
            AccountableIdentityReference,
            occurredAt);
        _publications.Add(publication);
        SetState(LegalDocumentLifecycleState.Published, occurredAt);
        return publication;
    }

    public LegalDocumentPublication Retire(DateTime occurredAt)
    {
        EnsureState(LegalDocumentLifecycleState.Published);
        EnsureUtc(occurredAt, nameof(occurredAt));
        LegalDocumentVersion version = Current();
        version.Retire(occurredAt);
        var retirement = LegalDocumentPublication.Create(
            this,
            version,
            LegalDocumentLifecycleState.Retired,
            AccountableIdentityReference!,
            occurredAt);
        _publications.Add(retirement);
        SetState(LegalDocumentLifecycleState.Retired, occurredAt);
        return retirement;
    }

    public LegalDocumentVersion CreateRevision(
        LegalDocumentAudience audience,
        IReadOnlyList<LegalDocumentLocalizedSource> sources,
        LegalDocumentTemplateProvenance? templateProvenance,
        bool requiresFreshAcceptance,
        DateTime occurredAt)
    {
        if (State is not (LegalDocumentLifecycleState.Published
            or LegalDocumentLifecycleState.Retired))
        {
            throw new InvalidOperationException(
                "A revision starts only from published or retired state.");
        }

        EnsureUtc(occurredAt, nameof(occurredAt));
        LegalDocumentVersion version = LegalDocumentVersion.Create(
            Id,
            checked(CurrentVersion + 1),
            audience,
            sources,
            templateProvenance,
            sourceOrigin: null,
            requiresFreshAcceptance,
            LegalDocumentLifecycleState.Draft,
            occurredAt);
        _versions.Add(version);
        CurrentVersion = version.Version;
        AccountableIdentityReference = null;
        SetState(LegalDocumentLifecycleState.Draft, occurredAt);
        return version;
    }

    public LegalDocumentVersion CreateImportedRevision(
        LegalDocumentAudience audience,
        IReadOnlyList<LegalDocumentLocalizedSource> sources,
        LegalDocumentTemplateProvenance? templateProvenance,
        string sourceOrigin,
        bool requiresFreshAcceptance,
        DateTime occurredAt)
    {
        if (State is not (LegalDocumentLifecycleState.Published
            or LegalDocumentLifecycleState.Retired))
        {
            throw new InvalidOperationException(
                "An imported revision cannot replace an unpublished legal draft.");
        }

        EnsureUtc(occurredAt, nameof(occurredAt));
        LegalDocumentVersion version = LegalDocumentVersion.Create(
            Id,
            checked(CurrentVersion + 1),
            audience,
            sources,
            templateProvenance,
            NormalizeRequired(sourceOrigin, 200, nameof(sourceOrigin)),
            requiresFreshAcceptance,
            LegalDocumentLifecycleState.ReviewRequired,
            occurredAt);
        _versions.Add(version);
        CurrentVersion = version.Version;
        AccountableIdentityReference = null;
        SetState(LegalDocumentLifecycleState.ReviewRequired, occurredAt);
        return version;
    }

    private static LegalDocument Create(
        LegalDocumentScope scope,
        Guid? tenantId,
        LegalDocumentKind kind,
        LegalDocumentAudience audience,
        IReadOnlyList<LegalDocumentLocalizedSource> sources,
        LegalDocumentTemplateProvenance? templateProvenance,
        string? sourceOrigin,
        string? accountableIdentityReference,
        bool requiresFreshAcceptance,
        LegalDocumentLifecycleState initialState,
        DateTime occurredAt)
    {
        EnsureUtc(occurredAt, nameof(occurredAt));
        ValidateTarget(scope, tenantId, kind);
        var document = new LegalDocument
        {
            Id = Guid.CreateVersion7(),
            Scope = scope,
            TenantId = tenantId,
            AuthorityKey = scope == LegalDocumentScope.Instance
                ? "instance"
                : $"tenant:{tenantId!.Value:N}",
            Kind = kind,
            OwnerRole = LegalDocumentKindCatalog.Get(kind).OwnerRole,
            State = initialState,
            CurrentVersion = 1,
            AccountableIdentityReference = accountableIdentityReference,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = occurredAt
        };
        document._versions.Add(LegalDocumentVersion.Create(
            document.Id,
            1,
            audience,
            sources,
            templateProvenance,
            sourceOrigin,
            requiresFreshAcceptance,
            initialState,
            occurredAt));
        return document;
    }

    private static void ValidateTarget(
        LegalDocumentScope scope,
        Guid? tenantId,
        LegalDocumentKind kind)
    {
        if (!Enum.IsDefined(scope) || !Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(scope));

        bool targetIsInvalid = scope == LegalDocumentScope.Instance
            ? tenantId is not null
            : tenantId is not { } targetTenantId || targetTenantId == Guid.Empty;
        if (targetIsInvalid)
        {
            throw new ArgumentException(
                "Legal document target scope and tenant are inconsistent.",
                nameof(tenantId));
        }

        LegalDocumentKindDescriptor descriptor = LegalDocumentKindCatalog.Get(kind);
        if (descriptor.Scope != scope)
            throw new ArgumentException(
                "Legal document kind is not owned by the target scope.",
                nameof(kind));
    }

    private void SetState(
        LegalDocumentLifecycleState state,
        DateTime occurredAt)
    {
        State = state;
        Current().SetState(state);
        UpdatedAt = occurredAt;
    }

    private LegalDocumentVersion Current() =>
        _versions.Single(version => version.Version == CurrentVersion);

    private void EnsureState(LegalDocumentLifecycleState expected)
    {
        if (State != expected)
            throw new InvalidOperationException(
                $"Legal document transition requires {expected} state.");
    }

    private static string NormalizeIdentityReference(string value) =>
        NormalizeRequired(value, 200, nameof(value));

    private static string NormalizeRequired(
        string value,
        int maximumLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC time is required.", parameterName);
    }
}

public sealed class LegalDocumentVersion
{
    private readonly List<LegalDocumentLocalizedSource> _sources = [];

    private LegalDocumentVersion()
    {
    }

    public Guid Id { get; private set; }
    public Guid LegalDocumentId { get; private set; }
    public LegalDocument? LegalDocument { get; private set; }
    public int Version { get; private set; }
    public LegalDocumentAudience Audience { get; private set; }
    public LegalDocumentLifecycleState State { get; private set; }
    public string ContentDigest { get; private set; } = string.Empty;
    public string? SourceOrigin { get; private set; }
    public bool IsImported => SourceOrigin is not null;
    public bool RequiresFreshAcceptance { get; private set; }
    public string? TemplateId { get; private set; }
    public string? TemplateVersion { get; private set; }
    public LegalDocumentTemplateSourceKind? TemplateSourceKind { get; private set; }
    public string? TemplateLicenseExpression { get; private set; }
    public string? TemplateReviewReference { get; private set; }
    public Guid? ReviewerId { get; private set; }
    public string? ReviewEvidenceReference { get; private set; }
    public string? AccountableIdentityReference { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public DateTime? ProposedEffectiveAt { get; private set; }
    public DateTime? PublishedAt { get; private set; }
    public DateTime? RetiredAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public IReadOnlyList<LegalDocumentLocalizedSource> Sources => _sources;

    internal static LegalDocumentVersion Create(
        Guid legalDocumentId,
        int version,
        LegalDocumentAudience audience,
        IReadOnlyList<LegalDocumentLocalizedSource> sources,
        LegalDocumentTemplateProvenance? templateProvenance,
        string? sourceOrigin,
        bool requiresFreshAcceptance,
        LegalDocumentLifecycleState initialState,
        DateTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count is < 1 or > ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumLocalesPerDocument)
            throw new ArgumentOutOfRangeException(nameof(sources));
        if (!Enum.IsDefined(audience))
            throw new ArgumentOutOfRangeException(nameof(audience));

        LegalDocumentLocalizedSource[] ownedSources = sources.ToArray();
        if (ownedSources.Any(source => source is null)
            || ownedSources.Select(source => source.LanguageTag)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != ownedSources.Length)
        {
            throw new ArgumentException(
                "Legal document locales must be non-null and unique.",
                nameof(sources));
        }

        var result = new LegalDocumentVersion
        {
            Id = Guid.CreateVersion7(),
            LegalDocumentId = legalDocumentId,
            Version = version,
            Audience = audience,
            State = initialState,
            SourceOrigin = sourceOrigin,
            RequiresFreshAcceptance = requiresFreshAcceptance,
            TemplateId = templateProvenance?.TemplateId,
            TemplateVersion = templateProvenance?.TemplateVersion,
            TemplateSourceKind = templateProvenance?.SourceKind,
            TemplateLicenseExpression = templateProvenance?.LicenseExpression,
            TemplateReviewReference = templateProvenance?.ReviewReference,
            CreatedAt = occurredAt
        };
        foreach (LegalDocumentLocalizedSource source in ownedSources
                     .OrderBy(source => source.LanguageTag, StringComparer.Ordinal))
        {
            source.BindVersion(result.Id, result);
            result._sources.Add(source);
        }

        result.ContentDigest = ComputeDigest(result);
        return result;
    }

    internal void Approve(
        Guid reviewerId,
        string reviewEvidenceReference,
        string accountableIdentityReference,
        DateTime occurredAt)
    {
        ReviewerId = reviewerId;
        ReviewEvidenceReference = reviewEvidenceReference;
        AccountableIdentityReference = accountableIdentityReference;
        ApprovedAt = occurredAt;
    }

    internal void Schedule(DateTime effectiveAt) =>
        ProposedEffectiveAt = effectiveAt;

    internal void Publish(DateTime occurredAt) => PublishedAt = occurredAt;

    internal void Retire(DateTime occurredAt) => RetiredAt = occurredAt;

    internal void SetState(LegalDocumentLifecycleState state) => State = state;

    private static string ComputeDigest(LegalDocumentVersion version)
    {
        var canonical = new StringBuilder();
        Append(
            canonical,
            ((int)version.Audience).ToString(CultureInfo.InvariantCulture));
        Append(canonical, version.SourceOrigin ?? string.Empty);
        Append(canonical, version.RequiresFreshAcceptance ? "1" : "0");
        Append(canonical, version.TemplateId ?? string.Empty);
        Append(canonical, version.TemplateVersion ?? string.Empty);
        Append(canonical, version.TemplateSourceKind?.ToString() ?? string.Empty);
        Append(canonical, version.TemplateLicenseExpression ?? string.Empty);
        Append(canonical, version.TemplateReviewReference ?? string.Empty);
        foreach (LegalDocumentLocalizedSource source in version._sources)
        {
            Append(canonical, source.LanguageTag);
            Append(canonical, source.Title);
            Append(canonical, source.Summary);
            Append(canonical, source.Markdown);
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length);
        builder.Append(':');
        builder.Append(value);
        builder.Append(';');
    }
}

public sealed class LegalDocumentPublication
{
    private LegalDocumentPublication()
    {
    }

    public Guid Id { get; private set; }
    public Guid LegalDocumentId { get; private set; }
    public LegalDocument? LegalDocument { get; private set; }
    public Guid LegalDocumentVersionId { get; private set; }
    public LegalDocumentVersion? LegalDocumentVersion { get; private set; }
    public int Version { get; private set; }
    public LegalDocumentLifecycleState LifecycleState { get; private set; }
    public string ContentDigest { get; private set; } = string.Empty;
    public string AccountableIdentityReference { get; private set; } = string.Empty;
    public string ReviewEvidenceReference { get; private set; } = string.Empty;
    public DateTime EffectiveAt { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public bool RequiresFreshAcceptance { get; private set; }

    internal static LegalDocumentPublication Create(
        LegalDocument document,
        LegalDocumentVersion version,
        LegalDocumentLifecycleState lifecycleState,
        string accountableIdentityReference,
        DateTime occurredAt)
    {
        if (lifecycleState is not (LegalDocumentLifecycleState.Published
            or LegalDocumentLifecycleState.Retired))
        {
            throw new ArgumentOutOfRangeException(nameof(lifecycleState));
        }

        return new LegalDocumentPublication
        {
            Id = Guid.CreateVersion7(),
            LegalDocumentId = document.Id,
            LegalDocument = document,
            LegalDocumentVersionId = version.Id,
            LegalDocumentVersion = version,
            Version = version.Version,
            LifecycleState = lifecycleState,
            ContentDigest = version.ContentDigest,
            AccountableIdentityReference = accountableIdentityReference,
            ReviewEvidenceReference = version.ReviewEvidenceReference
                ?? throw new InvalidOperationException("Review evidence is required."),
            EffectiveAt = version.ProposedEffectiveAt
                ?? throw new InvalidOperationException("Effective time is required."),
            OccurredAt = occurredAt,
            RequiresFreshAcceptance = version.RequiresFreshAcceptance
        };
    }
}
