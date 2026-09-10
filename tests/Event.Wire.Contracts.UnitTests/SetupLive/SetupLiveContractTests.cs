namespace ISLAMU.Wire.Contracts.UnitTests.SetupLive;

using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using ISLAMU.Wire.Contracts.SetupLive;

public sealed class SetupLiveContractTests
{
    private const string ContractNamespace = "ISLAMU.Wire.Contracts.SetupLive.";
    private static readonly Assembly WireAssembly =
        typeof(ConfigurationManifestV1Alpha2).Assembly;

    [Test]
    public async Task ContractClosureHasExactPackageFreePublicShapes()
    {
        Type request = RequireType("CreateSetupTargetEnrollmentRequest");
        Type enrollment = RequireType("SetupTargetEnrollmentData");
        Type readiness = RequireType("SetupSecretBindingReadinessItem");
        Type operation = RequireType("SetupSecretBindingOperationData");

        await AssertClosedRequiredProperties(
            request, "ClientChallenge", "RequestedScopes");
        await AssertClosedRequiredProperties(
            enrollment,
            "EnrollmentId", "State", "Generation", "ExpiresAt", "Scopes",
            "Issuance");
        await AssertClosedRequiredProperties(readiness, "BindingKey", "State");
        await AssertClosedRequiredProperties(
            operation,
            "OperationId", "State", "Outcome", "EnrollmentGeneration",
            "CreatedAt", "SettledAt");

        await Assert.That(RequiredProperty(enrollment, "EnrollmentId").PropertyType)
            .IsEqualTo(typeof(Guid));
        await Assert.That(RequiredProperty(enrollment, "Generation").PropertyType)
            .IsEqualTo(typeof(long));
        await Assert.That(RequiredProperty(enrollment, "ExpiresAt").PropertyType)
            .IsEqualTo(typeof(DateTimeOffset));
        await Assert.That(RequiredProperty(operation, "OperationId").PropertyType)
            .IsEqualTo(typeof(Guid));
        await Assert.That(RequiredProperty(operation, "EnrollmentGeneration").PropertyType)
            .IsEqualTo(typeof(long));
        await Assert.That(RequiredProperty(operation, "CreatedAt").PropertyType)
            .IsEqualTo(typeof(DateTimeOffset));
        await Assert.That(RequiredProperty(operation, "SettledAt").PropertyType)
            .IsEqualTo(typeof(DateTimeOffset?));
    }

    [Test]
    public async Task ClosedValuesHaveExactWireVocabulary()
    {
        await AssertEnumValues(
            "SetupEnrollmentScope",
            ("TargetRead", "target.read"),
            ("SecretBindingReadiness", "secret_binding.readiness"),
            ("SecretBindingWrite", "secret_binding.write"));
        await AssertEnumValues(
            "SetupEnrollmentState",
            ("Active", "active"),
            ("Revoked", "revoked"),
            ("Expired", "expired"));
        await AssertEnumValues(
            "SetupEnrollmentIssuance",
            ("Issued", "issued"),
            ("AlreadyIssued", "already_issued"));
        await AssertEnumValues(
            "SetupSecretBindingReadinessState",
            ("Unconfigured", "unconfigured"),
            ("Ready", "ready"),
            ("Unavailable", "unavailable"),
            ("Unauthorized", "unauthorized"),
            ("Invalid", "invalid"));
        await AssertEnumValues(
            "SetupSecretBindingOperationState",
            ("Accepted", "accepted"),
            ("Succeeded", "succeeded"),
            ("Failed", "failed"),
            ("Cancelled", "cancelled"));
        await AssertEnumValues(
            "SetupSecretBindingOperationOutcome",
            ("Accepted", "accepted"),
            ("Ready", "ready"),
            ("Unavailable", "unavailable"),
            ("Unauthorized", "unauthorized"),
            ("Invalid", "invalid"),
            ("Cancelled", "cancelled"),
            ("UnavailableEnrollment", "unavailable_enrollment"));
    }

