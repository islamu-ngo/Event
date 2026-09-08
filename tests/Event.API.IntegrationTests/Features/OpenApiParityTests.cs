using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Compares the native OpenAPI document against the Swashbuckle baseline for high-risk
/// canary endpoints. This intentionally avoids byte-for-byte JSON equality because the two
/// generators use different OpenAPI versions and schema-id conventions.
/// </summary>
[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public sealed class OpenApiParityTests
{
    private const string NativeOpenApiEndpoint = "/openapi/islamu-event.json";
    private const string SwashbuckleOpenApiEndpoint = "/swagger/v0.1/swagger.json";
    private const string KeycloakAuthorizationUrl = "https://auth.example.com/realms/ISLAMU/protocol/openid-connect/auth";
    private const string ManagedControlPlaneHeader = "X-Control-Plane-Key";
    private const string PrivacyErasureReceiptScheme = "PrivacyErasureReceipt";
    private const string PrivacyErasureReceiptHeader = "Authorization";

    private static readonly OperationSelector[] CanaryOperations =
    [
        new("/api/event", "get"),
        new("/api/event/{id}", "get")
    ];

    private static readonly OperationSelector RequestBodyAliasCanaryOperation = new("/api/event", "post");

    private static readonly OperationSecurityExpectation[] ManagementOperationSecurityExpectations =
    [
        new(new("/api/management/capabilities", "get"), []),
        new(new("/api/management/tenants/preflight", "post"), [ApiAuthenticationSchemeNames.ManagedControlPlane]),
        new(new("/api/management/tenants/provision", "post"), [ApiAuthenticationSchemeNames.ManagedControlPlane]),
        new(new("/api/management/tenant-provisioning/{operationId}", "get"), [ApiAuthenticationSchemeNames.ManagedControlPlane]),
        new(new("/api/management/tenant-provisioning/{operationId}/cancel", "post"), [ApiAuthenticationSchemeNames.ManagedControlPlane]),
        new(new("/api/management/registration", "post"), ["Keycloak"]),
        new(new("/api/event", "post"), ["Keycloak"])
    ];

    private static readonly OperationSelector UnrelatedAnonymousOperation = new("/api/event", "get");

    private static readonly OperationSecurityExpectation PrivacyErasureStatusSecurityExpectation =
        new(new("/api/privacy-erasure/status", "get"), [PrivacyErasureReceiptScheme]);

    private static readonly string[] RepresentativeHalComponentSchemas =
    [
        "HalCollectionResourceOfEventListDto",
        "HalResourceOfEventDto",
        "HalResourceOfEventAgendaItemDto"
    ];

    private static readonly string[] RepresentativeHalEmbeddedSchemas =
    [
        "HalCollectionEmbeddedOfEventListDto",
        "HalCollectionEmbeddedOfEventSessionListDto",
        "HalCollectionEmbeddedOfOrganizationReviewDto"
    ];

    private readonly ContractApiFixture _fixture;

    public OpenApiParityTests(ContractApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task NativeAndSwashbuckleDocs_AreReachable()
    {
        using var nativeResponse = await _fixture.Client.GetAsync(NativeOpenApiEndpoint);
        using var swashbuckleResponse = await _fixture.Client.GetAsync(SwashbuckleOpenApiEndpoint);

        await Assert.That(nativeResponse.StatusCode).IsEqualTo(HttpStatusCode.OK)
            .Because("The native OpenAPI document is the canonical build-time/runtime contract source.");
        await Assert.That(swashbuckleResponse.StatusCode).IsEqualTo(HttpStatusCode.OK)
            .Because("The Swashbuckle document must remain available until native parity is proven and cleanup lands.");
    }

    [Test]
    public async Task NativeAndSwashbuckleDocs_MatchCanaryOperationSurface()
    {
        using var nativeDocument = await GetOpenApiDocumentAsync(NativeOpenApiEndpoint);
        using var swashbuckleDocument = await GetOpenApiDocumentAsync(SwashbuckleOpenApiEndpoint);

        var differences = new List<string>();

        foreach (var selector in CanaryOperations)
        {
            var nativeOperation = GetOperation(nativeDocument, selector);
            var swashbuckleOperation = GetOperation(swashbuckleDocument, selector);

            CompareOperation(selector, nativeOperation, swashbuckleOperation, differences);
        }

        await Assert.That(differences)
            .IsEmpty()
            .Because("Native OpenAPI and Swashbuckle must describe the same canary endpoint contract before Swashbuckle/runtime export cleanup. "
                + string.Join("; ", differences.Take(10)));
    }

    [Test]
    public async Task NativeAndSwashbuckleDocs_OmitKeycloakSecurityWithoutAuthorityAndHaveNoDanglingReferences()
    {
        await using var app = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Keycloak:AuthorizationUrl"] = null,
                    ["Keycloak:Authority"] = null
                }));
        });
        using var client = app.CreateClient();
        using var nativeDocument = await GetOpenApiDocumentAsync(client, NativeOpenApiEndpoint);
        using var swashbuckleDocument = await GetOpenApiDocumentAsync(
            client,
            SwashbuckleOpenApiEndpoint);

        await Assert.That(HasKeycloakSecurityScheme(nativeDocument)).IsFalse();
        await Assert.That(HasKeycloakSecurityScheme(swashbuckleDocument)).IsFalse();
        await Assert.That(UndefinedSecuritySchemeReferences(nativeDocument)).IsEmpty()
            .Because("native build-time generation must never reference an unavailable runtime authority");
        await Assert.That(UndefinedSecuritySchemeReferences(swashbuckleDocument)).IsEmpty()
            .Because("Swashbuckle build-time generation must never emit dangling security references");
    }

    [Test]
    public async Task NativeAndSwashbuckleDocs_DeriveDeterministicKeycloakMetadataFromAuthority()
    {
        const string authority = "https://auth.example.com/realms/ISLAMU";
        await using var app = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Keycloak:Authority"] = authority }));
        });

        using var client = app.CreateClient();
        using var nativeDocument = await GetOpenApiDocumentAsync(client, NativeOpenApiEndpoint);
        using var swashbuckleDocument = await GetOpenApiDocumentAsync(client, SwashbuckleOpenApiEndpoint);
        var differences = new List<string>();
        CompareKeycloakSecurityScheme(nativeDocument, swashbuckleDocument, differences);

        string expected = $"{authority}/protocol/openid-connect/auth";
        await Assert.That(ReadKeycloakAuthorizationUrl(nativeDocument)).IsEqualTo(expected);
        await Assert.That(ReadKeycloakAuthorizationUrl(swashbuckleDocument)).IsEqualTo(expected);
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task NativeAndSwashbuckleDocs_MatchConfiguredKeycloakSecurityMetadata()
    {
        await using var app = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Keycloak:AuthorizationUrl"] = KeycloakAuthorizationUrl
                });
            });
        });

        using var client = app.CreateClient();
        using var nativeDocument = await GetOpenApiDocumentAsync(client, NativeOpenApiEndpoint);
        using var swashbuckleDocument = await GetOpenApiDocumentAsync(client, SwashbuckleOpenApiEndpoint);

        var differences = new List<string>();
        CompareKeycloakSecurityScheme(nativeDocument, swashbuckleDocument, differences);
        CompareDocumentSecurityRequirements(nativeDocument, swashbuckleDocument, differences);

        await Assert.That(differences)
            .IsEmpty()
            .Because("Native OpenAPI and Swashbuckle must expose equivalent configured Keycloak OAuth2 metadata before Swashbuckle cleanup. "
                + string.Join("; ", differences));
    }

    [Test]
    public async Task NativeAndSwashbuckleDocs_MatchManagedControlPlaneSecurityScheme()
    {
        using var nativeDocument = await GetOpenApiDocumentAsync(NativeOpenApiEndpoint);
        using var swashbuckleDocument = await GetOpenApiDocumentAsync(SwashbuckleOpenApiEndpoint);

        var differences = new List<string>();
        CompareManagedControlPlaneSecurityScheme(nativeDocument, swashbuckleDocument, differences);

        await Assert.That(differences)
            .IsEmpty()
            .Because("Both documents must expose the same header apiKey contract for managed machine calls. "
                + string.Join("; ", differences));
    }

    [Test]
    public async Task NativeAndSwashbuckleDocs_MatchManagedControlPlaneOperationSecurity()
    {
        await using var app = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Keycloak:AuthorizationUrl"] = KeycloakAuthorizationUrl
                });
            });
        });

        using var client = app.CreateClient();
        using var nativeDocument = await GetOpenApiDocumentAsync(client, NativeOpenApiEndpoint);
        using var swashbuckleDocument = await GetOpenApiDocumentAsync(client, SwashbuckleOpenApiEndpoint);

        var differences = new List<string>();
        foreach (var expectation in ManagementOperationSecurityExpectations)
        {
            CompareEffectiveOperationSecurity(
                expectation,
                nativeDocument,
                swashbuckleDocument,
                differences);
        }

        CompareUnrelatedAnonymousOperationSecurity(
            nativeDocument,
            swashbuckleDocument,
            differences);

        await Assert.That(differences)
            .IsEmpty()
            .Because("Only managed-policy operations may require the Control Plane key; capabilities, registration, and unrelated endpoints retain their existing security. "
                + string.Join("; ", differences));
    }

    [Test]
    public async Task NativeAndSwashbuckleDocs_MatchPrivacyErasureReceiptSecurity()
    {
        using var nativeDocument = await GetOpenApiDocumentAsync(NativeOpenApiEndpoint);
        using var swashbuckleDocument = await GetOpenApiDocumentAsync(SwashbuckleOpenApiEndpoint);

        var differences = new List<string>();
        ComparePrivacyErasureReceiptSecurityScheme(nativeDocument, swashbuckleDocument, differences);
        CompareEffectiveOperationSecurity(
            PrivacyErasureStatusSecurityExpectation,
            nativeDocument,
            swashbuckleDocument,
            differences);

        await Assert.That(differences)
            .IsEmpty()
            .Because("The receipt-only status route must document only its bounded Authorization: ErasureReceipt <receipt> scheme, not inherited bearer authentication. "
                + string.Join("; ", differences));
    }

    [Test]
    public async Task NativeAndSwashbuckleDocs_MatchRequestBodyVersionedContentAliases()
    {
        using var nativeDocument = await GetOpenApiDocumentAsync(NativeOpenApiEndpoint);
        using var swashbuckleDocument = await GetOpenApiDocumentAsync(SwashbuckleOpenApiEndpoint);

        var nativeOperation = GetOperation(nativeDocument, RequestBodyAliasCanaryOperation);
        var swashbuckleOperation = GetOperation(swashbuckleDocument, RequestBodyAliasCanaryOperation);
        var differences = new List<string>();

        CompareRequestBodyVersionedContent(RequestBodyAliasCanaryOperation, nativeOperation, swashbuckleOperation, differences);

        await Assert.That(differences)
            .IsEmpty()
            .Because("Native OpenAPI and Swashbuckle must expose the same versioned request-body media-type aliases for write clients. "
                + string.Join("; ", differences));
    }

    [Test]
    public async Task NativeAndSwashbuckleDocs_MatchRepresentativeHalComponentSchemaShapes()
    {
        using var nativeDocument = await GetOpenApiDocumentAsync(NativeOpenApiEndpoint);
        using var swashbuckleDocument = await GetOpenApiDocumentAsync(SwashbuckleOpenApiEndpoint);

        var differences = new List<string>();
        foreach (var schemaName in RepresentativeHalComponentSchemas)
        {
            CompareComponentSchemaShape(schemaName, nativeDocument, swashbuckleDocument, differences);
        }

        await Assert.That(differences)
            .IsEmpty()
            .Because("Representative HAL wrapper component schemas must stay semantically aligned while Swashbuckle remains the baseline. "
                + string.Join("; ", differences.Take(10)));
    }

    [Test]
    public async Task NativeAndSwashbuckleDocs_MatchRepresentativeHalEmbeddedItemReferences()
    {
        using var nativeDocument = await GetOpenApiDocumentAsync(NativeOpenApiEndpoint);
        using var swashbuckleDocument = await GetOpenApiDocumentAsync(SwashbuckleOpenApiEndpoint);

        var differences = new List<string>();
        foreach (var schemaName in RepresentativeHalEmbeddedSchemas)
        {
            CompareEmbeddedItemsReference(schemaName, nativeDocument, swashbuckleDocument, differences);
        }

        await Assert.That(differences)
            .IsEmpty()
            .Because("Representative HAL embedded collection schemas must type items equivalently while Swashbuckle remains the baseline. "
                + string.Join("; ", differences.Take(10)));
    }

    [Test]
    public async Task NativeAndSwashbuckleDocs_MatchPublicEnumSchemaShapes()
    {
        using var nativeDocument = await GetOpenApiDocumentAsync(NativeOpenApiEndpoint);
        using var swashbuckleDocument = await GetOpenApiDocumentAsync(SwashbuckleOpenApiEndpoint);

        var differences = new List<string>();
        CompareEnumSchema("RoleEnum", nativeDocument, swashbuckleDocument, differences);
        CompareEnumSchema("GuestRecoveryPolicyEnum", nativeDocument, swashbuckleDocument, differences);
        CompareEnumSchema("DeploymentMode", nativeDocument, swashbuckleDocument, differences);

        await Assert.That(differences)
            .IsEmpty()
            .Because("Native OpenAPI and Swashbuckle must agree on public enum schema shapes so generated clients match runtime JSON. "
                + string.Join("; ", differences));
    }

    private async Task<JsonDocument> GetOpenApiDocumentAsync(string endpoint)
        => await GetOpenApiDocumentAsync(_fixture.Client, endpoint);

    private static async Task<JsonDocument> GetOpenApiDocumentAsync(HttpClient client, string endpoint)
    {
        using var response = await client.GetAsync(endpoint);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    private static bool HasKeycloakSecurityScheme(JsonDocument document)
        => TryGetKeycloakSecurityScheme(document, out _);

    private static string? ReadKeycloakAuthorizationUrl(JsonDocument document) =>
        TryGetKeycloakSecurityScheme(document, out JsonElement scheme)
            ? GetStringProperty(scheme.GetProperty("flows").GetProperty("implicit"), "authorizationUrl")
            : null;

    private static string[] UndefinedSecuritySchemeReferences(JsonDocument document)
    {
        var definitions = document.RootElement.GetProperty("components")
            .GetProperty("securitySchemes")
            .EnumerateObject()
            .Select(entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);
        var references = new HashSet<string>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("security", out JsonElement documentSecurity))
            references.UnionWith(GetSecurityRequirementSchemeNames(documentSecurity));
        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        foreach (JsonProperty operation in path.Value.EnumerateObject())
        {
            if (operation.Value.ValueKind == JsonValueKind.Object
                && operation.Value.TryGetProperty("security", out JsonElement operationSecurity))
            {
                references.UnionWith(GetSecurityRequirementSchemeNames(operationSecurity));
            }
        }

        return references.Where(reference => !definitions.Contains(reference)).ToArray();
    }

    private static void CompareManagedControlPlaneSecurityScheme(
        JsonDocument nativeDocument,
        JsonDocument swashbuckleDocument,
        List<string> differences)
    {
        if (!TryGetSecurityScheme(nativeDocument, ApiAuthenticationSchemeNames.ManagedControlPlane, out var nativeScheme))
        {
            differences.Add($"native document is missing components.securitySchemes.{ApiAuthenticationSchemeNames.ManagedControlPlane}");
            return;
        }

        if (!TryGetSecurityScheme(swashbuckleDocument, ApiAuthenticationSchemeNames.ManagedControlPlane, out var swashbuckleScheme))
        {
            differences.Add($"Swashbuckle document is missing components.securitySchemes.{ApiAuthenticationSchemeNames.ManagedControlPlane}");
            return;
        }

        CompareString("Managed Control Plane security scheme", "type", nativeScheme, swashbuckleScheme, differences);
        CompareString("Managed Control Plane security scheme", "in", nativeScheme, swashbuckleScheme, differences);
        CompareString("Managed Control Plane security scheme", "name", nativeScheme, swashbuckleScheme, differences);

        if (GetStringProperty(nativeScheme, "type") != "apiKey"
            || GetStringProperty(nativeScheme, "in") != "header"
            || GetStringProperty(nativeScheme, "name") != ManagedControlPlaneHeader)
        {
            differences.Add("Managed Control Plane security scheme must be an X-Control-Plane-Key header apiKey");
        }
    }

    private static void ComparePrivacyErasureReceiptSecurityScheme(
        JsonDocument nativeDocument,
        JsonDocument swashbuckleDocument,
        List<string> differences)
    {
        if (!TryGetSecurityScheme(nativeDocument, PrivacyErasureReceiptScheme, out var nativeScheme))
        {
            differences.Add($"native document is missing components.securitySchemes.{PrivacyErasureReceiptScheme}");
            return;
        }

        if (!TryGetSecurityScheme(swashbuckleDocument, PrivacyErasureReceiptScheme, out var swashbuckleScheme))
        {
            differences.Add($"Swashbuckle document is missing components.securitySchemes.{PrivacyErasureReceiptScheme}");
            return;
        }

        CompareString("Privacy-erasure receipt security scheme", "type", nativeScheme, swashbuckleScheme, differences);
        CompareString("Privacy-erasure receipt security scheme", "in", nativeScheme, swashbuckleScheme, differences);
        CompareString("Privacy-erasure receipt security scheme", "name", nativeScheme, swashbuckleScheme, differences);
        CompareString("Privacy-erasure receipt security scheme", "description", nativeScheme, swashbuckleScheme, differences);

        string? nativeDescription = GetStringProperty(nativeScheme, "description");
        if (GetStringProperty(nativeScheme, "type") != "apiKey"
            || GetStringProperty(nativeScheme, "in") != "header"
            || GetStringProperty(nativeScheme, "name") != PrivacyErasureReceiptHeader
            || nativeDescription is null
            || !nativeDescription.Contains("Authorization: ErasureReceipt <receipt>", StringComparison.Ordinal)
            || nativeDescription.Contains("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            differences.Add("Privacy-erasure receipt security scheme must be a non-bearer Authorization header apiKey documented as Authorization: ErasureReceipt <receipt>");
        }
    }

    private static void CompareEffectiveOperationSecurity(
        OperationSecurityExpectation expectation,
        JsonDocument nativeDocument,
        JsonDocument swashbuckleDocument,
        List<string> differences)
    {
        var nativeSchemes = GetEffectiveSecurityRequirementSchemeNames(nativeDocument, expectation.Selector);
        var swashbuckleSchemes = GetEffectiveSecurityRequirementSchemeNames(swashbuckleDocument, expectation.Selector);
        var expectedSchemes = expectation.ExpectedSchemes.ToHashSet(StringComparer.Ordinal);

        if (!nativeSchemes.SetEquals(expectedSchemes))
        {
            differences.Add($"{expectation.Selector}: native effective security differs (expected={string.Join(',', expectedSchemes)}, actual={string.Join(',', nativeSchemes)})");
        }

        if (!swashbuckleSchemes.SetEquals(expectedSchemes))
        {
            differences.Add($"{expectation.Selector}: Swashbuckle effective security differs (expected={string.Join(',', expectedSchemes)}, actual={string.Join(',', swashbuckleSchemes)})");
        }
    }

    private static void CompareUnrelatedAnonymousOperationSecurity(
        JsonDocument nativeDocument,
        JsonDocument swashbuckleDocument,
        List<string> differences)
    {
        var nativeSchemes = GetEffectiveSecurityRequirementSchemeNames(nativeDocument, UnrelatedAnonymousOperation);
        var swashbuckleSchemes = GetEffectiveSecurityRequirementSchemeNames(swashbuckleDocument, UnrelatedAnonymousOperation);

        if (!nativeSchemes.SetEquals(swashbuckleSchemes))
        {
            differences.Add($"{UnrelatedAnonymousOperation}: effective security differs (native={string.Join(',', nativeSchemes)}, swashbuckle={string.Join(',', swashbuckleSchemes)})");
        }

        if (nativeSchemes.Contains(ApiAuthenticationSchemeNames.ManagedControlPlane)
            || swashbuckleSchemes.Contains(ApiAuthenticationSchemeNames.ManagedControlPlane))
        {
            differences.Add($"{UnrelatedAnonymousOperation}: unrelated anonymous operation must not use {ApiAuthenticationSchemeNames.ManagedControlPlane}");
        }
    }

    private static void CompareEnumSchema(string schemaName, JsonDocument nativeDocument, JsonDocument swashbuckleDocument, List<string> differences)
    {
        if (!TryGetSchema(nativeDocument, schemaName, out var nativeSchema))
        {
            differences.Add($"native document is missing enum schema {schemaName}");
            return;
        }

        if (!TryGetSchema(swashbuckleDocument, schemaName, out var swashbuckleSchema))
        {
            differences.Add($"Swashbuckle document is missing enum schema {schemaName}");
            return;
        }

        CompareString($"{schemaName} schema", "type", nativeSchema, swashbuckleSchema, differences);

        var nativeValues = GetEnumValues(nativeSchema);
        var swashbuckleValues = GetEnumValues(swashbuckleSchema);
        if (!nativeValues.SetEquals(swashbuckleValues))
        {
            differences.Add($"{schemaName} enum values differ (native={string.Join(',', nativeValues)}, swashbuckle={string.Join(',', swashbuckleValues)})");
        }
    }

    private static void CompareKeycloakSecurityScheme(JsonDocument nativeDocument, JsonDocument swashbuckleDocument, List<string> differences)
    {
        if (!TryGetKeycloakSecurityScheme(nativeDocument, out var nativeScheme))
        {
            differences.Add("native document is missing components.securitySchemes.Keycloak");
            return;
        }

        if (!TryGetKeycloakSecurityScheme(swashbuckleDocument, out var swashbuckleScheme))
        {
            differences.Add("Swashbuckle document is missing components.securitySchemes.Keycloak");
            return;
        }

        CompareString("Keycloak security scheme", "type", nativeScheme, swashbuckleScheme, differences);

        var nativeImplicit = nativeScheme.GetProperty("flows").GetProperty("implicit");
        var swashbuckleImplicit = swashbuckleScheme.GetProperty("flows").GetProperty("implicit");
        CompareString("Keycloak implicit flow", "authorizationUrl", nativeImplicit, swashbuckleImplicit, differences);
        CompareScopes(nativeImplicit, swashbuckleImplicit, differences);
    }

    private static void CompareDocumentSecurityRequirements(JsonDocument nativeDocument, JsonDocument swashbuckleDocument, List<string> differences)
    {
        var nativeRequirements = GetDocumentSecurityRequirementSchemeNames(nativeDocument);
        var swashbuckleRequirements = GetDocumentSecurityRequirementSchemeNames(swashbuckleDocument);

        if (!nativeRequirements.SetEquals(swashbuckleRequirements))
        {
            differences.Add($"document security requirements differ (native={string.Join(',', nativeRequirements)}, swashbuckle={string.Join(',', swashbuckleRequirements)})");
        }
    }

    private static bool TryGetKeycloakSecurityScheme(JsonDocument document, out JsonElement scheme)
        => TryGetSecurityScheme(document, "Keycloak", out scheme);

    private static bool TryGetSecurityScheme(JsonDocument document, string schemeName, out JsonElement scheme)
    {
        scheme = default;
        return document.RootElement.TryGetProperty("components", out var components)
            && components.TryGetProperty("securitySchemes", out var securitySchemes)
            && securitySchemes.TryGetProperty(schemeName, out scheme);
    }

    private static void CompareScopes(JsonElement nativeImplicitFlow, JsonElement swashbuckleImplicitFlow, List<string> differences)
    {
        var nativeScopes = GetScopeNames(nativeImplicitFlow);
        var swashbuckleScopes = GetScopeNames(swashbuckleImplicitFlow);

        if (!nativeScopes.SetEquals(swashbuckleScopes))
        {
            differences.Add($"Keycloak implicit flow scopes differ (native={string.Join(',', nativeScopes)}, swashbuckle={string.Join(',', swashbuckleScopes)})");
        }
    }

    private static HashSet<string> GetScopeNames(JsonElement implicitFlow)
    {
        if (!implicitFlow.TryGetProperty("scopes", out var scopes)
            || scopes.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return scopes.EnumerateObject()
            .Select(scope => scope.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> GetDocumentSecurityRequirementSchemeNames(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("security", out var security)
            || security.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return security.EnumerateArray()
            .Where(requirement => requirement.ValueKind == JsonValueKind.Object)
            .SelectMany(requirement => requirement.EnumerateObject().Select(entry => entry.Name))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> GetEffectiveSecurityRequirementSchemeNames(
        JsonDocument document,
        OperationSelector selector)
    {
        var operation = GetOperation(document, selector);
        return operation.TryGetProperty("security", out var security)
            ? GetSecurityRequirementSchemeNames(security)
            : GetDocumentSecurityRequirementSchemeNames(document);
    }

    private static HashSet<string> GetSecurityRequirementSchemeNames(JsonElement security)
        => security.ValueKind == JsonValueKind.Array
            ? security.EnumerateArray()
                .Where(requirement => requirement.ValueKind == JsonValueKind.Object)
                .SelectMany(requirement => requirement.EnumerateObject().Select(entry => entry.Name))
                .ToHashSet(StringComparer.Ordinal)
            : [];

    private static JsonElement GetOperation(JsonDocument document, OperationSelector selector) => document.RootElement
        .GetProperty("paths")
        .GetProperty(selector.Path)
        .GetProperty(selector.Method);

    private static void CompareOperation(
        OperationSelector selector,
        JsonElement nativeOperation,
        JsonElement swashbuckleOperation,
        List<string> differences)
    {
        CompareString(selector, "operationId", nativeOperation, swashbuckleOperation, differences);
        CompareRequestBodyPresence(selector, nativeOperation, swashbuckleOperation, differences);
        CompareResponseStatusCodes(selector, nativeOperation, swashbuckleOperation, differences);
        CompareResponseContent(selector, nativeOperation, swashbuckleOperation, differences);
    }

    private static void CompareString(
        OperationSelector selector,
        string propertyName,
        JsonElement nativeOperation,
        JsonElement swashbuckleOperation,
        List<string> differences)
    {
        var nativeValue = GetStringProperty(nativeOperation, propertyName);
        var swashbuckleValue = GetStringProperty(swashbuckleOperation, propertyName);

        if (!string.Equals(nativeValue, swashbuckleValue, StringComparison.Ordinal))
        {
            differences.Add($"{selector}: {propertyName} differs (native='{nativeValue}', swashbuckle='{swashbuckleValue}')");
        }
    }

    private static void CompareString(
        string context,
        string propertyName,
        JsonElement nativeElement,
        JsonElement swashbuckleElement,
        List<string> differences)
    {
        var nativeValue = GetStringProperty(nativeElement, propertyName);
        var swashbuckleValue = GetStringProperty(swashbuckleElement, propertyName);

        if (!string.Equals(nativeValue, swashbuckleValue, StringComparison.Ordinal))
        {
            differences.Add($"{context}: {propertyName} differs (native='{nativeValue}', swashbuckle='{swashbuckleValue}')");
        }
    }

    private static void CompareRequestBodyPresence(
        OperationSelector selector,
        JsonElement nativeOperation,
        JsonElement swashbuckleOperation,
        List<string> differences)
    {
        var nativeHasRequestBody = nativeOperation.TryGetProperty("requestBody", out _);
        var swashbuckleHasRequestBody = swashbuckleOperation.TryGetProperty("requestBody", out _);

        if (nativeHasRequestBody != swashbuckleHasRequestBody)
        {
            differences.Add($"{selector}: requestBody presence differs (native={nativeHasRequestBody}, swashbuckle={swashbuckleHasRequestBody})");
        }
    }

    private static void CompareRequestBodyVersionedContent(
        OperationSelector selector,
        JsonElement nativeOperation,
        JsonElement swashbuckleOperation,
        List<string> differences)
    {
        var nativeContent = GetRequestBodyContentSchemas(nativeOperation)
            .Where(pair => IsVersionedMediaType(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var swashbuckleContent = GetRequestBodyContentSchemas(swashbuckleOperation)
            .Where(pair => IsVersionedMediaType(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        if (!nativeContent.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(swashbuckleContent.Keys))
        {
            differences.Add($"{selector}: requestBody versioned content types differ (native={string.Join(',', nativeContent.Keys)}, swashbuckle={string.Join(',', swashbuckleContent.Keys)})");
            return;
        }

        foreach (var (contentType, nativeSchemaRef) in nativeContent)
        {
            var swashbuckleSchemaRef = swashbuckleContent[contentType];
            if (!string.Equals(NormalizeSchemaReference(nativeSchemaRef), NormalizeSchemaReference(swashbuckleSchemaRef), StringComparison.Ordinal))
            {
                differences.Add($"{selector}: requestBody {contentType} schema differs (native='{nativeSchemaRef}', swashbuckle='{swashbuckleSchemaRef}')");
            }
        }
    }

    private static void CompareComponentSchemaShape(string schemaName, JsonDocument nativeDocument, JsonDocument swashbuckleDocument, List<string> differences)
    {
        if (!TryGetSchema(nativeDocument, schemaName, out var nativeSchema))
        {
            differences.Add($"native document is missing component schema {schemaName}");
            return;
        }

        if (!TryGetSchema(swashbuckleDocument, schemaName, out var swashbuckleSchema))
        {
            differences.Add($"Swashbuckle document is missing component schema {schemaName}");
            return;
        }

        var nativeProperties = GetPropertyNames(nativeSchema);
        var swashbuckleProperties = GetPropertyNames(swashbuckleSchema);
        if (!nativeProperties.SetEquals(swashbuckleProperties))
        {
            differences.Add($"{schemaName} properties differ (native={string.Join(',', nativeProperties)}, swashbuckle={string.Join(',', swashbuckleProperties)})");
        }

        var nativeRequired = GetRequiredNames(nativeSchema);
        var swashbuckleRequired = GetRequiredNames(swashbuckleSchema);
        if (!nativeRequired.SetEquals(swashbuckleRequired))
        {
            differences.Add($"{schemaName} required properties differ (native={string.Join(',', nativeRequired)}, swashbuckle={string.Join(',', swashbuckleRequired)})");
        }
    }

    private static void CompareEmbeddedItemsReference(string schemaName, JsonDocument nativeDocument, JsonDocument swashbuckleDocument, List<string> differences)
    {
        if (!TryGetSchema(nativeDocument, schemaName, out var nativeSchema))
        {
            differences.Add($"native document is missing embedded schema {schemaName}");
            return;
        }

        if (!TryGetSchema(swashbuckleDocument, schemaName, out var swashbuckleSchema))
        {
            differences.Add($"Swashbuckle document is missing embedded schema {schemaName}");
            return;
        }

        var nativeReference = GetEmbeddedItemsReference(nativeSchema);
        var swashbuckleReference = GetEmbeddedItemsReference(swashbuckleSchema);
        if (!string.Equals(NormalizeSchemaReference(nativeReference), NormalizeSchemaReference(swashbuckleReference), StringComparison.Ordinal))
        {
            differences.Add($"{schemaName} embedded item reference differs (native='{nativeReference}', swashbuckle='{swashbuckleReference}')");
        }
    }

    private static void CompareResponseStatusCodes(
        OperationSelector selector,
        JsonElement nativeOperation,
        JsonElement swashbuckleOperation,
        List<string> differences)
    {
        var nativeStatusCodes = GetResponseStatusCodes(nativeOperation);
        var swashbuckleStatusCodes = GetResponseStatusCodes(swashbuckleOperation);

        if (!nativeStatusCodes.SetEquals(swashbuckleStatusCodes))
        {
            differences.Add($"{selector}: response status codes differ (native={string.Join(',', nativeStatusCodes)}, swashbuckle={string.Join(',', swashbuckleStatusCodes)})");
        }
    }

    private static void CompareResponseContent(
        OperationSelector selector,
        JsonElement nativeOperation,
        JsonElement swashbuckleOperation,
        List<string> differences)
    {
        var nativeResponses = nativeOperation.GetProperty("responses");
        var swashbuckleResponses = swashbuckleOperation.GetProperty("responses");

        foreach (var nativeResponse in nativeResponses.EnumerateObject())
        {
            if (!swashbuckleResponses.TryGetProperty(nativeResponse.Name, out var swashbuckleResponse))
            {
                continue;
            }

            var nativeContent = GetContentSchemas(nativeResponse.Value);
            var swashbuckleContent = GetContentSchemas(swashbuckleResponse);
            if (!nativeContent.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(swashbuckleContent.Keys))
            {
                differences.Add($"{selector}: response {nativeResponse.Name} content types differ (native={string.Join(',', nativeContent.Keys)}, swashbuckle={string.Join(',', swashbuckleContent.Keys)})");
                continue;
            }

            foreach (var (contentType, nativeSchemaRef) in nativeContent)
            {
                if (!IsVersionedMediaType(contentType))
                {
                    continue;
                }

                var swashbuckleSchemaRef = swashbuckleContent[contentType];
                if (!string.Equals(NormalizeSchemaReference(nativeSchemaRef), NormalizeSchemaReference(swashbuckleSchemaRef), StringComparison.Ordinal))
                {
                    differences.Add($"{selector}: response {nativeResponse.Name} {contentType} schema differs (native='{nativeSchemaRef}', swashbuckle='{swashbuckleSchemaRef}')");
                }
            }
        }
    }

    private static HashSet<string> GetResponseStatusCodes(JsonElement operation)
    {
        if (!operation.TryGetProperty("responses", out var responses)
            || responses.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return responses.EnumerateObject()
            .Select(response => response.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static Dictionary<string, string?> GetContentSchemas(JsonElement response)
    {
        if (!response.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return content.EnumerateObject()
            .ToDictionary(
                entry => entry.Name,
                entry => GetSchemaReference(entry.Value),
                StringComparer.Ordinal);
    }

    private static Dictionary<string, string?> GetRequestBodyContentSchemas(JsonElement operation)
    {
        if (!operation.TryGetProperty("requestBody", out var requestBody)
            || !requestBody.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return content.EnumerateObject()
            .ToDictionary(
                entry => entry.Name,
                entry => GetSchemaReference(entry.Value),
                StringComparer.Ordinal);
    }

    private static string? GetSchemaReference(JsonElement contentEntry)
    {
        if (!contentEntry.TryGetProperty("schema", out var schema))
        {
            return null;
        }

        return GetReference(schema);
    }

    private static string? GetReference(JsonElement element) => GetStringProperty(element, "$ref");

    private static bool TryGetSchema(JsonDocument document, string schemaName, out JsonElement schema)
    {
        schema = default;
        if (!document.RootElement.TryGetProperty("components", out var components)
            || !components.TryGetProperty("schemas", out var schemas)
            || schemas.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (schemas.TryGetProperty(schemaName, out schema))
        {
            return true;
        }

        foreach (var candidate in schemas.EnumerateObject())
        {
            if (string.Equals(NormalizeSchemaName(candidate.Name), schemaName, StringComparison.Ordinal))
            {
                schema = candidate.Value;
                return true;
            }
        }

        return false;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static HashSet<string> GetPropertyNames(JsonElement schema)
        => schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object
            ? properties.EnumerateObject()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal)
            : [];

    private static HashSet<string> GetRequiredNames(JsonElement schema)
        => schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array
            ? required.EnumerateArray()
                .Where(property => property.ValueKind == JsonValueKind.String)
                .Select(property => property.GetString()!)
                .ToHashSet(StringComparer.Ordinal)
            : [];

    private static string? GetEmbeddedItemsReference(JsonElement schema)
    {
        if (!schema.TryGetProperty("properties", out var properties)
            || !properties.TryGetProperty("items", out var items)
            || !items.TryGetProperty("items", out var itemSchema))
        {
            return null;
        }

        return GetReference(itemSchema);
    }

    private static string? NormalizeSchemaReference(string? schemaReference)
    {
        if (string.IsNullOrWhiteSpace(schemaReference))
        {
            return schemaReference;
        }

        const string schemaPrefix = "#/components/schemas/";
        var schemaName = schemaReference.StartsWith(schemaPrefix, StringComparison.Ordinal)
            ? schemaReference[schemaPrefix.Length..]
            : schemaReference;

        return NormalizeSchemaName(schemaName);
    }

    private static string NormalizeSchemaName(string schemaName)
    {
        var halWrapperName = TryNormalizeSwashbuckleHalWrapperSchemaName(schemaName);
        if (halWrapperName is not null)
        {
            return halWrapperName;
        }

        var lastNamespaceSeparator = schemaName.LastIndexOf('.');
        return lastNamespaceSeparator >= 0
            ? schemaName[(lastNamespaceSeparator + 1)..]
            : schemaName;
    }

    private static HashSet<string> GetEnumValues(JsonElement schema)
        => schema.TryGetProperty("enum", out var enumValues) && enumValues.ValueKind == JsonValueKind.Array
            ? enumValues.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()!)
                .ToHashSet(StringComparer.Ordinal)
            : [];

    private static string? TryNormalizeSwashbuckleHalWrapperSchemaName(string schemaName)
    {
        if (!schemaName.Contains("Explore.Application.Hateoas.Hal", StringComparison.Ordinal)
            || !schemaName.Contains("Explore.Application.DTOs.", StringComparison.Ordinal))
        {
            return null;
        }

        var wrapperName = schemaName.Contains("HalCollectionResource`1", StringComparison.Ordinal)
            ? "HalCollectionResourceOf"
            : schemaName.Contains("HalCollectionEmbedded`1", StringComparison.Ordinal)
                ? "HalCollectionEmbeddedOf"
            : schemaName.Contains("HalResource`1", StringComparison.Ordinal)
                ? "HalResourceOf"
                : null;

        if (wrapperName is null)
        {
            return null;
        }

        const string dtoMarker = "Explore.Application.DTOs.";
        var dtoStart = schemaName.IndexOf(dtoMarker, StringComparison.Ordinal);
        if (dtoStart < 0)
        {
            return null;
        }

        var dtoTypeStart = schemaName.LastIndexOf('.', schemaName.IndexOf(',', dtoStart)) + 1;
        if (dtoTypeStart <= 0)
        {
            return null;
        }

        var dtoTypeEnd = schemaName.IndexOf(',', dtoTypeStart);
        if (dtoTypeEnd <= dtoTypeStart)
        {
            return null;
        }

        return wrapperName + schemaName[dtoTypeStart..dtoTypeEnd];
    }

    private static bool IsVersionedMediaType(string contentType)
        => contentType.Contains("; v=", StringComparison.Ordinal);

    private readonly record struct OperationSelector(string Path, string Method)
    {
        public override string ToString() => $"{Method.ToUpperInvariant()} {Path}";
    }

    private readonly record struct OperationSecurityExpectation(
        OperationSelector Selector,
        IReadOnlyCollection<string> ExpectedSchemes);
}
