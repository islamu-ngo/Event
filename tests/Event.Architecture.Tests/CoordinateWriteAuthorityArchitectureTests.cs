using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.Federation.Atproto.Models;
using MediatR;

namespace Event.Architecture.Tests;

public sealed class CoordinateWriteAuthorityArchitectureTests
{
    private const string GeneratedClientNamespace = "Explore.Blazor.Client.Clients";
    private const string GeneratedCodeMarker = "[System.CodeDom.Compiler.GeneratedCode(";
    private const string GeneratedTagClientsPath = "src/Explore.Blazor.Client/Clients/EventApiTagClients.g.cs";

    private static readonly Assembly ApplicationAssembly = typeof(CreateLocationDto).Assembly;
    private static readonly Lazy<GeneratedClientModel> GeneratedClient = new(CreateGeneratedClientModel);

    private static readonly ImmutableArray<string> ExpectedGeneratedRequestRoots =
    [
        "Explore.Blazor.Client.Clients.CreateEventDraftRequestDto",
        "Explore.Blazor.Client.Clients.CreateLocationDto",
        "Explore.Blazor.Client.Clients.UpdateLocationDto"
    ];

    private static readonly ImmutableArray<string> ExpectedGeneratedRequestGraph =
    [
        "Explore.Blazor.Client.Clients.CreateEventLocationDto"
    ];

    private static readonly Type[] AuthorizedEventLocationDisclosureTypes =
    [
        typeof(EventLocationPublicFieldsDto),
        typeof(EventLocationAttendeeFieldsDto),
        typeof(EventLocationManagementFieldsDto)
    ];

    private static readonly Type[] AuthorizedCoordinateReadTypes =
    [
        .. AuthorizedEventLocationDisclosureTypes,
        typeof(LocationDto),
        typeof(EventLocationDisclosureValues),
        typeof(AtprotoEventLocationSnapshot)
    ];

    private static readonly Type[] PermittedCoordinateTypes =
    [
        .. AuthorizedCoordinateReadTypes,
        typeof(CreateEventLocationDto),
        typeof(CreateEventDraftRequestDto),
        typeof(CreateLocationDto),
        typeof(UpdateLocationDto)
    ];

    private static readonly string[] WriteNamePrefixes =
    [
        "Abandon",
        "Add",
        "Apply",
        "Archive",
        "Assign",
        "Bootstrap",
        "Cancel",
        "Clone",
        "Complete",
        "Configure",
        "Confirm",
        "Create",
        "Decide",
        "Delete",
        "Drain",
        "Execute",
        "Import",
        "Manage",
        "Open",
        "Patch",
        "Pause",
        "Propose",
        "Publish",
        "Purge",
        "Rebuild",
        "Reconcile",
        "Record",
        "Redrive",
        "Remove",
        "Repair",
        "Resume",
        "Review",
        "Rotate",
        "Save",
        "Schedule",
        "Send",
        "Set",
        "Submit",
        "Subscribe",
        "Sync",
        "Transition",
        "Triage",
        "Unsubscribe",
        "Update",
        "Withdraw"
    ];

    [Test]
    public async Task AuthorizedCoordinateReadsAreCompleteAndExcludedFromWriteDiscovery()
    {
        string[] disclosureCoordinates = AuthorizedEventLocationDisclosureTypes
            .SelectMany(FindRawCoordinateMembers)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] coordinateContractTypes = DiscoverApplicationCoordinateContractTypes()
            .Select(GetTypeName)
            .ToArray();
        Type[] applicationWriteRoots = DiscoverUntrustedApplicationWriteContractRoots();
        string[] writeRootTypes = applicationWriteRoots
            .Select(GetTypeName)
            .ToArray();
        string[] writeGraphTypes = DiscoverReachableContractTypes(applicationWriteRoots)
            .Select(GetTypeName)
            .ToArray();
        GeneratedClientContract[] generatedWriteRoots = DiscoverGeneratedClientRequestRoots();
        string[] generatedWriteRootTypes = generatedWriteRoots
            .Select(GetGeneratedTypeName)
            .ToArray();
        string[] generatedWriteGraphTypes = DiscoverReachableGeneratedContractTypes(generatedWriteRoots)
            .Select(GetGeneratedTypeName)
            .ToArray();
        string[] authorizedTypeNames = AuthorizedCoordinateReadTypes
            .Select(GetTypeName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(disclosureCoordinates).IsEquivalentTo(
        [
            $"{GetTypeName(typeof(EventLocationAttendeeFieldsDto))}.Latitude",
            $"{GetTypeName(typeof(EventLocationAttendeeFieldsDto))}.Longitude",
            $"{GetTypeName(typeof(EventLocationManagementFieldsDto))}.Latitude",
            $"{GetTypeName(typeof(EventLocationManagementFieldsDto))}.Longitude",
            $"{GetTypeName(typeof(EventLocationPublicFieldsDto))}.Latitude",
            $"{GetTypeName(typeof(EventLocationPublicFieldsDto))}.Longitude"
        ]);
        await Assert.That(authorizedTypeNames.Except(coordinateContractTypes, StringComparer.Ordinal)).IsEmpty();
        await Assert.That(writeRootTypes.Intersect(authorizedTypeNames, StringComparer.Ordinal)).IsEmpty();
        await Assert.That(writeGraphTypes.Intersect(authorizedTypeNames, StringComparer.Ordinal)).IsEmpty();
        await Assert.That(generatedWriteRootTypes.Intersect(authorizedTypeNames, StringComparer.Ordinal)).IsEmpty();
        await Assert.That(generatedWriteGraphTypes.Intersect(authorizedTypeNames, StringComparer.Ordinal)).IsEmpty();
    }

