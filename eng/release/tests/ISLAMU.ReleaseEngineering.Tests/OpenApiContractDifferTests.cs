using System.Text.Json;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class OpenApiContractDifferTests
{
    private static readonly string BaseOid = new('a', 40);
    private static readonly string HeadOid = new('b', 40);

    [Test]
    public async Task ClassifiesOperationHalLinkRequiredTypeAndSecurityChangesConservatively()
    {
        string baseSpec = SerializeSpec(new
        {
            openapi = "3.1.1",
            paths = new Dictionary<string, object>
            {
                ["/api/events"] = new Dictionary<string, object>
                {
                    ["get"] = new
                    {
                        security = Array.Empty<object>(),
                        parameters = new object[]
                        {
                            new { name = "page", @in = "query", required = false, schema = new { type = "integer" } },
                        },
                        responses = JsonResponseRef("#/components/schemas/EventResource"),
                    },
                    ["delete"] = new { responses = new Dictionary<string, object> { ["204"] = new { description = "Deleted" } } },
                },
            },
            components = new
            {
                schemas = new Dictionary<string, object>
                {
                    ["EventResource"] = EventResourceSchema(
                        ["id"],
                        new Dictionary<string, object>
                        {
                            ["id"] = new { type = "string" },
                            ["title"] = new { type = new[] { "null", "string" } },
                            ["_links"] = HalLinks("self", "edit"),
                        }),
                },
            },
        });

        string headSpec = SerializeSpec(new
        {
            openapi = "3.1.1",
            paths = new Dictionary<string, object>
            {
                ["/api/events"] = new Dictionary<string, object>
                {
                    ["get"] = new
                    {
                        security = new object[] { new Dictionary<string, object> { ["bearer"] = Array.Empty<string>() } },
                        parameters = new object[]
                        {
                            new { name = "page", @in = "query", required = true, schema = new { type = "integer" } },
                            new { name = "cursor", @in = "query", required = false, schema = new { type = "string" } },
                        },
                        responses = JsonResponseRef("#/components/schemas/EventResource"),
                    },
                },
                ["/api/events/featured"] = new Dictionary<string, object>
                {
                    ["get"] = new { responses = new Dictionary<string, object> { ["200"] = new { description = "OK" } } },
                },
            },
            components = new
            {
                schemas = new Dictionary<string, object>
                {
                    ["EventResource"] = EventResourceSchema(
                        ["id", "title"],
                        new Dictionary<string, object>
                        {
                            ["id"] = new { type = "string" },
                            ["title"] = new { type = "string" },
                            ["subtitle"] = new { type = new[] { "null", "string" } },
                            ["_links"] = HalLinks("self", "publish"),
                        }),
                },
            },
        });

        ContractDifferResult result = DiffSpecs(baseSpec, headSpec);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "openapi diff");
        await Assert.That(result.Changes.Any(c => c.Kind == "operation_removed" && c.Severity == ContractChangeSeverity.Breaking && c.Target.Contains("DELETE /api/events", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "operation_added" && c.Severity == ContractChangeSeverity.Additive && c.Target.Contains("GET /api/events/featured", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "security_narrowed" && c.Severity == ContractChangeSeverity.Breaking)).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "parameter_narrowed_required" && c.Severity == ContractChangeSeverity.Breaking)).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "optional_parameter_added" && c.Severity == ContractChangeSeverity.Additive)).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "hal_link_removed" && c.Severity == ContractChangeSeverity.Breaking && c.Target.Contains("edit", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "hal_link_added" && c.Severity == ContractChangeSeverity.Additive && c.Target.Contains("publish", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "required_property_added" && c.Severity == ContractChangeSeverity.Breaking && c.Target.Contains("title", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "schema_type_narrowed" && c.Severity == ContractChangeSeverity.Breaking && c.Target.Contains("title", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "optional_property_added" && c.Severity == ContractChangeSeverity.Additive && c.Target.Contains("subtitle", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task MarksUnresolvedRefsVendorExtensionsAndUnknownStructuralChangesAsReviewRequired()
    {
        string baseSpec = SerializeSpec(new
        {
            openapi = "3.1.1",
            paths = new Dictionary<string, object>
            {
                ["/api/events"] = new Dictionary<string, object>
                {
                    ["get"] = new Dictionary<string, object>
                    {
                        ["x-islamu-tier"] = "public",
                        ["responses"] = JsonResponseRef("#/components/schemas/MissingExternal"),
                    },
                },
            },
            components = new
            {
                schemas = new Dictionary<string, object>
                {
                    ["Payload"] = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["status"] = new { type = "string", @default = "draft", pattern = "^[a-z]+$" },
                        },
                    },
                },
            },
        });

        string headSpec = SerializeSpec(new
        {
            openapi = "3.1.1",
            paths = new Dictionary<string, object>
            {
                ["/api/events"] = new Dictionary<string, object>
                {
                    ["get"] = new Dictionary<string, object>
                    {
                        ["x-islamu-tier"] = "federated",
                        ["responses"] = JsonResponseRef("https://example.invalid/external.json#/Payload"),
                    },
                },
            },
            components = new
            {
                schemas = new Dictionary<string, object>
                {
                    ["Payload"] = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["status"] = new { type = "string", @default = "published", pattern = "^[a-z0-9-]+$" },
                        },
                    },
                },
            },
        });

        ContractDifferResult result = DiffSpecs(baseSpec, headSpec);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "review required");
        await Assert.That(result.Changes.Any(c => c.Kind == "unresolved_ref" && c.Severity == ContractChangeSeverity.ReviewRequired)).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "extension_changed" && c.Severity == ContractChangeSeverity.ReviewRequired)).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "schema_review_required" && c.Severity == ContractChangeSeverity.ReviewRequired && c.Target.Contains("status", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task SurfacesChangesToSecuritySchemesRootSecurityServersPathParametersAndOperationMetadata()
    {
        string baseSpec = SerializeSpec(new
        {
            openapi = "3.1.1",
            servers = new object[] { new { url = "https://api.example.invalid/v1" } },
            security = Array.Empty<object>(),
            paths = new Dictionary<string, object>
            {
                ["/api/events/{id}"] = new Dictionary<string, object>
                {
                    ["parameters"] = new object[]
                    {
                        new { name = "id", @in = "path", required = true, schema = new { type = "string" } },
                    },
                    ["get"] = new Dictionary<string, object>
                    {
                        ["deprecated"] = false,
                        ["responses"] = new Dictionary<string, object>
                        {
                            ["200"] = new Dictionary<string, object>
                            {
                                ["description"] = "OK",
                                ["headers"] = new Dictionary<string, object>
                                {
                                    ["ETag"] = new { schema = new { type = "string" } },
                                },
                            },
                        },
                    },
                },
            },
            components = new Dictionary<string, object>
            {
                ["securitySchemes"] = new Dictionary<string, object>
                {
                    ["bearer"] = new { type = "http", scheme = "bearer", bearerFormat = "JWT" },
                },
            },
        });

        string headSpec = SerializeSpec(new
        {
            openapi = "3.1.1",
            servers = new object[] { new { url = "https://api.example.invalid/v2" } },
            security = new object[] { new Dictionary<string, object> { ["bearer"] = new[] { "events.read" } } },
            paths = new Dictionary<string, object>
            {
                ["/api/events/{id}"] = new Dictionary<string, object>
                {
                    ["parameters"] = new object[]
                    {
                        new { name = "id", @in = "path", required = true, schema = new { type = "string" } },
                        new { name = "X-Tenant-Slug", @in = "header", required = true, schema = new { type = "string" } },
                    },
                    ["get"] = new Dictionary<string, object>
                    {
                        ["deprecated"] = true,
                        ["responses"] = new Dictionary<string, object>
                        {
                            ["200"] = new Dictionary<string, object>
                            {
                                ["description"] = "OK",
                                ["headers"] = new Dictionary<string, object>
                                {
                                    ["ETag"] = new { schema = new { type = "string" } },
                                    ["X-RateLimit-Remaining"] = new { schema = new { type = "integer" } },
                                },
                            },
                        },
                    },
                },
            },
            components = new Dictionary<string, object>
            {
                ["securitySchemes"] = new Dictionary<string, object>
                {
                    ["bearer"] = new { type = "oauth2", flows = new Dictionary<string, object>() },
                },
            },
        });

        ContractDifferResult result = DiffSpecs(baseSpec, headSpec);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "structural fallback");
        await Assert.That(result.Changes.Any(c => c.Kind == "openapi_component_changed" && c.Severity == ContractChangeSeverity.ReviewRequired && c.Target == "components.securitySchemes.bearer")).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "security_narrowed" && c.Severity == ContractChangeSeverity.Breaking && c.Target == "document")).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "openapi_review_required" && c.Severity == ContractChangeSeverity.ReviewRequired && c.Target == "document.servers")).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "required_parameter_added" && c.Severity == ContractChangeSeverity.Breaking && c.Target == "path:/api/events/{id} param:header:X-Tenant-Slug")).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "openapi_review_required" && c.Severity == ContractChangeSeverity.ReviewRequired && c.Target == "GET /api/events/{id}.deprecated")).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "openapi_review_required" && c.Severity == ContractChangeSeverity.ReviewRequired && c.Target == "GET /api/events/{id} response:200.headers")).IsTrue();
    }

    [Test]
    public async Task DetectsChangedSiblingConstraintsBesideUnchangedOrChangedDollarRef()
    {
        string baseSpec = SerializeSpec(new
        {
            openapi = "3.1.1",
            paths = new Dictionary<string, object>(),
            components = new
            {
                schemas = new Dictionary<string, object>
                {
                    ["Slug"] = new { type = "string" },
                    ["AltSlug"] = new { type = "string" },
                    ["CreateRequest"] = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["slug"] = new Dictionary<string, object> { ["$ref"] = "#/components/schemas/Slug" },
                            ["unchanged"] = new Dictionary<string, object> { ["$ref"] = "#/components/schemas/Slug" },
                            ["swapped"] = new Dictionary<string, object> { ["$ref"] = "#/components/schemas/Slug", ["maxLength"] = 100 },
                        },
                    },
                },
            },
        });

        string headSpec = SerializeSpec(new
        {
            openapi = "3.1.1",
            paths = new Dictionary<string, object>(),
            components = new
            {
                schemas = new Dictionary<string, object>
                {
                    ["Slug"] = new { type = "string" },
                    ["AltSlug"] = new { type = "string" },
                    ["CreateRequest"] = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["slug"] = new Dictionary<string, object> { ["$ref"] = "#/components/schemas/Slug", ["maxLength"] = 64 },
                            ["unchanged"] = new Dictionary<string, object> { ["$ref"] = "#/components/schemas/Slug" },
                            ["swapped"] = new Dictionary<string, object> { ["$ref"] = "#/components/schemas/AltSlug", ["maxLength"] = 32 },
                        },
                    },
                },
            },
        });

        ContractDifferResult result = DiffSpecs(baseSpec, headSpec);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "ref sibling diff");
        await Assert.That(result.Changes.Any(c =>
            c.Kind == "schema_review_required" &&
            c.Severity == ContractChangeSeverity.ReviewRequired &&
            c.Target == "components.schemas.CreateRequest.slug" &&
            c.Summary.Contains("maxLength", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c =>
            c.Kind == "schema_review_required" &&
            c.Severity == ContractChangeSeverity.ReviewRequired &&
            c.Target == "components.schemas.CreateRequest.swapped" &&
            c.Summary.Contains("maxLength", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Target.Contains("unchanged", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task ResolvesReferencedParametersAndRequestBodiesConservatively()
    {
        var sharedComponents = new Dictionary<string, object>
        {
            ["parameters"] = new Dictionary<string, object>
            {
                ["TenantId"] = new { name = "X-Tenant-Id", @in = "header", required = true, schema = new { type = "string" } },
                ["TraceId"] = new { name = "X-Trace-Id", @in = "header", required = false, schema = new { type = "string" } },
            },
            ["requestBodies"] = new Dictionary<string, object>
            {
                ["CreateEvent"] = new
                {
                    required = true,
                    content = new Dictionary<string, object>
                    {
                        ["application/json"] = new { schema = new { type = "object" } },
                    },
                },
            },
        };
        var createdResponses = new Dictionary<string, object> { ["201"] = new { description = "Created" } };

        string baseSpec = SerializeSpec(new
        {
            openapi = "3.1.1",
            paths = new Dictionary<string, object>
            {
                ["/api/events"] = new Dictionary<string, object>
                {
                    ["post"] = new { parameters = Array.Empty<object>(), responses = createdResponses },
                },
            },
            components = sharedComponents,
        });

        string headSpec = SerializeSpec(new
        {
            openapi = "3.1.1",
            paths = new Dictionary<string, object>
            {
                ["/api/events"] = new Dictionary<string, object>
                {
                    ["post"] = new Dictionary<string, object>
                    {
                        ["parameters"] = new object[]
                        {
                            RefObj("#/components/parameters/TenantId"),
                            RefObj("#/components/parameters/TraceId"),
                            RefObj("#/components/parameters/MissingParam"),
                        },
                        ["requestBody"] = RefObj("#/components/requestBodies/CreateEvent"),
                        ["responses"] = createdResponses,
                    },
                },
            },
            components = sharedComponents,
        });

        ContractDifferResult result = DiffSpecs(baseSpec, headSpec);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "referenced parameter/body diff");
        await Assert.That(result.Changes.Any(c =>
            c.Kind == "required_parameter_added" &&
            c.Severity == ContractChangeSeverity.Breaking &&
            c.Target == "POST /api/events param:header:X-Tenant-Id")).IsTrue();
        await Assert.That(result.Changes.Any(c =>
            c.Kind == "optional_parameter_added" &&
            c.Severity == ContractChangeSeverity.Additive &&
            c.Target == "POST /api/events param:header:X-Trace-Id")).IsTrue();
        await Assert.That(result.Changes.Any(c =>
            c.Kind == "unresolved_ref" &&
            c.Severity == ContractChangeSeverity.ReviewRequired &&
            c.Target.Contains("MissingParam", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c =>
            c.Kind == "required_request_body_added" &&
            c.Severity == ContractChangeSeverity.Breaking &&
            c.Target == "POST /api/events requestBody")).IsTrue();
    }

    [Test]
    public async Task EvaluatesCombinedRefTypeConstraintsWithoutFalseBreakingOnRedundantTypeRemoval()
    {
        object[] rootSecurity = [new Dictionary<string, object> { ["bearer"] = Array.Empty<string>() }];
        var okResponses = new Dictionary<string, object> { ["200"] = new { description = "OK" } };

        string BuildPayloadSpec(object operation, Dictionary<string, object> payloadProperties) => SerializeSpec(new
        {
            openapi = "3.1.1",
            security = rootSecurity,
            paths = new Dictionary<string, object> { ["/api/events"] = new Dictionary<string, object> { ["get"] = operation } },
            components = new
            {
                schemas = new Dictionary<string, object>
                {
                    ["Slug"] = new { type = "string" },
                    ["NullableSlug"] = new { type = new[] { "null", "string" } },
                    ["Payload"] = new { type = "object", properties = payloadProperties },
                },
            },
        });

        string baseSpec = BuildPayloadSpec(
            new { responses = okResponses },
            new Dictionary<string, object>
            {
                ["redundant"] = new Dictionary<string, object> { ["$ref"] = "#/components/schemas/Slug", ["type"] = "string" },
                ["narrowed"] = RefObj("#/components/schemas/NullableSlug"),
                ["unconstrained"] = new { type = "string" },
            });

        string headSpec = BuildPayloadSpec(
            new { security = Array.Empty<object>(), responses = okResponses },
            new Dictionary<string, object>
            {
                ["redundant"] = RefObj("#/components/schemas/Slug"),
                ["narrowed"] = new Dictionary<string, object> { ["$ref"] = "#/components/schemas/NullableSlug", ["type"] = "string" },
                ["unconstrained"] = new Dictionary<string, object>(),
            });

        ContractDifferResult result = DiffSpecs(baseSpec, headSpec);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "combined ref type diff");
        await Assert.That(result.Changes.Any(c => c.Target.Contains("redundant", StringComparison.Ordinal))).IsFalse();
        await Assert.That(result.Changes.Any(c =>
            c.Kind == "schema_type_narrowed" &&
            c.Severity == ContractChangeSeverity.Breaking &&
            c.Target == "components.schemas.Payload.narrowed")).IsTrue();
        await Assert.That(result.Changes.Any(c =>
            c.Kind == "schema_type_changed" &&
            c.Severity == ContractChangeSeverity.ReviewRequired &&
            c.Target == "components.schemas.Payload.unconstrained")).IsTrue();
        await Assert.That(result.Changes.Any(c =>
            c.Kind == "security_changed" &&
            c.Severity == ContractChangeSeverity.ReviewRequired &&
            c.Target == "GET /api/events")).IsTrue();
    }

    [Test]
    public async Task MalformedOpenApiJsonFailsClosed()
    {
        ContractDifferResult result = DiffSpecs("{}", "{ invalid json");

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Diagnostic).IsEqualTo("contract_diff_openapi_malformed");
    }

    private static ContractDifferResult DiffSpecs(string baseSpec, string headSpec)
    {
        InMemoryGitObjectReader reader = new InMemoryGitObjectReader()
            .AddCommit(BaseOid, new Dictionary<string, string> { ["schemas/openapi_islamu-event.json"] = baseSpec })
            .AddCommit(HeadOid, new Dictionary<string, string> { ["schemas/openapi_islamu-event.json"] = headSpec });
        return new OpenApiContractDiffer().Diff(reader, BaseOid, HeadOid);
    }

    private static string SerializeSpec(object spec) => JsonSerializer.Serialize(spec);

    private static Dictionary<string, object> RefObj(string reference) => new() { ["$ref"] = reference };

    private static Dictionary<string, object> JsonResponseRef(string reference) => new()
    {
        ["200"] = new
        {
            content = new Dictionary<string, object>
            {
                ["application/json"] = new
                {
                    schema = new Dictionary<string, object> { ["$ref"] = reference },
                },
            },
        },
    };

    private static object EventResourceSchema(string[] required, Dictionary<string, object> properties) => new
    {
        type = "object",
        required,
        properties,
    };

    private static object HalLinks(params string[] relations) => new
    {
        type = "object",
        properties = relations.ToDictionary(relation => relation, _ => (object)new { type = "object" }),
    };
}
