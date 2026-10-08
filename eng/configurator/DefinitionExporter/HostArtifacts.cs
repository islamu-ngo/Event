namespace ISLAMU.Event.DefinitionExporter;

using System.Text;
using System.Text.Json;
using Explore.Domain;
using Explore.Domain.Settings;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using ISLAMU.Wire.Contracts.Identity;
using ISLAMU.Event.DefinitionExporter.Environment;

internal sealed class HostArtifacts(JsonElement schema)
{
    internal JsonElement SettingsSchema(bool tenant) =>
        schema.GetProperty("$defs").GetProperty(tenant ? "tenantSettings" : "instanceSettings").GetProperty("properties");

    internal byte[] Validate(string artifact, ReadOnlyMemory<byte> bytes)
    {
        if (artifact == "operator-identity")
            return OperatorIdentityManifestJson.Serialize(OperatorIdentityManifestJson.Parse(bytes));
        if (artifact == "environment")
        {
            ValidateEnvironment(bytes);
            return bytes.ToArray();
        }
        if (artifact == "tenant-package")
        {
            TenantConfigurationPackageV1Alpha2 package = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(bytes);
            ValidateSettings(package.Spec.Settings, tenant: true);
            ValidateLegal(package.Spec.LegalDocuments, LegalDocumentScope.Tenant);
            return ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(package);
        }
        if (artifact is not ("configuration-manifest" or "legal-drafts"))
            throw new InvalidDataException("artifact-unknown");
        ConfigurationManifestV1Alpha2 manifest = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(bytes);
        ValidateSettings(manifest.Spec.Instance.Settings, tenant: false);
        ValidateLegal(manifest.Spec.Instance.LegalDocuments, LegalDocumentScope.Instance);
        if (manifest.Spec.Tenants.Select(tenant => tenant.Metadata.Name).Distinct(StringComparer.Ordinal).Count()
            != manifest.Spec.Tenants.Count)
            throw new InvalidDataException("tenant-name-duplicate");
        foreach (ConfigurationManifestTenantV1Alpha2 tenant in manifest.Spec.Tenants)
        {
            ValidateSettings(tenant.Spec.Settings, tenant: true);
            ValidateLegal(tenant.Spec.LegalDocuments, LegalDocumentScope.Tenant);
        }
        return ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(manifest);
    }

    private void ValidateSettings(IReadOnlyDictionary<string, JsonElement> settings, bool tenant)
    {
        JsonElement allowed = SettingsSchema(tenant);
        foreach ((string key, JsonElement value) in settings)
        {
            SettingDefinition? definition = SettingRegistry.Get(key);
            if (definition is null || definition.IsSensitive || !allowed.TryGetProperty(key, out JsonElement shape))
                throw new InvalidDataException("setting-not-portable");
            string type = shape.GetProperty("type").GetString()!;
            bool valid = type switch
            {
                "string" => value.ValueKind == JsonValueKind.String,
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
                "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
                "array" => value.ValueKind == JsonValueKind.Array
                    && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String
                        && ScalarValid(shape.GetProperty("items"), item)),
                _ => false
            };
            if (!valid || !ScalarValid(shape, value))
                throw new InvalidDataException("setting-value-invalid");
        }
    }

    private static bool ScalarValid(JsonElement schema, JsonElement value) =>
        (!schema.TryGetProperty("enum", out JsonElement choices)
            || choices.EnumerateArray().Any(choice => JsonElement.DeepEquals(choice, value)))
        && (value.ValueKind != JsonValueKind.String
            || (!schema.TryGetProperty("minLength", out JsonElement min) || value.GetString()!.Length >= min.GetInt32())
            && (!schema.TryGetProperty("maxLength", out JsonElement max) || value.GetString()!.Length <= max.GetInt32()));

    private static void ValidateLegal(
        IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> documents, LegalDocumentScope scope)
    {
        if (documents.Count > LegalMarkdownContentLimits.MaximumDocumentsPerScope)
            throw new InvalidDataException("legal-document-limit");
        foreach ((string key, ConfigurationManifestLegalDocumentV1Alpha2 document) in documents)
        {
            if (!Enum.TryParse(document.Kind, ignoreCase: false, out LegalDocumentKind kind)
                || !Enum.IsDefined(kind) || key != document.Kind || LegalDocumentKindCatalog.Get(kind).Scope != scope)
                throw new InvalidDataException("legal-scope-invalid");
            if (!Enum.TryParse(document.Audience, ignoreCase: false, out LegalDocumentAudience audience)
                || !Enum.IsDefined(audience) || document.LifecycleIntent is not ("Draft" or "ReviewRequired")
                || document.ProposedEffectiveAt is not null || document.AccountableIdentityReference is not null)
                throw new InvalidDataException("legal-authority-forbidden");
            if (document.TemplateProvenance is { } provenance
                && (provenance.SourceKind is not ("ProjectOwned" or "ApprovedFoss")
                    || !Required(provenance.TemplateId, 100) || !Required(provenance.TemplateVersion, 50)
                    || !Required(provenance.LicenseExpression, 100) || !Required(provenance.ReviewReference, 200)))
                throw new InvalidDataException("legal-provenance-invalid");
            if (document.JurisdictionAssumptions is null || document.JurisdictionAssumptions.Count > 16
                || document.JurisdictionAssumptions.Any(value => !Required(value, 100))
                || document.Localizations is null
                || document.Localizations.Count is < 1 or > LegalMarkdownContentLimits.MaximumLocalesPerDocument)
                throw new InvalidDataException("legal-locales-invalid");
            var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ConfigurationManifestLegalDocumentLocalizedSourceV1Alpha2 locale in document.Localizations)
            {
                if (locale is null || !Required(locale.LanguageTag, LegalMarkdownContentLimits.MaximumLanguageTagLength)
                    || locale.LanguageTag.Split('-').Any(part => part.Length == 0 || !part.All(char.IsAsciiLetterOrDigit))
                    || !languages.Add(locale.LanguageTag)
                    || !Required(locale.Title, LegalMarkdownContentLimits.MaximumTitleLength)
                    || !Required(locale.Summary, LegalMarkdownContentLimits.MaximumSummaryLength))
                    throw new InvalidDataException("legal-locales-invalid");
                _ = LegalMarkdownCodec.Inspect(locale.Markdown);
            }
        }
    }

    private static bool Required(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;

    private static void ValidateEnvironment(ReadOnlyMemory<byte> bytes)
    {
        DotenvParseResult parsed = DotenvCodec.Parse(bytes);
        if (!parsed.Succeeded)
            throw new InvalidDataException("environment-dotenv-invalid");
        const string prefix = "IDENTITYCORRELATION__TRUSTEDISSUERS__";
        var authorities = new HashSet<string>(StringComparer.Ordinal);
        foreach (DotenvEntry entry in parsed.Document!.Entries)
        {
            if (!entry.Key.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            string suffix = entry.Key[prefix.Length..];
            if (!int.TryParse(suffix, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int index)
                || suffix != index.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new InvalidDataException("issuer-index-invalid");
            string value = entry.Value ?? "";
            if (value.Contains('*', StringComparison.Ordinal)
                || !authorities.Add(OidcIssuerAuthority.Normalize(value)))
                throw new InvalidDataException("issuer-authority-invalid");
        }
    }
}
