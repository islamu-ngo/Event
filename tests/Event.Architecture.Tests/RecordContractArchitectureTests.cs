// ABOUTME: Enforces final record-contract and HTTP body-authority ratchets after records adoption.
// ABOUTME: Uses compiled metadata and exact reasoned JSON baselines so new, stale, or hidden debt fails deterministically.

namespace Event.Architecture.Tests
{

using System.CodeDom.Compiler;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Explore.API.Controllers;
using Explore.Application.Authorization;
using Explore.Application.DTOs.CustomPropertyProjection.Validators;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.DTOs.RegistrationSubmissions;
using Explore.Application.Responses;
using Explore.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;

public sealed class RecordContractArchitectureTests
{
    private const string ClassBaselinePath =
        "tests/Event.Architecture.Tests/Baselines/record-contract-class-baseline.json";

    private const string BodyBaselinePath =
        "tests/Event.Architecture.Tests/Baselines/http-body-authority-dispositions.json";

    private static readonly Assembly ApplicationAssembly = typeof(AuthorizeResourceAttribute).Assembly;
    private static readonly Assembly ApiAssembly = typeof(OrganizationReviewController).Assembly;
    private static readonly Assembly DomainAssembly = typeof(ITenantEntity).Assembly;

    private static readonly string[] ExpectedBaseCommandResponseDescendants =
    [
        "Explore.Application.DTOs.RegistrationOrders.GuestRegistrationOrderLifecycleResponseDto",
        "Explore.Application.DTOs.RegistrationOrders.GuestRegistrationOrderStartDto",
        "Explore.Application.DTOs.RegistrationOrders.RegistrationMaterialChangeChoiceCommandResultDto",
        "Explore.Application.DTOs.RegistrationOrders.RegistrationOrderLifecycleResponseDto",
        "Explore.Application.DTOs.RegistrationOrders.RegistrationPaymentCommandResultDto",
        "Explore.Application.DTOs.RegistrationOrders.RegistrationRefundCommandResultDto",
        "Explore.Application.DTOs.SupportAccess.SupportAccessSessionCommandResponseDto",
        "Explore.Application.Features.Authentication.Atproto.Models.AtprotoTransientCommandResult",
        "Explore.Application.Features.Promotions.PromotionCodeIssuedCommandResponseDto",
        "Explore.Application.Features.Promotions.PromotionManagementCommandResponseDto",
        "Explore.Application.Features.Promotions.Requests.Commands.PromotionRedemptionResponseDto",
        "Explore.Application.Responses.CreateExternalApiKeyCommandResponse",
        "Explore.Application.Responses.WebhookProviderPortalAccessCommandResponse",
    ];

    private static readonly HashSet<string> ClassCategories = new(StringComparer.Ordinal)
    {
        "retained-class",
    };

    private static readonly HashSet<string> BodyCategories = new(StringComparer.Ordinal)
    {
        "legitimate-target",
    };

