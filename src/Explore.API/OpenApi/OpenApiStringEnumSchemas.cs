using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.DTOs.Geocoding;
using Explore.Application.DTOs.Location;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.DTOs.OrganizationTenantEvidence;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Features.EventReporting.Models;
using Explore.Application.Models;
using Explore.Application.Models.PublicExperience;
using Explore.Application.Onboarding;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Enums.Analytics;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Explore.API.OpenApi;

/// <summary>
/// Explicit allowlist of public enum schemas that must be represented as strings in OpenAPI.
/// </summary>
internal static class OpenApiStringEnumSchemaCatalog
{
    private static IReadOnlyCollection<Type> EagerNestedEnumTypes { get; } =
    [
        typeof(AddressProviderOutcome),
        typeof(AdmissionCheckInDependencyStatus),
        typeof(AdmissionCheckInOperationalAction),
        typeof(AdmissionCheckInOperationalStatus),
        typeof(ConfigurationImportPreviewCategory),
        typeof(ConfigurationImportOperationKind),
        typeof(ConfigurationImportOperationStatus),
        typeof(ConfigurationImportEffectStatus),
        typeof(ConfigurationImportScope),
        typeof(ConfigurationImportSessionState),
        typeof(LocationAddressSourceEnum),
        typeof(LocationAddressVisibilityEnum)
    ];

    public static IReadOnlyCollection<Type> EnumTypes { get; } =
    [
        typeof(AddressProviderOutcome),
        typeof(AdmissionCheckInAction),
        typeof(AdmissionCheckInDependencyStatus),
        typeof(AdmissionCheckInOperationalAction),
        typeof(AdmissionCheckInOperationalReasonCode),
        typeof(AdmissionCheckInOperationalStatus),
        typeof(AdmissionCheckInUndoReasonCodeEnum),
        typeof(CustomPropertyProjectionScopeType),
        typeof(CustomPropertyProjectionState),
        typeof(CustomPropertyFilterOperator),
        typeof(ConfigurationImportApplyMode),
        typeof(ConfigurationImportOperationKind),
        typeof(ConfigurationImportOperationStatus),
        typeof(ConfigurationImportEffectStatus),
        typeof(ConfigurationImportPreviewCategory),
        typeof(ConfigurationImportScope),
        typeof(ConfigurationImportSessionState),
        typeof(ConfigurationPortabilityClass),
        typeof(ConfigurationManifestExportView),
        typeof(DeclineBehavior),
        typeof(EmailDispatchUnknownReconciliationOutcome),
        typeof(EntityTypeName),
        typeof(EventReportDecisionKind),
        typeof(EventReportProviderEvidenceMode),
        typeof(EventReportPriority),
        typeof(EventReportSeverityHint),
        typeof(EventOrganizerClaimReviewDecisionDto),
        typeof(EventLocationDisclosureField),
        typeof(EventLocationDisclosureFieldClass),
        typeof(EventLocationDisclosureFields),
        typeof(EventLocationDisclosurePolicyGate),
        typeof(EventLocationDisclosurePurpose),
        typeof(EventLocationDisclosureState),
        typeof(EventRoleAssignmentStatus),
        typeof(ExposureLevel),
        typeof(GuestRecoveryPolicyEnum),
        typeof(GenderSegregationMode),
        typeof(HomeDiscoveryMode),
        typeof(HomeDiscoverySectionStatus),
        typeof(IntegrationSyncRecoveryDecision),
        typeof(KeycloakBootstrapMode),
        typeof(LocationAddressSourceEnum),
        typeof(LocationAddressVisibilityEnum),
        typeof(ManagedProviderOrganizerKindDto),
        typeof(OrganizationTenantEvidenceReviewDecisionDto),
        typeof(PosthogCookielessMode),
        typeof(PosthogPersonProfiles),
        typeof(PrayerTime),
        typeof(PromotionRecommendation),
        typeof(PropertyType),
        typeof(PublicExperienceCtaPlacement),
        typeof(PublicExperienceCtaStyle),
        typeof(PublicExperienceHomeBlockKind),
        typeof(PublicExperienceMode),
        typeof(PublicExperiencePrimaryOrganizationState),
        typeof(RegistrationAnswerSubjectTypeEnum),
        typeof(RoleEnum),
        typeof(SessionStartTimeType),
        typeof(SessionEndTimeType),
        typeof(SettingSource),
        typeof(SkillLevel),
        typeof(SupportAccessModeEnum),
        typeof(BatchUpdateMode),
        typeof(DeploymentMode)
    ];

