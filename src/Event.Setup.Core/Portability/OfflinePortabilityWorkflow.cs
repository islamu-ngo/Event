namespace ISLAMU.Event.Setup.Core;

using System.Text.Json;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public static class OfflinePortabilityWorkflow
{
    private static readonly HashSet<string> ManifestSections = new(StringComparer.Ordinal)
    {
        "instance.settings", "instance.documents", "instance.legal_documents",
        "tenant.settings", "tenant.documents", "tenant.legal_documents"
    };
    private static readonly HashSet<string> TenantSections = new(StringComparer.Ordinal)
    {
        "tenant.settings", "tenant.documents", "tenant.legal_documents"
    };

    public static OfflinePortabilityResult CreateManifest(
        SetupProfile profile, SetupSelection selection, string sourceName,
        string? sourceTenantName, string? sourceTenantDisplayName)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(selection);
        try
        {
            SetupText.Identifier(sourceName, nameof(sourceName));
            if (selection.Scope == SetupScope.Tenant && sourceTenantName is null)
                return Failed("source-identity-required", "$.identity.source_tenant_name");
            ConfigurationManifestTenantV1Alpha2[] tenants = sourceTenantName is null ? [] :
            [
                new ConfigurationManifestTenantV1Alpha2
                {
                    Metadata = new ConfigurationManifestTenantMetadataV1Alpha2
                        { Name = SetupText.Identifier(sourceTenantName, nameof(sourceTenantName)) },
                    Spec = new ConfigurationManifestTenantSpecV1Alpha2
                    {
                        DisplayName = sourceTenantDisplayName ?? sourceTenantName,
                        Settings = EmptySettings(), Documents = EmptyDocuments(), LegalDocuments = EmptyLegal()
                    }
                }
            ];
            var manifest = new ConfigurationManifestV1Alpha2
            {
                Schema = ConfigurationManifestContractMetadata.SchemaId,
                ApiVersion = ConfigurationManifestContractMetadata.ApiVersion,
                Kind = ConfigurationManifestContractMetadata.Kind,
                Metadata = new ConfigurationManifestMetadataV1Alpha2 { Name = sourceName },
                Spec = new ConfigurationManifestSpecV1Alpha2
                {
                    Instance = new ConfigurationManifestInstanceV1Alpha2
                    { Settings = EmptySettings(), Documents = EmptyDocuments(), LegalDocuments = EmptyLegal() },
                    Tenants = tenants
                }
            };
            _ = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(
                ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(manifest));
            OfflinePortabilityDocument document = NewDocument(profile, selection, manifest, null,
                sourceName, selection.Scope == SetupScope.Tenant ? sourceTenantName : null,
                SetupWorkflowState.Draft);
            List<SetupDiagnostic> diagnostics = SelectionDiagnostics(document);
            return diagnostics.Count == 0
                ? new OfflinePortabilityResult(document, [])
                : new OfflinePortabilityResult(null, diagnostics);
        }
        catch (ConfigurationPortabilityContractException exception)
        {
            return ContractFailure(exception);
        }
        catch (ArgumentException)
        {
            return Failed("source-identity-invalid", "$.identity");
        }
    }

    public static OfflinePortabilityResult CreateTenantPackage(
        SetupProfile profile, SetupSelection selection, string sourceName,
        string sourceTenantName, string sourceTenantDisplayName, string? sourceInstanceName)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(selection);
        try
        {
            var package = new TenantConfigurationPackageV1Alpha2
            {
                Schema = TenantConfigurationPackageContractMetadata.SchemaId,
                ApiVersion = TenantConfigurationPackageContractMetadata.ApiVersion,
                Kind = TenantConfigurationPackageContractMetadata.Kind,
                Metadata = new TenantConfigurationPackageMetadataV1Alpha2
                {
                    Name = SetupText.Identifier(sourceName, nameof(sourceName)),
                    Source = new TenantConfigurationPackageSourceV1Alpha2
                    {
                        TenantName = SetupText.Identifier(sourceTenantName, nameof(sourceTenantName)),
                        InstanceName = sourceInstanceName is null ? null
                            : SetupText.Identifier(sourceInstanceName, nameof(sourceInstanceName))
                    }
                },
                Spec = new TenantConfigurationPackageSpecV1Alpha2
                {
                    DisplayName = sourceTenantDisplayName,
                    Settings = EmptySettings(),
                    Documents = EmptyDocuments(),
                    LegalDocuments = EmptyLegal()
                }
            };
            _ = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(
                ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(package));
            OfflinePortabilityDocument document = NewDocument(profile, selection, null, package,
                sourceName, sourceTenantName, SetupWorkflowState.Draft);
            List<SetupDiagnostic> diagnostics = SelectionDiagnostics(document);
            return diagnostics.Count == 0
                ? new OfflinePortabilityResult(document, [])
                : new OfflinePortabilityResult(null, diagnostics);
        }
        catch (ConfigurationPortabilityContractException exception)
        {
            return ContractFailure(exception);
        }
        catch (ArgumentException)
        {
            return Failed("source-identity-invalid", "$.identity");
        }
    }

    public static OfflinePortabilityResult OpenManifest(
        SetupProfile profile, SetupSelection selection, ReadOnlyMemory<byte> artifact) =>
        Open(profile, selection, () => ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(artifact), null);

    public static OfflinePortabilityResult OpenTenantPackage(
        SetupProfile profile, SetupSelection selection, ReadOnlyMemory<byte> artifact) =>
        Open(profile, selection, null, () => ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(artifact));

    public static OfflinePortabilityResult Edit(
        OfflinePortabilityDocument document, OfflinePortabilitySectionEdit edit)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(edit);
        string key = edit.Section.Value;
        if (!Allowed(document).Contains(key) || !document.Selection.Sections.Contains(edit.Section))
            return Failed("section-not-registered", "$.sections." + key);

        try
        {
            ConfigurationManifestV1Alpha2? manifest = document.Manifest;
            TenantConfigurationPackageV1Alpha2? package = document.TenantPackage;
            if (manifest is not null)
                manifest = EditManifest(manifest, document.Selection.Scope, key, edit);
            else
                package = EditPackage(package!, key, edit);
            SetupWorkflowState state = document.State == SetupWorkflowState.Draft
                ? SetupWorkflowState.Draft
                : SetupWorkflow.Transition(document.State, SetupWorkflowAction.Revise).State;
            var revised = new OfflinePortabilityDocument(
                document.Identity, document.Selection, manifest, package, state);
            return new OfflinePortabilityResult(revised, []);
        }
        catch (ArgumentException)
        {
            return Failed("section-value-invalid", "$.sections." + key);
        }
    }

    public static OfflinePortabilityResult Validate(OfflinePortabilityDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<SetupDiagnostic> diagnostics = SelectionDiagnostics(document);
        if (diagnostics.Count > 0)
            return new OfflinePortabilityResult(null, diagnostics);
        try
        {
            byte[] canonical = Serialize(document);
            if (document.Manifest is not null)
                _ = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(canonical);
            else
                _ = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(canonical);
            ValidateLegal(document);
            SetupTransitionResult validated = document.State == SetupWorkflowState.Draft
                ? SetupWorkflow.Transition(document.State, SetupWorkflowAction.Validate)
                : new SetupTransitionResult(document.State, document.State == SetupWorkflowState.Validated, null);
            if (!validated.Succeeded)
                return FailedTransition(validated);
            SetupTransitionResult ready = SetupWorkflow.Transition(validated.State, SetupWorkflowAction.MarkReady);
            if (!ready.Succeeded)
                return FailedTransition(ready);
            return new OfflinePortabilityResult(new OfflinePortabilityDocument(
                document.Identity, document.Selection, document.Manifest, document.TenantPackage, ready.State), []);
        }
        catch (ConfigurationPortabilityContractException exception)
        {
            return ContractFailure(exception);
        }
        catch (ArgumentException)
        {
            return Failed("legal-draft-invalid", "$.legal_documents");
        }
    }

    public static OfflinePortabilityFormatResult Format(OfflinePortabilityDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.State != SetupWorkflowState.Ready)
            return new OfflinePortabilityFormatResult(null, [WorkflowDiagnostic()]);
        try
        {
            return new OfflinePortabilityFormatResult(new OfflinePortabilityOutput(
                Serialize(document), MediaType(document)), []);
        }
        catch (ConfigurationPortabilityContractException exception)
        {
            OfflinePortabilityResult failure = ContractFailure(exception);
            return new OfflinePortabilityFormatResult(null, failure.Diagnostics);
        }
    }

    public static OfflinePortabilityExportResult Export(OfflinePortabilityDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        OfflinePortabilityFormatResult formatted = Format(document);
        if (!formatted.Succeeded)
            return new OfflinePortabilityExportResult(null, null, formatted.Diagnostics);
        SetupTransitionResult transition = SetupWorkflow.Transition(document.State, SetupWorkflowAction.Export);
        if (!transition.Succeeded)
            return new OfflinePortabilityExportResult(null, null, [transition.Diagnostic!]);
        return new OfflinePortabilityExportResult(new OfflinePortabilityDocument(
            document.Identity, document.Selection, document.Manifest, document.TenantPackage, transition.State),
            formatted.Output, []);
    }

    public static SetupDiffResult Diff(OfflinePortabilityDocument baseline, OfflinePortabilityDocument candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        return SetupDiff.Compare(new SetupDiffInput(SectionDigests(baseline), SectionDigests(candidate)));
    }

    public static SetupCoverageResult Coverage(OfflinePortabilityDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return SetupCoverage.Calculate(new SetupCoverageInput(document.Selection.Sections, document.Sections));
    }

    private static OfflinePortabilityResult Open(
        SetupProfile profile, SetupSelection selection,
        Func<ConfigurationManifestV1Alpha2>? manifestParser,
        Func<TenantConfigurationPackageV1Alpha2>? packageParser)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(selection);
        try
        {
            ConfigurationManifestV1Alpha2? manifest = manifestParser?.Invoke();
            TenantConfigurationPackageV1Alpha2? package = packageParser?.Invoke();
            string sourceName = manifest?.Metadata.Name ?? package!.Metadata.Name;
            string? tenantName = manifest is null
                ? package!.Metadata.Source.TenantName
                : selection.Scope == SetupScope.Tenant && manifest.Spec.Tenants.Count > 0
                    ? manifest.Spec.Tenants[0].Metadata.Name : null;
            OfflinePortabilityDocument draft = NewDocument(profile, selection, manifest, package,
                sourceName, tenantName, SetupWorkflowState.Draft);
            return Validate(draft);
        }
        catch (ConfigurationPortabilityContractException exception)
        {
            return ContractFailure(exception);
        }
        catch (ArgumentException)
        {
            return Failed("source-identity-invalid", "$.identity");
        }
    }

    private static ConfigurationManifestV1Alpha2 EditManifest(
        ConfigurationManifestV1Alpha2 manifest, SetupScope scope, string key, OfflinePortabilitySectionEdit edit)
    {
        if (key.StartsWith("instance.", StringComparison.Ordinal))
            return manifest with { Spec = manifest.Spec with { Instance = EditInstance(manifest.Spec.Instance, key, edit) } };
        if (manifest.Spec.Tenants.Count != 1)
            throw new ArgumentException("A tenant source is required.");
        ConfigurationManifestTenantV1Alpha2 tenant = manifest.Spec.Tenants[0];
        ConfigurationManifestTenantV1Alpha2 revised = tenant with { Spec = EditTenant(tenant.Spec, key, edit) };
        return manifest with { Spec = manifest.Spec with { Tenants = [revised] } };
    }

    private static ConfigurationManifestInstanceV1Alpha2 EditInstance(
        ConfigurationManifestInstanceV1Alpha2 value, string key, OfflinePortabilitySectionEdit edit) => key switch
        {
            "instance.settings" => value with { Settings = edit.IsRemoval ? EmptySettings() : edit.Replacement!.RequireSettings() },
            "instance.documents" => value with { Documents = edit.IsRemoval ? EmptyDocuments() : edit.Replacement!.RequireDocuments() },
            "instance.legal_documents" => value with { LegalDocuments = edit.IsRemoval ? EmptyLegal() : edit.Replacement!.RequireLegalDocuments() },
            _ => throw new ArgumentException("Section is invalid.")
        };

    private static ConfigurationManifestTenantSpecV1Alpha2 EditTenant(
        ConfigurationManifestTenantSpecV1Alpha2 value, string key, OfflinePortabilitySectionEdit edit) => key switch
        {
            "tenant.settings" => value with { Settings = edit.IsRemoval ? EmptySettings() : edit.Replacement!.RequireSettings() },
            "tenant.documents" => value with { Documents = edit.IsRemoval ? EmptyDocuments() : edit.Replacement!.RequireDocuments() },
            "tenant.legal_documents" => value with { LegalDocuments = edit.IsRemoval ? EmptyLegal() : edit.Replacement!.RequireLegalDocuments() },
            _ => throw new ArgumentException("Section is invalid.")
        };

    private static TenantConfigurationPackageV1Alpha2 EditPackage(
        TenantConfigurationPackageV1Alpha2 package, string key, OfflinePortabilitySectionEdit edit) =>
        package with
        {
            Spec = key switch
            {
                "tenant.settings" => package.Spec with { Settings = edit.IsRemoval ? EmptySettings() : edit.Replacement!.RequireSettings() },
                "tenant.documents" => package.Spec with { Documents = edit.IsRemoval ? EmptyDocuments() : edit.Replacement!.RequireDocuments() },
                "tenant.legal_documents" => package.Spec with { LegalDocuments = edit.IsRemoval ? EmptyLegal() : edit.Replacement!.RequireLegalDocuments() },
                _ => throw new ArgumentException("Section is invalid.")
            }
        };

    private static List<SetupDiagnostic> SelectionDiagnostics(OfflinePortabilityDocument document)
    {
        var diagnostics = new List<SetupDiagnostic>();
        HashSet<string> allowed = Allowed(document);
        foreach (PortableSectionKey section in document.Selection.Sections)
            if (!allowed.Contains(section.Value))
                diagnostics.Add(Diagnostic("section-not-registered", "$.sections." + section.Value));
        if (document.TenantPackage is not null && document.Selection.Scope != SetupScope.Tenant)
            diagnostics.Add(Diagnostic("scope-invalid", "$.selection.scope"));
        if (document.Manifest is not null && document.Selection.Scope == SetupScope.Tenant
            && document.Manifest.Spec.Tenants.Count != 1)
            diagnostics.Add(Diagnostic("scope-invalid", "$.spec.tenants"));
        foreach (string populated in PopulatedSections(document))
            if (!document.Selection.Sections.Any(item => item.Value == populated))
                diagnostics.Add(Diagnostic("section-not-selected", "$.sections." + populated));
        return diagnostics;
    }

    private static string[] PopulatedSections(OfflinePortabilityDocument document)
    {
        var populated = new HashSet<string>(StringComparer.Ordinal);
        if (document.Manifest is { } manifest)
        {
            if (manifest.Spec.Instance.Settings.Count > 0) populated.Add("instance.settings");
            if (manifest.Spec.Instance.Documents.Count > 0) populated.Add("instance.documents");
            if (manifest.Spec.Instance.LegalDocuments.Count > 0) populated.Add("instance.legal_documents");
            foreach (ConfigurationManifestTenantV1Alpha2 tenant in manifest.Spec.Tenants)
            {
                if (tenant.Spec.Settings.Count > 0) populated.Add("tenant.settings");
                if (tenant.Spec.Documents.Count > 0) populated.Add("tenant.documents");
                if (tenant.Spec.LegalDocuments.Count > 0) populated.Add("tenant.legal_documents");
            }
        }
        else
        {
            TenantConfigurationPackageSpecV1Alpha2 spec = document.TenantPackage!.Spec;
            if (spec.Settings.Count > 0) populated.Add("tenant.settings");
            if (spec.Documents.Count > 0) populated.Add("tenant.documents");
            if (spec.LegalDocuments.Count > 0) populated.Add("tenant.legal_documents");
        }
        return populated.Order(StringComparer.Ordinal).ToArray();
    }

    private static void ValidateLegal(OfflinePortabilityDocument document)
    {
        if (document.Manifest is { } manifest)
        {
            ValidateLegalSet(OfflineLegalDraftScope.Instance, manifest.Spec.Instance.LegalDocuments);
            foreach (ConfigurationManifestTenantV1Alpha2 tenant in manifest.Spec.Tenants)
                ValidateLegalSet(OfflineLegalDraftScope.Tenant, tenant.Spec.LegalDocuments);
        }
        else
            ValidateLegalSet(OfflineLegalDraftScope.Tenant, document.TenantPackage!.Spec.LegalDocuments);
    }

    private static void ValidateLegalSet(OfflineLegalDraftScope scope,
        IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> documents)
    {
        if (documents.Count > LegalMarkdownContentLimits.MaximumDocumentsPerScope)
            throw new ArgumentOutOfRangeException(nameof(documents));
        foreach ((string key, ConfigurationManifestLegalDocumentV1Alpha2 value) in documents)
        {
            OfflineLegalDraft draft = OfflineLegalDraft.FromWire(scope, key, value);
            if (draft.Localizations.Any(locale =>
                    LegalMarkdownCodec.Inspect(locale.Markdown).PlaceholderCount > 0))
                throw new ArgumentException("Legal identity placeholders remain unresolved.", nameof(documents));
        }
    }

    private static Dictionary<PortableSectionKey, ArtifactDigest> SectionDigests(
        OfflinePortabilityDocument document)
    {
        var result = new Dictionary<PortableSectionKey, ArtifactDigest>();
        foreach (PortableSectionKey section in document.Sections)
        {
            OfflinePortabilityDocument isolated = Isolate(document, section.Value);
            result[section] = ArtifactDigest.Compute(Serialize(isolated));
        }
        return result;
    }

    private static OfflinePortabilityDocument Isolate(OfflinePortabilityDocument document, string keep)
    {
        ConfigurationManifestV1Alpha2? manifest = document.Manifest;
        TenantConfigurationPackageV1Alpha2? package = document.TenantPackage;
        if (manifest is not null)
        {
            ConfigurationManifestInstanceV1Alpha2 instance = manifest.Spec.Instance with
            {
                Settings = keep == "instance.settings" ? manifest.Spec.Instance.Settings : EmptySettings(),
                Documents = keep == "instance.documents" ? manifest.Spec.Instance.Documents : EmptyDocuments(),
                LegalDocuments = keep == "instance.legal_documents" ? manifest.Spec.Instance.LegalDocuments : EmptyLegal()
            };
            ConfigurationManifestTenantV1Alpha2[] tenants = manifest.Spec.Tenants.Select(tenant => tenant with
            {
                Metadata = tenant.Metadata with { Name = "section-source" },
                Spec = tenant.Spec with
                {
                    DisplayName = "Section source",
                    Settings = keep == "tenant.settings" ? tenant.Spec.Settings : EmptySettings(),
                    Documents = keep == "tenant.documents" ? tenant.Spec.Documents : EmptyDocuments(),
                    LegalDocuments = keep == "tenant.legal_documents" ? tenant.Spec.LegalDocuments : EmptyLegal()
                }
            }).ToArray();
            manifest = manifest with
            {
                Metadata = manifest.Metadata with { Name = "section-digest", Export = null },
                Spec = manifest.Spec with { Instance = instance, Tenants = tenants }
            };
        }
        else
        {
            package = package! with
            {
                Metadata = package!.Metadata with
                {
                    Name = "section-digest",
                    Source = package.Metadata.Source with
                    {
                        TenantName = "section-source",
                        InstanceName = null
                    },
                    Export = null
                },
                Spec = package.Spec with
                {
                    DisplayName = "Section source",
                    Settings = keep == "tenant.settings" ? package.Spec.Settings : EmptySettings(),
                    Documents = keep == "tenant.documents" ? package.Spec.Documents : EmptyDocuments(),
                    LegalDocuments = keep == "tenant.legal_documents" ? package.Spec.LegalDocuments : EmptyLegal()
                }
            };
        }
        return new OfflinePortabilityDocument(document.Identity, document.Selection, manifest, package, document.State);
    }

    private static OfflinePortabilityDocument NewDocument(
        SetupProfile profile, SetupSelection selection, ConfigurationManifestV1Alpha2? manifest,
        TenantConfigurationPackageV1Alpha2? package, string sourceName, string? tenantName,
        SetupWorkflowState state) => new(
            new OfflinePortabilityIdentity(profile.Identity, selection.Scope,
                manifest is null ? OfflinePortabilityArtifactKind.TenantConfigurationPackage
                    : OfflinePortabilityArtifactKind.ConfigurationManifest,
                sourceName, tenantName), selection, manifest, package, state);

    private static byte[] Serialize(OfflinePortabilityDocument document) => document.Manifest is { } manifest
        ? ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(manifest)
        : ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(document.TenantPackage!);

    private static string MediaType(OfflinePortabilityDocument document) => document.Manifest is null
        ? TenantConfigurationPackageContractMetadata.MediaType
        : ConfigurationManifestContractMetadata.MediaType;

    private static HashSet<string> Allowed(OfflinePortabilityDocument document) =>
        document.Identity.ArtifactKind == OfflinePortabilityArtifactKind.TenantConfigurationPackage
            || document.Selection.Scope == SetupScope.Tenant ? TenantSections : ManifestSections;

    private static Dictionary<string, JsonElement> EmptySettings() => new(StringComparer.Ordinal);
    private static Dictionary<string, ConfigurationManifestDocumentV1Alpha2> EmptyDocuments() => new(StringComparer.Ordinal);
    private static Dictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> EmptyLegal() => new(StringComparer.Ordinal);

    private static OfflinePortabilityResult ContractFailure(ConfigurationPortabilityContractException exception) =>
        new(null, [Diagnostic(exception.Code, SafePath(exception.Path))]);
    private static OfflinePortabilityResult Failed(string code, string path) =>
        new(null, [Diagnostic(code, path)]);
    private static OfflinePortabilityResult FailedTransition(SetupTransitionResult transition) =>
        new(null, [transition.Diagnostic ?? WorkflowDiagnostic()]);
    private static SetupDiagnostic WorkflowDiagnostic() => Diagnostic("invalid-transition", "$.workflow.state");
    private static SetupDiagnostic Diagnostic(string code, string path) => new(
        new SetupDiagnosticCode(code), new SetupDiagnosticPath(SafePath(path)), SetupDiagnosticSeverity.Error);
    private static string SafePath(string path) => path.ToLowerInvariant();
}
