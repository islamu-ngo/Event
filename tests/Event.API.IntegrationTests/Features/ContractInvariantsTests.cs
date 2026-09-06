// ABOUTME: Contract invariant tests over the exported OpenAPI document (/openapi/event-api.json).
// ABOUTME: RED tests documenting 464-duplicate-operation defect - enforces invariants expected post-stabilization.

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.Constants;
using Explore.API.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Invariants every canonical OpenAPI document MUST satisfy. These are enforced at the
/// contract boundary so drift is caught at CI time, not at client-generation time.
///
/// Strategy:
/// 1. Fetch the runtime OpenAPI document at <c>/openapi/event-api.json</c>.
/// 2. Walk <c>paths.*.operations.*</c> and assert invariants.
///
/// Phase: P0 Guardrails (api-contract-stabilization plan).
/// Current state: these tests are EXPECTED TO FAIL until Phase 2 (delete URL-segment alias)
/// and Phase 3 (stable operationId) land. They prove the defect exists and prevent regression
/// once fixed.
/// </summary>
[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public class ContractInvariantsTests
{
    private const string OpenApiEndpoint = "/openapi/islamu-event.json";

    /// <summary>HTTP verbs that carry an operation in OpenAPI 3.0.</summary>
    private static readonly string[] HttpVerbs =
    [
        "get", "post", "put", "delete", "patch", "head", "options", "trace"
    ];

    /// <summary>
    /// Placeholder/fallback names that indicate a missing or broken <c>operationId</c>.
    /// These are what NSwag synthesizes when the OpenAPI doc lacks a stable id.
    /// </summary>
    private static readonly string[] BannedOperationIdPatterns =
    [
        "GET", "GET2", "GET3", "POST", "POST2", "POST3",
        "PUT", "PUT2", "PUT3", "DELETE", "DELETE2", "DELETE3",
        "PATCH", "PATCH2", "PATCH3"
    ];

    private static readonly IReadOnlyDictionary<string, string[]> TrustedBodyRecordSchemas =
        new Dictionary<string, string[]>(System.StringComparer.Ordinal)
        {
            ["CreateCategoryDto"] = ["masterCode", "fullName"],
            ["ImportEventRequestDto"] = ["title", "ownerActorId", "provenanceSource", "provenanceExternalId", "participationConfiguration"],
            ["CreateEventSessionDto"] = [],
            ["CreateEventSessionAgendaItemDto"] = ["title"],
            ["CreateEventSessionLanguageDto"] = [],
            ["CreateEventSessionSpeakerDto"] = [],
            ["CreateLocationDto"] = ["fullName", "address", "postcode", "country", "city"],
            ["CreateTagDto"] = ["masterCode", "fullName"],
        };

    private readonly ContractApiFixture _fixture;

    public ContractInvariantsTests(ContractApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task OpenApiDocument_IsReachable_AndReturnsJson()
    {
        var response = await _fixture.Client.GetAsync(OpenApiEndpoint);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
    }

    [Test]
    public async Task OpenApiDocument_DoesNotExposeClientAssertedIdentityCrud()
    {
        using var document = await GetOpenApiDocumentAsync();
        var paths = EnumeratePaths(document).ToList();

        await Assert.That(paths)
            .DoesNotContain(path => path.StartsWith("/api/userexternallogin", System.StringComparison.OrdinalIgnoreCase));
        await Assert.That(paths)
            .DoesNotContain(path => path.StartsWith("/api/indexeddid", System.StringComparison.OrdinalIgnoreCase));
        await Assert.That(paths)
            .DoesNotContain(path => path.StartsWith("/api/actorkeystore", System.StringComparison.OrdinalIgnoreCase));
        await Assert.That(paths)
            .DoesNotContain(path => path.StartsWith("/api/syncstate", System.StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task OpenApiDocument_TrustedBodyRecordsOmitTenantAuthorityAndPreserveRequiredMetadata()
    {
        using var document = await GetOpenApiDocumentAsync();

        foreach (var (schemaName, expectedRequired) in TrustedBodyRecordSchemas)
        {
            var schema = GetSchema(document, schemaName);
            var properties = GetSchemaPropertyNames(schema);

            await Assert.That(properties).DoesNotContain("tenantId")
                .Because($"{schemaName} must not accept current-tenant authority from JSON.");
            await Assert.That(GetRequiredPropertyNames(schema)).IsEquivalentTo(expectedRequired)
                .Because($"{schemaName} requiredness must match runtime record binding.");
        }
    }

    [Test]
    public async Task OpenApiDocument_ContainsNoUrlSegmentVersionedPaths()
    {
        using var document = await GetOpenApiDocumentAsync();

        var versionedPaths = EnumeratePaths(document)
            .Where(path => path.StartsWith("/api/v", System.StringComparison.OrdinalIgnoreCase)
                && path.Length > 6
                && char.IsDigit(path[6]))
            .ToList();

        await Assert.That(versionedPaths)
            .IsEmpty()
            .Because($"URL-segment versioning is retired. Found {versionedPaths.Count} versioned path(s): {string.Join(", ", versionedPaths.Take(5))}");
    }

    [Test]
    public async Task OpenApiDocument_ContainsNoDuplicatePathMethodPairs()
    {
        using var document = await GetOpenApiDocumentAsync();

        var pairs = EnumerateOperations(document)
            .Select(op => $"{op.Method.ToUpperInvariant()} {op.Path}")
            .ToList();

        var duplicates = pairs
            .GroupBy(p => p)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} (x{g.Count()})")
            .ToList();

        await Assert.That(duplicates)
            .IsEmpty()
            .Because($"Every (method, path) pair must appear exactly once. Duplicates: {string.Join("; ", duplicates.Take(5))}");
    }

    [Test]
    public async Task OpenApiDocument_EveryOperationHasOperationId()
    {
        using var document = await GetOpenApiDocumentAsync();

        var missing = EnumerateOperations(document)
            .Where(op => string.IsNullOrWhiteSpace(op.OperationId))
            .Select(op => $"{op.Method.ToUpperInvariant()} {op.Path}")
            .ToList();

        await Assert.That(missing)
            .IsEmpty()
            .Because($"Every operation MUST declare a stable operationId. {missing.Count} missing: {string.Join("; ", missing.Take(5))}");
    }

    [Test]
    public async Task OpenApiDocument_OperationIdsAreUnique()
    {
        using var document = await GetOpenApiDocumentAsync();

        var duplicates = EnumerateOperations(document)
            .Where(op => !string.IsNullOrWhiteSpace(op.OperationId))
            .GroupBy(op => op.OperationId!)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} (x{g.Count()})")
            .ToList();

        await Assert.That(duplicates)
            .IsEmpty()
            .Because($"operationId values must be globally unique. Duplicates: {string.Join("; ", duplicates.Take(5))}");
    }

    [Test]
    public async Task OpenApiDocument_OperationIdsMatchTheCompleteRouteNameCatalog()
    {
        using var document = await GetOpenApiDocumentAsync();

        var operationIds = EnumerateOperations(document)
            .Select(operation => operation.OperationId)
            .Where(operationId => operationId is not null)
            .Cast<string>()
            .OrderBy(operationId => operationId, System.StringComparer.Ordinal)
            .ToArray();
        var intentionallyPrivateOperations = _fixture.Factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<ApiExplorerSettingsAttribute>()?.IgnoreApi == true)
            .Select(endpoint => endpoint.Metadata.GetMetadata<IRouteNameMetadata>()?.RouteName
                ?? endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.AttributeRouteInfo?.Name)
            .OfType<string>()
            .ToHashSet(System.StringComparer.Ordinal);
        var routeNames = typeof(RouteNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(routeName => !intentionallyPrivateOperations.Contains(routeName))
            .OrderBy(routeName => routeName, System.StringComparer.Ordinal)
            .ToArray();

        await Assert.That(operationIds.Intersect(intentionallyPrivateOperations, System.StringComparer.Ordinal)).IsEmpty()
            .Because("Endpoint metadata explicitly excludes private capabilities from public discovery.");
        await Assert.That(operationIds).IsEquivalentTo(routeNames)
            .Because("Every public route name must generate exactly one stable OpenAPI operationId.");
    }

    [Test]
    public async Task OpenApiDocument_ActorDidIngressRemainsAScalarRouteParameter()
    {
        using var document = await GetOpenApiDocumentAsync();
        var operation = document.RootElement.GetProperty("paths")
            .GetProperty("/api/actor/by-did/{did}")
            .GetProperty("get");
        var parameter = operation.GetProperty("parameters").EnumerateArray()
            .Single(value => value.GetProperty("name").GetString() == "did");

        await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(RouteNames.GetActorByDid);
        await Assert.That(parameter.GetProperty("in").GetString()).IsEqualTo("path");
        await Assert.That(parameter.GetProperty("required").GetBoolean()).IsTrue();
        await Assert.That(parameter.GetProperty("schema").GetProperty("type").GetString()).IsEqualTo("string");
    }

    [Test]
    public async Task OpenApiDocument_NoOperationIdsUsePlaceholderFallbackNames()
    {
        using var document = await GetOpenApiDocumentAsync();

        var banned = EnumerateOperations(document)
            .Where(op => !string.IsNullOrWhiteSpace(op.OperationId))
            .Where(op => BannedOperationIdPatterns.Contains(op.OperationId, System.StringComparer.Ordinal)
                || EndsWithDigitBeforeSuffix(op.OperationId!, "Async")
                || EndsWithDigit(op.OperationId!))
            .Select(op => $"{op.OperationId} @ {op.Method.ToUpperInvariant()} {op.Path}")
            .ToList();

        await Assert.That(banned)
            .IsEmpty()
            .Because($"operationIds must be human-readable, not NSwag collision fallbacks (GET, POST2, TenantGET2, FooAsync2, ...). Found: {string.Join("; ", banned.Take(5))}");
    }

    [Test]
    public async Task SettingsControllers_ExposeCanonicalPatchActionsWithoutLegacyPutAttributes()
    {
        (MethodInfo Action, string Template, string OperationId)[] patchActions =
        [
            (
                typeof(TenantStorageSettingsController).GetMethod(nameof(TenantStorageSettingsController.PatchStorageSettings))!,
                string.Empty,
                RouteNames.PatchTenantStorageSettings),
            (
                typeof(TenantSettingsDocumentsController).GetMethod(nameof(TenantSettingsDocumentsController.PatchBranding))!,
                "branding",
                RouteNames.PatchTenantBrandingSettingsDocument),
            (
                typeof(FooterController).GetMethod(nameof(FooterController.PatchSettings))!,
                "settings",
                RouteNames.PatchTenantFooterSettings)
        ];

        foreach (var (action, template, operationId) in patchActions)
        {
            var patch = action.GetCustomAttribute<HttpPatchAttribute>();

            await Assert.That(patch).IsNotNull();
            await Assert.That(patch!.Template).IsEqualTo(template);
            await Assert.That(patch.Name).IsEqualTo(operationId);
            await Assert.That(action.GetCustomAttribute<HttpPutAttribute>()).IsNull();
        }

        var footerGet = typeof(FooterController).GetMethod(nameof(FooterController.GetSettings))!;

        await Assert.That(footerGet.GetCustomAttribute<HttpGetAttribute>()?.Name)
            .IsEqualTo(RouteNames.GetTenantFooterSettings);
        await Assert.That(footerGet.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(footerGet.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
    }

    [Test]
    public async Task OpenApiDocument_CanonicalSettingsRoutesUsePatchAndNoLegacyPut()
    {
        using var document = await GetOpenApiDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");
        (string Path, string OperationId, string RequestSchema)[] patchRoutes =
        [
            ("/api/tenant/settings/storage", "PatchTenantStorageSettings", "PatchTenantStorageSettingsDto"),
            ("/api/tenant/settings/documents/branding", "PatchTenantBrandingSettingsDocument", "PatchTenantBrandingSettingsDocumentDto"),
            ("/api/footer/settings", "PatchTenantFooterSettings", "PatchTenantFooterSettingsDto")
        ];

        await AssertPatchRoutes(paths, patchRoutes);

        var footerGet = paths.GetProperty("/api/footer/settings").GetProperty("get");
        await Assert.That(GetStringProperty(footerGet, "operationId")).IsEqualTo("GetTenantFooterSettings");
        await Assert.That(GetStringProperty(footerGet, "x-endpoint-class")).IsEqualTo("Authenticated");

        string[] legacyOperationIds =
        [
            "UpdateTenantStorageSettings",
            "ReplaceTenantBrandingSettingsDocument",
            "UpdateTenantFooterSettings"
        ];
        var legacyOperations = EnumerateOperations(document)
            .Where(operation => legacyOperationIds.Contains(operation.OperationId, System.StringComparer.Ordinal))
            .Select(operation => operation.OperationId!)
            .ToList();

        await Assert.That(legacyOperations).IsEmpty();
    }

    [Test]
    public async Task OpenApiDocument_InstanceSettingsRoutesUsePatchWithDedicatedSchemas_AndNoOnboardingWriteAliases()
    {
        using var document = await GetOpenApiDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");
        (string Path, string OperationId, string RequestSchema)[] patchRoutes =
        [
            ("/api/instance/settings/modules", RouteNames.UpdateInstanceModuleSettings, "PatchModuleSettingsDto"),
            ("/api/instance/settings/events", RouteNames.UpdateInstanceEventPolicy, "PatchEventPolicyDto"),
            ("/api/instance/settings/organizations", RouteNames.UpdateInstanceOrganizationPolicy, "PatchOrganizationPolicyDto"),
            ("/api/instance/settings/branding", RouteNames.UpdateInstanceBrandingSettings, "PatchBrandingSettingsDto"),
            ("/api/instance/settings/domains", RouteNames.UpdateInstanceDomainSettings, "PatchDomainSettingsDto"),
            ("/api/instance/settings/tenant-delegation", RouteNames.UpdateInstanceTenantDelegationSettings, "PatchTenantDelegationSettingsDto"),
            ("/api/instance/settings/admin-portal", RouteNames.UpdateInstanceAdminPortalSettings, "PatchAdminPortalSettingsDto"),
            ("/api/instance/settings/ai-assistant", RouteNames.UpdateInstanceAiAssistantGovernanceSettings, "PatchAiAssistantGovernanceSettingsDto"),
            ("/api/instance/settings/mcp", RouteNames.UpdateInstanceMcpGovernanceSettings, "PatchMcpGovernanceSettingsDto"),
            ("/api/instance/settings/render-policy", RouteNames.UpdateInstanceRenderPolicySettings, "PatchRenderPolicySettingsDto"),
            ("/api/instance/settings/storage", RouteNames.UpdateInstanceStorageSettings, "PatchInstanceStorageSettingsDto"),
            ("/api/instance/settings/smtp", RouteNames.UpdateInstanceSmtpSettings, "PatchInstanceSmtpSettingsDto"),
            ("/api/instance/settings/resolver-config", RouteNames.UpdateInstanceResolverConfiguration, "PatchResolverConfigurationDto"),
            ("/api/instance/settings/analytics-governance", RouteNames.UpdateInstanceAnalyticsGovernanceSettings, "PatchAnalyticsGovernanceSettingsDto"),
            ("/api/instance/settings/footer-governance", RouteNames.UpdateFooterGovernanceSettings, "PatchFooterGovernanceSettingsDto"),
            ("/api/instance/settings/auth-provider", RouteNames.UpdateInstanceAuthProviderConfiguration, "PatchAuthProviderConfigurationDto"),
            ("/api/instance/settings/authz-provider", RouteNames.UpdateInstanceAuthorizationProviderConfiguration, "PatchAuthorizationProviderConfigurationDto")
        ];

        await AssertPatchRoutes(paths, patchRoutes);

        string[] obsoleteWriteAliases =
        [
            "/api/instanceonboarding/auth-provider-configuration",
            "/api/instanceonboarding/authz-provider-configuration"
        ];
        var exposedAliases = obsoleteWriteAliases
            .Where(path => paths.TryGetProperty(path, out var pathItem)
                && pathItem.TryGetProperty("put", out _))
            .ToList();

        await Assert.That(exposedAliases).IsEmpty();
    }

    [Test]
    public async Task OpenApiDocument_TenantStoragePatchSchemaIsGroupedAndPresenceAware()
    {
        using var document = await GetOpenApiDocumentAsync();
        var schema = GetSchema(document, "PatchTenantStorageSettingsDto");
        var properties = schema.GetProperty("properties");

        await Assert.That(GetSchemaPropertyNames(schema)).IsEquivalentTo(["policy", "s3"]);
        await Assert.That(GetRequiredPropertyNames(schema)).IsEmpty();
        await Assert.That(GetReferenceOrNullableReference(properties.GetProperty("policy")))
            .IsEqualTo("#/components/schemas/PatchTenantStoragePolicyDto");
        await Assert.That(GetReferenceOrNullableReference(properties.GetProperty("s3")))
            .IsEqualTo("#/components/schemas/PatchTenantStorageS3Dto");

        await AssertPresenceAwareProperties(
            document,
            "PatchTenantStoragePolicyDto",
            ["provider", "maxUploadBytes", "tenantQuotaBytes", "routes"]);
        await AssertPresenceAwareProperties(
            document,
            "PatchTenantStorageS3Dto",
            ["endpoint", "publicEndpoint", "bucketName", "region", "forcePathStyle", "uploadUrlExpirationMinutes"]);
    }

    [Test]
    public async Task OpenApiDocument_TenantBrandingPatchSchemaRequiresConcurrencyAndUsesOptionalGroups()
    {
        using var document = await GetOpenApiDocumentAsync();
        var schema = GetSchema(document, "PatchTenantBrandingSettingsDocumentDto");
        var properties = schema.GetProperty("properties");

        await Assert.That(GetSchemaPropertyNames(schema))
            .IsEquivalentTo(["expectedConcurrencyStamp", "displayName", "assets"]);
        await Assert.That(GetRequiredPropertyNames(schema)).IsEquivalentTo(["expectedConcurrencyStamp"]);
        await Assert.That(GetReferenceOrNullableReference(properties.GetProperty("displayName")))
            .IsEqualTo("#/components/schemas/PatchTenantBrandingDisplayNameDto");
        await Assert.That(GetReferenceOrNullableReference(properties.GetProperty("assets")))
            .IsEqualTo("#/components/schemas/PatchTenantBrandingAssetsDto");

        await AssertPresenceAwareProperties(document, "PatchTenantBrandingDisplayNameDto", ["value"]);
        await AssertPresenceAwareProperties(
            document,
            "PatchTenantBrandingAssetsDto",
            ["logoUrl", "faviconUrl", "customCssUrl"]);
    }

    [Test]
    public async Task OpenApiDocument_TenantFooterPatchSchemaContainsOnlyScalarSettingGroups()
    {
        using var document = await GetOpenApiDocumentAsync();
        var schema = GetSchema(document, "PatchTenantFooterSettingsDto");
        var properties = schema.GetProperty("properties");
        (string Property, string Schema)[] groups =
        [
            ("general", "PatchTenantFooterGeneralDto"),
            ("template", "PatchTenantFooterTemplateDto"),
            ("description", "PatchTenantFooterDescriptionDto"),
            ("socialLinks", "PatchTenantFooterSocialLinksDto"),
            ("copyright", "PatchTenantFooterCopyrightDto")
        ];

        await Assert.That(GetSchemaPropertyNames(schema))
            .IsEquivalentTo(groups.Select(group => group.Property));
        await Assert.That(GetRequiredPropertyNames(schema)).IsEmpty();

        foreach (var (property, groupSchema) in groups)
        {
            await Assert.That(GetReferenceOrNullableReference(properties.GetProperty(property)))
                .IsEqualTo($"#/components/schemas/{groupSchema}");
        }

        await AssertPresenceAwareProperties(document, "PatchTenantFooterGeneralDto", ["enabled", "showCookieSettingsLink"]);
        await AssertPresenceAwareProperties(document, "PatchTenantFooterTemplateDto", ["value"]);
        await AssertPresenceAwareProperties(document, "PatchTenantFooterDescriptionDto", ["show", "text"]);
        await AssertPresenceAwareProperties(document, "PatchTenantFooterSocialLinksDto", ["show", "items"]);
        await AssertPresenceAwareProperties(document, "PatchTenantFooterCopyrightDto", ["text"]);
    }

    [Test]
    public async Task OpenApiDocument_EventListResponseReferencesHalCollectionSchema()
    {
        using var document = await GetOpenApiDocumentAsync();

        JsonElement operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/event")
            .GetProperty("get");
        var content = operation
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content");

        await Assert.That(GetSchemaReference(content.GetProperty("application/hal+json; v=0.1")))
            .IsEqualTo("#/components/schemas/HalCollectionResourceOfEventDiscoveryItemDto")
            .Because("The canonical HAL event list response must reference the HAL collection wrapper schema.");
        await Assert.That(GetSchemaReference(content.GetProperty("application/json; v=0.1")))
            .IsEqualTo("#/components/schemas/HalCollectionResourceOfEventDiscoveryItemDto")
            .Because("The versioned JSON event list response must stay aligned with the HAL collection wrapper schema.");
        await Assert.That(GetStringProperty(operation, "x-output-cache-policy"))
            .IsEqualTo("EventDiscovery")
            .Because("Federated ingestion must evict only the dedicated event-discovery cache surface.");
    }

    [Test]
    public async Task OpenApiDocument_HalCollectionResourceSchemaHasLinksEmbeddedAndPagination()
    {
        using var document = await GetOpenApiDocumentAsync();

        var properties = GetSchemaProperties(document, "HalCollectionResourceOfEventDiscoveryItemDto");
        var expectedProperties = new[] { "_links", "_embedded", "pageNumber", "pageSize", "totalCount", "totalPages" };

        var missingProperties = expectedProperties
            .Where(propertyName => !properties.TryGetProperty(propertyName, out _))
            .ToList();

        await Assert.That(missingProperties)
            .IsEmpty()
            .Because($"The event list HAL collection wrapper must expose pagination plus HAL affordances. Missing: {string.Join(", ", missingProperties)}");
        await Assert.That(GetReference(properties.GetProperty("_embedded")))
            .IsEqualTo("#/components/schemas/HalCollectionEmbeddedOfEventDiscoveryItemDto")
            .Because("The HAL collection wrapper must reference the typed embedded collection schema.");
        await Assert.That(GetReference(properties.GetProperty("_links").GetProperty("additionalProperties")))
            .IsEqualTo("#/components/schemas/HalLink")
            .Because("HAL _links must be documented as a relation-name map of HalLink objects.");
    }

    [Test]
    public async Task OpenApiDocument_HalCollectionEmbeddedSchemaHasItemsArray()
    {
        using var document = await GetOpenApiDocumentAsync();

        var embeddedProperties = GetSchemaProperties(document, "HalCollectionEmbeddedOfEventDiscoveryItemDto");
        var items = embeddedProperties.GetProperty("items");

        await Assert.That(GetStringProperty(items, "type"))
            .IsEqualTo("array")
            .Because("The event list embedded HAL collection must expose an items array for embedded resources.");
        await Assert.That(GetReference(items.GetProperty("items")))
            .IsEqualTo("#/components/schemas/HalResourceOfEventDiscoveryItemDto")
            .Because("Embedded event list items must be typed as HAL resources, not as object arrays.");
    }

    [Test]
    public async Task OpenApiDocumentAtprotoSettingGroupHalResourceIsFlattened()
    {
        using var document = await GetOpenApiDocumentAsync();

        JsonElement properties = GetSchemaProperties(document, "HalResourceOfSettingGroupResponseDto");

        await Assert.That(properties.TryGetProperty("category", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("settings", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("_links", out _)).IsTrue();
    }

    [Test]
    public async Task OpenApiDocument_AtprotoSettingItemsReferenceEffectiveSettingDto()
    {
        using var document = await GetOpenApiDocumentAsync();

        JsonElement properties = GetSchemaProperties(document, "HalResourceOfSettingGroupResponseDto");
        JsonElement items = properties.GetProperty("settings").GetProperty("items");

        await Assert.That(GetReference(items))
            .IsEqualTo("#/components/schemas/EffectiveSettingDto")
            .Because("ATProto setting items must reuse the DTO schema whose source enum is serialized as a string.");
    }

    [Test]
    public async Task OpenApiDocument_HalCollectionEmbeddedItemsReferenceTypedHalResources()
    {
        using var document = await GetOpenApiDocumentAsync();

        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        var untypedEmbeddedCollections = schemas
            .EnumerateObject()
            .Where(schema => schema.Name.StartsWith("HalCollectionEmbeddedOf", System.StringComparison.Ordinal))
            .Select(schema =>
            {
                var itemReference = schema.Value.TryGetProperty("properties", out var properties)
                    && properties.TryGetProperty("items", out var items)
                    && items.TryGetProperty("items", out var itemSchema)
                    ? GetReference(itemSchema)
                    : null;

                return new { SchemaName = schema.Name, ItemReference = itemReference };
            })
            .Where(schema => string.IsNullOrWhiteSpace(schema.ItemReference)
                || !schema.ItemReference.StartsWith("#/components/schemas/HalResourceOf", System.StringComparison.Ordinal))
            .Select(schema => $"{schema.SchemaName} -> {schema.ItemReference ?? "<missing>"}")
            .ToList();

        await Assert.That(untypedEmbeddedCollections)
            .IsEmpty()
            .Because("Every HAL embedded collection must type its items as HAL resources so generated clients do not fall back to ICollection<object>. "
                + $"Untyped: {string.Join(", ", untypedEmbeddedCollections)}");
    }

    [Test]
    public async Task OpenApiDocument_HalResourceSchemaIsFlattenedAndHasHalProperties()
    {
        using var document = await GetOpenApiDocumentAsync();

        var properties = GetSchemaProperties(document, "HalResourceOfEventDto");
        var expectedProperties = new[] { "id", "title", "_links", "_embedded" };
        var missingProperties = expectedProperties
            .Where(propertyName => !properties.TryGetProperty(propertyName, out _))
            .ToList();

        await Assert.That(missingProperties)
            .IsEmpty()
            .Because($"The event HAL resource schema must stay flattened and expose HAL affordances. Missing: {string.Join(", ", missingProperties)}");
        await Assert.That(properties.TryGetProperty("data", out _))
            .IsFalse()
            .Because("HAL resource schemas must be flattened; a nested data property would break the client contract.");
    }

    [Test]
    public async Task OpenApiDocument_RegistrationOrderAndStudioContextHalResourcesAreFlattened()
    {
        using var document = await GetOpenApiDocumentAsync();

        var expectedPropertiesBySchema = new Dictionary<string, string[]>
        {
            ["HalResourceOfRegistrationOrderDto"] = ["id", "statusCode", "lines", "_links"],
            ["HalResourceOfStudioContextDto"] = ["_links"]
        };

        var missingProperties = expectedPropertiesBySchema
            .SelectMany(expected =>
            {
                var properties = GetSchemaProperties(document, expected.Key);
                return expected.Value
                    .Where(propertyName => !properties.TryGetProperty(propertyName, out _))
                    .Select(propertyName => $"{expected.Key}.{propertyName}");
            })
            .ToList();

        await Assert.That(missingProperties)
            .IsEmpty()
            .Because($"Registration order and Studio context HAL resources must expose typed DTO fields and server-authored affordances. Missing: {string.Join(", ", missingProperties)}");

        var registrationOrderProperties = GetSchemaProperties(document, "HalResourceOfRegistrationOrderDto");
        var studioContextProperties = GetSchemaProperties(document, "HalResourceOfStudioContextDto");

        await Assert.That(GetStringProperty(registrationOrderProperties.GetProperty("statusCode"), "type"))
            .IsEqualTo("string");
        await Assert.That(GetStringProperty(registrationOrderProperties.GetProperty("lines"), "type"))
            .IsEqualTo("array");
        await Assert.That(GetStringProperty(
                registrationOrderProperties.GetProperty("lines").GetProperty("items").GetProperty("properties").GetProperty("ticketTypeName"),
                "type"))
            .IsEqualTo("string");
        await Assert.That(GetReference(studioContextProperties.GetProperty("_links").GetProperty("additionalProperties")))
            .IsEqualTo("#/components/schemas/HalLink");
    }

    [Test]
    public async Task OpenApiDocument_CatalogedHalResourceSchemasAreFlattened()
    {
        using var document = await GetOpenApiDocumentAsync();
        var catalogedSchemaNames = new[]
        {
            "HalResourceOfEventDto",
            "HalResourceOfEventReportOptionsDto",
            "HalResourceOfMyEventReportDto",
            "HalResourceOfEventSessionDto",
            "HalResourceOfEventSessionGroupDto",
            "HalResourceOfCategoryDto",
            "HalResourceOfTagDto",
            "HalResourceOfLocationDto",
            "HalResourceOfOrganizationDto",
            "HalResourceOfActorDto",
            "HalResourceOfCustomPropertyDefinitionDto",
            "HalResourceOfEventCustomPropertyDefinitionDto",
            "HalResourceOfEventSessionCustomPropertyDefinitionDto",
            "HalResourceOfEventTemplateDto",
            "HalResourceOfEventSessionTemplateDto",
            "HalResourceOfGroupDto",
            "HalResourceOfLocationRoomDto",
            "HalResourceOfEventDayDto",
            "HalResourceOfEventAgendaItemDto",
            "HalResourceOfNotificationPreferenceMatrixDto",
            "HalResourceOfRegistrationAnswerFileDto",
            "HalResourceOfTemplateDiffDto"
        };

        var unflattened = catalogedSchemaNames
            .Where(schemaName =>
            {
                var properties = GetSchemaProperties(document, schemaName);
                return properties.TryGetProperty("data", out _)
                    || !properties.TryGetProperty("_links", out _)
                    || !properties.TryGetProperty("_embedded", out _);
            })
            .ToList();

        await Assert.That(unflattened)
            .IsEmpty()
            .Because($"Every explicitly cataloged HAL detail schema must be flattened and expose HAL affordances. Offenders: {string.Join(", ", unflattened)}");
    }

    [Test]
    public async Task OpenApiDocument_NonHalDtoSchemasAreNotMutatedAsHalResources()
    {
        using var document = await GetOpenApiDocumentAsync();

        var eventDtoProperties = GetSchemaProperties(document, "EventDto");

        await Assert.That(eventDtoProperties.TryGetProperty("_links", out _))
            .IsFalse()
            .Because("Plain DTO component schemas must not receive HAL affordance properties; only explicit HAL wrapper schemas are mutated.");
        await Assert.That(eventDtoProperties.TryGetProperty("_embedded", out _))
            .IsFalse()
            .Because("Plain DTO component schemas must stay reusable outside HAL wrappers.");
    }

    [Test]
    public async Task OpenApiDocument_TenantEffectiveSettingExposesNestedHalLinks()
    {
        using var document = await GetOpenApiDocumentAsync();
        var links = GetSchemaProperties(document, "ControlPlaneTenantEffectiveSettingDto")
            .GetProperty("_links");

        await Assert.That(GetReference(links.GetProperty("additionalProperties")))
            .IsEqualTo("#/components/schemas/HalLink");
    }

    [Test]
    public async Task OpenApiDocument_PublicHalDetailResourceSchemasAreNotEmpty()
    {
        using var document = await GetOpenApiDocumentAsync();

        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        var emptyHalDetailSchemas = schemas
            .EnumerateObject()
            .Where(schema => schema.Name.StartsWith("HalResourceOf", System.StringComparison.Ordinal))
            .Where(schema => !schema.Value.TryGetProperty("properties", out var properties)
                || properties.ValueKind != JsonValueKind.Object
                || !properties.EnumerateObject().Any())
            .Select(schema => schema.Name)
            .ToList();

        await Assert.That(emptyHalDetailSchemas)
            .IsEmpty()
            .Because($"Every public HAL detail wrapper must be explicitly evaluated and documented instead of remaining an empty schema. Empty: {string.Join(", ", emptyHalDetailSchemas)}");
    }

    [Test]
    public async Task OpenApiDocument_HalResourceArrayItemsRemainComponentReferences()
    {
        using var document = await GetOpenApiDocumentAsync();

        var properties = GetSchemaProperties(document, "HalResourceOfEventDto");

        await Assert.That(GetReference(properties.GetProperty("tags").GetProperty("items")))
            .IsEqualTo("#/components/schemas/TagListDto")
            .Because("HAL event tag arrays must reference the registered DTO component to avoid duplicate NSwag DTO artifacts.");
        await Assert.That(GetReference(properties.GetProperty("categories").GetProperty("items")))
            .IsEqualTo("#/components/schemas/CategoryListDto")
            .Because("HAL event category arrays must reference the registered DTO component to avoid duplicate NSwag DTO artifacts.");
    }

    [Test]
    public async Task OpenApiDocument_PublicEnumSchemasUseStringValues()
    {
        using var document = await GetOpenApiDocumentAsync();

        var roleEnum = GetSchema(document, "RoleEnum");
        var guestRecoveryPolicyEnum = GetSchema(document, "GuestRecoveryPolicyEnum");
        var deploymentMode = GetSchema(document, "DeploymentMode");

        await Assert.That(GetStringProperty(roleEnum, "type"))
            .IsEqualTo("string")
            .Because("The API serializes enums with JsonStringEnumConverter, so RoleEnum must not be documented as an integer.");
        await Assert.That(GetEnumValues(roleEnum))
            .IsEquivalentTo(["Admin", "Moderator", "Member", "TenantAdmin", "TenantModerator", "TenantMember", "OrgAdmin", "OrgModerator", "OrgMember", "GroupAdmin", "GroupModerator", "GroupMember", "EventOwner", "EventManager", "RegistrationManager", "CheckInStaff"])
            .Because("RoleEnum must expose the public string literals clients receive over JSON.");

        await Assert.That(GetStringProperty(guestRecoveryPolicyEnum, "type"))
            .IsEqualTo("string")
            .Because("GuestRecoveryPolicyEnum must remain documented as a string enum so the generated client does not fall back to integers.");
        await Assert.That(GetEnumValues(guestRecoveryPolicyEnum))
            .IsEquivalentTo(["VerifiedEmailRequired", "UnverifiedEmailAccepted", "EmailOptional", "CapabilityLinkOnly", "NoRecovery"])
            .Because("GuestRecoveryPolicyEnum must expose the exact public literals used by the API JSON contract.");

        await Assert.That(GetStringProperty(deploymentMode, "type"))
            .IsEqualTo("string")
            .Because("DeploymentMode must remain documented as the same string enum shape generated for the client.");
        await Assert.That(GetEnumValues(deploymentMode))
            .IsEquivalentTo(["SingleTenant", "MultiTenant"])
            .Because("DeploymentMode must expose public string literals, not numeric enum values.");
    }

    [Test]
    public async Task OpenApiDocument_EndpointPostureExtensionsExposeRateCacheAndTenantMetadata()
    {
        using var document = await GetOpenApiDocumentAsync();
        var operations = EnumerateOperations(document).ToList();

        var rateLimitPolicies = operations
            .Select(operation => GetStringProperty(operation.Operation, "x-rate-limit-policy"))
            .Where(policy => !string.IsNullOrWhiteSpace(policy))
            .Distinct()
            .ToList();
        var cachePolicies = operations
            .Select(operation => GetStringProperty(operation.Operation, "x-output-cache-policy"))
            .Where(policy => !string.IsNullOrWhiteSpace(policy))
            .Distinct()
            .ToList();
        var tenantModes = operations
            .Select(operation => GetStringProperty(operation.Operation, "x-tenant-mode"))
            .Where(mode => !string.IsNullOrWhiteSpace(mode))
            .Distinct()
            .ToList();

        await Assert.That(rateLimitPolicies)
            .Contains("AiAssistant")
            .Because("Endpoints decorated with [EnableRateLimiting] must expose their policy in OpenAPI for client and governance review.");
        await Assert.That(rateLimitPolicies)
            .Contains("ControlPlane")
            .Because("Control-plane endpoints must expose their dedicated API-owned saturation policy in OpenAPI.");
        await Assert.That(cachePolicies)
            .Contains("ListData")
            .Because("Endpoints decorated with [OutputCache] must expose their cache policy in OpenAPI for contract inventory review.");
        await Assert.That(tenantModes)
            .Contains("multi-tenant-required")
            .Because("Endpoints decorated with [RequireMultiTenant] must expose tenant-mode posture in OpenAPI.");
    }

    [Test]
    public async Task OpenApiDocument_ManagementMachineOperationsRequireDedicatedApiKey()
    {
        using var document = await GetOpenApiDocumentAsync();
        JsonElement root = document.RootElement;
        JsonElement scheme = root.GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty(ApiAuthenticationSchemeNames.ManagedControlPlane);

        await Assert.That(scheme.GetProperty("type").GetString()).IsEqualTo("apiKey");
        await Assert.That(scheme.GetProperty("in").GetString()).IsEqualTo("header");
        await Assert.That(scheme.GetProperty("name").GetString()).IsEqualTo("X-Control-Plane-Key");

        JsonElement capabilities = root.GetProperty("paths")
            .GetProperty("/api/management/capabilities")
            .GetProperty("get");
        await Assert.That(capabilities.GetProperty("security").GetArrayLength()).IsEqualTo(0);

        (string Path, string Method)[] protectedOperations =
        [
            ("/api/management/instance", "get"),
            ("/api/management/version", "get"),
            ("/api/management/health", "get"),
            ("/api/management/upgrade/preflight", "post"),
            ("/api/management/upgrade/postflight", "post"),
            ("/api/management/tenants/preflight", "post"),
            ("/api/management/tenants/provision", "post"),
            ("/api/management/tenant-provisioning/{operationId}", "get"),
            ("/api/management/tenant-provisioning/{operationId}/cancel", "post"),
            ("/api/management/credentials/rotate", "post"),
            ("/api/management/credentials", "delete")
        ];

        foreach ((string path, string method) in protectedOperations)
        {
            JsonElement security = root.GetProperty("paths")
                .GetProperty(path)
                .GetProperty(method)
                .GetProperty("security");
            bool hasManagedRequirement = security.EnumerateArray()
                .Any(requirement => requirement.TryGetProperty(ApiAuthenticationSchemeNames.ManagedControlPlane, out _));
            await Assert.That(hasManagedRequirement)
                .IsTrue()
                .Because($"{method.ToUpperInvariant()} {path} must require X-Control-Plane-Key.");
        }
    }

    [Test]
    public async Task OpenApiDocument_RepresentativeNullablePropertiesIncludeNull()
    {
        using var document = await GetOpenApiDocumentAsync();

        var eventProperties = GetSchemaProperties(document, "EventDto");
        var organizationProperties = GetSchemaProperties(document, "OrganizationListDto");

        await Assert.That(SchemaAllowsNull(eventProperties.GetProperty("subtitle")))
            .IsTrue()
            .Because("Nullable scalar properties must include null in the schema so generated clients preserve optionality.");
        await Assert.That(SchemaAllowsNull(eventProperties.GetProperty("description")))
            .IsTrue()
            .Because("Additional nullable scalar properties must include null in the schema so generated clients preserve optionality.");
        var currentUserRoleId = organizationProperties.GetProperty("currentUserRoleId");
        await Assert.That(SchemaAllowsNull(currentUserRoleId))
            .IsTrue()
            .Because("Nullable normalized lookup identifiers must include null so generated clients preserve optionality.");
        await Assert.That(currentUserRoleId.GetProperty("format").GetString())
            .IsEqualTo("int32")
            .Because("Normalized lookup identifiers use the platform's int lookup-key convention.");
    }

    [Test]
    public async Task OpenApiDocument_QuotaExceededDetailsIsNullableReferencedAndTenantSafe()
    {
        using var document = await GetOpenApiDocumentAsync();
        JsonElement quotaProperties = GetSchemaProperties(document, "QuotaExceededDetails");

        await Assert.That(quotaProperties.TryGetProperty("tenantId", out _)).IsFalse();

        foreach (string responseSchemaName in new[]
                 {
                     "BaseCommandResponseOfGuid",
                     "PromotionManagementCommandResponseDto",
                     "PromotionCodeIssuedCommandResponseDto"
                 })
        {
            JsonElement quotaExceeded = GetSchemaProperties(document, responseSchemaName)
                .GetProperty("quotaExceeded");
            await Assert.That(GetReferenceOrNullableReference(quotaExceeded))
                .IsEqualTo("#/components/schemas/QuotaExceededDetails");
            await Assert.That(SchemaAllowsNull(quotaExceeded)).IsTrue();
        }
    }

    [Test]
    public async Task OpenApiDocument_ActorSubscriptionPatchMarksMandatoryLeavesRequired()
    {
        using var document = await GetOpenApiDocumentAsync();
        var patchSchema = GetSchema(document, "UpdateActorSubscriptionNotificationLevelDto");
        var levelSchema = GetSchema(document, "UpdateActorSubscriptionNotificationLevelValueDto");
        var patchProperties = patchSchema.GetProperty("properties");

        await Assert.That(GetRequiredPropertyNames(patchSchema)).Contains("expectedConcurrencyStamp");
        await Assert.That(GetRequiredPropertyNames(levelSchema)).Contains("id");
        await Assert.That(SchemaAllowsNull(patchProperties.GetProperty("notificationLevel"))).IsTrue();
        await Assert.That(patchProperties.TryGetProperty("targetActorId", out _)).IsFalse();
    }

    [Test]
    public async Task OpenApiDocument_PublicActionExposesSafetyGuidanceButNoAuthorizationMetadata()
    {
        using var document = await GetOpenApiDocumentAsync();
        var actionProperties = GetSchemaProperties(document, "EventPublicActionDto");
        var claimProperties = GetSchemaProperties(document, "EventOrganizerClaimDto");

        await Assert.That(actionProperties.TryGetProperty("openInNewTab", out _)).IsTrue();
        await Assert.That(actionProperties.TryGetProperty("rel", out _)).IsTrue();

        foreach (var internalProperty in new[]
        {
            "tenantId",
            "eventActorId",
            "eventActorUserId",
            "eventActorOrganizationId",
            "eventActorGroupId",
            "eventProvenanceTypeId",
            "eventProvenanceTypeCode",
            "eventOrganizerActorId",
            "eventSubmittedByUserId"
        })
        {
            await Assert.That(actionProperties.TryGetProperty(internalProperty, out _)).IsFalse();
            await Assert.That(claimProperties.TryGetProperty(internalProperty, out _)).IsFalse();
        }
    }

    [Test]
    public async Task OpenApiDocument_EventListExposesTypedProvenanceCode()
    {
        using var document = await GetOpenApiDocumentAsync();
        var eventListProperties = GetSchemaProperties(document, "EventListDto");

        await Assert.That(eventListProperties.TryGetProperty("provenanceTypeCode", out var provenanceTypeCode)).IsTrue();
        await Assert.That(SchemaAllowsNull(provenanceTypeCode)).IsTrue();
    }

    [Test]
    public async Task OpenApiDocument_TenantOnboardingSettingsDoesNotExposeBroadWrite()
    {
        using var document = await GetOpenApiDocumentAsync();
        JsonElement path = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/tenantonboarding/settings");

        await Assert.That(path.TryGetProperty("get", out _)).IsTrue();
        await Assert.That(path.TryGetProperty("put", out _)).IsFalse();
    }

    private async Task<JsonDocument> GetOpenApiDocumentAsync()
    {
        var response = await _fixture.Client.GetAsync(OpenApiEndpoint);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    private static IEnumerable<string> EnumeratePaths(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("paths", out var paths)
            || paths.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var pathEntry in paths.EnumerateObject())
        {
            yield return pathEntry.Name;
        }
    }

    private static IEnumerable<OperationRef> EnumerateOperations(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("paths", out var paths)
            || paths.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var pathEntry in paths.EnumerateObject())
        {
            if (pathEntry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var operationEntry in pathEntry.Value.EnumerateObject())
            {
                if (!HttpVerbs.Contains(operationEntry.Name, System.StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? operationId = null;
                if (operationEntry.Value.TryGetProperty("operationId", out var opIdElement)
                    && opIdElement.ValueKind == JsonValueKind.String)
                {
                    operationId = opIdElement.GetString();
                }

                yield return new OperationRef(pathEntry.Name, operationEntry.Name, operationId, operationEntry.Value);
            }
        }
    }

    private static bool EndsWithDigit(string value)
        => value.Length > 0 && char.IsDigit(value[^1]);

    private static async Task AssertPatchRoutes(
        JsonElement paths,
        IEnumerable<(string Path, string OperationId, string RequestSchema)> patchRoutes)
    {
        foreach (var (path, operationId, requestSchema) in patchRoutes)
        {
            var pathItem = paths.GetProperty(path);
            var patch = pathItem.GetProperty("patch");

            await Assert.That(pathItem.TryGetProperty("put", out _)).IsFalse()
                .Because($"{path} must not expose its retired PUT replacement operation.");
            await Assert.That(GetStringProperty(patch, "operationId")).IsEqualTo(operationId);
            await Assert.That(GetStringProperty(patch, "x-endpoint-class")).IsEqualTo("Authenticated");
            await Assert.That(GetSchemaReference(
                    patch.GetProperty("requestBody")
                        .GetProperty("content")
                        .GetProperty("application/json; v=0.1")))
                .IsEqualTo($"#/components/schemas/{requestSchema}");
        }
    }

    private static bool EndsWithDigitBeforeSuffix(string value, string suffix)
    {
        if (!value.EndsWith(suffix, System.StringComparison.Ordinal)) return false;
        var stem = value[..^suffix.Length];
        return stem.Length > 0 && char.IsDigit(stem[^1]);
    }

    private static JsonElement GetSchemaProperties(JsonDocument document, string schemaName) => document.RootElement
        .GetProperty("components")
        .GetProperty("schemas")
        .GetProperty(schemaName)
        .GetProperty("properties");

    private static JsonElement GetSchema(JsonDocument document, string schemaName) => document.RootElement
        .GetProperty("components")
        .GetProperty("schemas")
        .GetProperty(schemaName);

    private static IReadOnlyList<string> GetSchemaPropertyNames(JsonElement schema) => schema
        .GetProperty("properties")
        .EnumerateObject()
        .Select(property => property.Name)
        .ToArray();

    private static IReadOnlyList<string> GetRequiredPropertyNames(JsonElement schema)
        => schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array
            ? required.EnumerateArray()
                .Where(property => property.ValueKind == JsonValueKind.String)
                .Select(property => property.GetString()!)
                .ToArray()
            : [];

    private static string? GetSchemaReference(JsonElement contentEntry) => GetReference(contentEntry.GetProperty("schema"));

    private static string? GetReference(JsonElement element) => GetStringProperty(element, "$ref");

    private static string? GetReferenceOrNullableReference(JsonElement element)
    {
        var directReference = GetReference(element);
        if (directReference is not null)
        {
            return directReference;
        }

        return element.TryGetProperty("oneOf", out var oneOf) && oneOf.ValueKind == JsonValueKind.Array
            ? oneOf.EnumerateArray()
                .Select(GetReference)
                .FirstOrDefault(reference => reference is not null)
            : null;
    }

    private static async Task AssertPresenceAwareProperties(
        JsonDocument document,
        string schemaName,
        IReadOnlyList<string> expectedPropertyNames)
    {
        var properties = GetSchemaProperties(document, schemaName);

        await Assert.That(properties.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(expectedPropertyNames);

        foreach (var property in properties.EnumerateObject())
        {
            var reference = GetReference(property.Value);
            await Assert.That(reference?.StartsWith("#/components/schemas/OptionalUpdateOf", System.StringComparison.Ordinal) == true)
                .IsTrue()
                .Because($"{schemaName}.{property.Name} must preserve omitted-vs-explicit patch intent through OptionalUpdate<T>.");

            var optionalUpdateSchema = GetSchema(document, reference!["#/components/schemas/".Length..]);
            await Assert.That(GetSchemaPropertyNames(optionalUpdateSchema))
                .IsEquivalentTo(["hasValue", "value"])
                .Because($"{schemaName}.{property.Name} must retain the generated presence marker and value.");
        }
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static IReadOnlyList<string> GetEnumValues(JsonElement schema)
        => schema.TryGetProperty("enum", out var enumValues) && enumValues.ValueKind == JsonValueKind.Array
            ? enumValues.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()!)
                .ToArray()
            : [];

    private static bool SchemaAllowsNull(JsonElement schema)
    {
        if (schema.TryGetProperty("type", out var type))
        {
            if (type.ValueKind == JsonValueKind.String)
            {
                return string.Equals(type.GetString(), "null", System.StringComparison.Ordinal);
            }

            if (type.ValueKind == JsonValueKind.Array
                && type.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String
                    && string.Equals(value.GetString(), "null", System.StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return schema.TryGetProperty("oneOf", out var oneOf)
            && oneOf.ValueKind == JsonValueKind.Array
            && oneOf.EnumerateArray().Any(SchemaAllowsNull);
    }

    private readonly record struct OperationRef(string Path, string Method, string? OperationId, JsonElement Operation);
}
