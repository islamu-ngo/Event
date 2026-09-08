namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using System.Reflection;
using System.Text.Json;
using Event.Application.UnitTests.Features.ConfigurationManifest;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Application.Settings;
using Explore.Domain.Settings;

public sealed class ConfigurationManifestInstanceAuthorityTests
{
    private const string WrongScopeReasonCode =
        "configuration_manifest_setting_scope_invalid";

    private static readonly string[] ApprovedInstanceSettingKeys =
    [
        "appearance.default_theme_mode",
        "branding.custom_css_url",
        "branding.display_name",
        "branding.favicon_url",
        "branding.logo_url",
        "events.group_submission_enabled",
        "events.organization_submission_enabled",
        "events.require_approval",
        "events.user_submission_enabled",
        "footer.lock_tenant_copyright",
        "footer.lock_tenant_description",
        "footer.lock_tenant_link_groups",
        "footer.lock_tenant_social_links",
        "footer.lock_tenant_template",
        "groups.self_registration_enabled",
        "modules.islamic_enabled",
        "modules.tech_enabled",
        "organizations.self_registration_enabled",
        "organizations.tenant_can_omit_verification",
        "organizations.verification_required",
        "public_experience.event_catalog_label",
        "public_experience.mode",
        "routing.default_public_home_page",
        "tenants.self_service_registration",
        "tenants.white_labeling_enabled"
    ];

    [Test]
    public async Task InstanceSettingCatalog_ContainsExactlyApprovedV1Alpha2Keys()
    {
        PropertyInfo? property = typeof(ConfigurationManifestCatalog).GetProperty(
            "InstanceSettings",
            BindingFlags.Public | BindingFlags.Static);

        await Assert.That(property).IsNotNull();
        if (property is null)
            return;

        var entries = property.GetValue(null)
            as IReadOnlyDictionary<string, ConfigurationManifestSettingCatalogEntry>;
        await Assert.That(entries).IsNotNull();
        if (entries is null)
            return;

        string[] actual = entries.Keys
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(actual.SequenceEqual(
            ApprovedInstanceSettingKeys,
            StringComparer.Ordinal)).IsTrue();
        await Assert.That(entries.Count < SettingRegistry.Count).IsTrue();
    }

    [Test]
    public async Task InstanceSettingAuthority_ApprovedKeysResolveToSafeInstanceDefinitions()
    {
        string[] missing = ApprovedInstanceSettingKeys
            .Where(key => SettingRegistry.Get(key) is null)
            .ToArray();

        await Assert.That(missing).IsEmpty();
        if (missing.Length > 0)
            return;

        SettingDefinition[] unsafeDefinitions = ApprovedInstanceSettingKeys
            .Select(key => SettingRegistry.Get(key)!)
            .Where(definition =>
                definition.IsSensitive
                || definition.MinScope > SettingScope.Instance
                || definition.MaxScope < SettingScope.Instance)
            .ToArray();

        await Assert.That(unsafeDefinitions).IsEmpty();
    }

    [Test]
    public async Task InstanceSettingAuthority_ClassifiesEveryUnapprovedRegistryEntry()
    {
        var approved = ApprovedInstanceSettingKeys.ToHashSet(StringComparer.Ordinal);
        Dictionary<string, string> rejected = SettingRegistry.All
            .Where(definition => !approved.Contains(definition.Key))
            .ToDictionary(
                definition => definition.Key,
                RejectionReason,
                StringComparer.Ordinal);

        await Assert.That(rejected).IsNotEmpty();
        await Assert.That(rejected.Values.All(reason =>
            !string.IsNullOrWhiteSpace(reason))).IsTrue();
        await Assert.That(rejected.Any(candidate =>
            SettingRegistry.Get(candidate.Key)!.IsSensitive
            && candidate.Value == "sensitive")).IsTrue();
    }