    [Test]
    public async Task MetadataPinsHeadersMediaLimitsHalAndGenericProblems()
    {
        Type metadata = RequireType("SetupLiveContractMetadata");
        Type limits = RequireType("SetupLiveContentLimits");
        Type relations = RequireType("SetupLiveHalRelations");
        Type problems = RequireType("SetupLiveProblemContracts");

        await Assert.That(ReadStringConstant(metadata, "CapabilityHeader"))
            .IsEqualTo("X-Setup-Enrollment-Capability");
        await Assert.That(ReadStringConstant(metadata, "IdempotencyHeader"))
            .IsEqualTo("Idempotency-Key");
        await Assert.That(ReadStringConstant(metadata, "CreateRequestMediaType"))
            .IsEqualTo("application/json");
        await Assert.That(ReadStringConstant(metadata, "SecretWriteRequestMediaType"))
            .IsEqualTo("application/octet-stream");
        await Assert.That(ReadStringConstant(metadata, "SuccessMediaType"))
            .IsEqualTo("application/hal+json");
        await Assert.That(ReadStringConstant(metadata, "ErrorMediaType"))
            .IsEqualTo("application/problem+json");
        await Assert.That(ReadStringConstant(metadata, "EnrollmentWriteRatePolicy"))
            .IsEqualTo("SetupEnrollmentWrite");
        await Assert.That(ReadStringConstant(metadata, "SecretWriteRatePolicy"))
            .IsEqualTo("SetupSecretBindingWrite");
        await Assert.That(ReadStringConstant(metadata, "EnrollmentTimeoutPolicy"))
            .IsEqualTo("SetupEnrollment");
        await Assert.That(ReadStringConstant(metadata, "SecretWriteTimeoutPolicy"))
            .IsEqualTo("SetupSecretBinding");
        await Assert.That(ReadIntConstant(limits, "MaximumCreateRequestBytes"))
            .IsEqualTo(16_384);
        await Assert.That(ReadIntConstant(limits, "MaximumSecretWriteBytes"))
            .IsEqualTo(65_536);

        string[] relationValues = relations.GetFields(
                BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.GetRawConstantValue() as string)
            .Where(value => value is not null)
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(relationValues).IsEquivalentTo(
        [
            "create-setup-enrollment",
            "self",
            "revoke",
            "rotate-capability",
            "secret-binding-readiness",
            "write-secret-binding",
            "secret-binding-operation"
        ]);
        await AssertConstantNames(
            relations,
            "CreateSetupEnrollment", "Self", "Revoke", "RotateCapability",
            "SecretBindingReadiness", "WriteSecretBinding",
            "SecretBindingOperation");

        await Assert.That(ReadStringConstant(problems, "UnavailableType"))
            .IsEqualTo("/problems/setup-enrollment-unavailable");
        await Assert.That(ReadStringConstant(problems, "UnavailableTitle"))
            .IsEqualTo("Setup enrollment unavailable");
        await Assert.That(ReadStringConstant(problems, "UnavailableCode"))
            .IsEqualTo("setup_enrollment_unavailable");
        await Assert.That(ReadStringConstant(problems, "UnavailableDetail"))
            .IsEqualTo("The requested setup enrollment is unavailable.");
        await Assert.That(ReadIntConstant(problems, "UnavailableStatus"))
            .IsEqualTo(404);
        await Assert.That(ReadStringConstant(problems, "IdempotencyConflictType"))
            .IsEqualTo("/problems/setup-enrollment-idempotency-conflict");
        await Assert.That(ReadStringConstant(problems, "IdempotencyConflictTitle"))
            .IsEqualTo(
                "Setup enrollment request conflicts with an existing operation");
        await Assert.That(ReadStringConstant(problems, "IdempotencyConflictCode"))
            .IsEqualTo("setup_enrollment_idempotency_conflict");
        await Assert.That(ReadStringConstant(problems, "IdempotencyConflictDetail"))
            .IsEqualTo(
                "The idempotency key is already bound to different setup enrollment input.");
        await Assert.That(ReadIntConstant(problems, "IdempotencyConflictStatus"))
            .IsEqualTo(409);

        await AssertConstantNames(
            metadata,
            "CapabilityHeader", "IdempotencyHeader", "CreateRequestMediaType",
            "SecretWriteRequestMediaType", "SuccessMediaType", "ErrorMediaType",
            "EnrollmentWriteRatePolicy", "SecretWriteRatePolicy",
            "EnrollmentTimeoutPolicy", "SecretWriteTimeoutPolicy");
        await AssertConstantNames(
            limits, "MaximumCreateRequestBytes", "MaximumSecretWriteBytes");
        await AssertConstantNames(
            problems,
            "UnavailableStatus", "UnavailableType", "UnavailableTitle",
            "UnavailableCode", "UnavailableDetail", "IdempotencyConflictStatus",
            "IdempotencyConflictType", "IdempotencyConflictTitle",
            "IdempotencyConflictCode", "IdempotencyConflictDetail");
    }