    public static bool IsStringEnum(Type type)
        => type.IsEnum && EnumTypes.Contains(type);

    public static bool RequiresEagerNestedComponent(Type type) =>
        EagerNestedEnumTypes.Contains(type);

    public static bool TryGetEnumType(string schemaName, out Type enumType)
    {
        enumType = EnumTypes.FirstOrDefault(type => string.Equals(type.Name, schemaName, StringComparison.Ordinal))
            ?? typeof(void);

        return enumType != typeof(void);
    }
}

internal static class OpenApiStringEnumSchemaMutator
{
    /// <summary>
    /// Serializer used only to discover the true wire spelling of an enum that declares its own
    /// converter. No global string-enum converter is registered here on purpose: a member without a
    /// type-level converter must fall back to its CLR name, matching <c>JsonStringEnumConverter</c>.
    /// </summary>
    private static readonly JsonSerializerOptions WireNameProbe = new();

    public static void Apply(OpenApiSchema schema, Type enumType)
    {
        schema.Type = JsonSchemaType.String;
        schema.Format = null;
        schema.Pattern = null;
        schema.Enum = GetWireNames(enumType)
            .Select(name => JsonValue.Create(name)!)
            .ToList<JsonNode>();
    }

    /// <summary>
    /// Resolves the values the API actually serializes. Enums such as
    /// <see cref="EventLocationDisclosureState"/> carry a custom converter that emits snake_case, so
    /// publishing CLR names would ship a contract no generated client could deserialize.
    /// </summary>
    internal static IReadOnlyList<string> GetWireNames(Type enumType)
    {
        ArgumentNullException.ThrowIfNull(enumType);
        bool hasCustomConverter = enumType
            .GetCustomAttributes(typeof(JsonConverterAttribute), inherit: false)
            .Length > 0;

        var names = new List<string>();
        foreach (object value in Enum.GetValues(enumType))
        {
            names.Add(hasCustomConverter
                ? SerializeWireName(value, enumType)
                : Enum.GetName(enumType, value)!);
        }

        return names;
    }

    private static string SerializeWireName(object value, Type enumType)
    {
        string json = JsonSerializer.Serialize(value, enumType, WireNameProbe);
        return JsonSerializer.Deserialize<string>(json, WireNameProbe)
            ?? Enum.GetName(enumType, value)!;
    }
}

/// <summary>
/// Native OpenAPI document transformer that normalizes known public enum component schemas.
/// </summary>
internal sealed class OpenApiStringEnumDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Components?.Schemas is null)
        {
            return Task.CompletedTask;
        }

        foreach (var (schemaName, schema) in document.Components.Schemas)
        {
            var openApiSchema = schema as OpenApiSchema;

            if (openApiSchema is not null
                && OpenApiStringEnumSchemaCatalog.TryGetEnumType(schemaName, out var enumType))
            {
                OpenApiStringEnumSchemaMutator.Apply(openApiSchema, enumType);
            }

            if (schema.Properties is not null
                && schema.Properties.ContainsKey("endTimeType"))
            {
                schema.Properties["endTimeType"] =
                    new OpenApiSchemaReference(nameof(SessionEndTimeType), document);
            }

            NormalizeGuestRecoveryPolicy(schema, document);
        }

        return Task.CompletedTask;
    }

    private static void NormalizeGuestRecoveryPolicy(IOpenApiSchema schema, OpenApiDocument document)
    {
        if (schema.Properties is null)
        {
            return;
        }

        if (schema.Properties.TryGetValue("guestRecoveryPolicy", out var guestRecoveryPolicy)
            && guestRecoveryPolicy is OpenApiSchema { Type: JsonSchemaType.Integer })
        {
            schema.Properties["guestRecoveryPolicy"] = new OpenApiSchema
            {
                OneOf =
                [
                    new OpenApiSchema { Type = JsonSchemaType.Null },
                    new OpenApiSchemaReference(nameof(GuestRecoveryPolicyEnum), document)
                ]
            };
        }

        foreach (var property in schema.Properties.Values.OfType<OpenApiSchema>())
        {
            NormalizeGuestRecoveryPolicy(property, document);
        }
    }
}

/// <summary>
/// Swashbuckle schema filter that mirrors native string-enum schema normalization.
/// </summary>
internal sealed class OpenApiStringEnumSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is OpenApiSchema openApiSchema
            && OpenApiStringEnumSchemaCatalog.IsStringEnum(context.Type))
        {
            OpenApiStringEnumSchemaMutator.Apply(openApiSchema, context.Type);
        }
    }
}
