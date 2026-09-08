namespace Event.Architecture.Tests;

using System.Reflection;
using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

/// <summary>
/// An inventory that is only a document goes stale the first time somebody adds a query. These tests
/// rediscover the real surface from the compiled Application assembly on every run, so the catalog either
/// keeps up or the build says so.
/// </summary>
public sealed class SensitiveCollectionInventoryTests
{
    private static readonly Assembly ApplicationAssembly = typeof(AuthorizeResourceAttribute).Assembly;

    /// <summary>
    /// A paged, authorized query is exactly the shape that can leak a count: it is gated on something, so
    /// somebody decided it was not public, and it returns a total the caller can read.
    /// <para>
    /// Discovery keys off the <em>response</em> carrying a <c>PaginatedResult&lt;T&gt;</c> rather than off
    /// property names. Naming conventions vary across this codebase — <c>PageNumber</c> in some requests,
    /// <c>Page</c> in others — and a discovery rule that misses one of those spellings would silently
    /// stop guarding whole queries while still reporting green.
    /// </para>
    /// </summary>
    private static IReadOnlyList<Type> DiscoverPagedSecureQueries() =>
        [.. ApplicationAssembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => typeof(ISecureRequest).IsAssignableFrom(type))
            .Where(ReturnsAPaginatedResult)
            .OrderBy(type => type.Name, StringComparer.Ordinal)];

    /// <summary>
    /// Whether the request's response type contains a <c>PaginatedResult&lt;T&gt;</c> anywhere — directly,
    /// or wrapped in an envelope such as <c>BaseCommandResponse&lt;PaginatedResult&lt;T&gt;&gt;</c>.
    /// </summary>
    private static bool ReturnsAPaginatedResult(Type requestType) =>
        requestType
            .GetInterfaces()
            .Where(contract => contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IRequest<>))
            .Select(contract => contract.GetGenericArguments()[0])
            .Any(ContainsPaginatedResult);

    private static bool ContainsPaginatedResult(Type type)
    {
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(PaginatedResult<>))
                return true;

            return type.GetGenericArguments().Any(ContainsPaginatedResult);
        }

        return false;
    }

    /// <summary>
    /// The guardrail. Adding a paginated authorized query forces an explicit call on whether its rows,
    /// count, and existence are sensitive — the judgement Phase 4 exists to make once, in the open.
    /// </summary>
    [Test]
    public async Task EveryPagedSecureQueryIsClassifiedInTheSensitiveCollectionCatalog()
    {
        var unclassified = DiscoverPagedSecureQueries()
            .Select(type => type.Name)
            .Where(name => !SensitiveCollectionCatalog.IsClassified(name))
            .ToArray();

        await Assert.That(unclassified).IsEmpty()
            .Because(
                "every paginated authorized query must be classified in SensitiveCollectionCatalog, either as "
                + "Protected with its required row scope or as PublicByDesign with a stated reason. "
                + $"Unclassified: {string.Join(", ", unclassified)}");
    }

    /// <summary>
    /// The mirror of the guardrail above: an entry naming a query that no longer exists is dead weight
    /// that makes the catalog look more complete than it is.
    /// </summary>
    [Test]
    public async Task CatalogContainsNoEntriesForQueriesThatNoLongerExist()
    {
        var existing = DiscoverPagedSecureQueries()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        var orphaned = SensitiveCollectionCatalog.Protected
            .Select(collection => collection.CollectionName)
            .Concat(SensitiveCollectionCatalog.PublicByDesign.Keys)
            .Where(name => !existing.Contains(name))
            .ToArray();

        await Assert.That(orphaned).IsEmpty()
            .Because($"catalog entries must name a live paged secure query. Orphaned: {string.Join(", ", orphaned)}");
    }

    /// <summary>
    /// A collection cannot be both protected and public. Contradictory classification would let a reader
    /// take whichever answer suited them.
    /// </summary>
    [Test]
    public async Task NoCollectionIsBothProtectedAndPublicByDesign()
    {
        var overlapping = SensitiveCollectionCatalog.Protected
            .Select(collection => collection.CollectionName)
            .Where(SensitiveCollectionCatalog.PublicByDesign.ContainsKey)
            .ToArray();

        await Assert.That(overlapping).IsEmpty();
    }

    [Test]
    public async Task ProtectedCollectionsAreUniquelyNamed()
    {
        var duplicates = SensitiveCollectionCatalog.Protected
            .GroupBy(collection => collection.CollectionName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        await Assert.That(duplicates).IsEmpty();
    }

    /// <summary>
    /// A rationale is the reviewable part of the classification. An entry without one is an assertion,
    /// not a decision, and cannot be argued with later.
    /// </summary>
    [Test]
    public async Task EveryProtectedCollectionStatesWhyDisclosureMatters()
    {
        foreach (var collection in SensitiveCollectionCatalog.Protected)
        {
            await Assert.That(string.IsNullOrWhiteSpace(collection.Sensitivity)).IsFalse()
                .Because($"{collection.CollectionName} must state why its rows or count are sensitive.");

            await Assert.That(collection.Sensitivity.Length).IsGreaterThan(40)
                .Because($"{collection.CollectionName} needs a real rationale, not a placeholder.");
        }
    }

    [Test]
    public async Task EveryPublicByDesignCollectionStatesWhyItIsSafe()
    {
        foreach (var (collectionName, reason) in SensitiveCollectionCatalog.PublicByDesign)
        {
            await Assert.That(string.IsNullOrWhiteSpace(reason)).IsFalse()
                .Because($"{collectionName} must state why its rows and count are safe to disclose.");

            await Assert.That(reason.Length).IsGreaterThan(40)
                .Because($"{collectionName} needs a real justification, not a placeholder.");
        }
    }

    /// <summary>
    /// The catalog's resource kind and action must be real catalogued capabilities. A typo here would
    /// describe a gate that does not exist while reading as though the collection were covered.
    /// </summary>
    [Test]
    public async Task ProtectedCollectionsReferenceRealCapabilities()
    {
        var knownResourceKinds = typeof(ResourceKinds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, FieldType: { } type } && type == typeof(string))
            .Select(field => (string?)field.GetRawConstantValue())
            .Where(value => value is not null)
            .ToHashSet(StringComparer.Ordinal)!;

        foreach (var collection in SensitiveCollectionCatalog.Protected)
        {
            await Assert.That(knownResourceKinds.Contains(collection.ResourceKind)).IsTrue()
                .Because($"{collection.CollectionName} names resource kind '{collection.ResourceKind}', which is not in ResourceKinds.");

            await Assert.That(string.IsNullOrWhiteSpace(collection.Action)).IsFalse()
                .Because($"{collection.CollectionName} must name the action that gates it.");
        }
    }
}