    [Test]
    public async Task CapabilitySyntaxIsCanonicalAndStringRepresentationsAreRedacted()
    {
        Type capabilityType = RequireType("SetupEnrollmentCapability");
        string candidate = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        MethodInfo tryCreate = capabilityType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "TryCreate");
        object?[] arguments = [candidate, null];

        bool accepted = (bool)tryCreate.Invoke(null, arguments)!;
        object capability = arguments[1]
            ?? throw new InvalidOperationException("missing-setup-capability-value");
        PropertyInfo[] publicProperties = capabilityType.GetProperties(
            BindingFlags.Public | BindingFlags.Instance);
        MethodInfo? toHeaderValue = capabilityType.GetMethod(
            "ToHeaderValue",
            BindingFlags.Public | BindingFlags.Instance);
        string defaultJson = JsonSerializer.Serialize(
            capability,
            capabilityType);

        await Assert.That(accepted).IsTrue();
        await Assert.That(candidate.Length).IsEqualTo(43);
        await Assert.That(publicProperties).IsEmpty();
        await Assert.That(toHeaderValue).IsNotNull();
        await Assert.That(toHeaderValue!.Invoke(capability, null)).IsEqualTo(candidate);
        await Assert.That(capability.ToString()).DoesNotContain(candidate);
        await Assert.That(defaultJson).DoesNotContain(candidate);

