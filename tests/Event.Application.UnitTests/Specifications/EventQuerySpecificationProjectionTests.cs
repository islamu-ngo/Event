using Explore.Application.Specifications.Events;

namespace Event.Application.UnitTests.Specifications;

public class EventQuerySpecificationProjectionTests
{
    [Test]
    public async Task Constructor_WhenCalled_ShouldHaveNoProjectionFiltersAndHasFiltersFalse()
    {
        // Arrange
        var specification = new EventQuerySpecification();

        // Act
        var projectionFilterCount = specification.ProjectionFilters.Count;

        // Assert
        await Assert.That(projectionFilterCount).IsEqualTo(0);
        await Assert.That(specification.HasFilters).IsEqualTo(false);
    }

    [Test]
    public async Task And_WhenAddingProjectionFilter_ShouldReturnNewInstance()
    {
        // Arrange
        var specification = new EventQuerySpecification();
        var projectionFilter = EventCustomPropertyProjectionFilter.ExactMatch("test_ns", "test_key", "normalized_value");

        // Act
        var updatedSpecification = specification.And(projectionFilter);

        // Assert
        await Assert.That(object.ReferenceEquals(specification, updatedSpecification)).IsEqualTo(false);
        await Assert.That(specification.ProjectionFilters.Count).IsEqualTo(0);
        await Assert.That(updatedSpecification.ProjectionFilters.Count).IsEqualTo(1);
    }

    [Test]
    public async Task And_WhenAddingProjectionFilter_ShouldSetHasFiltersTrue()
    {
        // Arrange
        var specification = new EventQuerySpecification();
        var projectionFilter = EventCustomPropertyProjectionFilter.ExactMatch("test_ns", "test_key", "normalized_value");

        // Act
        var updatedSpecification = specification.And(projectionFilter);

        // Assert
        await Assert.That(updatedSpecification.HasFilters).IsEqualTo(true);
    }

    [Test]
    public async Task And_WhenAddingMultipleProjectionFilters_ShouldComposeCorrectly()
    {
        // Arrange
        var firstFilter = EventCustomPropertyProjectionFilter.ExactMatch("test_ns", "first_key", "first_value");
        var secondFilter = EventCustomPropertyProjectionFilter.TextSearch("test_ns", "second_key", "second_value");

        // Act
        var specification = new EventQuerySpecification()
            .And(firstFilter)
            .And(secondFilter);

        // Assert
        await Assert.That(specification.ProjectionFilters.Count).IsEqualTo(2);
        await Assert.That(specification.ProjectionFilters[0].FilterType).IsEqualTo(EventCustomPropertyProjectionFilterType.ExactMatch);
        await Assert.That(specification.ProjectionFilters[1].FilterType).IsEqualTo(EventCustomPropertyProjectionFilterType.TextSearch);
    }

    [Test]
    public async Task And_WhenProjectionFiltersMixedWithRegularAndSubqueryFilters_ShouldNotInterfere()
    {
        // Arrange
        var directFilter = EventFilter.SearchTerm("workshop");
        var subqueryFilter = EventSubqueryFilter.TagsIncludedAny(new List<Guid> { Guid.NewGuid(), Guid.NewGuid() });
        var projectionFilter = EventCustomPropertyProjectionFilter.ExactMatch("test_ns", "test_key", "normalized_value");

        // Act
        var specification = new EventQuerySpecification()
            .And(directFilter)
            .And(subqueryFilter)
            .And(projectionFilter);

        // Assert
        await Assert.That(specification.Filters.Count).IsEqualTo(1);
        await Assert.That(specification.SubqueryFilters.Count).IsEqualTo(1);
        await Assert.That(specification.ProjectionFilters.Count).IsEqualTo(1);
        await Assert.That(specification.HasFilters).IsEqualTo(true);
    }

    [Test]
    public async Task ToCacheKeySuffix_WhenProjectionFilterExists_ShouldIncludeProjectionPrefix()
    {
        // Arrange
        var specification = new EventQuerySpecification()
            .And(EventCustomPropertyProjectionFilter.ExactMatch("test_ns", "test_key", "normalized_value"));

        // Act
        var cacheKeySuffix = specification.ToCacheKeySuffix();

        // Assert
        await Assert.That(cacheKeySuffix.Contains("pf:ExactMatch:")).IsEqualTo(true);
        await Assert.That(cacheKeySuffix.Contains("normalized_value")).IsEqualTo(true);
    }

    [Test]
    public async Task ToCacheKeySuffix_WhenSpecificationEmpty_ShouldReturnNone()
    {
        // Arrange
        var specification = new EventQuerySpecification();

        // Act
        var cacheKeySuffix = specification.ToCacheKeySuffix();

        // Assert
        await Assert.That(cacheKeySuffix).IsEqualTo("none");
    }

    [Test]
    public async Task ToCacheKeySuffix_WhenProjectionFilterMixedWithRegularFilter_ShouldIncludeProjectionFilter()
    {
        // Arrange
        var specification = new EventQuerySpecification()
            .And(EventFilter.SearchTerm("workshop"))
            .And(EventCustomPropertyProjectionFilter.TextSearch("test_ns", "test_key", "search_value"));

        // Act
        var cacheKeySuffix = specification.ToCacheKeySuffix();

        // Assert
        await Assert.That(cacheKeySuffix.Contains("f:")).IsEqualTo(true);
        await Assert.That(cacheKeySuffix.Contains("pf:TextSearch:")).IsEqualTo(true);
        await Assert.That(cacheKeySuffix.Contains("search_value")).IsEqualTo(true);
    }
}
