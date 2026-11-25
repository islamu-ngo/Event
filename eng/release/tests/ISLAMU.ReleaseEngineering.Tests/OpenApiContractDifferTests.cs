using System.Text.Json;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class OpenApiContractDifferTests
{
    private const string OpenApiVersion = "3.1.1";
    private const string EventsPath = "/api/events";
    private const string StringType = "string";
    private const string ObjectType = "object";
    private const string TitleField = "title";
    private const string BearerScheme = "bearer";
    private const string ResponsesField = "responses";
    private const string ParametersField = "parameters";
    private const string SlugRef = "#/components/schemas/Slug";
    private const string MaxLengthKeyword = "maxLength";

    private static readonly string BaseOid = new('a', 40);
    private static readonly string HeadOid = new('b', 40);

    [Test]
    public async Task ClassifiesOperationHalLinkRequiredTypeAndSecurityChangesConservatively()
    {
        string baseSpec = SerializeSpec(new
        {
            openapi = OpenApiVersion,
            paths = new Dictionary<string, object>
            {
                [EventsPath] = new Dictionary<string, object>
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
                            ["id"] = new { type = StringType },
                            [TitleField] = new { type = new[] { "null", StringType } },
                            ["_links"] = HalLinks("self", "edit"),
                        }),
                },
            },
        });

        string headSpec = SerializeSpec(new
        {
            openapi = OpenApiVersion,
            paths = new Dictionary<string, object>
            {
                [EventsPath] = new Dictionary<string, object>
                {
                    ["get"] = new
                    {
                        security = new object[] { new Dictionary<string, object> { [BearerScheme] = Array.Empty<string>() } },
                        parameters = new object[]
                        {
                            new { name = "page", @in = "query", required = true, schema = new { type = "integer" } },
                            new { name = "cursor", @in = "query", required = false, schema = new { type = StringType } },
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
                        ["id", TitleField],
                        new Dictionary<string, object>
                        {
                            ["id"] = new { type = StringType },
                            [TitleField] = new { type = StringType },
                            ["subtitle"] = new { type = new[] { "null", StringType } },
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
        await Assert.That(result.Changes.Any(c => c.Kind == "required_property_added" && c.Severity == ContractChangeSeverity.Breaking && c.Target.Contains(TitleField, StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "schema_type_narrowed" && c.Severity == ContractChangeSeverity.Breaking && c.Target.Contains(TitleField, StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "optional_property_added" && c.Severity == ContractChangeSeverity.Additive && c.Target.Contains("subtitle", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task MarksUnresolvedRefsVendorExtensionsAndUnknownStructuralChangesAsReviewRequired()
    {
        string baseSpec = SerializeSpec(new
        {
            openapi = OpenApiVersion,
            paths = new Dictionary<string, object>
            {
                [EventsPath] = new Dictionary<string, object>
                {
                    ["get"] = new Dictionary<string, object>
                    {
                        ["x-islamu-tier"] = "public",
                        [ResponsesField] = JsonResponseRef("#/components/schemas/MissingExternal"),
                    },
                },
            },
            components = new
            {
                schemas = new Dictionary<string, object>
                {
                    ["Payload"] = new
                    {
                        type = ObjectType,
                        properties = new Dictionary<string, object>
                        {
                            ["status"] = new { type = StringType, @default = "draft", pattern = "^[a-z]+$" },
                        },
                    },
                },
            },
        });

        string headSpec = SerializeSpec(new
        {
            openapi = OpenApiVersion,
            paths = new Dictionary<string, object>
            {
                [EventsPath] = new Dictionary<string, object>
                {
                    ["get"] = new Dictionary<string, object>
                    {
                        ["x-islamu-tier"] = "federated",
                        [ResponsesField] = JsonResponseRef("https://example.invalid/external.json#/Payload"),
                    },
                },
            },
            components = new
            {
                schemas = new Dictionary<string, object>
                {
                    ["Payload"] = new
                    {
                        type = ObjectType,
                        properties = new Dictionary<string, object>
                        {
                            ["status"] = new { type = StringType, @default = "published", pattern = "^[a-z0-9-]+$" },
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
            openapi = OpenApiVersion,
            servers = new object[] { new { url = "https://api.example.invalid/v1" } },
            security = Array.Empty<object>(),
            paths = new Dictionary<string, object>
            {
                ["/api/events/{id}"] = new Dictionary<string, object>
                {
                    [ParametersField] = new object[]
                    {
                        new { name = "id", @in = "path", required = true, schema = new { type = StringType } },
                    },
                    ["get"] = new Dictionary<string, object>
                    {
                        ["deprecated"] = false,
                        [ResponsesField] = new Dictionary<string, object>
                        {
                            ["200"] = new Dictionary<string, object>
                            {
                                ["description"] = "OK",
                                ["headers"] = new Dictionary<string, object>
                                {
                                    ["ETag"] = new { schema = new { type = StringType } },
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
                    [BearerScheme] = new { type = "http", scheme = BearerScheme, bearerFormat = "JWT" },
                },
            },
        });

        string headSpec = SerializeSpec(new
        {
            openapi = OpenApiVersion,
            servers = new object[] { new { url = "https://api.example.invalid/v2" } },
            security = new object[] { new Dictionary<string, object> { [BearerScheme] = new[] { "events.read" } } },
            paths = new Dictionary<string, object>
            {
                ["/api/events/{id}"] = new Dictionary<string, object>
                {
                    [ParametersField] = new object[]
                    {
                        new { name = "id", @in = "path", required = true, schema = new { type = StringType } },
                        new { name = "X-Tenant-Slug", @in = "header", required = true, schema = new { type = StringType } },
                    },
                    ["get"] = new Dictionary<string, object>
                    {
                        ["deprecated"] = true,
                        [ResponsesField] = new Dictionary<string, object>
                        {
                            ["200"] = new Dictionary<string, object>
                            {
                                ["description"] = "OK",
                                ["headers"] = new Dictionary<string, object>
                                {
                                    ["ETag"] = new { schema = new { type = StringType } },
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
                    [BearerScheme] = new { type = "oauth2", flows = new Dictionary<string, object>() },
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
            openapi = OpenApiVersion,
            paths = new Dictionary<string, object>(),
            components = new
            {
                schemas = new Dictionary<string, object>
                {
                    ["Slug"] = new { type = StringType },
                    ["AltSlug"] = new { type = StringType },
                    ["CreateRequest"] = new
                    {
                        type = ObjectType,
                        properties = new Dictionary<string, object>
                        {
                            ["slug"] = new Dictionary<string, object> { ["$ref"] = SlugRef },
                            ["unchanged"] = new Dictionary<string, object> { ["$ref"] = SlugRef },
                            ["swapped"] = new Dictionary<string, object> { ["$ref"] = SlugRef, [MaxLengthKeyword] = 100 },
                        },
                    },
                },
            },
        });

        string headSpec = SerializeSpec(new
        {
            openapi = OpenApiVersion,
            paths = new Dictionary<string, object>(),
            components = new
            {
                schemas = new Dictionary<string, object>
                {
                    ["Slug"] = new { type = StringType },
                    ["AltSlug"] = new { type = StringType },
                    ["CreateRequest"] = new
                    {
                        type = ObjectType,
                        properties = new Dictionary<string, object>
                        {
                            ["slug"] = new Dictionary<string, object> { ["$ref"] = SlugRef, [MaxLengthKeyword] = 64 },
                            ["unchanged"] = new Dictionary<string, object> { ["$ref"] = SlugRef },
                            ["swapped"] = new Dictionary<string, object> { ["$ref"] = "#/components/schemas/AltSlug", [MaxLengthKeyword] = 32 },
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
            c.Summary.Contains(MaxLengthKeyword, StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c =>
            c.Kind == "schema_review_required" &&
            c.Severity == ContractChangeSeverity.ReviewRequired &&
            c.Target == "components.schemas.CreateRequest.swapped" &&
            c.Summary.Contains(MaxLengthKeyword, StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Target.Contains("unchanged", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task ResolvesReferencedParametersAndRequestBodiesConservatively()
    {
        var sharedComponents = new Dictionary<string, object>
        {
            [ParametersField] = new Dictionary<string, object>
            {
                ["TenantId"] = new { name = "X-Tenant-Id", @in = "header", required = true, schema = new { type = StringType } },
                ["TraceId"] = new { name = "X-Trace-Id", @in = "header", required = false, schema = new { type = StringType } },
            },
            ["requestBodies"] = new Dictionary<string, object>
            {
                ["CreateEvent"] = new
                {
                    required = true,
                    content = new Dictionary<string, object>
                    {
                        ["application/json"] = new { schema = new { type = ObjectType } },
                    },
                },
            },
        };
        var createdResponses = new Dictionary<string, object> { ["201"] = new { description = "Created" } };

        string baseSpec = SerializeSpec(new
        {
            openapi = OpenApiVersion,
            paths = new Dictionary<string, object>
            {
                [EventsPath] = new Dictionary<string, object>
                {
                    ["post"] = new { parameters = Array.Empty<object>(), responses = createdResponses },
                },
            },
            components = sharedComponents,
        });

        string headSpec = SerializeSpec(new
        {
            openapi = OpenApiVersion,
            paths = new Dictionary<string, object>
            {
                [EventsPath] = new Dictionary<string, object>
                {
                    ["post"] = new Dictionary<string, object>
                    {
                        [ParametersField] = new object[]
                        {
                            RefObj("#/components/parameters/TenantId"),
                            RefObj("#/components/parameters/TraceId"),
                            RefObj("#/components/parameters/MissingParam"),
                        },
                        ["requestBody"] = RefObj("#/components/requestBodies/CreateEvent"),
                        [ResponsesField] = createdResponses,
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
        object[] rootSecurity = [new Dictionary<string, object> { [BearerScheme] = Array.Empty<string>() }];
        var okResponses = new Dictionary<string, object> { ["200"] = new { description = "OK" } };

        string BuildPayloadSpec(object operation, Dictionary<string, object> payloadProperties) => SerializeSpec(new
        {
            openapi = OpenApiVersion,
            security = rootSecurity,
            paths = new Dictionary<string, object> { [EventsPath] = new Dictionary<string, object> { ["get"] = operation } },
            components = new
            {
                schemas = new Dictionary<string, object>
                {
                    ["Slug"] = new { type = StringType },
                    ["NullableSlug"] = new { type = new[] { "null", StringType } },
                    ["Payload"] = new { type = ObjectType, properties = payloadProperties },
                },
            },
        });

        string baseSpec = BuildPayloadSpec(
            new { responses = okResponses },
            new Dictionary<string, object>
            {
                ["redundant"] = new Dictionary<string, object> { ["$ref"] = SlugRef, ["type"] = StringType },
                ["narrowed"] = RefObj("#/components/schemas/NullableSlug"),
                ["unconstrained"] = new { type = StringType },
            });

        string headSpec = BuildPayloadSpec(
            new { security = Array.Empty<object>(), responses = okResponses },
            new Dictionary<string, object>
            {
                ["redundant"] = RefObj(SlugRef),
                ["narrowed"] = new Dictionary<string, object> { ["$ref"] = "#/components/schemas/NullableSlug", ["type"] = StringType },
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
    public async Task DetectsExtensionsBesideReferencedRequestBodiesAndResponsesAndEquatesEmptySecurityForms()
    {
        var sharedComponents = new Dictionary<string, object>
        {
            ["requestBodies"] = new Dictionary<string, object>
            {
                ["EventPayload"] = new
                {
                    required = true,
                    content = new Dictionary<string, object>
                    {
                        ["application/json"] = new { schema = new { type = ObjectType } },
                    },
                },
            },
            [ResponsesField] = new Dictionary<string, object>
            {
                ["OkEvent"] = new { description = "OK" },
            },
        };

        string BuildSpec(object getOperation, Dictionary<string, object> postRequestBody, Dictionary<string, object> postResponse) =>
            SerializeSpec(new
            {
                openapi = OpenApiVersion,
                security = new object[] { new Dictionary<string, object>() },
                paths = new Dictionary<string, object>
                {
                    [EventsPath] = new Dictionary<string, object>
                    {
                        ["get"] = getOperation,
                        ["post"] = new
                        {
                            requestBody = postRequestBody,
                            responses = new Dictionary<string, object> { ["200"] = postResponse },
                        },
                    },
                },
                components = sharedComponents,
            });

        string baseSpec = BuildSpec(
            new
            {
                security = Array.Empty<object>(),
                responses = new Dictionary<string, object> { ["200"] = new { description = "OK" } },
            },
            new Dictionary<string, object> { ["$ref"] = "#/components/requestBodies/EventPayload", ["x-islamu-audit"] = "v1" },
            new Dictionary<string, object> { ["$ref"] = "#/components/responses/OkEvent", ["x-islamu-cache"] = "private" });

        string headSpec = BuildSpec(
            new
            {
                responses = new Dictionary<string, object> { ["200"] = new { description = "OK" } },
            },
            new Dictionary<string, object> { ["$ref"] = "#/components/requestBodies/EventPayload", ["x-islamu-audit"] = "v2" },
            new Dictionary<string, object> { ["$ref"] = "#/components/responses/OkEvent", ["x-islamu-cache"] = "public" });

        ContractDifferResult result = DiffSpecs(baseSpec, headSpec);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "raw ref extensions and anonymous security");
        await Assert.That(result.Changes.Any(c =>
            c.Kind == "extension_changed" &&
            c.Severity == ContractChangeSeverity.ReviewRequired &&
            c.Target == "POST /api/events requestBody x-islamu-audit")).IsTrue();
        await Assert.That(result.Changes.Any(c =>
            c.Kind == "extension_changed" &&
            c.Severity == ContractChangeSeverity.ReviewRequired &&
            c.Target == "POST /api/events response:200 x-islamu-cache")).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Target.Contains("GET /api/events", StringComparison.Ordinal))).IsFalse();
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
        type = ObjectType,
        required,
        properties,
    };

    private static object HalLinks(params string[] relations) => new
    {
        type = ObjectType,
        properties = relations.ToDictionary(relation => relation, _ => (object)new { type = ObjectType }),
    };
}
