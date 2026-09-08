namespace ISLAMU.Event.Setup.Core;

using System.Collections.ObjectModel;
using System.Text.Json;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public enum OfflinePortabilityArtifactKind
{
    ConfigurationManifest,
    TenantConfigurationPackage
}

public sealed record OfflinePortabilityIdentity
{
    internal OfflinePortabilityIdentity(
        SetupProfileIdentity profile, SetupScope scope,
        OfflinePortabilityArtifactKind artifactKind, string sourceName, string? sourceTenantName)
    {
        Profile = profile;
        Scope = scope;
        ArtifactKind = artifactKind;
        SourceName = SetupText.Identifier(sourceName, nameof(sourceName));
        SourceTenantName = sourceTenantName is null
            ? null : SetupText.Identifier(sourceTenantName, nameof(sourceTenantName));
    }

    public SetupProfileIdentity Profile { get; }
    public SetupScope Scope { get; }
    public OfflinePortabilityArtifactKind ArtifactKind { get; }
    public string SourceName { get; }
    public string? SourceTenantName { get; }
    public override string ToString() =>
        $"{nameof(OfflinePortabilityIdentity)}:{ArtifactKind}:{Profile}:{Scope}:TenantSource={SourceTenantName is not null}";
}

public sealed record OfflinePortabilityDocument
{
    private readonly PortableSectionKey[] _sections;

    internal OfflinePortabilityDocument(
        OfflinePortabilityIdentity identity, SetupSelection selection,
        ConfigurationManifestV1Alpha2? manifest,
        TenantConfigurationPackageV1Alpha2? tenantPackage,
        SetupWorkflowState state)
    {
        Identity = identity;
        Selection = selection;
        Manifest = manifest;
        TenantPackage = tenantPackage;
        State = state;
        _sections = selection.Sections.ToArray();
    }

    public OfflinePortabilityIdentity Identity { get; }
    public SetupSelection Selection { get; }
    public ConfigurationManifestV1Alpha2? Manifest { get; }
    public TenantConfigurationPackageV1Alpha2? TenantPackage { get; }
    public SetupWorkflowState State { get; }
    public IReadOnlyList<PortableSectionKey> Sections =>
        Array.AsReadOnly((PortableSectionKey[])_sections.Clone());
    public override string ToString() =>
        $"{nameof(OfflinePortabilityDocument)}:{Identity}:State={State}:Sections={_sections.Length}";
}

public sealed class OfflinePortabilityResult
{
    private readonly SetupDiagnostic[] _diagnostics;

    internal OfflinePortabilityResult(OfflinePortabilityDocument? document, IEnumerable<SetupDiagnostic> diagnostics)
    {
        Document = document;
        _diagnostics = diagnostics.OrderBy(item => item.Path.Value, StringComparer.Ordinal)
            .ThenBy(item => item.Code.Value, StringComparer.Ordinal).ToArray();
    }

    public OfflinePortabilityDocument? Document { get; }
    public IReadOnlyList<SetupDiagnostic> Diagnostics =>
        Array.AsReadOnly((SetupDiagnostic[])_diagnostics.Clone());
    public bool Succeeded => Document is not null && _diagnostics.Length == 0;
    public override string ToString() =>
        $"{nameof(OfflinePortabilityResult)}:Succeeded={Succeeded}:Diagnostics={_diagnostics.Length}";
}

public sealed class OfflinePortabilityOutput
{
    private readonly byte[] _bytes;

    internal OfflinePortabilityOutput(byte[] bytes, string mediaType)
    {
        _bytes = (byte[])bytes.Clone();
        MediaType = mediaType;
        Digest = ArtifactDigest.Compute(_bytes);
    }

    public ReadOnlyMemory<byte> Bytes => new((byte[])_bytes.Clone());
    public string MediaType { get; }
    public ArtifactDigest Digest { get; }
    public override string ToString() =>
        $"{nameof(OfflinePortabilityOutput)}:Length={_bytes.Length}:Digest={Digest}";
}

public sealed class OfflinePortabilityFormatResult
{
    private readonly SetupDiagnostic[] _diagnostics;

