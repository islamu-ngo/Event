namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using System.Text.Json;
using Explore.Application.DTOs.PaidEventPolicies;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Definitions;
using Explore.Domain.Settings.Documents;

public sealed class ConfigurationManifestValidatorTests
{
    [Test]
    public async Task Validate_MinimalManifest_IsValid()
    {
        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(ConfigurationManifestTestData.Valid());

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Errors).IsEmpty();
    }

    [Test]
    public async Task Validate_UnsupportedEnvelopeFields_RejectsContract()
    {
        ConfigurationManifestV1Alpha2 manifest = ConfigurationManifestTestData.Valid() with
        {
            ApiVersion = "configuration.islamu.org/v2"
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(manifest);

        await Assert.That(HasCode(result, ConfigurationManifestFailureCodes.ContractInvalid)).IsTrue();
    }

    [Test]
    public async Task Validate_LegacyTenantNarrowingAuthorityScope_RejectsContract()
    {
        ConfigurationManifestV1Alpha2 valid = ConfigurationManifestTestData.Valid();
        ConfigurationManifestV1Alpha2 manifest = valid with
        {
            Metadata = valid.Metadata with
            {
                Export = new ConfigurationManifestExportMetadataV1Alpha2
                {
                    View = ConfigurationManifestExportMetadataValues.OverridesView,
                    EffectiveValuesFlattened = false,
                    SensitiveValuesOmitted = true,
                    AuthorityScope = "TenantNarrowingOnly",
                    SovereignValuesOmitted = true,
                    SovereignLockedFields = PaidEventPolicyAuthorityMetadata
                        .SovereignLockedFields
                }
            }
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(manifest);

        await Assert.That(result.Errors.Any(error =>
                error.Code == ConfigurationManifestFailureCodes.ContractInvalid
                && error.Path == "$.metadata.export"))
            .IsTrue();
    }

    [Test]
    public async Task Validate_DuplicateTenantSlug_RejectsOrdinalDuplicate()
    {
        ConfigurationManifestV1Alpha2 valid = ConfigurationManifestTestData.Valid();
        ConfigurationManifestTenantV1Alpha2 tenant = valid.Spec.Tenants[0];
        ConfigurationManifestV1Alpha2 manifest = valid with
        {
            Spec = valid.Spec with { Tenants = [tenant, tenant] }
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(manifest);

        await Assert.That(HasCode(result, ConfigurationManifestFailureCodes.TenantDuplicate)).IsTrue();
    }

    [Test]
    public async Task Validate_SensitiveSetting_RejectsBeforeGenericAllowlistFailure()
    {
        const string suppliedValue = "must-not-appear";
        var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [InfrastructureSecretSettingKeys.Reporting.OspreyApiKey] = ConfigurationManifestTestData.Json($"\"{suppliedValue}\"")
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(ConfigurationManifestTestData.Valid(settings: settings));

        await Assert.That(HasCode(result, ConfigurationManifestFailureCodes.SensitiveKeyForbidden)).IsTrue();
        await Assert.That(result.Errors.Any(error => error.Message.Contains(
            suppliedValue,
            StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Validate_UnknownSetting_RejectsClosedAllowlist()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["made.up.key"] = ConfigurationManifestTestData.Json("true")
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(ConfigurationManifestTestData.Valid(settings: settings));

        await Assert.That(HasCode(result, ConfigurationManifestFailureCodes.KeyNotAllowed)).IsTrue();
    }

    [Test]
    public async Task Validate_BooleanString_RejectsWithoutCoercion()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["events.require_approval"] = ConfigurationManifestTestData.Json("\"false\"")
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(ConfigurationManifestTestData.Valid(settings: settings));

        await Assert.That(HasCode(result, ConfigurationManifestFailureCodes.ValueInvalid)).IsTrue();
    }

    [Test]
    public async Task Validate_EnumCasing_RejectsOrdinalMismatch()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["appearance.default_theme_mode"] = ConfigurationManifestTestData.Json("\"System\"")
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(ConfigurationManifestTestData.Valid(settings: settings));

        await Assert.That(HasCode(result, ConfigurationManifestFailureCodes.ValueInvalid)).IsTrue();
    }

    [Test]
    public async Task Validate_DisabledIntakeWithOpenSubmissionAndNoApproval_RejectsCrossPolicy()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["event_reporting.intake_enabled"] = ConfigurationManifestTestData.Json("false"),
            ["events.require_approval"] = ConfigurationManifestTestData.Json("false"),
            ["events.user_submission_enabled"] = ConfigurationManifestTestData.Json("true")
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(ConfigurationManifestTestData.Valid(settings: settings));

        ConfigurationManifestValidationError error = result.Errors.Single(candidate =>
            candidate.Code == ConfigurationManifestFailureCodes.CrossReferenceInvalid);
        await Assert.That(error.ReasonCode).IsEqualTo(
            ReportingIntakePolicyReasonCodes.UnsafePublicationPolicy);
    }

    [Test]
    public async Task Validate_DisabledIntakeWithApproval_IsValid()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["event_reporting.intake_enabled"] = ConfigurationManifestTestData.Json("false"),
            ["events.require_approval"] = ConfigurationManifestTestData.Json("true")
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(ConfigurationManifestTestData.Valid(settings: settings));

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_UnknownDocument_RejectsClosedAllowlist()
    {
        var documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(StringComparer.Ordinal)
        {
            ["tenant.event_defaults"] = BrandingDocument("{}")
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(ConfigurationManifestTestData.Valid(documents: documents));

        await Assert.That(HasCode(result, ConfigurationManifestFailureCodes.KeyNotAllowed)).IsTrue();
    }

    [Test]
    public async Task Validate_BrandingHttpUrl_RejectsDocument()
    {
        var documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(StringComparer.Ordinal)
        {
            [SettingsDocumentKeys.Tenant.Branding] = BrandingDocument(
                """{"logoUrl":"http://example.test/logo.svg"}""")
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(ConfigurationManifestTestData.Valid(documents: documents));

        await Assert.That(HasCode(result, ConfigurationManifestFailureCodes.DocumentInvalid)).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(UnsafeBrandingUrls))]
    public async Task Validate_InstanceBrandingHttpsUrlWithUnsafeComponents_RejectsSafely(
        string suppliedUrl)
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [BrandingSettingDefinitions.LogoUrl.Key] = ConfigurationManifestTestData.Json($"\"{suppliedUrl}\""),
            [BrandingSettingDefinitions.FaviconUrl.Key] = ConfigurationManifestTestData.Json($"\"{suppliedUrl}\""),
            [BrandingSettingDefinitions.CustomCssUrl.Key] = ConfigurationManifestTestData.Json($"\"{suppliedUrl}\"")
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(ConfigurationManifestTestData.Valid(instanceSettings: settings));

        await Assert.That(HasCode(result, ConfigurationManifestFailureCodes.ValueInvalid)).IsTrue();
        await Assert.That(result.Errors.Any(error => error.Message.Contains(
            suppliedUrl,
            StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    [MethodDataSource(nameof(UnsafeBrandingUrls))]
    public async Task Validate_BrandingHttpsUrlWithUnsafeComponents_RejectsSafely(
        string suppliedUrl)
    {
        var documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(StringComparer.Ordinal)
        {
            [SettingsDocumentKeys.Tenant.Branding] = BrandingDocument(
                $$"""{"logoUrl":"{{suppliedUrl}}","faviconUrl":"{{suppliedUrl}}","customCssUrl":"{{suppliedUrl}}"}""")
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(ConfigurationManifestTestData.Valid(documents: documents));

        await Assert.That(HasCode(result, ConfigurationManifestFailureCodes.DocumentInvalid)).IsTrue();
        await Assert.That(result.Errors.Any(error => error.Message.Contains(
            suppliedUrl,
            StringComparison.Ordinal))).IsFalse();
    }

    public static IEnumerable<string> UnsafeBrandingUrls()
    {
        yield return "https://user:password@example.test/logo.svg";
        yield return "https://example.test/logo.svg?tenant=secret";
        yield return "https://example.test/logo.svg#secret";
    }

    [Test]
    public async Task Validate_BrandingHttpsUrlsAndNullableFields_IsValid()
    {
        var documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(StringComparer.Ordinal)
        {
            [SettingsDocumentKeys.Tenant.Branding] = BrandingDocument(
                """{"displayName":"Community","logoUrl":"https://example.test/logo.svg","faviconUrl":null}""")
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(ConfigurationManifestTestData.Valid(documents: documents));

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_PaidPolicyNarrowingDocument_IsValid()
    {
        var documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
            StringComparer.Ordinal)
        {
            ["tenant.paid_event_policy"] = PaidPolicyDocument()
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(
                ConfigurationManifestTestData.Valid(documents: documents));

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_PaidPolicyUnsupportedCurrencyRiskCombination_RejectsDocument()
    {
        const string payload =
            """
            {
              "isPaymentsEnabled": true,
              "allowedOrganizerKindIds": [2],
              "requiresLocalVerification": true,
              "allowedCurrencyCodes": ["USD"],
              "defaultCurrencyCode": "EUR",
              "refundProtectionIds": [1, 2, 3, 4, 5, 6, 7],
              "currencyRiskLimits": [
                {
                  "currencyCode": "EUR",
                  "perEventSalesCeilingMinor": 10000
                }
              ],
              "requiresFirstPaidEventReview": true,
              "farFutureReviewThresholdDays": 90
            }
            """;
        var documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
            StringComparer.Ordinal)
        {
            ["tenant.paid_event_policy"] = PaidPolicyDocument(payload)
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(
                ConfigurationManifestTestData.Valid(documents: documents));

        await Assert.That(HasCode(
            result,
            ConfigurationManifestFailureCodes.DocumentInvalid)).IsTrue();
    }

    [Test]
    public async Task Validate_NullTenantDocument_RejectsSafely()
    {
        var documents =
            new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                StringComparer.Ordinal)
            {
                [SettingsDocumentKeys.Tenant.Branding] = null!
            };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(
                ConfigurationManifestTestData.Valid(documents: documents));

        await Assert.That(HasCode(
            result,
            ConfigurationManifestFailureCodes.DocumentInvalid)).IsTrue();
    }

    [Test]
    public async Task Validate_PaidPolicySovereignFields_RejectWithoutReflectingValues()
    {
        const string suppliedValue = "must-not-appear";
        const string payload =
            """
            {
              "isPaymentsEnabled": true,
              "allowedOrganizerKindIds": [2],
              "requiresLocalVerification": true,
              "allowedCurrencyCodes": ["USD"],
              "defaultCurrencyCode": "USD",
              "refundProtectionIds": [1, 2, 3, 4, 5, 6, 7],
              "currencyRiskLimits": [],
              "requiresFirstPaidEventReview": true,
              "farFutureReviewThresholdDays": 90,
              "operatorIdentity": "must-not-appear",
              "officialStatus": "must-not-appear",
              "providerCredentials": "must-not-appear",
              "providerProfiles": "must-not-appear",
              "connectedAccounts": "must-not-appear",
              "chargeType": "must-not-appear",
              "refundExecution": "must-not-appear",
              "disputeHandling": "must-not-appear",
              "liability": "must-not-appear",
              "negativeBalances": "must-not-appear",
              "buyerEmail": "must-not-appear",
              "saleControl": "must-not-appear",
              "providerHandoff": "must-not-appear",
              "reconciliation": "must-not-appear"
            }
            """;
        var documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
            StringComparer.Ordinal)
        {
            ["tenant.paid_event_policy"] = PaidPolicyDocument(payload)
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(
                ConfigurationManifestTestData.Valid(documents: documents));

        await Assert.That(HasCode(
            result,
            ConfigurationManifestFailureCodes.DocumentInvalid)).IsTrue();
        await Assert.That(result.Errors.Any(error => error.Message.Contains(
            suppliedValue,
            StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Validate_PaidPolicyCrossTenantTarget_RejectsDocument()
    {
        const string payload =
            """
            {
              "isPaymentsEnabled": true,
              "allowedOrganizerKindIds": [2],
              "requiresLocalVerification": true,
              "allowedCurrencyCodes": ["USD"],
              "defaultCurrencyCode": "USD",
              "refundProtectionIds": [1, 2, 3, 4, 5, 6, 7],
              "currencyRiskLimits": [],
              "requiresFirstPaidEventReview": true,
              "farFutureReviewThresholdDays": 90,
              "tenantId": "0199464e-e388-7f56-9281-cefabd6a5674"
            }
            """;
        var documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
            StringComparer.Ordinal)
        {
            ["tenant.paid_event_policy"] = PaidPolicyDocument(payload)
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(
                ConfigurationManifestTestData.Valid(documents: documents));

        await Assert.That(HasCode(
            result,
            ConfigurationManifestFailureCodes.DocumentInvalid)).IsTrue();
    }

    [Test]
    public async Task Validate_PaidPolicyCallerSelectedInstanceRevision_RejectsDocument()
    {
        string invalidPayload = PaidPolicyPayload().Replace(
            "\"isPaymentsEnabled\": true",
            "\"instancePolicyVersion\": 777,\n  \"isPaymentsEnabled\": true",
            StringComparison.Ordinal);
        var documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
            StringComparer.Ordinal)
        {
            ["tenant.paid_event_policy"] = PaidPolicyDocument(invalidPayload)
        };

        ConfigurationManifestValidationResult result =
            ConfigurationManifestValidator.Validate(
                ConfigurationManifestTestData.Valid(documents: documents));

        await Assert.That(HasCode(
            result,
            ConfigurationManifestFailureCodes.DocumentInvalid)).IsTrue();
        await Assert.That(JsonSerializer.Serialize(result).Contains(
            "777",
            StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Validate_PaidPolicyNullCollectionsAndRiskElements_RejectSafely()
    {
        string valid = PaidPolicyPayload();
        string[] invalidPayloads =
        [
            valid.Replace(
                "\"allowedOrganizerKindIds\": [2]",
                "\"allowedOrganizerKindIds\": null",
                StringComparison.Ordinal),
            valid.Replace(
                "\"allowedCurrencyCodes\": [\"USD\"]",
                "\"allowedCurrencyCodes\": null",
                StringComparison.Ordinal),
            valid.Replace(
                "\"refundProtectionIds\": [1, 2, 3, 4, 5, 6, 7]",
                "\"refundProtectionIds\": null",
                StringComparison.Ordinal),
            valid.Replace(
                "\"currencyRiskLimits\": [",
                "\"currencyRiskLimits\": [\n    null,",
                StringComparison.Ordinal),
            ReplaceRiskLimitsWithNull(valid)
        ];

        foreach (string invalidPayload in invalidPayloads)
        {
            var documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                StringComparer.Ordinal)
            {
                ["tenant.paid_event_policy"] = PaidPolicyDocument(invalidPayload)
            };

            ConfigurationManifestValidationResult result =
                ConfigurationManifestValidator.Validate(
                    ConfigurationManifestTestData.Valid(documents: documents));

            await Assert.That(HasCode(
                result,
                ConfigurationManifestFailureCodes.DocumentInvalid)).IsTrue();
        }
    }

    private static ConfigurationManifestDocumentV1Alpha2 BrandingDocument(string payload) =>
        new()
        {
            SchemaVersion = 1,
            Payload = ConfigurationManifestTestData.Json(payload)
        };

    private static ConfigurationManifestDocumentV1Alpha2 PaidPolicyDocument(
        string? payload = null) =>
        new()
        {
            SchemaVersion = 1,
            Payload = ConfigurationManifestTestData.Json(payload ?? PaidPolicyPayload())
        };

    private static string PaidPolicyPayload() =>
        """
        {
          "isPaymentsEnabled": true,
          "allowedOrganizerKindIds": [2],
          "requiresLocalVerification": true,
          "allowedCurrencyCodes": ["USD"],
          "defaultCurrencyCode": "USD",
          "refundProtectionIds": [1, 2, 3, 4, 5, 6, 7],
          "currencyRiskLimits": [
            {
              "currencyCode": "USD",
              "perEventSalesCeilingMinor": 10000,
              "perEventSalesCountCeiling": 100,
              "rollingOrganizerSalesCeilingMinor": 50000,
              "rollingOrganizerSalesCountCeiling": 500,
              "rollingOrganizerWindowDays": 30,
              "highValueReviewThresholdMinor": 5000
            }
          ],
          "requiresFirstPaidEventReview": true,
          "farFutureReviewThresholdDays": 90
        }
        """;

    private static string ReplaceRiskLimitsWithNull(string payload)
    {
        const string startMarker = "\"currencyRiskLimits\": [";
        const string endMarker = "],\n  \"requiresFirstPaidEventReview\"";
        int start = payload.IndexOf(startMarker, StringComparison.Ordinal);
        int end = payload.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            throw new InvalidOperationException(
                "Paid-policy test payload markers were not found.");
        }

        return payload[..start]
            + "\"currencyRiskLimits\": null"
            + payload[(end + 1)..];
    }

    private static bool HasCode(
        ConfigurationManifestValidationResult result,
        string code) =>
        result.Errors.Any(error => error.Code == code);
}
