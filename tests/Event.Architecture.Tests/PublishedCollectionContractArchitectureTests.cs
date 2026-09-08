using System.Collections;
using System.CodeDom.Compiler;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Explore.API.Controllers;
using Explore.Application.Authorization;
using Explore.Blazor.Client.Clients;
using Explore.Domain.Interfaces;
using Explore.GeneratedContracts;

namespace Event.Architecture.Tests;

public sealed class PublishedCollectionContractArchitectureTests
{
    private const string BaselinePath =
        "tests/Event.Architecture.Tests/Baselines/published-collection-contract-dispositions.json";
    private const string GeneratedClientPath =
        "src/Explore.Blazor.Client/Clients/EventApiTagClients.g.cs";
    private const string MutableGeneratedContractsPath =
        "eng/tools/Explore.GeneratedContracts/mutable-generated-contracts.txt";
    private const string GeneratedRecordDeclaration =
        "public partial record class ";

    private static readonly Assembly[] ContractAssemblies =
    [
        typeof(ITenantEntity).Assembly,
        typeof(AuthorizeResourceAttribute).Assembly,
        typeof(OrganizationReviewController).Assembly,
        typeof(Explore.Blazor.Client.Extensions.GeneratedEventApiClients).Assembly,
    ];

    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    {
        "framework-owned",
        "generated-contract",
        "immutable-snapshot",
        "intentionally-mutable-owner",
    };

    private static readonly Lazy<HashSet<string>> GeneratedClientRecordNames =
        new(ReadGeneratedClientRecordNames);

    [Test]
    public async Task DispositionSchemaRejectsMalformedDuplicateUnsortedAndStaleEntries()
    {
        const string fields =
            "\"category\":\"immutable-snapshot\",\"reason\":\"Published snapshot.\",\"owner\":\"Application\",\"removalTrigger\":\"Remove when the member is removed.\"";
        var current = DiscoverCollectionProperties();
        var existing = current.First(candidate => !candidate.IsGenerated);
        var failures = new List<string>();

        failures.AddRange(Parse("[{\"fullyQualifiedMember\":\"Missing.Fields\"}]").Failures);
        failures.AddRange(Parse("[{\"fullyQualifiedMember\":\"B.Type.Items\"," + fields
            + "},{\"fullyQualifiedMember\":\"A.Type.Items\"," + fields + "}]").Failures);
        failures.AddRange(Parse("[{\"fullyQualifiedMember\":\"A.Type.Items\"," + fields
            + "},{\"fullyQualifiedMember\":\"A.Type.Items\"," + fields + "}]").Failures);
        failures.AddRange(Parse(
            """[{"fullyQualifiedMember":"Unknown.Category","category":"invented","reason":"reason","owner":"owner","removalTrigger":"trigger"}]""").Failures);
        failures.AddRange(Parse(
            """[{"fullyQualifiedMember":"Blank.Fields","category":"immutable-snapshot","reason":" ","owner":"","removalTrigger":" "}]""").Failures);
        failures.AddRange(ValidateEntries(
            [Entry("Removed.Type.Items"), Entry(existing.Member, category: "generated-contract")],
            current));

        await Assert.That(HasFailure(failures, "missing required")).IsTrue();
        await Assert.That(HasFailure(failures, "sorted")).IsTrue();
        await Assert.That(HasFailure(failures, "duplicate")).IsTrue();
        await Assert.That(HasFailure(failures, "unknown category")).IsTrue();
        await Assert.That(HasFailure(failures, "blank")).IsTrue();
        await Assert.That(HasFailure(failures, "stale")).IsTrue();
        await Assert.That(HasFailure(failures, "not generated")).IsTrue();
        await Assert.That(GeneratedClientRecordNames.Value)
            .IsEquivalentTo(ReadPolicyDerivedGeneratedClientRecordNames());
        await Assert.That(IsGenerated(typeof(ActorDto))).IsTrue();
    }