    [Test]
    public async Task DetectorCharacterizationRecognizesEstablishedRecordAndExplicitExclusions()
    {
        var entity = DomainAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .First(type => typeof(ITenantEntity).IsAssignableFrom(type));

        var generated = typeof(Explore.Application.DTOs.RecordContractCharacterization.GeneratedContract);
        var generatedRequest = typeof(Explore.Application.DTOs.RecordContractCharacterization.GeneratedRequest);
        var compilerGeneratedRequest = typeof(Explore.Application.DTOs.RecordContractCharacterization.CompilerGeneratedRequest);
        var editState = typeof(Explore.Application.DTOs.RecordContractCharacterization.MutableContractEditState);
        var fixture = typeof(Explore.Application.DTOs.RecordContractCharacterization.ContractTestFixture);
        var fixtureRequest = typeof(SyntheticTestFixtureRequest);
        var genericRecordRequest = typeof(Explore.Application.Features.Webhooks.Requests.Queries.GetWebhookEventTypesQuery);
        var nonGenericRecordRequest = typeof(Explore.Application.Features.UserAuthenticationTokens.Requests.Commands.DeleteUserAuthenticationTokenCommand);
        var abstractRecordRequest = typeof(Explore.Application.Features.Promotions.Requests.Commands.PromotionManagementCommandBase<>);
        var inheritedRecordRequest = typeof(Explore.Application.Features.Promotions.Requests.Commands.CreatePromotionDraftCommand);
        var classContracts = DiscoverHandwrittenApplicationClassDtos().ToHashSet(StringComparer.Ordinal);
        var compiledRequests = DiscoverConcreteCompiledMediatRRequests().ToHashSet();

        await Assert.That(Classify(typeof(NativeRegistrationFormDefinitionDto))).IsEqualTo(ContractClassification.Record);
        await Assert.That(IsRecord(genericRecordRequest)).IsTrue();
        await Assert.That(genericRecordRequest.GetInterfaces().Any(contract =>
            contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IRequest<>))).IsTrue();
        await Assert.That(typeof(IRequest).IsAssignableFrom(nonGenericRecordRequest)).IsTrue();
        await Assert.That(IsRecord(abstractRecordRequest)).IsTrue();
        await Assert.That(IsRecord(inheritedRecordRequest)).IsTrue();
        await Assert.That(inheritedRecordRequest.BaseType!.GetGenericTypeDefinition()).IsEqualTo(abstractRecordRequest);
        await Assert.That(compiledRequests.Contains(genericRecordRequest)).IsTrue();
        await Assert.That(compiledRequests.Contains(inheritedRecordRequest)).IsTrue();
        await Assert.That(compiledRequests.Contains(nonGenericRecordRequest)).IsTrue();
        await Assert.That(compiledRequests.Contains(abstractRecordRequest)).IsFalse();
        await Assert.That(IsApplicationContractOwned(generated)).IsTrue();
        await Assert.That(Classify(generated)).IsEqualTo(ContractClassification.Generated);
        await Assert.That(typeof(IBaseRequest).IsAssignableFrom(generatedRequest)).IsTrue();
        await Assert.That(IsGenerated(generatedRequest)).IsTrue();
        await Assert.That(IsCompiledApplicationRequest(generatedRequest)).IsFalse();
        await Assert.That(typeof(IBaseRequest).IsAssignableFrom(compilerGeneratedRequest)).IsTrue();
        await Assert.That(IsGenerated(compilerGeneratedRequest)).IsTrue();
        await Assert.That(IsCompiledApplicationRequest(compilerGeneratedRequest)).IsFalse();
        await Assert.That(typeof(IBaseRequest).IsAssignableFrom(fixtureRequest)).IsTrue();
        await Assert.That(IsTestFixture(fixtureRequest)).IsTrue();
        await Assert.That(IsCompiledApplicationRequest(fixtureRequest)).IsFalse();
        await Assert.That(IsApplicationContractOwned(typeof(RebuildProjectionRequestDtoValidator))).IsTrue();
        await Assert.That(Classify(typeof(RebuildProjectionRequestDtoValidator))).IsEqualTo(ContractClassification.Validator);
        await Assert.That(Classify(entity)).IsEqualTo(ContractClassification.Entity);
        await Assert.That(IsApplicationContractOwned(editState)).IsTrue();
        await Assert.That(Classify(editState)).IsEqualTo(ContractClassification.MutableEditState);
        await Assert.That(IsApplicationContractOwned(fixture)).IsTrue();
        await Assert.That(Classify(fixture)).IsEqualTo(ContractClassification.TestFixture);
        await Assert.That(classContracts).IsEmpty();
    }

    [Test]
    public async Task BodyDetectorCharacterizationRecognizesMvcInferenceAndBindingSourceExclusions()
    {
        var discovered = DiscoverHttpBodyContractTypes().Select(GetTypeName).ToHashSet(StringComparer.Ordinal);

        await Assert.That(discovered.Contains("Explore.API.Controllers.RegistrationWorkflowInput")).IsTrue();
        await Assert.That(discovered.Contains("Explore.API.Controllers.RegistrationFormInput")).IsTrue();
        await Assert.That(discovered.Contains("Explore.API.Controllers.RegistrationFormFieldCreateInput")).IsTrue();
        await Assert.That(discovered.Contains("Explore.Application.DTOs.RegistrationForms.RegistrationFormTemplateInputDto")).IsTrue();

        var method = typeof(BindingCharacterizationController).GetMethod(nameof(BindingCharacterizationController.Post))!;
        var parameters = method.GetParameters().ToDictionary(parameter => parameter.Name!, StringComparer.Ordinal);

        await Assert.That(IsBodyParameter(typeof(BindingCharacterizationController), parameters["body"])).IsTrue();
        await Assert.That(IsBodyParameter(typeof(BindingCharacterizationController), parameters["route"])).IsFalse();
        await Assert.That(IsBodyParameter(typeof(BindingCharacterizationController), parameters["query"])).IsFalse();
        await Assert.That(IsBodyParameter(typeof(BindingCharacterizationController), parameters["form"])).IsFalse();
        await Assert.That(IsBodyParameter(typeof(BindingCharacterizationController), parameters["header"])).IsFalse();
        await Assert.That(IsBodyParameter(typeof(BindingCharacterizationController), parameters["service"])).IsFalse();
        await Assert.That(IsBodyParameter(typeof(BindingCharacterizationController), parameters["cancellationToken"])).IsFalse();
    }