    internal OfflinePortabilityFormatResult(OfflinePortabilityOutput? output, IEnumerable<SetupDiagnostic> diagnostics)
    {
        Output = output;
        _diagnostics = diagnostics.ToArray();
    }

    public OfflinePortabilityOutput? Output { get; }
    public IReadOnlyList<SetupDiagnostic> Diagnostics =>
        Array.AsReadOnly((SetupDiagnostic[])_diagnostics.Clone());
    public bool Succeeded => Output is not null && _diagnostics.Length == 0;
}

public sealed class OfflinePortabilityExportResult
{
    private readonly SetupDiagnostic[] _diagnostics;

    internal OfflinePortabilityExportResult(
        OfflinePortabilityDocument? document, OfflinePortabilityOutput? output,
        IEnumerable<SetupDiagnostic> diagnostics)
    {
        Document = document;
        Output = output;
        _diagnostics = diagnostics.ToArray();
    }

    public OfflinePortabilityDocument? Document { get; }
    public OfflinePortabilityOutput? Output { get; }
    public IReadOnlyList<SetupDiagnostic> Diagnostics =>
        Array.AsReadOnly((SetupDiagnostic[])_diagnostics.Clone());
    public bool Succeeded => Document is not null && Output is not null && _diagnostics.Length == 0;
}

public sealed class OfflinePortabilitySectionSnapshot
{
    private readonly IReadOnlyDictionary<string, JsonElement>? _settings;
    private readonly IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2>? _documents;
    private readonly IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>? _legalDocuments;

    private OfflinePortabilitySectionSnapshot(
        IReadOnlyDictionary<string, JsonElement>? settings,
        IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2>? documents,
        IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>? legalDocuments)
    {
        _settings = settings is null ? null : SnapshotSettings(settings);
        _documents = documents is null ? null : SnapshotDictionary(documents);
        _legalDocuments = legalDocuments is null ? null : SnapshotDictionary(legalDocuments);
    }

    public static OfflinePortabilitySectionSnapshot Settings(IReadOnlyDictionary<string, JsonElement> settings) =>
        new(settings, null, null);
    public static OfflinePortabilitySectionSnapshot Documents(
        IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2> documents) => new(null, documents, null);
    public static OfflinePortabilitySectionSnapshot LegalDocuments(
        IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> legalDocuments) => new(null, null, legalDocuments);

    internal IReadOnlyDictionary<string, JsonElement> RequireSettings() =>
        _settings ?? throw new ArgumentException("The section snapshot kind is invalid.");
    internal IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2> RequireDocuments() =>
        _documents ?? throw new ArgumentException("The section snapshot kind is invalid.");
    internal IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> RequireLegalDocuments() =>
        _legalDocuments ?? throw new ArgumentException("The section snapshot kind is invalid.");

    private static ReadOnlyDictionary<string, JsonElement> SnapshotSettings(
        IReadOnlyDictionary<string, JsonElement> values) =>
        new ReadOnlyDictionary<string, JsonElement>(values.ToDictionary(
            item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal));

    private static ReadOnlyDictionary<string, T> SnapshotDictionary<T>(IReadOnlyDictionary<string, T> values) =>
        new ReadOnlyDictionary<string, T>(new Dictionary<string, T>(values, StringComparer.Ordinal));
}

public sealed record OfflinePortabilitySectionEdit
{
    public OfflinePortabilitySectionEdit(PortableSectionKey section, OfflinePortabilitySectionSnapshot replacement)
    {
        Section = section ?? throw new ArgumentNullException(nameof(section));
        Replacement = replacement ?? throw new ArgumentNullException(nameof(replacement));
    }

    private OfflinePortabilitySectionEdit(PortableSectionKey section)
    {
        Section = section;
        IsRemoval = true;
    }

    public PortableSectionKey Section { get; }
    public OfflinePortabilitySectionSnapshot? Replacement { get; }
    public bool IsRemoval { get; }
    public static OfflinePortabilitySectionEdit Remove(PortableSectionKey section) => new(section);
}
