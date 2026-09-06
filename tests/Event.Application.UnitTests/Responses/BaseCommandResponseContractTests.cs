// ABOUTME: Specifies the immutable BaseCommandResponse result and named valid-state factory contract.
// ABOUTME: Covers every concrete descendant, generated JSON metadata, valid states, and complete payload preservation.

using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Promotions;
using Explore.Domain;
using Explore.Application.Features.Promotions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Serialization;

namespace ApplicationUnitTests.Responses;

public sealed class BaseCommandResponseContractTests
{
    private const string SyntheticReveal = "fixture.reveal.not-a-credential";
    private const string SyntheticKeyId = "fixture-key-id";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid ResultId = Guid.Parse("01991a70-1600-7000-8000-000000000101");
    private static readonly Guid RelatedId = Guid.Parse("01991a70-1600-7000-8000-000000000102");
    private static readonly Guid TenantId = Guid.Parse("01991a70-1600-7000-8000-000000000103");
    private static readonly Guid ChoiceId = Guid.Parse("01991a70-1600-7000-8000-000000000104");
    private static readonly Guid RefundId = Guid.Parse("01991a70-1600-7000-8000-000000000105");
    private static readonly DateTime FixtureUtc = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixtureOffset = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly Type[] ConcreteDescendantTypes = typeof(BaseCommandResponse<>).Assembly.GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
            && DerivesFromBaseCommandResponse(type))
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .ToArray();

    // Internal factory-only CQRS results are not wire DTOs. A JSON constructor or source-generation
    // registration opts a response into the complete wire-contract checks below; neither can silently drift.
    private static readonly Type[] WireDescendantTypes = ConcreteDescendantTypes
        .Where(type => type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Any(constructor => constructor.GetCustomAttribute<JsonConstructorAttribute>() is not null)
            || typeof(ExploreJsonContext).GetCustomAttributesData()
                .Any(registration => registration.AttributeType == typeof(JsonSerializableAttribute)
                    && registration.ConstructorArguments[0].Value as Type == type))
        .ToArray();

    private static readonly Type[] RegisteredResponseTypes =
    [
        typeof(BaseCommandResponse<Guid>),
        typeof(BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>),
        typeof(BaseCommandResponse<IReadOnlyList<EmailDispatchStatusDto>>),
        typeof(BaseCommandResponse<StorageUploadSessionDto>),
        .. WireDescendantTypes,
    ];

    private static readonly string[] BaseJsonPropertyNames =
        ["errors", "failureCode", "id", "message", "quotaExceeded", "success"];
    private static readonly string[] QuotaJsonPropertyNames =
        ["actual", "attempted", "limit", "quotaKey", "scope"];
    private static readonly string[] BlankValidationErrors = [" "];
    private static readonly string[] OneValidationError = ["validation.alpha"];
    private static readonly string[] ManyValidationErrors = ["validation.alpha", "validation.beta"];
    private static readonly string[] QuotaMachineErrors = ["quota.machine"];
    private static readonly string[] FeatureErrors = ["feature.alpha", "feature.beta"];
    private static readonly string[] ConcreteFactoryNames = ["Failure", "Success"];
    private static readonly string[] CanonicalFactoryFailureCodes =
    [
        FailureCodes.NotFound,
        FailureCodes.ConcurrencyConflict,
        FailureCodes.AdminRequired,
        FailureCodes.AuthenticationRequired,
    ];

    private static readonly FactorySignature[] ExpectedFactorySignatures =
    [
        new("Authentication", [Optional<string>("message")]),
        new("Authorization", [Optional<string>("message")]),
        new("Conflict", [Required<Guid>("id"), Optional<string>("message")]),
        new("Failure",
        [
            Required<string>("failureCode"),
            Optional<string>("message"),
            Optional<IEnumerable<string>>("errors"),
            Optional<Guid>("id"),
        ]),
        new("NotFound", [Optional<string>("message"), Optional<Guid>("id")]),
        new("Quota",
        [
            Required<string>("message"),
            Required<QuotaExceededDetails>("quotaExceeded"),
            Optional<string>("error"),
            Optional<Guid>("id"),
        ]),
        new("Success", [Required<Guid>("id"), Optional<string>("message")]),
        new("Validation",
        [
            Required<IEnumerable<string>>("errors"),
            Optional<string>("message"),
            Optional<Guid>("id"),
        ]),
    ];

    [Test]
    public async Task EveryDiscoveredDescendantHasExecutableFactoryAndApplicableWireScenarios()
    {
        await Assert.That(CreateDerivedFactoryScenarios().Select(scenario => scenario.ResponseType))
            .IsEquivalentTo(ConcreteDescendantTypes)
            .Because("Every concrete response needs executable success, failure, payload and invalid-state coverage.");
        await Assert.That(CreateDerivedWireScenarios().Select(scenario => scenario.ResponseType))
            .IsEquivalentTo(WireDescendantTypes)
            .Because("Every response declaring JSON construction or generated metadata needs complete wire round-trip coverage.");
    }

    [Test]
    public async Task StableCamelCaseJsonPreservesBaseAndQuotaMachineValues()
    {
        JsonObject successFixture = ResponseJson(ResultId);
        object successful = JsonSerializer.Deserialize<BaseCommandResponse<Guid>>(
            successFixture.ToJsonString(),
            JsonOptions)!;
        JsonObject successJson = Serialize(successful);

        await Assert.That(JsonNode.DeepEquals(successJson, successFixture)).IsTrue();
        await Assert.That(successJson.Select(property => property.Key).Order(StringComparer.Ordinal).SequenceEqual(
            BaseJsonPropertyNames,
            StringComparer.Ordinal)).IsTrue();
        await Assert.That(successJson["id"]!.GetValue<Guid>()).IsEqualTo(ResultId);
        await Assert.That(successJson["success"]!.GetValue<bool>()).IsTrue();

        QuotaExceededDetails quota = CreateQuota();
        var quotaFixture = new JsonObject
        {
            ["id"] = ResultId,
            ["success"] = false,
            ["message"] = "quota.blocked",
            ["errors"] = new JsonArray("quota.machine"),
            ["failureCode"] = FailureCodes.QuotaExceeded,
            ["quotaExceeded"] = JsonSerializer.SerializeToNode(quota, JsonOptions),
        };
        object quotaFailure = JsonSerializer.Deserialize<BaseCommandResponse<Guid>>(
            quotaFixture.ToJsonString(),
            JsonOptions)!;
        JsonObject quotaJson = Serialize(quotaFailure);
        JsonObject quotaDetails = quotaJson["quotaExceeded"]!.AsObject();

        await Assert.That(JsonNode.DeepEquals(quotaJson, quotaFixture)).IsTrue();
        await Assert.That(quotaJson["failureCode"]!.GetValue<string>()).IsEqualTo("quota_exceeded");
        await Assert.That(quotaDetails.Select(property => property.Key).Order(StringComparer.Ordinal).SequenceEqual(
            QuotaJsonPropertyNames,
            StringComparer.Ordinal)).IsTrue();
        await AssertQuotaJson(quotaDetails);
    }

    [Test]
    public async Task EveryWireDescendantRoundTripsItsCompleteRelevantPayloadJson()
    {
        DerivedWireScenario[] scenarios = CreateDerivedWireScenarios();

        foreach (DerivedWireScenario scenario in scenarios)
        {
            object response = JsonSerializer.Deserialize(
                scenario.ExpectedJson.ToJsonString(),
                scenario.ResponseType,
                JsonOptions)!;
            JsonObject roundTrip = Serialize(response);

            await Assert.That(JsonNode.DeepEquals(roundTrip, scenario.ExpectedJson)).IsTrue();
            await Assert.That(ResponsePayloadProperties(scenario.ResponseType).Select(JsonName).Order(StringComparer.Ordinal)
                .SequenceEqual(scenario.ExpectedPayloadJsonNames.Order(StringComparer.Ordinal), StringComparer.Ordinal)).IsTrue();
        }

        PropertyInfo guestToken = typeof(GuestRegistrationOrderStartDto)
            .GetProperty(nameof(GuestRegistrationOrderStartDto.GuestCapabilityToken))!;
        await Assert.That(guestToken.GetCustomAttribute<JsonIgnoreAttribute>()).IsNotNull();
    }

    [Test]
    public async Task PublicResponseStateHasNoSetterOrConstructorEscapeHatch()
    {
        Type[] responseTypes = [typeof(BaseCommandResponse<Guid>), .. ConcreteDescendantTypes];
        string[] publicSetters = responseTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(property => property.SetMethod?.IsPublic == true)
            .Select(property => $"{property.DeclaringType!.FullName}.{property.Name}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] publicConstructors = responseTypes
            .SelectMany(type => type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            .Select(constructor => constructor.DeclaringType!.FullName!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(publicSetters).IsEmpty();
        await Assert.That(publicConstructors).IsEmpty();
    }

    [Test]
    public async Task JsonConstructorsAreInternalAndBindTheIsSuccessClrState()
    {
        Type[] responseTypes = [typeof(BaseCommandResponse<Guid>), .. WireDescendantTypes];

        foreach (Type responseType in responseTypes)
        {
            ConstructorInfo[] jsonConstructors = responseType
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(constructor => constructor.GetCustomAttribute<JsonConstructorAttribute>() is not null)
                .ToArray();

            await Assert.That(jsonConstructors.Length).IsEqualTo(1);
            ConstructorInfo constructor = jsonConstructors[0];
            string[] parameterNames = constructor.GetParameters().Select(parameter => parameter.Name!).ToArray();

            await Assert.That(constructor.IsAssembly).IsTrue();
            await Assert.That(constructor.IsPublic).IsFalse();
            await Assert.That(constructor.IsPrivate).IsFalse();
            await Assert.That(parameterNames.Contains("isSuccess", StringComparer.Ordinal)).IsTrue();
            await Assert.That(parameterNames.Contains("success", StringComparer.Ordinal)).IsFalse();
        }
    }

    [Test]
    public async Task BaseResponseUsesRecordValueSemantics()
    {
        Type responseType = typeof(BaseCommandResponse<Guid>);

        await Assert.That(IsRecord(responseType)).IsTrue();

        object first = InvokeFactory(responseType, "Success", Facts(
            ("id", ResultId),
            ("message", "result.created")));
        object equivalent = InvokeFactory(responseType, "Success", Facts(
            ("id", ResultId),
            ("message", "result.created")));

        await Assert.That(first.Equals(equivalent)).IsTrue();
        await Assert.That(first.GetHashCode()).IsEqualTo(equivalent.GetHashCode());
    }

    [Test]
    public async Task InstanceSuccessStateIsReadableAsIsSuccessWithoutHidingTheSuccessFactory()
    {
        Type responseType = typeof(BaseCommandResponse<Guid>);
        Type[] responseTypes = [responseType, .. ConcreteDescendantTypes];

        foreach (Type contractType in responseTypes)
        {
            PropertyInfo? isSuccess = contractType.GetProperty("IsSuccess", BindingFlags.Public | BindingFlags.Instance);
            MemberInfo[] conflictingInstanceMembers = contractType.GetMember(
                "Success",
                BindingFlags.Public | BindingFlags.Instance);

            await Assert.That(isSuccess).IsNotNull();
            await Assert.That(isSuccess!.PropertyType).IsEqualTo(typeof(bool));
            await Assert.That(isSuccess.CanRead).IsTrue();
            await Assert.That(isSuccess.SetMethod is null || !isSuccess.SetMethod.IsPublic).IsTrue();
            await Assert.That(isSuccess.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name).IsEqualTo("success");
            await Assert.That(conflictingInstanceMembers).IsEmpty();
        }

        object response = InvokeFactory(responseType, "Success", Facts(("id", ResultId), ("message", "result.created")));
        await Assert.That(ReadIsSuccess(response)).IsTrue();
        await Assert.That(RequireFactory(responseType, "Success").IsStatic).IsTrue();
    }

    [Test]
    public async Task GenericStateHasNoFactoriesAndNonGenericCompanionExposesEightGenericFactories()
    {
        Type responseType = typeof(BaseCommandResponse<Guid>);
        MethodInfo[] stateFactories = responseType
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.ReturnType == responseType)
            .ToArray();
        await Assert.That(stateFactories).IsEmpty();

        Type? companionType = FindFactoryCompanion();
        await Assert.That(companionType).IsNotNull();
        await Assert.That(companionType!.IsAbstract && companionType.IsSealed).IsTrue();
        await Assert.That(companionType.IsGenericType).IsFalse();

        MethodInfo[] publicMethods = companionType
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        await Assert.That(publicMethods.Length).IsEqualTo(ExpectedFactorySignatures.Length);
        await Assert.That(publicMethods.All(method =>
            method.IsGenericMethodDefinition
            && method.GetGenericArguments().Length == 1)).IsTrue();

        MethodInfo[] genericFactories = publicMethods
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();

        for (var index = 0; index < ExpectedFactorySignatures.Length; index++)
        {
            FactorySignature expected = ExpectedFactorySignatures[index];
            MethodInfo openFactory = genericFactories[index];

            await Assert.That(openFactory.Name).IsEqualTo(expected.Name);
            await Assert.That(openFactory.GetGenericArguments().Length).IsEqualTo(1);

            MethodInfo closedFactory = openFactory.MakeGenericMethod(typeof(Guid));
            await Assert.That(closedFactory.ReturnType).IsEqualTo(responseType);
            await AssertFactoryParameters(closedFactory, expected.Parameters);
        }
    }

    [Test]
    public async Task SuccessFactoryCreatesOnlyASuccessState()
    {
        object response = InvokeFactory(typeof(BaseCommandResponse<Guid>), "Success", Facts(
            ("id", ResultId),
            ("message", "result.created")));
        JsonObject json = Serialize(response);

        await Assert.That(ReadIsSuccess(response)).IsTrue();
        await Assert.That(json["id"]!.GetValue<Guid>()).IsEqualTo(ResultId);
        await Assert.That(json["success"]!.GetValue<bool>()).IsTrue();
        await Assert.That(json["message"]!.GetValue<string>()).IsEqualTo("result.created");
        await Assert.That(json["errors"] is null).IsTrue();
        await Assert.That(json["failureCode"] is null).IsTrue();
        await Assert.That(json["quotaExceeded"] is null).IsTrue();
    }

    [Test]
    public async Task ValidationFactorySnapshotsOneAndManyErrorsAndUsesFirstErrorAsMessageFallback()
    {
        var oneError = new List<string> { "validation.alpha" };
        object fallbackResponse = InvokeFactory(typeof(BaseCommandResponse<Guid>), "Validation", Facts(
            ("errors", oneError),
            ("message", null),
            ("id", ResultId)));
        oneError[0] = "validation.changed";
        oneError.Add("validation.added");
        JsonObject fallbackJson = Serialize(fallbackResponse);

        await Assert.That(fallbackJson["success"]!.GetValue<bool>()).IsFalse();
        await Assert.That(fallbackJson["message"]!.GetValue<string>()).IsEqualTo("validation.alpha");
        await Assert.That(ReadErrors(fallbackJson).SequenceEqual(
            OneValidationError,
            StringComparer.Ordinal)).IsTrue();
        await Assert.That(fallbackJson["failureCode"] is null).IsTrue();
        await Assert.That(fallbackJson["quotaExceeded"] is null).IsTrue();

        var manyErrors = new List<string> { "validation.alpha", "validation.beta" };
        object explicitResponse = InvokeFactory(typeof(BaseCommandResponse<Guid>), "Validation", Facts(
            ("errors", manyErrors),
            ("message", "validation.summary"),
            ("id", ResultId)));
        manyErrors.Clear();
        JsonObject explicitJson = Serialize(explicitResponse);

        await Assert.That(explicitJson["message"]!.GetValue<string>()).IsEqualTo("validation.summary");
        await Assert.That(ReadErrors(explicitJson).SequenceEqual(
            ManyValidationErrors,
            StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ValidationFactoryRejectsNullEmptyOrBlankErrors()
    {
        MethodInfo factory = RequireFactory(typeof(BaseCommandResponse<Guid>), "Validation");

        await Assert.That(() => InvokeFactory(factory, Facts(
            ("errors", null), ("message", null), ("id", ResultId))))
            .Throws<ArgumentException>();
        await Assert.That(() => InvokeFactory(factory, Facts(
            ("errors", Array.Empty<string>()), ("message", null), ("id", ResultId))))
            .Throws<ArgumentException>();
        await Assert.That(() => InvokeFactory(factory, Facts(
            ("errors", BlankValidationErrors), ("message", null), ("id", ResultId))))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments("NotFound", FailureCodes.NotFound, true)]
    [Arguments("Conflict", FailureCodes.ConcurrencyConflict, true)]
    [Arguments("Authorization", FailureCodes.AdminRequired, false)]
    [Arguments("Authentication", FailureCodes.AuthenticationRequired, false)]
    public async Task NamedFailureFactorySetsItsCanonicalCodeInternally(
        string factoryName,
        string expectedFailureCode,
        bool preservesId)
    {
        Dictionary<string, object?> facts = factoryName switch
        {
            "NotFound" => Facts(("message", "failure.machine"), ("id", ResultId)),
            "Conflict" => Facts(("id", ResultId), ("message", "failure.machine")),
            _ => Facts(("message", "failure.machine")),
        };
        object response = InvokeFactory(typeof(BaseCommandResponse<Guid>), factoryName, facts);
        JsonObject json = Serialize(response);

        await Assert.That(ReadIsSuccess(response)).IsFalse();
        await Assert.That(json["success"]!.GetValue<bool>()).IsFalse();
        await Assert.That(json["failureCode"]!.GetValue<string>()).IsEqualTo(expectedFailureCode);
        await Assert.That(json["message"]!.GetValue<string>()).IsEqualTo("failure.machine");
        await Assert.That(json["quotaExceeded"] is null).IsTrue();
        await Assert.That(json["id"]!.GetValue<Guid>())
            .IsEqualTo(preservesId ? ResultId : Guid.Empty);
    }

    [Test]
    public async Task FeatureFailureFactoryOwnsStateCodeAndDefensiveOptionalErrors()
    {
        var sourceErrors = new List<string>(FeatureErrors);
        object response = InvokeFactory(typeof(BaseCommandResponse<Guid>), "Failure", Facts(
            ("failureCode", PromotionRedemptionFailureCodes.Unavailable),
            ("message", "feature.blocked"),
            ("errors", sourceErrors),
            ("id", ResultId)));
        sourceErrors[0] = "feature.changed";
        sourceErrors.Clear();
        JsonObject json = Serialize(response);

        await Assert.That(ReadIsSuccess(response)).IsFalse();
        await Assert.That(json["success"]!.GetValue<bool>()).IsFalse();
        await Assert.That(json["failureCode"]!.GetValue<string>())
            .IsEqualTo(PromotionRedemptionFailureCodes.Unavailable);
        await Assert.That(json["message"]!.GetValue<string>()).IsEqualTo("feature.blocked");
        await Assert.That(ReadErrors(json).SequenceEqual(
            FeatureErrors,
            StringComparer.Ordinal)).IsTrue();
        await Assert.That(json["quotaExceeded"] is null).IsTrue();

        object withoutErrors = InvokeFactory(typeof(BaseCommandResponse<Guid>), "Failure", Facts(
            ("failureCode", PromotionRedemptionFailureCodes.ValidationFailed)));
        JsonObject withoutErrorsJson = Serialize(withoutErrors);
        await Assert.That(withoutErrorsJson["errors"] is null).IsTrue();
        await Assert.That(withoutErrorsJson["message"] is null).IsTrue();

        object errorsWithoutMessage = InvokeFactory(typeof(BaseCommandResponse<Guid>), "Failure", Facts(
            ("failureCode", "feature.errors_without_message"),
            ("errors", FeatureErrors)));
        JsonObject errorsWithoutMessageJson = Serialize(errorsWithoutMessage);
        await Assert.That(errorsWithoutMessageJson["message"] is null).IsTrue();
        await Assert.That(ReadErrors(errorsWithoutMessageJson).SequenceEqual(
            FeatureErrors,
            StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task FeatureFailureRejectsBlankOwnedCodesCanonicalCodesInvalidErrorsAndQuotaFacts()
    {
        MethodInfo factory = RequireFactory(typeof(BaseCommandResponse<Guid>), "Failure");

        await Assert.That(() => InvokeFactory(factory, Facts(("failureCode", null))))
            .Throws<ArgumentException>();
        await Assert.That(() => InvokeFactory(factory, Facts(("failureCode", " "))))
            .Throws<ArgumentException>();
        foreach (string canonicalCode in CanonicalFactoryFailureCodes)
        {
            await Assert.That(() => InvokeFactory(factory, Facts(("failureCode", canonicalCode))))
                .Throws<ArgumentException>();
        }

        await Assert.That(() => InvokeFactory(factory, Facts(
            ("failureCode", "feature.invalid"), ("errors", Array.Empty<string>()))))
            .Throws<ArgumentException>();
        await Assert.That(() => InvokeFactory(factory, Facts(
            ("failureCode", "feature.invalid"), ("errors", BlankValidationErrors))))
            .Throws<ArgumentException>();

        await Assert.That(() => InvokeFactory(factory, Facts(
            ("failureCode", FailureCodes.QuotaExceeded))))
            .Throws<ArgumentException>();

        QuotaExceededDetails quota = CreateQuota();
        object quotaResponse = InvokeFactory(typeof(BaseCommandResponse<Guid>), "Quota", Facts(
            ("message", "quota.blocked"),
            ("quotaExceeded", quota),
            ("error", "quota.machine"),
            ("id", ResultId)));
        JsonObject quotaJson = Serialize(quotaResponse);

        await Assert.That(ReadIsSuccess(quotaResponse)).IsFalse();
        await Assert.That(quotaJson["failureCode"]!.GetValue<string>())
            .IsEqualTo(FailureCodes.QuotaExceeded);
        await AssertQuotaJson(quotaJson["quotaExceeded"]!.AsObject());
    }

    [Test]
    public async Task PublishedErrorsAreReadOnlyAndCannotBeReplaced()
    {
        PropertyInfo errors = typeof(BaseCommandResponse<Guid>).GetProperty("Errors")!;

        await Assert.That(errors.PropertyType).IsEqualTo(typeof(IReadOnlyList<string>));
        await Assert.That(errors.SetMethod is null || !errors.SetMethod.IsPublic).IsTrue();

        object response = InvokeFactory(typeof(BaseCommandResponse<Guid>), "Validation", Facts(
            ("errors", new List<string> { "validation.alpha" }),
            ("message", "validation.alpha"),
            ("id", ResultId)));
        object published = errors.GetValue(response)!;

        await Assert.That(published is List<string>).IsFalse();
        if (published is IList<string> mutableView)
        {
            await Assert.That(() => mutableView.Add("validation.changed")).Throws<NotSupportedException>();
        }
    }

    [Test]
    public async Task SuccessFactoryPreservesGuidIntAndStringIds()
    {
        (Type Type, object Id)[] cases =
        [
            (typeof(BaseCommandResponse<Guid>), ResultId),
            (typeof(BaseCommandResponse<int>), 42),
            (typeof(BaseCommandResponse<string>), "result-identifier"),
        ];

        foreach ((Type responseType, object id) in cases)
        {
            MethodInfo factory = RequireFactory(responseType, "Success");
            await AssertFactoryParameters(factory,
            [
                new ParameterContract("id", id.GetType(), HasDefaultValue: false),
                Optional<string>("message"),
            ]);
            object response = InvokeFactory(factory, Facts(("id", id), ("message", "result.created")));
            JsonObject json = Serialize(response);

            await Assert.That(json["id"]!.ToJsonString())
                .IsEqualTo(JsonSerializer.Serialize(id, id.GetType(), JsonOptions));
        }
    }

    [Test]
    public async Task SuccessAndConflictRequireReferenceIdsWhileOptionalFailureIdsMayBeNull()
    {
        Type responseType = typeof(BaseCommandResponse<string>);
        MethodInfo success = RequireFactory(responseType, "Success");
        MethodInfo conflict = RequireFactory(responseType, "Conflict");

        await Assert.That(() => InvokeFactory(success, Facts(("id", null), ("message", null))))
            .Throws<ArgumentException>();
        await Assert.That(() => InvokeFactory(conflict, Facts(("id", null), ("message", null))))
            .Throws<ArgumentException>();

        object featureFailure = InvokeFactory(responseType, "Failure", Facts(
            ("failureCode", "feature.unavailable"),
            ("id", null)));
        object notFound = InvokeFactory(responseType, "NotFound", Facts(("id", null)));

        await Assert.That(ReadIsSuccess(featureFailure)).IsFalse();
        await Assert.That(ReadIsSuccess(notFound)).IsFalse();
        await Assert.That(Serialize(featureFailure)["id"] is null).IsTrue();
        await Assert.That(Serialize(notFound)["id"] is null).IsTrue();
    }

    [Test]
    public async Task QuotaFactoryPreservesMetadataCanonicalCodeAndFallbackError()
    {
        var quota = CreateQuota();
        object explicitResponse = InvokeFactory(typeof(BaseCommandResponse<Guid>), "Quota", Facts(
            ("message", "quota.blocked"),
            ("quotaExceeded", quota),
            ("error", "quota.machine"),
            ("id", ResultId)));
        JsonObject explicitJson = Serialize(explicitResponse);
        JsonObject details = explicitJson["quotaExceeded"]!.AsObject();

        await Assert.That(explicitJson["success"]!.GetValue<bool>()).IsFalse();
        await Assert.That(explicitJson["failureCode"]!.GetValue<string>()).IsEqualTo(FailureCodes.QuotaExceeded);
        await Assert.That(ReadErrors(explicitJson).SequenceEqual(QuotaMachineErrors, StringComparer.Ordinal)).IsTrue();
        await AssertQuotaJson(details);

        object fallbackResponse = InvokeFactory(typeof(BaseCommandResponse<Guid>), "Quota", Facts(
            ("message", "quota.blocked"),
            ("quotaExceeded", quota),
            ("error", null),
            ("id", ResultId)));
        string[] fallbackErrors = ReadErrors(Serialize(fallbackResponse));

        await Assert.That(fallbackErrors.Length).IsEqualTo(1);
        await Assert.That(string.IsNullOrWhiteSpace(fallbackErrors[0])).IsFalse();
    }

    [Test]
    public async Task QuotaFactoryRejectsNullMetadataBlankMessageAndBlankExplicitError()
    {
        MethodInfo factory = RequireFactory(typeof(BaseCommandResponse<Guid>), "Quota");
        QuotaExceededDetails quota = CreateQuota();

        await Assert.That(() => InvokeFactory(factory, Facts(
            ("message", "quota.blocked"), ("quotaExceeded", null), ("error", null), ("id", ResultId))))
            .Throws<ArgumentException>();
        await Assert.That(() => InvokeFactory(factory, Facts(
            ("message", " "), ("quotaExceeded", quota), ("error", null), ("id", ResultId))))
            .Throws<ArgumentException>();
        await Assert.That(() => InvokeFactory(factory, Facts(
            ("message", "quota.blocked"), ("quotaExceeded", quota), ("error", " "), ("id", ResultId))))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task EveryConcreteDescendantDeclaresExactSuccessAndFailureFactoriesAndPreservesTheirStates()
    {
        DerivedFactoryScenario[] scenarios = CreateDerivedFactoryScenarios();

        foreach (DerivedFactoryScenario scenario in scenarios)
        {
            MethodInfo[] declaredFactories = scenario.ResponseType
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => method.ReturnType == scenario.ResponseType)
                .OrderBy(method => method.Name, StringComparer.Ordinal)
                .ToArray();
            await Assert.That(declaredFactories.Select(method => method.Name).SequenceEqual(
                ConcreteFactoryNames,
                StringComparer.Ordinal)).IsTrue();

            MethodInfo successFactory = declaredFactories.Single(method => method.Name == "Success");
            string[] expectedParameterNames = scenario.Facts.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            string[] actualParameterNames = successFactory.GetParameters()
                .Select(parameter => parameter.Name!)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            PropertyInfo[] payloadProperties = ResponsePayloadProperties(
                scenario.ResponseType,
                includeJsonIgnored: true);

            await Assert.That(actualParameterNames.SequenceEqual(
                expectedParameterNames,
                StringComparer.OrdinalIgnoreCase)).IsTrue();
            foreach (ParameterInfo parameter in successFactory.GetParameters())
            {
                object? fact = scenario.Facts[parameter.Name!];
                if (fact is not null)
                {
                    await Assert.That(parameter.ParameterType).IsEqualTo(fact.GetType());
                }
            }
            await Assert.That(payloadProperties.Select(property => property.Name).Order(StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(
                    scenario.ExpectedProperties.Select(property => property.PropertyName).Order(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase)).IsTrue();

            object success = InvokeFactory(successFactory, scenario.Facts);
            JsonObject successJson = Serialize(success);

            await Assert.That(ReadIsSuccess(success)).IsTrue();
            await Assert.That(successJson["success"]!.GetValue<bool>()).IsTrue();
            await Assert.That(successJson["failureCode"] is null).IsTrue();
            await Assert.That(successJson["errors"] is null).IsTrue();
            await Assert.That(successJson["quotaExceeded"] is null).IsTrue();
            foreach ((string propertyName, object? expected) in scenario.ExpectedProperties)
            {
                await Assert.That(scenario.ResponseType.GetProperty(propertyName)!.GetValue(success))
                    .IsEqualTo(expected);
            }

            if (scenario.ResponseType == typeof(WebhookProviderPortalAccessCommandResponse))
            {
                continue;
            }

            MethodInfo failureFactory = declaredFactories.Single(method => method.Name == "Failure");
            Type keyType = ResponseKeyType(scenario.ResponseType);
            Type baseStateType = typeof(BaseCommandResponse<>).MakeGenericType(keyType);
            await AssertFactoryParameters(failureFactory, FailureParameterContracts(keyType));
            string[] failureParameterNames = failureFactory.GetParameters()
                .Select(parameter => parameter.Name!)
                .ToArray();

            await Assert.That(failureParameterNames.Intersect(
                payloadProperties.Select(property => property.Name),
                StringComparer.OrdinalIgnoreCase)).IsEmpty();

            object id = keyType == typeof(Guid) ? ResultId : CreatePortal();
            var sourceErrors = new List<string>(FeatureErrors);
            object baseFailure = InvokeFactory(baseStateType, "Failure", Facts(
                ("failureCode", PromotionRedemptionFailureCodes.Unavailable),
                ("message", "feature.blocked"),
                ("errors", sourceErrors),
                ("id", id)));
            object failure = InvokeFactory(failureFactory, Facts(("failure", baseFailure)));
            sourceErrors.Clear();
            JsonObject failureJson = Serialize(failure);

            await Assert.That(failure.GetType()).IsEqualTo(scenario.ResponseType);
            await Assert.That(ReadIsSuccess(failure)).IsFalse();
            await Assert.That(failureJson["success"]!.GetValue<bool>()).IsFalse();
            await Assert.That(failureJson["failureCode"]!.GetValue<string>())
                .IsEqualTo(PromotionRedemptionFailureCodes.Unavailable);
            await Assert.That(ReadErrors(failureJson).SequenceEqual(
                FeatureErrors,
                StringComparer.Ordinal)).IsTrue();
            await Assert.That(failureJson["quotaExceeded"] is null).IsTrue();
            foreach (PropertyInfo payloadProperty in payloadProperties)
            {
                await Assert.That(payloadProperty.GetValue(failure))
                    .IsEqualTo(SafeDefault(payloadProperty.PropertyType));
            }

            object successfulBase = InvokeFactory(baseStateType, "Success", Facts(
                ("id", id),
                ("message", "result.created")));
            await Assert.That(() => InvokeFactory(failureFactory, Facts(("failure", successfulBase))))
                .Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task WebhookPortalFailureRequiresRetryabilityAndClearsCapabilityPayload()
    {
        Type responseType = typeof(WebhookProviderPortalAccessCommandResponse);
        MethodInfo failureFactory = RequireFactory(responseType, "Failure", declaredOnly: true);
        Type baseStateType = typeof(BaseCommandResponse<WebhookProviderPortalAccessDto>);

        await AssertFactoryParameters(failureFactory,
        [
            new ParameterContract("failure", baseStateType, HasDefaultValue: false),
            Required<bool>("isRetryable"),
        ]);

        WebhookProviderPortalAccessDto capability = CreatePortal() with { Token = SyntheticReveal };
        object baseFailure = InvokeFactory(baseStateType, "Failure", Facts(
            ("failureCode", PromotionRedemptionFailureCodes.Unavailable),
            ("message", "feature.blocked"),
            ("errors", FeatureErrors),
            ("id", capability)));

        foreach (bool isRetryable in new[] { true, false })
        {
            var failure = (WebhookProviderPortalAccessCommandResponse)InvokeFactory(
                failureFactory,
                Facts(("failure", baseFailure), ("isRetryable", isRetryable)));
            JsonObject failureJson = Serialize(failure);

            await Assert.That(failure.IsSuccess).IsFalse();
            await Assert.That(failureJson["success"]!.GetValue<bool>()).IsFalse();
            await Assert.That(failure.IsRetryable).IsEqualTo(isRetryable);
            await Assert.That(failureJson["isRetryable"]!.GetValue<bool>()).IsEqualTo(isRetryable);
            await Assert.That(failure.FailureCode)
                .IsEqualTo(PromotionRedemptionFailureCodes.Unavailable);
            await Assert.That(failure.Errors!.SequenceEqual(FeatureErrors, StringComparer.Ordinal)).IsTrue();
            await Assert.That(failure.QuotaExceeded).IsNull();
            await Assert.That(failure.Id).IsNull();
            await Assert.That(failureJson["id"] is null).IsTrue();
            string failureWire = failureJson.ToJsonString();
            await Assert.That(failureWire).DoesNotContain(capability.Url);
            await Assert.That(failureWire).DoesNotContain(SyntheticReveal);
        }

        object successfulBase = InvokeFactory(baseStateType, "Success", Facts(
            ("id", capability),
            ("message", "result.created")));
        await Assert.That(() => InvokeFactory(
            failureFactory,
            Facts(("failure", successfulBase), ("isRetryable", true))))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task EveryRequiredBaseAndConcreteResponseHasReflectionBackedSourceGeneratedMetadata()
    {
        foreach (Type responseType in RegisteredResponseTypes)
        {
            JsonTypeInfo? typeInfo = ExploreJsonContext.Default.GetTypeInfo(responseType);
            await Assert.That(typeInfo).IsNotNull();

            JsonPropertyInfo successMetadata = typeInfo!.Properties.Single(property => property.Name == "success");
            PropertyInfo? successProperty = successMetadata.AttributeProvider as PropertyInfo;
            await Assert.That(successProperty).IsNotNull();
            await Assert.That(successProperty!.Name).IsEqualTo("IsSuccess");
            await Assert.That(successProperty.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
                .IsEqualTo("success");

            const string validationJson =
                "{\"success\":false,\"message\":\"validation.alpha\",\"errors\":[\"validation.alpha\"]}";
            object? response = JsonSerializer.Deserialize(validationJson, typeInfo);
            await Assert.That(response).IsNotNull();
            await Assert.That(ReadIsSuccess(response!)).IsFalse();

            JsonObject json = Serialize(response!, typeInfo);
            await Assert.That(json["success"]!.GetValue<bool>()).IsFalse();
            await Assert.That(json.Select(property => property.Key)
                .All(name => char.IsLower(name[0]))).IsTrue();
        }
    }

    [Test]
    public async Task EveryWireDescendantIsRegisteredAndPreservesPayloadThroughExploreJsonContext()
    {
        var missing = new List<string>();

        foreach (DerivedWireScenario scenario in CreateDerivedWireScenarios())
        {
            JsonTypeInfo? typeInfo = ExploreJsonContext.Default.GetTypeInfo(scenario.ResponseType);
            if (typeInfo is null)
            {
                missing.Add(scenario.ResponseType.FullName!);
                continue;
            }

            object response = JsonSerializer.Deserialize(
                scenario.ExpectedJson.ToJsonString(),
                scenario.ResponseType,
                JsonOptions)!;
            JsonObject sourceGeneratedJson = Serialize(response, typeInfo);
            foreach (string payloadName in scenario.ExpectedPayloadJsonNames)
            {
                await Assert.That(sourceGeneratedJson.ContainsKey(payloadName)).IsTrue();
                JsonNode? expectedPayload = scenario.ExpectedJson[payloadName]?.DeepClone();
                RemoveNullObjectProperties(expectedPayload);
                await Assert.That(JsonNode.DeepEquals(
                    sourceGeneratedJson[payloadName],
                    expectedPayload)).IsTrue();
            }
        }

        await Assert.That(missing.Order(StringComparer.Ordinal)).IsEmpty();
    }

    private static DerivedWireScenario[] CreateDerivedWireScenarios()
    {
        RegistrationOrderDto order = CreateOrder();
        GuestRegistrationOrderDto guestOrder = GuestRegistrationOrderDto.From(order);
        RegistrationMaterialChangeChoiceDto choice = CreateChoice();
        RegistrationRefundDto refund = CreateRefund();
        RegistrationPaymentDto payment = CreatePayment(refund, choice);
        SupportAccessSessionDto session = CreateSession();
        PromotionManagementDto promotion = CreatePromotion();
        WebhookProviderPortalAccessDto portal = CreatePortal();

        return
        [
            Wire(typeof(CreateExternalApiKeyCommandResponse), ResultId,
                ("apiKey", SyntheticReveal), ("keyId", SyntheticKeyId)),
            Wire(typeof(GuestRegistrationOrderLifecycleResponseDto), ResultId, ("order", guestOrder)),
            Wire(typeof(GuestRegistrationOrderStartDto), ResultId),
            Wire(typeof(RegistrationMaterialChangeChoiceCommandResultDto), ResultId,
                ("choice", choice), ("refund", refund)),
            Wire(typeof(RegistrationOrderLifecycleResponseDto), ResultId, ("order", order)),
            Wire(typeof(RegistrationPaymentCommandResultDto), ResultId, ("payment", payment)),
            Wire(typeof(RegistrationRefundCommandResultDto), ResultId, ("refund", refund)),
            Wire(typeof(SupportAccessSessionCommandResponseDto), ResultId, ("session", session)),
            Wire(typeof(PromotionCodeIssuedCommandResponseDto), ResultId,
                ("promotion", promotion), ("issuedCode", SyntheticReveal)),
            Wire(typeof(PromotionManagementCommandResponseDto), ResultId, ("promotion", promotion)),
            Wire(typeof(PromotionRedemptionResponseDto), ResultId,
                ("appliedPromotionDisplayLabel", "fixture-label"),
                ("promotionDiscountTotalMinor", 125L),
                ("totalDueMinor", 875L),
                ("platformFeeTotalMinor", 50L),
                ("platformContributionTotalMinor", 25L)),
            Wire(typeof(WebhookProviderPortalAccessCommandResponse), portal, ("isRetryable", true)),
        ];
    }

    private static DerivedFactoryScenario[] CreateDerivedFactoryScenarios()
    {
        RegistrationOrderDto order = CreateOrder();
        GuestRegistrationOrderDto guestOrder = GuestRegistrationOrderDto.From(order);
        RegistrationMaterialChangeChoiceDto choice = CreateChoice();
        RegistrationRefundDto refund = CreateRefund();
        RegistrationPaymentDto payment = CreatePayment(refund, choice);
        SupportAccessSessionDto session = CreateSession();
        PromotionManagementDto promotion = CreatePromotion();
        WebhookProviderPortalAccessDto portal = CreatePortal();

        AtprotoTransientValue transient = CreateTransientValue();

        return
        [
            Factory(typeof(AtprotoTransientCommandResult), Facts(("value", transient)), ("Value", transient)),
            Factory(typeof(CreateExternalApiKeyCommandResponse),
                Facts(("id", ResultId), ("message", "result.created"),
                    ("apiKey", SyntheticReveal), ("keyId", SyntheticKeyId)),
                ("ApiKey", SyntheticReveal), ("KeyId", SyntheticKeyId)),
            Factory(typeof(GuestRegistrationOrderLifecycleResponseDto),
                Facts(("id", ResultId), ("message", "result.created"), ("order", guestOrder)),
                ("Order", guestOrder)),
            Factory(typeof(GuestRegistrationOrderStartDto),
                Facts(("id", ResultId), ("message", "result.created"),
                    ("guestCapabilityToken", SyntheticReveal)),
                ("GuestCapabilityToken", SyntheticReveal)),
            Factory(typeof(RegistrationMaterialChangeChoiceCommandResultDto),
                Facts(("id", ResultId), ("message", "result.created"),
                    ("choice", choice), ("refund", refund)),
                ("Choice", choice), ("Refund", refund)),
            Factory(typeof(RegistrationOrderLifecycleResponseDto),
                Facts(("id", ResultId), ("message", "result.created"), ("order", order)),
                ("Order", order)),
            Factory(typeof(RegistrationPaymentCommandResultDto),
                Facts(("id", ResultId), ("message", "result.created"), ("payment", payment)),
                ("Payment", payment)),
            Factory(typeof(RegistrationRefundCommandResultDto),
                Facts(("id", ResultId), ("message", "result.created"), ("refund", refund)),
                ("Refund", refund)),
            Factory(typeof(SupportAccessSessionCommandResponseDto),
                Facts(("id", ResultId), ("message", "result.created"), ("session", session)),
                ("Session", session)),
            Factory(typeof(PromotionCodeIssuedCommandResponseDto),
                Facts(("id", ResultId), ("message", "result.created"),
                    ("promotion", promotion), ("issuedCode", SyntheticReveal)),
                ("Promotion", promotion), ("IssuedCode", SyntheticReveal)),
            Factory(typeof(PromotionManagementCommandResponseDto),
                Facts(("id", ResultId), ("message", "result.created"), ("promotion", promotion)),
                ("Promotion", promotion)),
            Factory(typeof(PromotionRedemptionResponseDto),
                Facts(("id", ResultId), ("message", "result.created"),
                    ("appliedPromotionDisplayLabel", "fixture-label"),
                    ("promotionDiscountTotalMinor", 125L),
                    ("totalDueMinor", 875L),
                    ("platformFeeTotalMinor", 50L),
                    ("platformContributionTotalMinor", 25L)),
                ("AppliedPromotionDisplayLabel", "fixture-label"),
                ("PromotionDiscountTotalMinor", 125L),
                ("TotalDueMinor", 875L),
                ("PlatformFeeTotalMinor", 50L),
                ("PlatformContributionTotalMinor", 25L)),
            Factory(typeof(WebhookProviderPortalAccessCommandResponse),
                Facts(("id", portal), ("message", "result.created"), ("isRetryable", true)),
                ("IsRetryable", true)),
        ];
    }

    [Test]
    public async Task PrivateTransientFactoriesBindRowIdentityAndClearPayloadForEveryFailureState()
    {
        AtprotoTransientValue value = CreateTransientValue();
        AtprotoTransientCommandResult success = AtprotoTransientCommandResult.Success(value);
        await Assert.That(success.IsSuccess).IsTrue();
        await Assert.That(success.Id).IsEqualTo(value.Id);
        await Assert.That(success.Value).IsEqualTo(value);
        await Assert.That(success).IsEqualTo(AtprotoTransientCommandResult.Success(value with { }));
        await Assert.That(success).IsNotEqualTo(AtprotoTransientCommandResult.Success(value with { TenantId = RelatedId }));

        BaseCommandResponse<Guid>[] failures =
        [
            BaseCommandResponse.Validation<Guid>(OneValidationError),
            BaseCommandResponse.NotFound<Guid>(),
            BaseCommandResponse.Conflict(ResultId),
            BaseCommandResponse.Authentication<Guid>(),
            BaseCommandResponse.Authorization<Guid>(),
            BaseCommandResponse.Quota<Guid>("quota.blocked", CreateQuota()),
            BaseCommandResponse.Failure<Guid>("transient_unavailable", errors: FeatureErrors),
        ];
        foreach (var failure in failures)
        {
            AtprotoTransientCommandResult result = AtprotoTransientCommandResult.Failure(failure);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Id).IsEqualTo(failure.Id);
            await Assert.That(result.FailureCode).IsEqualTo(failure.FailureCode);
            await Assert.That(result.Message).IsEqualTo(failure.Message);
            await Assert.That(result.Errors ?? []).IsEquivalentTo(failure.Errors ?? []);
            await Assert.That(result.QuotaExceeded).IsEqualTo(failure.QuotaExceeded);
            await Assert.That(result.Value).IsNull();
        }
        await Assert.That(() => AtprotoTransientCommandResult.Failure(BaseCommandResponse.Success(ResultId)))
            .Throws<ArgumentException>();
    }

    private static AtprotoTransientValue CreateTransientValue() => new(ResultId, AtprotoTransientPurpose.OAuthState,
        new string('a', 64), TenantId, SyntheticReveal, FixtureOffset.AddMinutes(1).ToUnixTimeMilliseconds());

    private static RegistrationOrderDto CreateOrder() => new()
    {
        Id = ResultId,
        EventId = RelatedId,
        StatusCode = "READY",
        StatusName = "Ready",
        CurrencyCode = "EUR",
        TotalDueMinor = 875,
        Lines = [],
    };

    private static RegistrationMaterialChangeChoiceDto CreateChoice() => new()
    {
        Id = ChoiceId,
        CampaignId = RelatedId,
        StatusCode = "ACCEPTED",
        CreatedAt = FixtureUtc,
        DecidedAt = FixtureUtc.AddHours(1),
    };

    private static RegistrationRefundDto CreateRefund() => new()
    {
        Id = RefundId,
        StatusCode = "SUCCEEDED",
        StatusName = "Succeeded",
        FailureCode = null,
        AmountMinor = 125,
        CurrencyCode = "EUR",
        AcceptedRefundPolicyVersion = 2,
        CreatedAt = FixtureUtc,
        LastObservedAt = FixtureUtc.AddMinutes(5),
        SucceededAt = FixtureUtc.AddMinutes(5),
    };

    private static RegistrationPaymentDto CreatePayment(
        RegistrationRefundDto refund,
        RegistrationMaterialChangeChoiceDto choice) => new()
    {
        Id = ResultId,
        RegistrationOrderId = RelatedId,
        StatusCode = "CAPTURED",
        StatusName = "Captured",
        HostedRedirectAvailable = false,
        RetryAvailable = true,
        FailureCode = null,
        CreatedAt = FixtureUtc,
        LastUpdatedAt = FixtureUtc.AddMinutes(5),
        ExpiresAt = FixtureUtc.AddHours(1),
        RefundedAmountMinor = 125,
        RefundPendingAmountMinor = 0,
        Refunds = [refund],
        Disputes = [],
        MaterialChangeChoices = [choice],
        BuyerRefundRequestAvailable = true,
        OrganizerRefundAvailable = true,
        CapturedAmountMinor = 1_000,
        CurrencyCode = "EUR",
        CurrencyMinorUnitDigits = 2,
    };

    private static SupportAccessSessionDto CreateSession() => new()
    {
        Id = ResultId,
        TargetTenantId = TenantId,
        StatusName = "ACTIVE",
        ModeName = "ReadOnly",
        ReasonCode = "fixture.reason",
        TicketReference = "fixture-ticket",
        StartedAtUtc = FixtureOffset,
        ExpiresAtUtc = FixtureOffset.AddHours(1),
        IsActive = true,
    };

    private static PromotionManagementDto CreatePromotion() => new()
    {
        EventId = RelatedId,
        TicketCatalogVersionId = ChoiceId,
        TicketCatalogVersionNumber = 3,
        CurrencyCode = "EUR",
        DefinitionId = ResultId,
        DefinitionGroupId = RefundId,
        VersionNumber = 2,
        StatusId = 1,
        StatusCode = "DRAFT",
        StatusName = "Draft",
        DisplayLabel = "fixture-label",
        StartsAtUtc = FixtureUtc,
        EndsAtUtc = FixtureUtc.AddDays(1),
        TotalRedemptionLimit = 10,
        PerVerifiedPurchaserLimit = 2,
        DiscountKind = "fixed",
        FixedDiscountMinor = 125,
        BasisPointDiscount = null,
        MaximumDiscountMinor = 250,
        IncludesAllTickets = true,
        EligibleTicketTypeIds = [ChoiceId],
        PromotionCodeDisplayLabel = "****TURE",
    };

    private static WebhookProviderPortalAccessDto CreatePortal() => new()
    {
        Url = "https://portal.example.test/session",
        Token = null,
        ExpiresAt = FixtureOffset,
    };

    private static QuotaExceededDetails CreateQuota() => new(
        "fixture.quota",
        Limit: 5,
        Actual: 5,
        Attempted: 6,
        Scope: "fixture.scope",
        TenantId);

    private static DerivedWireScenario Wire(
        Type responseType,
        object id,
        params (string Name, object? Value)[] payload)
    {
        JsonObject expected = ResponseJson(id, payload);
        return new DerivedWireScenario(responseType, expected, payload.Select(value => value.Name).ToArray());
    }

    private static DerivedFactoryScenario Factory(
        Type responseType,
        Dictionary<string, object?> facts,
        params (string PropertyName, object? Expected)[] expectedProperties) =>
        new(responseType, facts, expectedProperties);

    private static JsonObject ResponseJson(object id, params (string Name, object? Value)[] payload)
    {
        var json = new JsonObject
        {
            ["id"] = JsonSerializer.SerializeToNode(id, id.GetType(), JsonOptions),
            ["success"] = true,
            ["message"] = "result.created",
            ["errors"] = null,
            ["failureCode"] = null,
            ["quotaExceeded"] = null,
        };

        foreach ((string name, object? value) in payload)
        {
            json[name] = JsonSerializer.SerializeToNode(value, value?.GetType() ?? typeof(object), JsonOptions);
        }

        return json;
    }

    private static PropertyInfo[] ResponsePayloadProperties(Type responseType, bool includeJsonIgnored = false) =>
        responseType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.DeclaringType is not null
                && !DerivesFromBaseCommandResponse(property.DeclaringType)
                ? false
                : property.DeclaringType is not null
                    && !(property.DeclaringType.IsGenericType
                        && property.DeclaringType.GetGenericTypeDefinition() == typeof(BaseCommandResponse<>)))
            .Where(property => includeJsonIgnored || property.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

    private static string JsonName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
        ?? JsonOptions.PropertyNamingPolicy!.ConvertName(property.Name);

    private static JsonObject Serialize(object response) =>
        JsonSerializer.SerializeToNode(response, response.GetType(), JsonOptions)!.AsObject();

    private static JsonObject Serialize(object response, JsonTypeInfo typeInfo) =>
        JsonNode.Parse(JsonSerializer.Serialize(response, typeInfo))!.AsObject();

    private static string[] ReadErrors(JsonObject json) =>
        json["errors"]!.AsArray().Select(error => error!.GetValue<string>()).ToArray();

    private static void RemoveNullObjectProperties(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (string propertyName in jsonObject
                .Where(property => property.Value is null)
                .Select(property => property.Key)
                .ToArray())
            {
                jsonObject.Remove(propertyName);
            }

            foreach (JsonNode? child in jsonObject.Select(property => property.Value))
            {
                RemoveNullObjectProperties(child);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (JsonNode? child in jsonArray)
            {
                RemoveNullObjectProperties(child);
            }
        }
    }

    private static bool DerivesFromBaseCommandResponse(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(BaseCommandResponse<>))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null;

    private static bool ReadIsSuccess(object response)
    {
        PropertyInfo? property = response.GetType().GetProperty("IsSuccess", BindingFlags.Public | BindingFlags.Instance);
        if (property is null)
        {
            throw new MissingMemberException(response.GetType().FullName, "IsSuccess");
        }

        return (bool)property.GetValue(response)!;
    }

    private static Type ResponseKeyType(Type responseType)
    {
        for (Type? current = responseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(BaseCommandResponse<>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        throw new InvalidOperationException($"{responseType.FullName} is not a command response.");
    }

    private static ParameterContract[] FailureParameterContracts(Type keyType) =>
    [
        new ParameterContract(
            "failure",
            typeof(BaseCommandResponse<>).MakeGenericType(keyType),
            HasDefaultValue: false),
    ];

    private static object? SafeDefault(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type) : null;

    private static ParameterContract Required<T>(string name) =>
        new(name, typeof(T), HasDefaultValue: false);

    private static ParameterContract Optional<T>(string name) =>
        new(name, typeof(T), HasDefaultValue: true);

    private static async Task AssertFactoryParameters(
        MethodInfo factory,
        IReadOnlyList<ParameterContract> expected)
    {
        ParameterInfo[] actual = factory.GetParameters();
        await Assert.That(actual.Length).IsEqualTo(expected.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            await Assert.That(actual[index].Name).IsEqualTo(expected[index].Name);
            await Assert.That(actual[index].ParameterType).IsEqualTo(expected[index].Type);
            await Assert.That(actual[index].HasDefaultValue).IsEqualTo(expected[index].HasDefaultValue);
        }
    }

    private static async Task AssertQuotaJson(JsonObject details)
    {
        await Assert.That(details["quotaKey"]!.GetValue<string>()).IsEqualTo("fixture.quota");
        await Assert.That(details["limit"]!.GetValue<int>()).IsEqualTo(5);
        await Assert.That(details["actual"]!.GetValue<int>()).IsEqualTo(5);
        await Assert.That(details["attempted"]!.GetValue<int>()).IsEqualTo(6);
        await Assert.That(details["scope"]!.GetValue<string>()).IsEqualTo("fixture.scope");
        await Assert.That(details.ContainsKey("tenantId")).IsFalse();
    }

    private static Dictionary<string, object?> Facts(params (string Name, object? Value)[] values) =>
        values.ToDictionary(value => value.Name, value => value.Value, StringComparer.OrdinalIgnoreCase);

    private static object InvokeFactory(
        Type responseType,
        string factoryName,
        IReadOnlyDictionary<string, object?> facts) =>
        InvokeFactory(RequireFactory(responseType, factoryName), facts);

    private static Type? FindFactoryCompanion() =>
        typeof(BaseCommandResponse<>).Assembly.GetType(
            "Explore.Application.Responses.BaseCommandResponse",
            throwOnError: false,
            ignoreCase: false);

    private static MethodInfo RequireFactory(Type responseType, string factoryName, bool declaredOnly = false)
    {
        if (responseType.IsGenericType
            && responseType.GetGenericTypeDefinition() == typeof(BaseCommandResponse<>))
        {
            return RequireGenericCompanionFactory(responseType, factoryName);
        }

        BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
        if (declaredOnly)
        {
            flags |= BindingFlags.DeclaredOnly;
        }

        MethodInfo[] factories = responseType.GetMethods(flags)
            .Where(method => method.Name == factoryName && method.ReturnType == responseType)
            .ToArray();
        if (factories.Length != 1)
        {
            throw new MissingMethodException(
                responseType.FullName,
                $"public static {responseType.Name} {factoryName}(...) factory (found {factories.Length})");
        }

        return factories[0];
    }

    private static MethodInfo RequireGenericCompanionFactory(Type responseType, string factoryName)
    {
        Type? companionType = FindFactoryCompanion();
        if (companionType is null)
        {
            throw new TypeLoadException(
                "The non-generic Explore.Application.Responses.BaseCommandResponse factory companion is missing.");
        }

        Type keyType = responseType.GetGenericArguments()[0];
        MethodInfo[] factories = companionType
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == factoryName
                && method.IsGenericMethodDefinition
                && method.GetGenericArguments().Length == 1)
            .Select(method => method.MakeGenericMethod(keyType))
            .Where(method => method.ReturnType == responseType)
            .ToArray();
        if (factories.Length != 1)
        {
            throw new MissingMethodException(
                companionType.FullName,
                $"public static BaseCommandResponse<{keyType.Name}> {factoryName}<{keyType.Name}>(...) factory (found {factories.Length})");
        }

        return factories[0];
    }

    private static object InvokeFactory(MethodInfo factory, IReadOnlyDictionary<string, object?> facts)
    {
        ParameterInfo[] parameters = factory.GetParameters();
        string[] ignoredFacts = facts.Keys
            .Where(fact => parameters.All(parameter => !string.Equals(parameter.Name, fact, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (ignoredFacts.Length > 0)
        {
            throw new InvalidOperationException(
                $"Factory {factory.DeclaringType!.FullName}.{factory.Name} ignored facts: {string.Join(", ", ignoredFacts)}.");
        }

        object?[] arguments = parameters.Select(parameter =>
        {
            if (facts.TryGetValue(parameter.Name!, out object? value))
            {
                return value;
            }

            if (parameter.HasDefaultValue)
            {
                return parameter.DefaultValue;
            }

            throw new InvalidOperationException(
                $"Factory {factory.DeclaringType!.FullName}.{factory.Name} is missing required fact '{parameter.Name}'.");
        }).ToArray();

        try
        {
            return factory.Invoke(null, arguments)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private sealed record ParameterContract(string Name, Type Type, bool HasDefaultValue);
    private sealed record FactorySignature(string Name, IReadOnlyList<ParameterContract> Parameters);
    private sealed record DerivedWireScenario(
        Type ResponseType,
        JsonObject ExpectedJson,
        IReadOnlyList<string> ExpectedPayloadJsonNames);
    private sealed record DerivedFactoryScenario(
        Type ResponseType,
        IReadOnlyDictionary<string, object?> Facts,
        IReadOnlyList<(string PropertyName, object? Expected)> ExpectedProperties);

}