    [Test]
    public async Task BaselineSchemaRejectsMalformedStaleDuplicateUnsortedAndDriftedEntries()
    {
        const string fields = "\"category\":\"nominal-record-candidate\",\"reason\":\"Concrete semantic reason.\",\"owner\":\"Application\",\"removalTrigger\":\"Convert after mapping coverage exists.\"";
        var failures = new List<string>();

        failures.AddRange(ParseBaseline("[{\"fullyQualifiedType\":\"Missing.Fields\"}]", BaselineKind.Class).Failures);
        failures.AddRange(ParseBaseline("[{\"fullyQualifiedType\":\"B.Type\"," + fields + "},{\"fullyQualifiedType\":\"A.Type\"," + fields + "}]", BaselineKind.Class).Failures);
        failures.AddRange(ParseBaseline("[{\"fullyQualifiedType\":\"A.Type\"," + fields + "},{\"fullyQualifiedType\":\"A.Type\"," + fields + "}]", BaselineKind.Class).Failures);
        failures.AddRange(ParseBaseline("""[{"fullyQualifiedType":"Unknown.Category","category":"invented","reason":"reason","owner":"owner","removalTrigger":"trigger"}]""", BaselineKind.Class).Failures);
        failures.AddRange(ParseBaseline("""[{"fullyQualifiedType":"Blank.Fields","category":"retained-class","reason":" ","owner":"","removalTrigger":" "}]""", BaselineKind.Class).Failures);

        var semanticEntries = new[]
        {
            Entry("obj/Generated.Dto"),
            Entry(typeof(Explore.Application.DTOs.RecordContractCharacterization.GeneratedContract).FullName!),
            Entry(typeof(NativeRegistrationFormDefinitionDto).FullName!),
            Entry("Explore.Application.DTOs.RemovedDto"),
        };
        failures.AddRange(ValidateClassEntries(semanticEntries, []));
        failures.AddRange(CompareExactBaseline(
            ["Current.UnlistedDto"],
            [Entry("Explore.Application.DTOs.RemovedDto")],
            "class contract"));

        await Assert.That(HasFailure(failures, "missing required")).IsTrue();
        await Assert.That(HasFailure(failures, "sorted")).IsTrue();
        await Assert.That(HasFailure(failures, "duplicate")).IsTrue();
        await Assert.That(HasFailure(failures, "unknown category")).IsTrue();
        await Assert.That(HasFailure(failures, "blank")).IsTrue();
        await Assert.That(HasFailure(failures, "build-output")).IsTrue();
        await Assert.That(HasFailure(failures, "generated")).IsTrue();
        await Assert.That(HasFailure(failures, "is now a record")).IsTrue();
        await Assert.That(HasFailure(failures, "stale")).IsTrue();
        await Assert.That(HasFailure(failures, "missing from the baseline")).IsTrue();
    }