    [Test]
    public async Task EveryPublishedCollectionContractHasADeterministicOwnershipDisposition()
    {
        var current = DiscoverCollectionProperties();
        var parsed = ReadBaseline();
        var exceptionalMembers = parsed.Entries
            .Select(entry => entry.Member)
            .Where(member => member is not null)
            .ToHashSet(StringComparer.Ordinal);
        var failures = parsed.Failures
            .Concat(ValidateEntries(parsed.Entries, current))
            .Concat(current
                .Where(property => IsMutableCollectionType(property.Property.PropertyType))
                .Where(property => !property.IsGenerated)
                .Where(property => !exceptionalMembers.Contains(property.Member))
                .Select(property => $"mutable published collection member '{property.Member}' has no reasoned exceptional disposition"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Report("published collection ownership dispositions", failures);
        await Assert.That(failures).IsEmpty()
            .Because("immutable/read-only and generated collections are classified automatically while every mutable exception requires an exact reasoned disposition");
    }

    [Test]
    public async Task PublishedImmutableContractsExposeNoUnapprovedMutableCollectionTypes()
    {
        var current = DiscoverCollectionProperties();
        var parsed = ReadBaseline();
        var approvedMutableMembers = parsed.Entries
            .Where(entry => entry.Category is "framework-owned" or "generated-contract" or "intentionally-mutable-owner")
            .Select(entry => entry.Member)
            .ToHashSet(StringComparer.Ordinal);
        var failures = current
            .Where(property => IsMutableCollectionType(property.Property.PropertyType))
            .Where(property => !property.IsGenerated)
            .Where(property => !approvedMutableMembers.Contains(property.Member))
            .Select(property => $"{property.Member} exposes mutable {GetTypeName(property.Property.PropertyType)}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Report("mutable published collection types", failures);
        await Assert.That(failures).IsEmpty()
            .Because("immutable record contracts must expose read-only collection shapes");
    }

    private static CollectionProperty[] DiscoverCollectionProperties() => ContractAssemblies
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => IsPublished(type) && IsRecord(type))
        .SelectMany(type => type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod?.IsPublic == true)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => IsCollectionType(property.PropertyType))
            .Select(property => new CollectionProperty(
                $"{GetTypeName(type)}.{property.Name}",
                property,
                IsGenerated(type))))
        .OrderBy(candidate => candidate.Member, StringComparer.Ordinal)
        .ToArray();

    private static bool IsPublished(Type type) => type.IsPublic || type.IsNestedPublic;

