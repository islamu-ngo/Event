namespace Event.Architecture.Tests;

using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Clients;

public sealed class SetupLiveGeneratedContractTests
{
    private const string CapabilityHeader =
        "X-Setup-Enrollment-Capability";
    private const string SuccessMediaType = "application/hal+json";
    private const string ErrorMediaType = "application/problem+json";
    private const string WritePath =
        "/api/tenants/{tenantId}/setup/enrollments/{enrollmentId}/secret-bindings/{bindingKey}";

    [Test]
    public async Task OpenApiAndGeneratedClientCloseTheWriteOnlySetupContract()
    {
        string repositoryRoot = ResolveRepositoryRoot();
        await using FileStream schema = File.OpenRead(Path.Combine(
            repositoryRoot,
            "schemas",
            "openapi_islamu-event.json"));
        using JsonDocument document = await JsonDocument.ParseAsync(schema);
        JsonElement operation = document.RootElement.GetProperty("paths")
            .GetProperty(WritePath)
            .GetProperty("put");
        JsonElement requestBody = operation.GetProperty("requestBody");
        JsonElement binary = requestBody.GetProperty("content")
            .GetProperty("application/octet-stream")
            .GetProperty("schema");

        await Assert.That(requestBody.GetProperty("required").GetBoolean()).IsTrue();
        await Assert.That(binary.GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(binary.GetProperty("format").GetString()).IsEqualTo("binary");

        AssertSetupResponses(document.RootElement);
        AssertSetupRequests(document.RootElement);
        AssertSetupSchemas(document.RootElement.GetProperty("components")
            .GetProperty("schemas"));

        MethodInfo write = typeof(ISetup_LiveClient).GetMethod(
            "WriteSetupSecretBindingAsync")
            ?? throw new InvalidOperationException("missing-generated-setup-write");
        Type[] writeParameters = write.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        await Assert.That(writeParameters.SequenceEqual(
        [
            typeof(Guid), typeof(Guid), typeof(string), typeof(string),
            typeof(string), typeof(Stream), typeof(string), typeof(string),
            typeof(CancellationToken)
        ])).IsTrue();
        await Assert.That(write.ReturnType)
            .IsEqualTo(typeof(Task<HalResourceOfSetupSecretBindingOperationData>));
        await Assert.That(typeof(ISetup_LiveClient).GetMethod(
                "CreateSetupTargetEnrollmentAsync")?.ReturnType)
            .IsEqualTo(typeof(Task<SwaggerResponse<HalResourceOfSetupTargetEnrollmentData>>));
        await Assert.That(typeof(ISetup_LiveClient).GetMethod(
                "RotateSetupTargetEnrollmentCapabilityAsync")?.ReturnType)
            .IsEqualTo(typeof(Task<SwaggerResponse<HalResourceOfSetupTargetEnrollmentData>>));
        await AssertRequiredStringParameters("CreateSetupTargetEnrollmentAsync",
            "idempotency_Key");
        await AssertRequiredStringParameters("GetSetupTargetEnrollmentAsync",
            "x_Setup_Enrollment_Capability");
        await AssertRequiredStringParameters("RevokeSetupTargetEnrollmentAsync",
            "x_Setup_Enrollment_Capability", "idempotency_Key");
        await AssertRequiredStringParameters("RotateSetupTargetEnrollmentCapabilityAsync",
            "x_Setup_Enrollment_Capability", "idempotency_Key");
        await AssertRequiredStringParameters("GetSetupSecretBindingReadinessAsync",
            "x_Setup_Enrollment_Capability");
        await AssertRequiredStringParameters("WriteSetupSecretBindingAsync",
            "x_Setup_Enrollment_Capability", "idempotency_Key");
        await AssertRequiredStringParameters("GetSetupSecretBindingOperationAsync",
            "x_Setup_Enrollment_Capability");

        await AssertPropertyType<CreateSetupTargetEnrollmentRequest>(
            "ClientChallenge", typeof(string));
        await AssertPropertyType<CreateSetupTargetEnrollmentRequest>(
            "RequestedScopes", typeof(ICollection<SetupEnrollmentScope>));
        await AssertPropertyType<HalResourceOfSetupTargetEnrollmentData>(
            "State", typeof(SetupEnrollmentState));
        await AssertPropertyType<HalResourceOfSetupTargetEnrollmentData>(
            "Scopes", typeof(ICollection<SetupEnrollmentScope>));
        await AssertPropertyType<HalResourceOfSetupTargetEnrollmentData>(
            "Issuance", typeof(SetupEnrollmentIssuance));
        await AssertPropertyType<HalResourceOfSetupSecretBindingOperationData>(
            "State", typeof(SetupSecretBindingOperationState));
        await AssertPropertyType<HalResourceOfSetupSecretBindingOperationData>(
            "Outcome", typeof(SetupSecretBindingOperationOutcome));
        await AssertPropertyType<HalResourceOfSetupSecretBindingReadinessItem>(
            "State", typeof(SetupSecretBindingReadinessState));

        await AssertProperties<HalResourceOfSetupTargetEnrollmentData>(
            "EnrollmentId", "State", "Generation", "ExpiresAt", "Scopes",
            "Issuance", "_links", "_embedded", "AdditionalProperties");
        await AssertProperties<HalResourceOfSetupSecretBindingOperationData>(
            "OperationId", "State", "Outcome", "EnrollmentGeneration",
            "CreatedAt", "SettledAt", "_links", "_embedded",
            "AdditionalProperties");
        await Assert.That(typeof(HalResourceOfSetupSecretBindingReadinessDocument)
                .GetProperty("_embedded")?.PropertyType.Name)
            .IsEqualTo("HalCollectionEmbeddedOfSetupSecretBindingReadinessItem");

        string[] forbidden =
        [
            "Secret", "Capability", "Provider", "Environment", "Path",
            "Coordinate", "Token", "Credential"
        ];
        string[] setupContractNames =
        [
            "CreateSetupTargetEnrollmentRequest",
            "HalResourceOfSetupTargetEnrollmentData",
            "HalResourceOfSetupSecretBindingOperationData",
            "HalResourceOfSetupSecretBindingReadinessDocument",
            "HalResourceOfSetupSecretBindingReadinessItem",
            "HalCollectionEmbeddedOfSetupSecretBindingReadinessItem"
        ];
        Type[] setupTypes = typeof(ISetup_LiveClient).Assembly.GetExportedTypes()
            .Where(type => setupContractNames.Contains(type.Name, StringComparer.Ordinal))
            .ToArray();
        await Assert.That(setupTypes.Select(type => type.Name))
            .IsEquivalentTo(setupContractNames);
        foreach (PropertyInfo property in setupTypes.SelectMany(type =>
                     type.GetProperties(BindingFlags.Public | BindingFlags.Instance)))
        foreach (string fragment in forbidden)
        {
            await Assert.That(property.Name).DoesNotContain(
                fragment,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Test]
    public async Task GeneratedCreateUsesCanonicalJsonAndExposesOnlyIssuedCapability()
    {
        string challenge = Base64Url(RandomNumberGenerator.GetBytes(32));
        string capability = Base64Url(RandomNumberGenerator.GetBytes(32));
        Guid enrollmentId = Guid.CreateVersion7();
        using var handler = new SetupResponseHandler(
            EnrollmentResponse(enrollmentId, capability, HttpStatusCode.Created),
            EnrollmentResponse(enrollmentId, null, HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://setup.invalid/")
        };
        var client = new Setup_LiveClient(httpClient);
        var request = new CreateSetupTargetEnrollmentRequest
        {
            ClientChallenge = challenge,
            RequestedScopes = [SetupEnrollmentScope.Target_read]
        };

        SwaggerResponse<HalResourceOfSetupTargetEnrollmentData> issued =
            await client.CreateSetupTargetEnrollmentAsync(
                Guid.CreateVersion7(),
                Guid.CreateVersion7().ToString("D"),
                request);
        SwaggerResponse<HalResourceOfSetupTargetEnrollmentData> duplicate =
            await client.CreateSetupTargetEnrollmentAsync(
                Guid.CreateVersion7(),
                Guid.CreateVersion7().ToString("D"),
                request);

        await Assert.That(issued.Headers[CapabilityHeader].Single())
            .IsEqualTo(capability);
        await Assert.That(duplicate.Headers.ContainsKey(CapabilityHeader)).IsFalse();
        await Assert.That(handler.AcceptMediaTypes)
            .IsEquivalentTo([SuccessMediaType, SuccessMediaType]);
        await Assert.That(handler.ContentTypes)
            .IsEquivalentTo(["application/json", "application/json"]);
        foreach (string body in handler.Bodies)
        {
            using JsonDocument json = JsonDocument.Parse(body);
            await Assert.That(json.RootElement.GetProperty("clientChallenge").GetString())
                .IsEqualTo(challenge);
            await Assert.That(json.RootElement.GetProperty("requestedScopes")[0].GetString())
                .IsEqualTo("target.read");
        }
    }

    private static void AssertSetupResponses(JsonElement document)
    {
        (string Path, string Method, string[] SuccessCodes)[] operations =
        [
            ("/api/tenants/{tenantId}/setup/enrollments", "post", ["200", "201"]),
            ("/api/tenants/{tenantId}/setup/enrollments/{enrollmentId}", "get", ["200"]),
            ("/api/tenants/{tenantId}/setup/enrollments/{enrollmentId}", "delete", ["200"]),
            ("/api/tenants/{tenantId}/setup/enrollments/{enrollmentId}/capability-rotations", "post", ["200"]),
            ("/api/tenants/{tenantId}/setup/enrollments/{enrollmentId}/secret-bindings/readiness", "get", ["200"]),
            (WritePath, "put", ["202"]),
            ("/api/tenants/{tenantId}/setup/enrollments/{enrollmentId}/secret-binding-operations/{operationId}", "get", ["200"])
        ];

        foreach ((string path, string method, string[] successCodes) in operations)
        {
            JsonElement responses = document.GetProperty("paths")
                .GetProperty(path)
                .GetProperty(method)
                .GetProperty("responses");
            foreach (JsonProperty response in responses.EnumerateObject())
            {
                string[] mediaTypes = response.Value.GetProperty("content")
                    .EnumerateObject()
                    .Select(content => content.Name)
                    .ToArray();
                string expected = successCodes.Contains(response.Name, StringComparer.Ordinal)
                    ? SuccessMediaType
                    : ErrorMediaType;
                if (!mediaTypes.SequenceEqual([expected], StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"setup-media-mismatch:{method}:{path}:{response.Name}:{string.Join(',', mediaTypes)}");
                }
            }
        }

        AssertCapabilityHeader(document, "/api/tenants/{tenantId}/setup/enrollments", "post", "201");
        AssertCapabilityHeader(document, "/api/tenants/{tenantId}/setup/enrollments/{enrollmentId}/capability-rotations", "post", "200");
    }

    private static void AssertSetupRequests(JsonElement document)
    {
        (string Path, string Method, string[] Headers)[] operations =
        [
            ("/api/tenants/{tenantId}/setup/enrollments", "post", ["Idempotency-Key"]),
            ("/api/tenants/{tenantId}/setup/enrollments/{enrollmentId}", "get", [CapabilityHeader]),
            ("/api/tenants/{tenantId}/setup/enrollments/{enrollmentId}", "delete", [CapabilityHeader, "Idempotency-Key"]),
            ("/api/tenants/{tenantId}/setup/enrollments/{enrollmentId}/capability-rotations", "post", [CapabilityHeader, "Idempotency-Key"]),
            ("/api/tenants/{tenantId}/setup/enrollments/{enrollmentId}/secret-bindings/readiness", "get", [CapabilityHeader]),
            (WritePath, "put", [CapabilityHeader, "Idempotency-Key"]),
            ("/api/tenants/{tenantId}/setup/enrollments/{enrollmentId}/secret-binding-operations/{operationId}", "get", [CapabilityHeader])
        ];
        foreach ((string path, string method, string[] requiredHeaders) in operations)
        {
            JsonElement operation = document.GetProperty("paths")
                .GetProperty(path)
                .GetProperty(method);
            JsonElement[] parameters = operation.GetProperty("parameters")
                .EnumerateArray()
                .ToArray();
            foreach (string header in requiredHeaders)
            {
                JsonElement parameter = parameters.Single(candidate =>
                    candidate.GetProperty("in").GetString() == "header"
                    && candidate.GetProperty("name").GetString() == header);
                if (!parameter.GetProperty("required").GetBoolean())
                    throw new InvalidOperationException($"setup-header-optional:{method}:{path}:{header}");
            }
        }

        string[] createMedia = document.GetProperty("paths")
            .GetProperty("/api/tenants/{tenantId}/setup/enrollments")
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .EnumerateObject()
            .Select(content => content.Name)
            .ToArray();
        if (!createMedia.SequenceEqual(["application/json"], StringComparer.Ordinal))
            throw new InvalidOperationException("setup-create-media-invalid");
    }

    private static void AssertCapabilityHeader(
        JsonElement document,
        string path,
        string method,
        string status)
    {
        JsonElement schema = document.GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method)
            .GetProperty("responses")
            .GetProperty(status)
            .GetProperty("headers")
            .GetProperty(CapabilityHeader)
            .GetProperty("schema");
        if (schema.GetProperty("type").GetString() != "string")
            throw new InvalidOperationException("setup-capability-header-not-string");
    }

    private static void AssertSetupSchemas(JsonElement schemas)
    {
        JsonElement challenge = schemas.GetProperty("SetupClientChallenge");
        if (challenge.GetProperty("type").GetString() != "string"
            || challenge.GetProperty("minLength").GetInt32() != 43
            || challenge.GetProperty("maxLength").GetInt32() != 43)
        {
            throw new InvalidOperationException("setup-client-challenge-schema-invalid");
        }

        AssertEnum(schemas, "SetupEnrollmentScope",
            "target.read", "secret_binding.readiness", "secret_binding.write");
        AssertEnum(schemas, "SetupEnrollmentState", "active", "revoked", "expired");
        AssertEnum(schemas, "SetupEnrollmentIssuance", "issued", "already_issued");
        AssertEnum(schemas, "SetupSecretBindingReadinessState",
            "unconfigured", "ready", "unavailable", "unauthorized", "invalid");
        AssertEnum(schemas, "SetupSecretBindingOperationState",
            "accepted", "succeeded", "failed", "cancelled");
        AssertEnum(schemas, "SetupSecretBindingOperationOutcome",
            "accepted", "ready", "unavailable", "unauthorized", "invalid",
            "cancelled", "unavailable_enrollment");

        JsonElement request = schemas.GetProperty("CreateSetupTargetEnrollmentRequest")
            .GetProperty("properties");
        AssertReference(request.GetProperty("clientChallenge"), "SetupClientChallenge");
        JsonElement requestedScopes = request.GetProperty("requestedScopes");
        AssertReference(requestedScopes.GetProperty("items"), "SetupEnrollmentScope");
        if (!requestedScopes.GetProperty("uniqueItems").GetBoolean()
            || requestedScopes.GetProperty("minItems").GetInt32() != 1
            || requestedScopes.GetProperty("maxItems").GetInt32() != 3)
        {
            throw new InvalidOperationException("setup-scope-set-schema-invalid");
        }
        JsonElement enrollment = schemas.GetProperty("HalResourceOfSetupTargetEnrollmentData")
            .GetProperty("properties");
        AssertReference(enrollment.GetProperty("state"), "SetupEnrollmentState");
        AssertReference(enrollment.GetProperty("scopes").GetProperty("items"), "SetupEnrollmentScope");
        AssertReference(enrollment.GetProperty("issuance"), "SetupEnrollmentIssuance");
    }

    private static void AssertEnum(JsonElement schemas, string name, params string[] expected)
    {
        JsonElement schema = schemas.GetProperty(name);
        string[] actual = schema.GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        if (schema.GetProperty("type").GetString() != "string"
            || !actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"setup-enum-schema-invalid:{name}");
        }
    }

    private static void AssertReference(JsonElement schema, string name)
    {
        string expected = $"#/components/schemas/{name}";
        if (schema.GetProperty("$ref").GetString() != expected)
            throw new InvalidOperationException($"setup-schema-reference-invalid:{name}");
    }

    private static async Task AssertProperties<T>(params string[] expected)
    {
        string[] actual = typeof(T).GetProperties(
                BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(actual).IsEquivalentTo(expected);
    }

    private static async Task AssertPropertyType<T>(
        string propertyName,
        Type expected)
    {
        Type? actual = typeof(T).GetProperty(propertyName)?.PropertyType;
        await Assert.That(actual).IsEqualTo(expected);
    }

    private static async Task AssertRequiredStringParameters(
        string methodName,
        params string[] parameterNames)
    {
        MethodInfo method = typeof(ISetup_LiveClient).GetMethod(methodName)
            ?? throw new InvalidOperationException($"missing-generated-method:{methodName}");
        var nullability = new NullabilityInfoContext();
        foreach (string parameterName in parameterNames)
        {
            ParameterInfo parameter = method.GetParameters()
                .Single(candidate => candidate.Name == parameterName);
            await Assert.That(parameter.ParameterType).IsEqualTo(typeof(string));
            await Assert.That(nullability.Create(parameter).ReadState)
                .IsEqualTo(NullabilityState.NotNull);
            await Assert.That(parameter.HasDefaultValue).IsFalse();
        }
    }

    private static string ResolveRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null
               && !File.Exists(Path.Combine(current.FullName, "Explore.slnx")))
        {
            current = current.Parent;
        }
        return current?.FullName
            ?? throw new InvalidOperationException("repository-root-not-found");
    }

    private static HttpResponseMessage EnrollmentResponse(
        Guid enrollmentId,
        string? capability,
        HttpStatusCode status)
    {
        string json =
            $"{{\"enrollmentId\":\"{enrollmentId:D}\",\"state\":\"active\",\"generation\":1,\"expiresAt\":\"2030-01-01T00:00:00Z\",\"scopes\":[],\"issuance\":\"issued\",\"_links\":{{}}}}";
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, SuccessMediaType)
        };
        if (capability is not null)
            response.Headers.TryAddWithoutValidation(CapabilityHeader, capability);
        return response;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class SetupResponseHandler(params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<string> AcceptMediaTypes { get; } = [];
        public List<string> ContentTypes { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AcceptMediaTypes.Add(request.Headers.Accept.Single().MediaType!);
            ContentTypes.Add(request.Content!.Headers.ContentType!.ToString());
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            HttpResponseMessage response = _responses.Dequeue();
            response.RequestMessage = request;
            return response;
        }
    }
}