    [Test]
    public async Task ConcreteMediatRClassRequestsMatchTheReasonedBaseline()
    {
        var current = DiscoverConcreteMediatRClassRequests();
        var allCurrent = DiscoverAllClassDebt();
        var parsed = ReadBaseline(ClassBaselinePath, BaselineKind.Class);
        var relevantEntries = EntriesClassifiedAs(parsed.Entries, ContractClassification.ConcreteMediatRClassRequest);
        var failures = parsed.Failures
            .Concat(ValidateClassEntries(parsed.Entries, allCurrent))
            .Concat(CompareExactBaseline(current, relevantEntries, "concrete MediatR class request"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        ReportDebt("concrete MediatR class requests", failures);
        await Assert.That(failures).IsEmpty()
            .Because("every compiled concrete MediatR class request must have a reasoned shrinking-baseline disposition");
    }

    [Test]
    public async Task EveryConcreteCompiledApplicationMediatRRequestIsARecord()
    {
        var classRequests = DiscoverConcreteCompiledMediatRRequests()
            .Where(type => !IsRecord(type))
            .Select(GetTypeName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        ReportRequestRecordDebt(classRequests);
        await Assert.That(classRequests).IsEmpty()
            .Because("every concrete compiled IRequest and IRequest<T> in Explore.Application must use record semantics independently of the shrinking class baseline");
    }

    [Test]
    public async Task BaseCommandResponseHierarchyIsExactlyTwelveImmutableDirectRecordDescendants()
    {
        var descendants = DiscoverConcreteBaseCommandResponseDescendants();
        var descendantNames = descendants
            .Select(GetTypeName)
            .ToArray();
        var nonRecords = descendants
            .Where(type => !IsRecord(type))
            .Select(GetTypeName)
            .ToArray();
        var indirectDescendants = descendants
            .Where(type => type.BaseType is not { IsGenericType: true } baseType
                || baseType.GetGenericTypeDefinition() != typeof(BaseCommandResponse<>))
            .Select(GetTypeName)
            .ToArray();
        var publicMutationDebt = descendants
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(property => property.SetMethod?.IsPublic == true)
            .Select(property => $"{GetTypeName(property.DeclaringType!)}.{property.Name}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var publicConstructionDebt = descendants
            .Where(type => type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length != 0)
            .Select(GetTypeName)
            .ToArray();

        await Assert.That(IsRecord(typeof(BaseCommandResponse<>))).IsTrue();
        await Assert.That(descendantNames).Count().IsEqualTo(13);
        await Assert.That(descendantNames).IsEquivalentTo(ExpectedBaseCommandResponseDescendants);
        await Assert.That(nonRecords).IsEmpty();
        await Assert.That(indirectDescendants).IsEmpty();
        await Assert.That(publicMutationDebt).IsEmpty();
        await Assert.That(publicConstructionDebt).IsEmpty();
    }

    [Test]
    public async Task EveryCompiledApplicationContractClassIsARecord()
    {
        var classContracts = DiscoverCompiledApplicationContractClasses()
            .Where(type => !IsRecord(type))
            .Select(GetTypeName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        ReportApplicationContractRecordDebt(classContracts);
        await Assert.That(classContracts).IsEmpty()
            .Because("every compiled Application-owned DTO contract must use record semantics independently of the shrinking class baseline");
    }

    [Test]
    public async Task PaymentProjectionRecordsExposeNoPostConstructionSetters()
    {
        Type[] paymentProjectionTypes =
        [
            typeof(RegistrationOrderDto),
            typeof(RegistrationPaymentDto),
        ];

        string[] mutableProperties = paymentProjectionTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(property => property.SetMethod?.IsPublic == true
                && !property.SetMethod.ReturnParameter
                    .GetRequiredCustomModifiers()
                    .Contains(typeof(IsExternalInit)))
            .Select(property => $"{property.DeclaringType!.FullName}.{property.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(mutableProperties).IsEmpty()
            .Because("payment and refund projections are authoritative immutable snapshots after construction");
    }

    [Test]
    public async Task FinalClassBaselineIsEmptyAndBodyBaselineContainsOnlyLegitimateTargets()
    {
        var classBaseline = ReadBaseline(ClassBaselinePath, BaselineKind.Class);
        var bodyBaseline = ReadBaseline(BodyBaselinePath, BaselineKind.Body);

        await Assert.That(classBaseline.Failures).IsEmpty();
        await Assert.That(bodyBaseline.Failures).IsEmpty();
        await Assert.That(classBaseline.Entries).IsEmpty();
        await Assert.That(bodyBaseline.Entries).Count().IsEqualTo(10);
        await Assert.That(bodyBaseline.Entries.All(entry => entry.Category == "legitimate-target")).IsTrue();
        await Assert.That(DiscoverConcreteMediatRClassRequests()).IsEmpty();
    }

    [Test]
    public async Task HandwrittenApplicationClassDtosMatchTheReasonedBaseline()
    {
        var current = DiscoverHandwrittenApplicationClassDtos();
        var allCurrent = DiscoverAllClassDebt();
        var parsed = ReadBaseline(ClassBaselinePath, BaselineKind.Class);
        var relevantEntries = EntriesClassifiedAs(parsed.Entries, ContractClassification.HandwrittenApplicationClassDto);
        var failures = parsed.Failures
            .Concat(ValidateClassEntries(parsed.Entries, allCurrent))
            .Concat(CompareExactBaseline(current, relevantEntries, "handwritten Application class DTO"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        ReportDebt("handwritten Application class DTOs", failures);
        await Assert.That(failures).IsEmpty()
            .Because("every handwritten Application class DTO must have a reasoned shrinking-baseline disposition");
    }

    [Test]
    public async Task HttpBodyAuthorityMembersMatchTheReasonedDispositionBaseline()
    {
        var current = DiscoverHttpBodyAuthorityMembers();
        var parsed = ReadBaseline(BodyBaselinePath, BaselineKind.Body);
        var failures = parsed.Failures
            .Concat(ValidateBodyEntries(parsed.Entries, current))
            .Concat(CompareExactBaseline(current, parsed.Entries, "HTTP body authority-like member"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        ReportDebt("HTTP body authority-like members", failures);
        await Assert.That(failures).IsEmpty()
            .Because("every UserId/TenantId-shaped member on an explicit or [ApiController]-inferred body contract needs a current-authority or legitimate-target disposition");
    }

    private static BaselineEntry Entry(string symbol) => new(
        symbol,
        "retained-class",
        "reason",
        "owner",
        "trigger");

    private static bool HasFailure(IEnumerable<string> failures, string text) =>
        failures.Any(failure => failure.Contains(text, StringComparison.Ordinal));

    private static string[] DiscoverAllClassDebt() => DiscoverConcreteMediatRClassRequests()
        .Concat(DiscoverHandwrittenApplicationClassDtos())
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static Type[] DiscoverConcreteCompiledMediatRRequests() => ApplicationAssembly
        .GetTypes()
        .Where(IsCompiledApplicationRequest)
        .OrderBy(GetTypeName, StringComparer.Ordinal)
        .ToArray();

    private static string[] DiscoverConcreteMediatRClassRequests() => DiscoverConcreteCompiledMediatRRequests()
        .Where(type => !IsRecord(type))
        .Select(GetTypeName)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static bool IsCompiledApplicationRequest(Type type) =>
        type.Assembly == ApplicationAssembly
        && type is { IsClass: true, IsAbstract: false }
        && typeof(IBaseRequest).IsAssignableFrom(type)
        && !IsGenerated(type)
        && !IsTestFixture(type);

    private static string[] DiscoverHandwrittenApplicationClassDtos() => ApplicationAssembly
        .GetTypes()
        .Where(type => Classify(type) == ContractClassification.HandwrittenApplicationClassDto)
        .Select(GetTypeName)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static Type[] DiscoverCompiledApplicationContractClasses() => ApplicationAssembly
        .GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false })
        .Where(IsApplicationContractOwned)
        .Where(type => !IsGenerated(type) && !IsValidator(type) && !IsMutableEditState(type) && !IsTestFixture(type))
        .OrderBy(GetTypeName, StringComparer.Ordinal)
        .ToArray();

    private static Type[] DiscoverConcreteBaseCommandResponseDescendants() => ApplicationAssembly
        .GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
        .Where(DerivesFromBaseCommandResponse)
        .OrderBy(GetTypeName, StringComparer.Ordinal)
        .ToArray();

    private static bool DerivesFromBaseCommandResponse(Type type) => EnumerateTypeHierarchy(type)
        .Any(current => current.IsGenericType
            && current.GetGenericTypeDefinition() == typeof(BaseCommandResponse<>));

    private static string[] DiscoverHttpBodyAuthorityMembers() => DiscoverHttpBodyContractTypes()
        .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        .Where(property => IsAuthorityLikeName(property.Name))
        .Select(property => $"{GetTypeName(property.DeclaringType!)}.{property.Name}")
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static Type[] DiscoverHttpBodyContractTypes() => ApiAssembly
        .GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false })
        .SelectMany(controller => controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(IsHttpAction)
            .SelectMany(method => method.GetParameters().Select(parameter => (Controller: controller, Parameter: parameter))))
        .Where(candidate => IsBodyParameter(candidate.Controller, candidate.Parameter))
        .Select(candidate => UnwrapBodyType(candidate.Parameter.ParameterType))
        .Distinct()
        .OrderBy(GetTypeName, StringComparer.Ordinal)
        .ToArray();

    private static ContractClassification Classify(Type type)
    {
        if (IsGenerated(type))
            return ContractClassification.Generated;
        if (IsValidator(type))
            return ContractClassification.Validator;
        if (IsEntity(type))
            return ContractClassification.Entity;
        if (IsMutableEditState(type))
            return ContractClassification.MutableEditState;
        if (IsTestFixture(type))
            return ContractClassification.TestFixture;
        if (IsRecord(type))
            return ContractClassification.Record;
        if (type is { IsClass: true, IsAbstract: false } && typeof(IBaseRequest).IsAssignableFrom(type))
            return ContractClassification.ConcreteMediatRClassRequest;
        if (type is { IsClass: true, IsAbstract: false } && IsApplicationContractOwned(type))
            return ContractClassification.HandwrittenApplicationClassDto;

        return ContractClassification.OutOfScope;
    }

    private static bool IsApplicationContractOwned(Type type) =>
        type.Namespace?.StartsWith("Explore.Application.DTOs", StringComparison.Ordinal) == true
        || (type.Namespace?.StartsWith("Explore.Application.Features", StringComparison.Ordinal) == true
            && type.Name.EndsWith("Dto", StringComparison.Ordinal));

    private static bool IsRecord(Type type) => type.IsClass
        && type.GetProperty("EqualityContract", BindingFlags.Instance | BindingFlags.NonPublic) is not null;

    private static bool IsGenerated(Type type) =>
        type.Name.Contains('<')
        || (type.DeclaringType is not null && IsGenerated(type.DeclaringType))
        || type.GetCustomAttribute<GeneratedCodeAttribute>() is not null
        || type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null;

    private static bool IsValidator(Type type) =>
        (type.DeclaringType is not null && IsValidator(type.DeclaringType))
        || EnumerateTypeHierarchy(type)
            .Any(current => current.IsGenericType
                && current.GetGenericTypeDefinition().FullName == "FluentValidation.AbstractValidator`1");

    private static bool IsEntity(Type type) => type.Assembly == DomainAssembly
        && type.GetInterfaces().Any(contract => contract.Name is
            "ITenantEntity" or "IAuditableEntity" or "ISoftDeletable" or "IConcurrencyAware");

    private static bool IsMutableEditState(Type type) =>
        type.Name.EndsWith("EditState", StringComparison.Ordinal)
        || (type.Namespace?.Contains(".EditState", StringComparison.Ordinal) ?? false);

    private static bool IsTestFixture(Type type) =>
        type.Assembly.GetName().Name?.EndsWith(".Tests", StringComparison.Ordinal) == true;

    private static IEnumerable<Type> EnumerateTypeHierarchy(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            yield return current;
    }

    private static bool IsHttpAction(MethodInfo method) => method
        .GetCustomAttributes(inherit: true)
        .OfType<IActionHttpMethodProvider>()
        .Any();

    /// <summary>
    /// Mirrors the deterministic part of ASP.NET Core's ApiController binding inference. Explicit MVC
    /// binding metadata always wins; otherwise a complex action parameter is body-bound. Route/query/form,
    /// header, service, and cancellation parameters are therefore excluded by meaning rather than by name.
    /// </summary>
    private static bool IsBodyParameter(Type controller, ParameterInfo parameter)
    {
        var source = parameter.GetCustomAttributes(inherit: true)
            .OfType<IBindingSourceMetadata>()
            .Select(metadata => metadata.BindingSource)
            .FirstOrDefault(bindingSource => bindingSource is not null);

        if (source is not null)
            return string.Equals(source.Id, BindingSource.Body.Id, StringComparison.Ordinal);

        return controller.GetCustomAttribute<ApiControllerAttribute>(inherit: true) is not null
            && IsComplexContractType(parameter.ParameterType);
    }

    private static bool IsComplexContractType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type != typeof(string)
            && type != typeof(decimal)
            && type != typeof(DateTime)
            && type != typeof(DateTimeOffset)
            && type != typeof(DateOnly)
            && type != typeof(TimeOnly)
            && type != typeof(Guid)
            && type != typeof(Uri)
            && type != typeof(CancellationToken)
            && !type.IsPrimitive
            && !type.IsEnum;
    }

    private static Type UnwrapBodyType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsArray)
            return type.GetElementType()!;
        if (type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            return type.GetGenericArguments()[0];
        return type;
    }

    private static bool IsAuthorityLikeName(string name) =>
        name.EndsWith("UserId", StringComparison.Ordinal)
        || name.EndsWith("TenantId", StringComparison.Ordinal);

    private static BaselineEntry[] EntriesClassifiedAs(
        IEnumerable<BaselineEntry> entries,
        ContractClassification classification) => entries
        .Where(entry => entry.Symbol is not null && ResolveType(entry.Symbol) is { } type && Classify(type) == classification)
        .ToArray();

    private static ParsedBaseline ReadBaseline(string relativePath, BaselineKind kind)
    {
        var path = Path.Combine(FindRepositoryRoot(), relativePath);
        return ParseBaseline(File.ReadAllText(path), kind);
    }

    private static ParsedBaseline ParseBaseline(string json, BaselineKind kind)
    {
        var failures = new List<string>();
        var entries = new List<BaselineEntry>();
        var symbolField = kind == BaselineKind.Class ? "fullyQualifiedType" : "fullyQualifiedMember";
        var allowedCategories = kind == BaselineKind.Class ? ClassCategories : BodyCategories;
        var requiredFields = new[] { symbolField, "category", "reason", "owner", "removalTrigger" };

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return new ParsedBaseline([], ["baseline root must be a JSON array"]);

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    failures.Add("baseline entries must be JSON objects");
                    continue;
                }

                var values = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!requiredFields.Contains(property.Name, StringComparer.Ordinal))
                        failures.Add($"baseline entry has unknown field '{property.Name}'");
                    values[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                }

                foreach (var field in requiredFields.Where(field => !values.ContainsKey(field)))
                    failures.Add($"baseline entry is missing required field '{field}'");

                var entry = new BaselineEntry(
                    values.GetValueOrDefault(symbolField),
                    values.GetValueOrDefault("category"),
                    values.GetValueOrDefault("reason"),
                    values.GetValueOrDefault("owner"),
                    values.GetValueOrDefault("removalTrigger"));
                entries.Add(entry);

                foreach (var (field, value) in new[]
                {
                    (symbolField, entry.Symbol),
                    ("category", entry.Category),
                    ("reason", entry.Reason),
                    ("owner", entry.Owner),
                    ("removalTrigger", entry.RemovalTrigger),
                })
                {
                    if (string.IsNullOrWhiteSpace(value))
                        failures.Add($"baseline field '{field}' must not be blank");
                }

                if (!string.IsNullOrWhiteSpace(entry.Category) && !allowedCategories.Contains(entry.Category))
                    failures.Add($"baseline entry '{entry.Symbol}' has unknown category '{entry.Category}'");
            }
        }
        catch (JsonException exception)
        {
            failures.Add($"baseline is not valid JSON: {exception.Message}");
        }

        var symbols = entries.Select(entry => entry.Symbol ?? string.Empty).ToArray();
        foreach (var duplicate in symbols.GroupBy(symbol => symbol, StringComparer.Ordinal).Where(group => group.Count() > 1))
            failures.Add($"baseline contains duplicate entry '{duplicate.Key}'");
        if (!symbols.SequenceEqual(symbols.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            failures.Add("baseline entries must be sorted by fully qualified symbol using ordinal order");

        return new ParsedBaseline(entries, failures);
    }

    private static IEnumerable<string> ValidateClassEntries(
        IReadOnlyCollection<BaselineEntry> entries,
        IReadOnlyCollection<string> current)
    {
        foreach (var entry in entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Symbol)))
        {
            if (LooksLikeBuildOutput(entry.Symbol!))
            {
                yield return $"class baseline entry '{entry.Symbol}' is a generated/build-output entry";
                continue;
            }

            var type = ResolveType(entry.Symbol!);
            if (type is null)
            {
                yield return $"class baseline entry '{entry.Symbol}' is stale because the type no longer exists";
                continue;
            }

            var classification = Classify(type);
            if (classification == ContractClassification.Record)
                yield return $"class baseline entry '{entry.Symbol}' is now a record; remove the resolved class debt";
            else if (classification == ContractClassification.Generated)
                yield return $"class baseline entry '{entry.Symbol}' is generated and must use the explicit generated exclusion";
            else if (classification is ContractClassification.Validator or ContractClassification.Entity or ContractClassification.MutableEditState or ContractClassification.TestFixture)
                yield return $"class baseline entry '{entry.Symbol}' is explicitly excluded as {classification}";
            else if (!current.Contains(entry.Symbol!, StringComparer.Ordinal))
                yield return $"class baseline entry '{entry.Symbol}' is stale because it is no longer discovered as current class debt";
        }
    }