    [Test]
    public async Task GeneratedClientRequestDiscoveryUsesApiMethodInputsAndTraversesNestedSchemas()
    {
        GeneratedClientContract[] generatedRoots = DiscoverGeneratedClientRequestRoots();
        string[] generatedRequestRoots = generatedRoots
            .Select(GetGeneratedTypeName)
            .ToArray();
        string[] generatedRequestGraph = DiscoverReachableGeneratedContractTypes(generatedRoots)
            .Select(GetGeneratedTypeName)
            .ToArray();

        await Assert.That(ExpectedGeneratedRequestRoots.Except(generatedRequestRoots, StringComparer.Ordinal)).IsEmpty();
        await Assert.That(ExpectedGeneratedRequestGraph.Except(generatedRequestGraph, StringComparer.Ordinal)).IsEmpty();
    }

    [Test]
    public async Task UntrustedAndGeneratedWriteContractsMustNotExposeRawCoordinates()
    {
        string[] violations = DiscoverRawCoordinateWriteMembers(
            DiscoverUntrustedApplicationWriteContractRoots())
            .Concat(DiscoverGeneratedRawCoordinateWriteMembers(DiscoverGeneratedClientRequestRoots()))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        ReportCoordinateWriteDebt(violations);
        await Assert.That(violations).IsEmpty()
            .Because(
                "raw latitude/longitude members let browser, AI, or generated input manufacture coordinate authority; "
                + "use governed manual address data or a protected provider selection instead. Violations:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations));
    }

    [Test]
    public async Task ScannerDetectsPartialPairsAliasesAndGeneratedRequestsWithoutReadOnlyFalsePositives()
    {
        Type[] candidates =
        [
            typeof(PartialCoordinateWritePayload),
            typeof(AliasedCoordinateWriteRequest),
            typeof(GeneratedCodeCoordinateContractFixture),
            typeof(CompilerGeneratedCoordinateContractFixture),
            typeof(ReadOnlyCoordinateWriteInput)
        ];
        Type[] writeRoots = candidates
            .Where(type => IsUntrustedApplicationWriteContractRoot(type, requireApplicationAssembly: false))
            .OrderBy(GetTypeName, StringComparer.Ordinal)
            .ToArray();
        string[] violations = DiscoverRawCoordinateWriteMembers(writeRoots);

        await Assert.That(writeRoots.Select(GetTypeName)).IsEquivalentTo(candidates.Select(GetTypeName));
        await Assert.That(violations).IsEquivalentTo(
        [
            $"{GetTypeName(typeof(AliasedCoordinateWriteRequest))}.GeneratedValue [json:longitude]",
            $"{GetTypeName(typeof(CompilerGeneratedCoordinateContractFixture))}.Longitude",
            $"{GetTypeName(typeof(GeneratedCodeCoordinateContractFixture))}.Latitude",
            $"{GetTypeName(typeof(PartialCoordinateWritePayload))}.Latitude"
        ]);
    }

    private static Type[] DiscoverUntrustedApplicationWriteContractRoots() => ApplicationAssembly
        .GetTypes()
        .Where(type => IsUntrustedApplicationWriteContractRoot(type, requireApplicationAssembly: true))
        .OrderBy(GetTypeName, StringComparer.Ordinal)
        .ToArray();

    private static GeneratedClientContract[] DiscoverGeneratedClientRequestRoots()
    {
        GeneratedClientModel model = GeneratedClient.Value;

        return model.InterfaceSection
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.EndsWith(");", StringComparison.Ordinal))
            .SelectMany(ExtractMethodParameterTypeIdentifiers)
            .Where(model.Contracts.ContainsKey)
            .Distinct(StringComparer.Ordinal)
            .Select(name => model.Contracts[name])
            .OrderBy(contract => contract.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static GeneratedClientModel CreateGeneratedClientModel()
    {
        string root = ResolveRepositoryRoot();
        string tagSource = File.ReadAllText(Path.Combine(root, GeneratedTagClientsPath));
        GeneratedClientContract[] contracts = ParseGeneratedClientContracts(tagSource);

        return new GeneratedClientModel(
            contracts.ToDictionary(contract => contract.Name, StringComparer.Ordinal),
            ExtractAllGeneratedInterfaceSections(tagSource));
    }

    private static string ExtractAllGeneratedInterfaceSections(string source)
    {
        var sb = new System.Text.StringBuilder();
        int searchIndex = 0;
        const string interfaceMarker = "public partial interface I";

        while ((searchIndex = source.IndexOf(interfaceMarker, searchIndex, StringComparison.Ordinal)) >= 0)
        {
            int sectionEnd = source.IndexOf("public partial class ", searchIndex, StringComparison.Ordinal);
            if (sectionEnd < 0) sectionEnd = source.Length;
            sb.AppendLine(source[searchIndex..sectionEnd]);
            searchIndex = sectionEnd;
        }

        return sb.ToString();
    }

    private static GeneratedClientContract[] ParseGeneratedClientContracts(string source)
    {
        var contracts = new List<GeneratedClientContract>();
        int markerIndex = 0;

        while ((markerIndex = source.IndexOf(GeneratedCodeMarker, markerIndex, StringComparison.Ordinal)) >= 0)
        {
            int declarationStart = source.IndexOf('\n', markerIndex) + 1;
            if (declarationStart == 0)
                break;

            int declarationEnd = source.IndexOf('\n', declarationStart);
            if (declarationEnd < 0)
                declarationEnd = source.Length;

            string declaration = source[declarationStart..declarationEnd].Trim();
            int nextMarker = source.IndexOf(GeneratedCodeMarker, declarationEnd, StringComparison.Ordinal);
            int sectionEnd = nextMarker < 0 ? source.Length : nextMarker;
            markerIndex = sectionEnd;

            const string classDeclaration = "public partial class ";
            if (!declaration.StartsWith(classDeclaration, StringComparison.Ordinal))
                continue;

            string name = declaration[classDeclaration.Length..]
                .Split([' ', ':'], StringSplitOptions.RemoveEmptyEntries)[0];
            GeneratedClientMember[] members = ParseGeneratedClientMembers(
                source[declarationEnd..sectionEnd]);
            contracts.Add(new GeneratedClientContract(name, members));
        }

        return contracts
            .OrderBy(contract => contract.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static GeneratedClientMember[] ParseGeneratedClientMembers(string section)
    {
        var members = new List<GeneratedClientMember>();
        string? jsonName = null;

        foreach (string sourceLine in section.Split('\n'))
        {
            string line = sourceLine.Trim();
            if (line.StartsWith("[System.Text.Json.Serialization.JsonPropertyName(\"", StringComparison.Ordinal))
            {
                int nameStart = line.IndexOf('"') + 1;
                int nameEnd = line.IndexOf('"', nameStart);
                jsonName = nameEnd < 0 ? null : line[nameStart..nameEnd];
                continue;
            }

            if (!line.StartsWith("public ", StringComparison.Ordinal))
                continue;

            int bodyStart = line.IndexOf('{');
            if (bodyStart < 0 || !line.Contains(" get;", StringComparison.Ordinal))
                continue;

            string declaration = line["public ".Length..bodyStart].Trim();
            int memberNameStart = declaration.LastIndexOf(' ');
            if (memberNameStart < 0)
                continue;

            members.Add(new GeneratedClientMember(
                declaration[..memberNameStart],
                declaration[(memberNameStart + 1)..],
                jsonName,
                line.Contains(" set;", StringComparison.Ordinal)
                    || line.Contains(" init;", StringComparison.Ordinal)));
            jsonName = null;
        }

        return members.ToArray();
    }

    private static string ExtractGeneratedTypeSection(string source, string declaration)
    {
        int declarationStart = source.IndexOf(declaration, StringComparison.Ordinal);
        if (declarationStart < 0)
            throw new InvalidOperationException($"Generated client declaration was not found: {declaration}.");

        int sectionEnd = source.IndexOf(GeneratedCodeMarker, declarationStart, StringComparison.Ordinal);
        if (sectionEnd < 0)
            sectionEnd = source.Length;

        return source[declarationStart..sectionEnd];
    }

    private static string[] ExtractMethodParameterTypeIdentifiers(string methodDeclaration)
    {
        int parametersStart = methodDeclaration.IndexOf('(');
        int parametersEnd = methodDeclaration.LastIndexOf(')');
        if (parametersStart < 0 || parametersEnd <= parametersStart)
            return [];

        return SplitTopLevel(methodDeclaration[(parametersStart + 1)..parametersEnd])
            .Select(RemoveParameterNameAndDefault)
            .SelectMany(ExtractTypeIdentifiers)
            .ToArray();
    }

    private static string RemoveParameterNameAndDefault(string parameter)
    {
        int genericDepth = 0;
        int parenthesesDepth = 0;
        int defaultStart = -1;

        for (int index = 0; index < parameter.Length; index++)
        {
            switch (parameter[index])
            {
                case '<': genericDepth++; break;
                case '>': genericDepth--; break;
                case '(': parenthesesDepth++; break;
                case ')': parenthesesDepth--; break;
                case '=' when genericDepth == 0 && parenthesesDepth == 0:
                    defaultStart = index;
                    index = parameter.Length;
                    break;
            }
        }

        string declaration = (defaultStart < 0 ? parameter : parameter[..defaultStart]).Trim();
        int parameterNameStart = declaration.LastIndexOf(' ');
        return parameterNameStart < 0 ? declaration : declaration[..parameterNameStart];
    }

    private static string[] SplitTopLevel(string value)
    {
        var values = new List<string>();
        int genericDepth = 0;
        int parenthesesDepth = 0;
        int itemStart = 0;

        for (int index = 0; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '<': genericDepth++; break;
                case '>': genericDepth--; break;
                case '(': parenthesesDepth++; break;
                case ')': parenthesesDepth--; break;
                case ',' when genericDepth == 0 && parenthesesDepth == 0:
                    values.Add(value[itemStart..index].Trim());
                    itemStart = index + 1;
                    break;
            }
        }

        if (itemStart < value.Length)
            values.Add(value[itemStart..].Trim());

        return values.ToArray();
    }

    private static string[] ExtractTypeIdentifiers(string typeExpression)
    {
        var identifiers = new List<string>();
        int identifierStart = -1;

        for (int index = 0; index <= typeExpression.Length; index++)
        {
            bool isIdentifierCharacter = index < typeExpression.Length
                && (char.IsLetterOrDigit(typeExpression[index]) || typeExpression[index] == '_');
            if (isIdentifierCharacter && identifierStart < 0)
            {
                identifierStart = index;
            }
            else if (!isIdentifierCharacter && identifierStart >= 0)
            {
                identifiers.Add(typeExpression[identifierStart..index]);
                identifierStart = -1;
            }
        }

        return identifiers.ToArray();
    }

    private static GeneratedClientContract[] DiscoverReachableGeneratedContractTypes(
        IEnumerable<GeneratedClientContract> roots)
    {
        Dictionary<string, GeneratedClientContract> contracts = GeneratedClient.Value.Contracts;
        var pending = new Queue<GeneratedClientContract>(roots.OrderBy(contract => contract.Name, StringComparer.Ordinal));
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (pending.TryDequeue(out GeneratedClientContract? contract))
        {
            if (!visited.Add(contract.Name))
                continue;

            foreach (string referencedName in contract.Members
                         .SelectMany(member => ExtractTypeIdentifiers(member.TypeExpression))
                         .Where(contracts.ContainsKey)
                         .Distinct(StringComparer.Ordinal)
                         .Order(StringComparer.Ordinal))
            {
                pending.Enqueue(contracts[referencedName]);
            }
        }

        return visited
            .Select(name => contracts[name])
            .OrderBy(contract => contract.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] DiscoverGeneratedRawCoordinateWriteMembers(
        IEnumerable<GeneratedClientContract> roots) =>
        DiscoverReachableGeneratedContractTypes(roots)
            .SelectMany(contract => contract.Members
                .Where(member => member.IsWritable)
                .Where(member => IsRawCoordinateName(member.Name)
                    || IsRawCoordinateName(member.JsonName))
                .Select(member => member.JsonName is { } jsonName
                    && !string.Equals(member.Name, jsonName, StringComparison.OrdinalIgnoreCase)
                        ? $"{GetGeneratedTypeName(contract)}.{member.Name} [json:{jsonName}]"
                        : $"{GetGeneratedTypeName(contract)}.{member.Name}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static Type[] DiscoverApplicationCoordinateContractTypes() => ApplicationAssembly
        .GetTypes()
        .Where(type => IsApplicationContractShape(type) || IsAuthorizedCoordinateRead(type))
        .Where(type => FindRawCoordinateMembers(type).Length != 0)
        .OrderBy(GetTypeName, StringComparer.Ordinal)
        .ToArray();

    private static bool IsUntrustedApplicationWriteContractRoot(Type type, bool requireApplicationAssembly)
    {
        if (requireApplicationAssembly && type.Assembly != ApplicationAssembly)
            return false;
        if (type is not { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
            return false;
        if (IsAuthorizedCoordinateRead(type))
            return false;

        return IsMachineGeneratedRequest(type)
            || IsWriteContractByConvention(type)
            || (IsApplicationContractShape(type) && FindRawCoordinateMembers(type).Length != 0);
    }

    private static bool IsWriteContractByConvention(Type type)
    {
        if (!IsApplicationContractShape(type))
            return false;

        string name = type.Name;
        string typeNamespace = type.Namespace ?? string.Empty;
        if (typeof(IBaseRequest).IsAssignableFrom(type))
        {
            return typeNamespace.Contains(".Requests.Commands", StringComparison.Ordinal)
                || name.EndsWith("Command", StringComparison.Ordinal)
                || IsGenerated(type);
        }

        return HasContractSuffix(name, "Input")
            || HasContractSuffix(name, "Payload")
            || WriteNamePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool IsApplicationContractShape(Type type)
    {
        string name = type.Name;
        string typeNamespace = type.Namespace ?? string.Empty;

        return typeof(IBaseRequest).IsAssignableFrom(type)
            || typeNamespace.StartsWith("Explore.Application", StringComparison.Ordinal)
                && !typeNamespace.Contains(".Validators", StringComparison.Ordinal)
                && !name.EndsWith("Validator", StringComparison.Ordinal)
                && (HasContractSuffix(name, "Dto")
                    || HasContractSuffix(name, "Request")
                    || HasContractSuffix(name, "Command")
                    || HasContractSuffix(name, "Input")
                    || HasContractSuffix(name, "Payload"))
            || HasContractSuffix(name, "Request")
            || HasContractSuffix(name, "Command")
            || HasContractSuffix(name, "Input")
            || HasContractSuffix(name, "Payload");
    }

    private static bool HasContractSuffix(string name, string suffix) =>
        name.EndsWith(suffix, StringComparison.Ordinal)
        || name.EndsWith($"{suffix}Dto", StringComparison.Ordinal);

    private static bool IsMachineGeneratedRequest(Type type) =>
        typeof(IBaseRequest).IsAssignableFrom(type) && IsGenerated(type);

    private static bool IsGenerated(Type type) =>
        type.GetCustomAttribute<GeneratedCodeAttribute>() is not null
        || type.GetCustomAttribute<CompilerGeneratedAttribute>() is not null;

    private static bool IsAuthorizedCoordinateRead(Type type) =>
        AuthorizedCoordinateReadTypes.Contains(type);

    private static string[] DiscoverRawCoordinateWriteMembers(IEnumerable<Type> roots) =>
        DiscoverReachableContractTypes(roots)
            .SelectMany(FindRawCoordinateMembers)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static Type[] DiscoverReachableContractTypes(IEnumerable<Type> roots)
    {
        var pending = new Queue<Type>(roots.OrderBy(GetTypeName, StringComparer.Ordinal));
        var visited = new HashSet<Type>();

        while (pending.TryDequeue(out Type? candidate))
        {
            Type type = UnwrapContractType(candidate);
            if (IsAuthorizedCoordinateRead(type) || !visited.Add(type))
                continue;

            foreach (Type referencedType in PublicInstanceMembers(type)
                         .Select(GetMemberType)
                         .SelectMany(GetNestedContractTypes)
                         .Where(IsReachableContractType)
                         .Where(type => !IsAuthorizedCoordinateRead(type))
                         .OrderBy(GetTypeName, StringComparer.Ordinal))
            {
                pending.Enqueue(referencedType);
            }
        }

        return visited.OrderBy(GetTypeName, StringComparer.Ordinal).ToArray();
    }

    private static string[] FindRawCoordinateMembers(Type type) => PublicInstanceMembers(type)
        .Where(IsPubliclyWritable)
        .Select(member => new
        {
            Member = member,
            JsonName = member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
        })
        .Where(candidate => IsRawCoordinateName(candidate.Member.Name)
            || IsRawCoordinateName(candidate.JsonName))
        .Select(candidate => candidate.JsonName is { } jsonName
            && !string.Equals(candidate.Member.Name, jsonName, StringComparison.OrdinalIgnoreCase)
                ? $"{GetTypeName(type)}.{candidate.Member.Name} [json:{jsonName}]"
                : $"{GetTypeName(type)}.{candidate.Member.Name}")
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static IEnumerable<MemberInfo> PublicInstanceMembers(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Cast<MemberInfo>()
            .Concat(type.GetFields(BindingFlags.Public | BindingFlags.Instance));

    private static bool IsPubliclyWritable(MemberInfo member) => member switch
    {
        PropertyInfo property => property.SetMethod?.IsPublic == true,
        FieldInfo field => !field.IsInitOnly && !field.IsLiteral,
        _ => false
    };

    private static Type GetMemberType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        _ => throw new ArgumentOutOfRangeException(nameof(member))
    };

    private static IEnumerable<Type> GetNestedContractTypes(Type type)
    {
        Type unwrapped = UnwrapContractType(type);
        yield return unwrapped;

        if (!unwrapped.IsGenericType)
            yield break;

        foreach (Type argument in unwrapped.GetGenericArguments())
        {
            foreach (Type nestedType in GetNestedContractTypes(argument))
                yield return nestedType;
        }
    }

    private static Type UnwrapContractType(Type type)
    {
        Type? nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType is not null)
            return UnwrapContractType(nullableType);

        if (!type.IsArray)
            return type;

        Type? elementType = type.GetElementType();
        if (elementType is null)
            return type;

        return UnwrapContractType(elementType);
    }

    private static bool IsReachableContractType(Type type) =>
        type.Assembly == ApplicationAssembly;

    private static bool IsRawCoordinateName(string? name) =>
        string.Equals(name, "latitude", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "longitude", StringComparison.OrdinalIgnoreCase);

    private static string GetTypeName(Type type) => type.FullName ?? type.Name;

    private static string GetGeneratedTypeName(GeneratedClientContract contract) =>
        $"{GeneratedClientNamespace}.{contract.Name}";

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root containing Explore.slnx.");
    }

    private static void ReportCoordinateWriteDebt(string[] violations)
    {
        if (violations.Length == 0)
            return;

        Console.WriteLine($"Coordinate write authority RED - raw coordinate write members ({violations.Length}):");
        foreach (string violation in violations)
            Console.WriteLine($"  - {violation}");
    }

    private sealed record GeneratedClientModel(
        Dictionary<string, GeneratedClientContract> Contracts,
        string InterfaceSection);

    private sealed record GeneratedClientContract(string Name, GeneratedClientMember[] Members);

    private sealed record GeneratedClientMember(
        string TypeExpression,
        string Name,
        string? JsonName,
        bool IsWritable);

    private sealed class PartialCoordinateWritePayload
    {
        public double? Latitude { get; init; }
    }

    private sealed class AliasedCoordinateWriteRequest
    {
        [JsonPropertyName("longitude")]
        public double? GeneratedValue { get; init; }
    }

    [GeneratedCode("CoordinateWriteAuthorityArchitectureTests", "1.0")]
    private sealed class GeneratedCodeCoordinateContractFixture : IRequest
    {
        public double? Latitude { get; init; }
    }

    [CompilerGenerated]
    private sealed class CompilerGeneratedCoordinateContractFixture : IRequest
    {
        public double? Longitude { get; init; }
    }

    private sealed class ReadOnlyCoordinateWriteInput
    {
        public double? Longitude => 4.3517;
    }
}