    private static bool IsRecord(Type type)
    {
        if (type.IsClass)
            return type.GetProperty("EqualityContract", BindingFlags.Instance | BindingFlags.NonPublic) is not null;

        var printMembers = type.GetMethod(
            "PrintMembers",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(StringBuilder)],
            modifiers: null);
        return type.IsValueType
            && printMembers?.GetCustomAttribute<CompilerGeneratedAttribute>() is not null;
    }

    private static bool IsGenerated(Type type) =>
        type.GetCustomAttribute<GeneratedCodeAttribute>() is not null
        || type.GetCustomAttribute<CompilerGeneratedAttribute>() is not null
        || (type.Assembly == typeof(Explore.Blazor.Client.Extensions.GeneratedEventApiClients).Assembly
            && type.Namespace == "Explore.Blazor.Client.Clients"
            && GeneratedClientRecordNames.Value.Contains(type.Name));

    private static HashSet<string> ReadGeneratedClientRecordNames()
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            GeneratedClientPath);
        return File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(
                GeneratedRecordDeclaration,
                StringComparison.Ordinal))
            .Select(line => line[GeneratedRecordDeclaration.Length..]
                .Split(
                    [' ', '<'],
                    StringSplitOptions.RemoveEmptyEntries)[0])
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyCollection<string>
        ReadPolicyDerivedGeneratedClientRecordNames()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root,
            GeneratedClientPath));
        HashSet<string> mutableTypes =
            GeneratedContractPolicy.LoadMutableStateTypes(Path.Combine(
                root,
                MutableGeneratedContractsPath));
        return GeneratedContractTransformer.Classify(source, mutableTypes)
            .RecordTypeNames;
    }

    private static bool IsCollectionType(Type type) =>
        type != typeof(string)
        && typeof(IEnumerable).IsAssignableFrom(type);

    private static bool IsMutableCollectionType(Type type)
    {
        if (type.Namespace == "System.Collections.Immutable")
            return false;

        if (type.IsArray || typeof(IList).IsAssignableFrom(type) || typeof(IDictionary).IsAssignableFrom(type))
            return true;

        if (!type.IsGenericType)
            return false;

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(ICollection<>)
            || definition == typeof(IList<>)
            || definition == typeof(ISet<>)
            || definition == typeof(IDictionary<,>)
            || definition == typeof(List<>)
            || definition == typeof(HashSet<>)
            || definition == typeof(Dictionary<,>);
    }

    private static ParsedBaseline ReadBaseline()
    {
        var path = Path.Combine(FindRepositoryRoot(), BaselinePath);
        return Parse(File.ReadAllText(path));
    }

    private static ParsedBaseline Parse(string json)
    {
        var failures = new List<string>();
        var entries = new List<DispositionEntry>();
        string[] requiredFields = ["fullyQualifiedMember", "category", "reason", "owner", "removalTrigger"];

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return new([], ["baseline root must be a JSON array"]);

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in element.EnumerateObject().Where(property =>
                        !requiredFields.Contains(property.Name, StringComparer.Ordinal)))
                    {
                        failures.Add($"baseline entry has unknown field '{property.Name}'");
                    }
                }

                var values = element.ValueKind == JsonValueKind.Object
                    ? element.EnumerateObject().ToDictionary(
                        property => property.Name,
                        property => property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null,
                        StringComparer.Ordinal)
                    : new Dictionary<string, string?>(StringComparer.Ordinal);

                foreach (var field in requiredFields.Where(field => !values.ContainsKey(field)))
                    failures.Add($"baseline entry is missing required field '{field}'");
                foreach (var field in requiredFields.Where(field => string.IsNullOrWhiteSpace(values.GetValueOrDefault(field))))
                    failures.Add($"baseline field '{field}' must not be blank");

                var entry = new DispositionEntry(
                    values.GetValueOrDefault("fullyQualifiedMember"),
                    values.GetValueOrDefault("category"),
                    values.GetValueOrDefault("reason"),
                    values.GetValueOrDefault("owner"),
                    values.GetValueOrDefault("removalTrigger"));
                entries.Add(entry);
                if (!string.IsNullOrWhiteSpace(entry.Category) && !Categories.Contains(entry.Category))
                    failures.Add($"baseline entry '{entry.Member}' has unknown category '{entry.Category}'");
            }
        }
        catch (JsonException exception)
        {
            failures.Add($"baseline is not valid JSON: {exception.Message}");
        }

        var members = entries.Select(entry => entry.Member ?? string.Empty).ToArray();
        foreach (var duplicate in members.GroupBy(member => member, StringComparer.Ordinal).Where(group => group.Count() > 1))
            failures.Add($"baseline contains duplicate entry '{duplicate.Key}'");
        if (!members.SequenceEqual(members.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            failures.Add("baseline entries must be sorted by fully qualified member using ordinal order");

        return new(entries, failures);
    }

    private static IEnumerable<string> ValidateEntries(
        IReadOnlyCollection<DispositionEntry> entries,
        IReadOnlyCollection<CollectionProperty> current)
    {
        var properties = current.ToDictionary(candidate => candidate.Member, StringComparer.Ordinal);
        foreach (var entry in entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Member)))
        {
            if (!properties.TryGetValue(entry.Member!, out var property))
                yield return $"collection baseline entry '{entry.Member}' is stale";
            else if (entry.Category == "generated-contract" && !property.IsGenerated)
                yield return $"collection baseline entry '{entry.Member}' is not generated";
            else if (entry.Category == "immutable-snapshot" && IsMutableCollectionType(property.Property.PropertyType))
                yield return $"collection baseline entry '{entry.Member}' claims a snapshot but exposes a mutable type";
        }
    }

    private static bool HasFailure(IEnumerable<string> failures, string text) =>
        failures.Any(failure => failure.Contains(text, StringComparison.Ordinal));

    private static DispositionEntry Entry(string member, string category = "immutable-snapshot") =>
        new(member, category, "reason", "owner", "trigger");

    private static string GetTypeName(Type type) => type.FullName
        ?? throw new InvalidOperationException($"Type '{type}' has no fully qualified name.");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static void Report(string name, string[] failures)
    {
        if (failures.Length == 0)
            return;

        Console.WriteLine($"Record adoption RED - {name} ({failures.Length}):");
        foreach (var failure in failures)
            Console.WriteLine($"  - {failure}");
        Console.WriteLine($"Record adoption RED summary - {name}: {failures.Length}");
    }

    private sealed record CollectionProperty(string Member, PropertyInfo Property, bool IsGenerated);
    private sealed record DispositionEntry(
        string? Member,
        string? Category,
        string? Reason,
        string? Owner,
        string? RemovalTrigger);
    private sealed record ParsedBaseline(
        IReadOnlyList<DispositionEntry> Entries,
        IReadOnlyList<string> Failures);
}