    private static IEnumerable<string> ValidateBodyEntries(
        IReadOnlyCollection<BaselineEntry> entries,
        IReadOnlyCollection<string> current)
    {
        foreach (var entry in entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Symbol)))
        {
            if (LooksLikeBuildOutput(entry.Symbol!))
                yield return $"body baseline entry '{entry.Symbol}' is a generated/build-output entry";
            else if (!current.Contains(entry.Symbol!, StringComparer.Ordinal))
                yield return $"body baseline entry '{entry.Symbol}' is stale because the member is no longer an API body authority candidate";
        }
    }

    private static IEnumerable<string> CompareExactBaseline(
        IReadOnlyCollection<string> current,
        IReadOnlyCollection<BaselineEntry> baseline,
        string debtName)
    {
        var listed = baseline.Select(entry => entry.Symbol).Where(symbol => symbol is not null).ToHashSet(StringComparer.Ordinal);
        var discovered = current.ToHashSet(StringComparer.Ordinal);

        foreach (var missing in discovered.Except(listed).Order(StringComparer.Ordinal))
            yield return $"{debtName} '{missing}' is missing from the baseline";
        foreach (var stale in listed.Except(discovered).Order(StringComparer.Ordinal))
            yield return $"{debtName} baseline entry '{stale}' is stale and must be removed";
    }

    private static bool LooksLikeBuildOutput(string symbol) =>
        symbol.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
        || symbol.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
        || symbol.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
        || symbol.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
        || symbol.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)
        || symbol.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase);

    private static Type? ResolveType(string fullyQualifiedName) => new[]
        {
            ApplicationAssembly,
            ApiAssembly,
            DomainAssembly,
            typeof(RecordContractArchitectureTests).Assembly,
        }
        .Select(assembly => assembly.GetType(fullyQualifiedName, throwOnError: false, ignoreCase: false))
        .FirstOrDefault(type => type is not null);

    private static string GetTypeName(Type type) => type.FullName
        ?? throw new InvalidOperationException($"Type '{type}' has no fully qualified name.");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root from the architecture-test output directory.");
    }

    private static void ReportDebt(string name, string[] failures)
    {
        if (failures.Length == 0)
            return;

        Console.WriteLine($"Record adoption RED - {name} ({failures.Length}):");
        foreach (var failure in failures)
            Console.WriteLine($"  - {failure}");
    }

    private static void ReportRequestRecordDebt(string[] classRequests)
    {
        if (classRequests.Length == 0)
            return;

        Console.WriteLine($"Record adoption RED - concrete compiled Application MediatR requests that remain classes ({classRequests.Length}):");
        foreach (var classRequest in classRequests)
            Console.WriteLine($"  - {classRequest}");
    }

    private static void ReportApplicationContractRecordDebt(string[] classContracts)
    {
        if (classContracts.Length == 0)
            return;

        Console.WriteLine($"Record adoption RED - non-retained compiled Application contract classes that remain classes ({classContracts.Length}):");
        foreach (var classContract in classContracts)
            Console.WriteLine($"  - {classContract}");
    }

    private enum BaselineKind { Class, Body }

    private enum ContractClassification
    {
        Record,
        ConcreteMediatRClassRequest,
        HandwrittenApplicationClassDto,
        Generated,
        Validator,
        Entity,
        MutableEditState,
        TestFixture,
        OutOfScope,
    }

    private sealed record BaselineEntry(
        string? Symbol,
        string? Category,
        string? Reason,
        string? Owner,
        string? RemovalTrigger);

    private sealed record ParsedBaseline(
        IReadOnlyList<BaselineEntry> Entries,
        IReadOnlyList<string> Failures);

    [ApiController]
    private sealed class BindingCharacterizationController : ControllerBase
    {
        [HttpPost("{route}")]
        public OkResult Post(
            [FromRoute] SyntheticBindingContract route,
            [FromQuery] SyntheticBindingContract query,
            [FromForm] SyntheticBindingContract form,
            [FromHeader] SyntheticBindingContract header,
            [FromServices] SyntheticBindingContract service,
            SyntheticBindingContract body,
            CancellationToken cancellationToken) => Ok();
    }

    private sealed class SyntheticBindingContract;

    private sealed class SyntheticTestFixtureRequest : IRequest;
}
}

namespace Explore.Application.DTOs.RecordContractCharacterization
{
    [System.CodeDom.Compiler.GeneratedCode("RecordContractArchitectureTests", "1.0")]
    internal sealed class GeneratedContract;

    [System.CodeDom.Compiler.GeneratedCode("RecordContractArchitectureTests", "1.0")]
    internal sealed class GeneratedRequest : MediatR.IRequest;

    [System.Runtime.CompilerServices.CompilerGenerated]
    internal sealed class CompilerGeneratedRequest : MediatR.IRequest;

    internal sealed class MutableContractEditState;

    internal sealed class ContractTestFixture;
}