    [Test]
    public async Task Validate_SensitiveInstanceSetting_FailsClosedWithoutValueReflection()
    {
        const string secret = "manifest-secret-sentinel";
        var instanceSettings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["auth.google_client_secret"] =
                ConfigurationManifestTestData.Json($"\"{secret}\"")
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(
                ConfigurationManifestTestData.Valid(
                    instanceSettings: instanceSettings));

        ConfigurationManifestValidationError? error = result.Errors
            .SingleOrDefault(candidate =>
                candidate.Code
                    == ConfigurationManifestFailureCodes.SensitiveKeyForbidden);

        await Assert.That(error).IsNotNull();
        if (error is null)
            return;

        await Assert.That(error.Path)
            .IsEqualTo("$.spec.instance.settings.auth.google_client_secret");
        await Assert.That(error.Message.Contains(secret, StringComparison.Ordinal))
            .IsFalse();
        await Assert.That(JsonSerializer.Serialize(result)
            .Contains(secret, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Validate_TenantUsesInstanceOnlyKey_ReturnsStableWrongScopeReason()
    {
        var tenantSettings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["branding.display_name"] =
                ConfigurationManifestTestData.Json("\"Tenant override\"")
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(
                ConfigurationManifestTestData.Valid(settings: tenantSettings));

        ConfigurationManifestValidationError? error = result.Errors
            .SingleOrDefault(candidate =>
                candidate.Code == ConfigurationManifestFailureCodes.KeyNotAllowed);

        await Assert.That(error).IsNotNull();
        if (error is null)
            return;

        await Assert.That(error.ReasonCode).IsEqualTo(WrongScopeReasonCode);
        await Assert.That(error.Path)
            .IsEqualTo("$.spec.tenants[0].spec.settings.branding.display_name");
    }

    [Test]
    public async Task Validate_ReportingIntakeRemainsTenantOwned()
    {
        const string key = "event_reporting.intake_enabled";
        JsonElement enabled =
            ConfigurationManifestTestData.Json("true");
        ConfigurationManifestValidationResult instanceResult =
            ConfigurationManifestValidator.Validate(
                ConfigurationManifestTestData.Valid(
                    instanceSettings: new Dictionary<string, JsonElement>(
                        StringComparer.Ordinal)
                    {
                        [key] = enabled
                    }));
        ConfigurationManifestValidationResult tenantResult =
            ConfigurationManifestValidator.Validate(
                ConfigurationManifestTestData.Valid(
                    settings: new Dictionary<string, JsonElement>(
                        StringComparer.Ordinal)
                    {
                        [key] = enabled
                    }));

        ConfigurationManifestValidationError? error = instanceResult.Errors
            .SingleOrDefault(candidate =>
                candidate.Code
                    == ConfigurationManifestFailureCodes.KeyNotAllowed);
        await Assert.That(error).IsNotNull();
        if (error is not null)
        {
            await Assert.That(error.ReasonCode)
                .IsEqualTo(WrongScopeReasonCode);
            await Assert.That(error.Path)
                .IsEqualTo(
                    "$.spec.instance.settings.event_reporting.intake_enabled");
        }

        await Assert.That(tenantResult.IsValid).IsTrue();
    }

    [Test]
    public async Task Compile_SensitiveInstanceSetting_RejectsBeforePlanConstruction()
    {
        const string secret = "compiler-secret-sentinel";
        ConfigurationManifestV1Alpha2 manifest =
            ConfigurationManifestTestData.Valid(
                instanceSettings: new Dictionary<string, JsonElement>(
                    StringComparer.Ordinal)
                {
                    ["auth.google_client_secret"] =
                        ConfigurationManifestTestData.Json($"\"{secret}\"")
                });
        var source = new ConfigurationManifestReadResult(
            manifest,
            ConfigurationManifestMode.Bootstrap,
            new string('a', 64),
            ByteLength: 512);
        ConfigurationManifestCompilationException? exception = null;

        try
        {
            _ = ConfigurationManifestCompiler.Compile(
                source,
                Guid.CreateVersion7(),
                DateTime.UnixEpoch);
        }
        catch (ConfigurationManifestCompilationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        if (exception is null)
            return;

        await Assert.That(exception.FailureCode)
            .IsEqualTo(
                ConfigurationManifestFailureCodes.SensitiveKeyForbidden);
        await Assert.That(exception.Message.Contains(secret, StringComparison.Ordinal))
            .IsFalse();
    }

    [Test]
    public async Task InstanceMutationBoundaries_ExposeCallerOwnedTransactionSeams()
    {
        MethodInfo? scalarMethod = typeof(SettingUpsertService).GetMethod(
            "UpsertInstanceValueInCurrentTransactionAsync",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo? publicationMethod =
            typeof(IPublicationPolicyMutationBoundary).GetMethod(
                "ApplyInstanceInCurrentTransactionAsync",
                BindingFlags.Instance | BindingFlags.Public);

        await Assert.That(scalarMethod).IsNotNull();
        await Assert.That(publicationMethod).IsNotNull();
    }

    [Test]
    public async Task Validate_InstanceBrandingUrlRequiresSafeHttpsOrigin()
    {
        var instanceSettings = new Dictionary<string, JsonElement>(
            StringComparer.Ordinal)
        {
            ["branding.logo_url"] =
                ConfigurationManifestTestData.Json(
                    "\"http://example.test/logo.svg\"")
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(
                ConfigurationManifestTestData.Valid(
                    instanceSettings: instanceSettings));

        ConfigurationManifestValidationError? error = result.Errors
            .SingleOrDefault(candidate =>
                candidate.Code == ConfigurationManifestFailureCodes.ValueInvalid);

        await Assert.That(error).IsNotNull();
        if (error is null)
            return;

        await Assert.That(error.Path)
            .IsEqualTo("$.spec.instance.settings.branding.logo_url");
    }

    [Test]
    public async Task Validate_InstanceBrandingDisplayNameIsBounded()
    {
        var instanceSettings = new Dictionary<string, JsonElement>(
            StringComparer.Ordinal)
        {
            ["branding.display_name"] =
                ConfigurationManifestTestData.Json(
                    $"\"{new string('a', 201)}\"")
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(
                ConfigurationManifestTestData.Valid(
                    instanceSettings: instanceSettings));

        await Assert.That(result.Errors.Any(candidate =>
            candidate.Code == ConfigurationManifestFailureCodes.ValueInvalid
            && candidate.Path
                == "$.spec.instance.settings.branding.display_name")).IsTrue();
    }

    private static string RejectionReason(SettingDefinition definition)
    {
        if (definition.IsSensitive)
            return "sensitive";
        if (definition.MinScope > SettingScope.Instance
            || definition.MaxScope < SettingScope.Instance)
        {
            return "wrong_scope";
        }

        return definition.RequiresCoordinatedMutation
            ? "requires_explicit_canonical_boundary"
            : "not_admitted_by_v1alpha2";
    }
}