        foreach (string invalid in new[]
                 {
                     candidate + "=",
                     candidate[..^1],
                     candidate[..^1] + "+",
                     new string('_', 43)
                 })
        {
            object?[] invalidArguments = [invalid, null];
            await Assert.That((bool)tryCreate.Invoke(null, invalidArguments)!)
                .IsFalse();
            await Assert.That(invalidArguments[1]).IsNull();
        }
    }

    [Test]
    public async Task RequestsSnapshotCollectionsAndRejectUnknownAuthorityMembers()
    {
        Type requestType = RequireType("CreateSetupTargetEnrollmentRequest");
        Type challengeType = RequireType("SetupClientChallenge");
        Type scopeType = RequireType("SetupEnrollmentScope");
        object first = Enum.Parse(scopeType, "TargetRead");
        object second = Enum.Parse(scopeType, "SecretBindingWrite");
        IList source = (IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(scopeType))!;
        source.Add(second);
        source.Add(first);
        object request = Activator.CreateInstance(requestType)
            ?? throw new InvalidOperationException("missing-setup-request-constructor");
        string challengeText = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        MethodInfo challengeTryCreate = challengeType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "TryCreate");
        object?[] challengeArguments = [challengeText, null];
        if (!(bool)challengeTryCreate.Invoke(null, challengeArguments)!)
            throw new InvalidOperationException("invalid-generated-client-challenge");
        RequiredProperty(requestType, "ClientChallenge").SetValue(
            request, challengeArguments[1]);
        RequiredProperty(requestType, "RequestedScopes").SetValue(request, source);
        source.Clear();

        object scopes = RequiredProperty(requestType, "RequestedScopes").GetValue(request)
            ?? throw new InvalidOperationException("missing-setup-request-scopes");
        await Assert.That(((IEnumerable)scopes).Cast<object>().Count()).IsEqualTo(2);
        await Assert.That(((IList)scopes).IsReadOnly).IsTrue();

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false
        };
        options.Converters.Add(new JsonStringEnumConverter());
        string json = JsonSerializer.Serialize(request, requestType, options);
        await Assert.That(json).Contains("\"clientChallenge\"");
        await Assert.That(json).Contains("\"requestedScopes\"");
        await Assert.That(json).DoesNotContain("tenant", StringComparison.OrdinalIgnoreCase);
        await Assert.That(json).DoesNotContain("provider", StringComparison.OrdinalIgnoreCase);
        await Assert.That(json).DoesNotContain("capability", StringComparison.OrdinalIgnoreCase);

        string smuggled = json[..^1] + ",\"tenantId\":\""
            + Guid.CreateVersion7().ToString("D") + "\"}";
        await Assert.That(() => JsonSerializer.Deserialize(smuggled, requestType, options))
            .Throws<JsonException>();
    }

    [Test]
    public async Task ShippedContextRejectsInvalidChallengeAndScopeSyntax()
    {
        string challenge = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        JsonTypeInfo requestType = SetupLiveJsonContext.Default
            .CreateSetupTargetEnrollmentRequest;
        string valid =
            $$"""{"clientChallenge":"{{challenge}}","requestedScopes":["target.read","secret_binding.write"]}""";

        object? request = JsonSerializer.Deserialize(valid, requestType);
        string roundTrip = JsonSerializer.Serialize(request, requestType);
        await Assert.That(roundTrip).IsEqualTo(valid);

        foreach (string invalid in new[]
                 {
                     """{"clientChallenge":null,"requestedScopes":["target.read"]}""",
                     """{"clientChallenge":"x","requestedScopes":["target.read"]}""",
                     $$"""{"clientChallenge":"{{challenge}}=","requestedScopes":["target.read"]}""",
                     $$"""{"clientChallenge":"{{challenge}}","requestedScopes":null}""",
                     $$"""{"clientChallenge":"{{challenge}}","requestedScopes":[]}""",
                     $$"""{"clientChallenge":"{{challenge}}","requestedScopes":["target.read","target.read"]}""",
                     $$"""{"clientChallenge":"{{challenge}}","requestedScopes":[0]}""",
                     $$"""{"clientChallenge":"{{challenge}}","requestedScopes":["TargetRead"]}""",
                     $$"""{"clientChallenge":"{{challenge}}","requestedScopes":["target_read"]}""",
                     $$"""{"clientChallenge":"{{challenge}}","requestedScopes":["unknown"]}""",
                     $$"""{"clientChallenge":"{{challenge}}","requestedScopes":["target.read"],"tenantId":"{{Guid.CreateVersion7():D}}"}""",
                     $$"""{"ClientChallenge":"{{challenge}}","requestedScopes":["target.read"]}""",
                     $$"""{"clientChallenge":"{{challenge}}","requestedScopes":["target.read",]}""",
                     $$"""{"clientChallenge":"{{challenge}}",/*comment*/"requestedScopes":["target.read"]}"""
                 })
        {
            await Assert.That(() => JsonSerializer.Deserialize(invalid, requestType))
                .Throws<JsonException>();
        }
    }

    [Test]
    public async Task ShippedContextUsesStringOnlyClosedEnumsForEveryOutput()
    {
        string enrollment =
            $$"""{"enrollmentId":"{{Guid.CreateVersion7():D}}","state":"active","generation":1,"expiresAt":"2026-09-01T00:00:00+00:00","scopes":["target.read"],"issuance":"issued"}""";
        string readiness = """{"bindingKey":"setup-signing","state":"ready"}""";
        string operation =
            $$"""{"operationId":"{{Guid.CreateVersion7():D}}","state":"accepted","outcome":"accepted","enrollmentGeneration":1,"createdAt":"2026-09-01T00:00:00+00:00","settledAt":null}""";

        await AssertContextRoundTrip(
            enrollment,
            SetupLiveJsonContext.Default.SetupTargetEnrollmentData);
        await AssertContextRoundTrip(
            readiness,
            SetupLiveJsonContext.Default.SetupSecretBindingReadinessItem);
        await AssertContextRoundTrip(
            operation,
            SetupLiveJsonContext.Default.SetupSecretBindingOperationData);

        await AssertContextRejects(
            enrollment.Replace("\"active\"", "0", StringComparison.Ordinal),
            SetupLiveJsonContext.Default.SetupTargetEnrollmentData);
        await AssertContextRejects(
            enrollment.Replace("\"issued\"", "0", StringComparison.Ordinal),
            SetupLiveJsonContext.Default.SetupTargetEnrollmentData);
        await AssertContextRejects(
            enrollment.Replace("[\"target.read\"]", "null", StringComparison.Ordinal),
            SetupLiveJsonContext.Default.SetupTargetEnrollmentData);
        await AssertContextRejects(
            readiness.Replace("\"ready\"", "0", StringComparison.Ordinal),
            SetupLiveJsonContext.Default.SetupSecretBindingReadinessItem);
        await AssertContextRejects(
            operation.Replace(
                "\"state\":\"accepted\"",
                "\"state\":0",
                StringComparison.Ordinal),
            SetupLiveJsonContext.Default.SetupSecretBindingOperationData);
        await AssertContextRejects(
            operation.Replace(
                "\"outcome\":\"accepted\"",
                "\"outcome\":0",
                StringComparison.Ordinal),
            SetupLiveJsonContext.Default.SetupSecretBindingOperationData);

        var undefinedEnrollment = new SetupTargetEnrollmentData
        {
            EnrollmentId = Guid.CreateVersion7(),
            State = (SetupEnrollmentState)999,
            Generation = 1,
            ExpiresAt = new DateTimeOffset(
                2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            Scopes = [SetupEnrollmentScope.TargetRead],
            Issuance = SetupEnrollmentIssuance.Issued
        };
        await Assert.That(() => JsonSerializer.Serialize(
                undefinedEnrollment,
                SetupLiveJsonContext.Default.SetupTargetEnrollmentData))
            .Throws<JsonException>();

        var undefinedIssuance = undefinedEnrollment with
        {
            State = SetupEnrollmentState.Active,
            Issuance = (SetupEnrollmentIssuance)999
        };
        await Assert.That(() => JsonSerializer.Serialize(
                undefinedIssuance,
                SetupLiveJsonContext.Default.SetupTargetEnrollmentData))
            .Throws<JsonException>();

        var undefinedReadiness = new SetupSecretBindingReadinessItem
        {
            BindingKey = "setup-signing",
            State = (SetupSecretBindingReadinessState)999
        };
        await Assert.That(() => JsonSerializer.Serialize(
                undefinedReadiness,
                SetupLiveJsonContext.Default.SetupSecretBindingReadinessItem))
            .Throws<JsonException>();

        var undefinedOperationState = new SetupSecretBindingOperationData
        {
            OperationId = Guid.CreateVersion7(),
            State = (SetupSecretBindingOperationState)999,
            Outcome = SetupSecretBindingOperationOutcome.Accepted,
            EnrollmentGeneration = 1,
            CreatedAt = new DateTimeOffset(
                2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            SettledAt = null
        };
        await Assert.That(() => JsonSerializer.Serialize(
                undefinedOperationState,
                SetupLiveJsonContext.Default.SetupSecretBindingOperationData))
            .Throws<JsonException>();
        await Assert.That(() => JsonSerializer.Serialize(
                undefinedOperationState with
                {
                    State = SetupSecretBindingOperationState.Accepted,
                    Outcome = (SetupSecretBindingOperationOutcome)999
                },
                SetupLiveJsonContext.Default.SetupSecretBindingOperationData))
            .Throws<JsonException>();

        await Assert.That(() => new SetupTargetEnrollmentData
        {
            EnrollmentId = Guid.CreateVersion7(),
            State = SetupEnrollmentState.Active,
            Generation = 1,
            ExpiresAt = new DateTimeOffset(
                    2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            Scopes = [(SetupEnrollmentScope)999],
            Issuance = SetupEnrollmentIssuance.Issued
        })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PublicOutputsAndNamespaceExcludeAuthorityAndP9008Surfaces()
    {
        string[] outputTypes =
        [
            "SetupTargetEnrollmentData",
            "SetupSecretBindingReadinessItem",
            "SetupSecretBindingOperationData"
        ];
        string[] forbiddenMembers =
        [
            "TenantId", "ActorId", "UserId", "TargetUrl", "Challenge",
            "Capability", "Digest", "Provider", "Source", "Coordinate",
            "CredentialHandle", "Secret", "Value", "Reason", "Exception",
            "ResponseBody"
        ];
        string[] forbiddenTypeFragments =
        [
            "Registration", "Callback", "ManualImport", "Connection",
            "ProviderUrl", "EmbedTicket"
        ];

        foreach (string typeName in outputTypes)
        {
            Type type = RequireType(typeName);
            await Assert.That(type.GetProperties()
                    .Select(property => property.Name)
                    .Intersect(forbiddenMembers, StringComparer.Ordinal))
                .IsEmpty();
        }

        Type[] setupTypes = WireAssembly.GetExportedTypes()
            .Where(type => type.Namespace == ContractNamespace.TrimEnd('.'))
            .ToArray();
        await Assert.That(setupTypes.Any(type => forbiddenTypeFragments.Any(fragment =>
                type.Name.Contains(fragment, StringComparison.Ordinal))))
            .IsFalse();

        Type jsonContext = RequireType("SetupLiveJsonContext");
        await Assert.That(typeof(JsonSerializerContext).IsAssignableFrom(jsonContext))
            .IsTrue();
        string[] serializedTypes = jsonContext
            .GetCustomAttributesData()
            .Where(attribute =>
                attribute.AttributeType == typeof(JsonSerializableAttribute))
            .Select(attribute =>
                ((Type)attribute.ConstructorArguments[0].Value!).Name)
            .ToArray();
        await Assert.That(serializedTypes).IsEquivalentTo(
            outputTypes.Append("CreateSetupTargetEnrollmentRequest"));
    }

    private static Type RequireType(string name) =>
        WireAssembly.GetType(ContractNamespace + name)
        ?? throw new InvalidOperationException(
            $"missing-setup-live-wire-contract:{name}");

    private static PropertyInfo RequiredProperty(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            $"missing-setup-live-property:{type.FullName}.{name}");

    private static async Task AssertClosedRequiredProperties(
        Type type,
        params string[] expectedNames)
    {
        JsonUnmappedMemberHandlingAttribute? unmapped =
            type.GetCustomAttribute<JsonUnmappedMemberHandlingAttribute>();
        await Assert.That(unmapped?.UnmappedMemberHandling)
            .IsEqualTo(JsonUnmappedMemberHandling.Disallow);
        await Assert.That(type.GetProperties()
                .Select(property => property.Name))
            .IsEquivalentTo(expectedNames);
        foreach (string name in expectedNames)
        {
            await Assert.That(RequiredProperty(type, name)
                    .GetCustomAttribute<System.Runtime.CompilerServices.RequiredMemberAttribute>())
                .IsNotNull();
        }
    }

    private static async Task AssertEnumValues(
        string typeName,
        params (string Member, string Wire)[] expected)
    {
        Type type = RequireType(typeName);
        await Assert.That(type.IsEnum).IsTrue();
        await Assert.That(Enum.GetNames(type)).IsEquivalentTo(
            expected.Select(item => item.Member));
        foreach ((string member, string wire) in expected)
        {
            FieldInfo field = type.GetField(member, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    $"missing-setup-live-enum-member:{typeName}.{member}");
            await Assert.That(field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name)
                .IsEqualTo(wire);
        }
    }

    private static string ReadStringConstant(Type type, string name) =>
        type.GetField(name, BindingFlags.Public | BindingFlags.Static)?
            .GetRawConstantValue() as string
        ?? throw new InvalidOperationException(
            $"missing-setup-live-string-constant:{type.Name}.{name}");

    private static int ReadIntConstant(Type type, string name) =>
        type.GetField(name, BindingFlags.Public | BindingFlags.Static)?
            .GetRawConstantValue() as int?
        ?? throw new InvalidOperationException(
            $"missing-setup-live-int-constant:{type.Name}.{name}");

    private static async Task AssertConstantNames(
        Type type,
        params string[] expected)
    {
        string[] actual = type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => field.Name)
            .ToArray();
        await Assert.That(actual).IsEquivalentTo(expected);
    }

    private static async Task AssertContextRoundTrip(
        string json,
        JsonTypeInfo typeInfo)
    {
        object? value = JsonSerializer.Deserialize(json, typeInfo);
        await Assert.That(JsonSerializer.Serialize(value, typeInfo))
            .IsEqualTo(json);
    }

    private static async Task AssertContextRejects(
        string json,
        JsonTypeInfo typeInfo)
    {
        await Assert.That(() => JsonSerializer.Deserialize(json, typeInfo))
            .Throws<JsonException>();
    }
}
