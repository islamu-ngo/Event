using Explore.Application.Specifications.Events;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Specifications;

public class EventCustomPropertyProjectionFilterTests
{
    [Test]
    public async Task ExactMatch_WhenCalled_ShouldCreateCorrectFilter()
    {
        // Arrange
        const string expectedNamespace = "test_ns";
        const string expectedKey = "test_key";
        const string expectedValue = "normalized_value";

        // Act
        var filter = EventCustomPropertyProjectionFilter.ExactMatch(expectedNamespace, expectedKey, expectedValue);

        // Assert
        await Assert.That(filter.FilterType).IsEqualTo(EventCustomPropertyProjectionFilterType.ExactMatch);
        var (@namespace, key, value) = ((string Namespace, string Key, string NormalizedValue))filter.Value;
        await Assert.That(@namespace).IsEqualTo(expectedNamespace);
        await Assert.That(key).IsEqualTo(expectedKey);
        await Assert.That(value).IsEqualTo(expectedValue);
    }

    [Test]
    public async Task OptionMatch_WhenCalled_ShouldCreateCorrectFilter()
    {
        // Arrange
        const string expectedNamespace = "test_ns";
        const string expectedKey = "test_key";
        var expectedOptionId = Guid.NewGuid();

        // Act
        var filter = EventCustomPropertyProjectionFilter.OptionMatch(expectedNamespace, expectedKey, expectedOptionId);

        // Assert
        await Assert.That(filter.FilterType).IsEqualTo(EventCustomPropertyProjectionFilterType.OptionMatch);
        var (@namespace, key, optionId) = ((string Namespace, string Key, Guid OptionId))filter.Value;
        await Assert.That(@namespace).IsEqualTo(expectedNamespace);
        await Assert.That(key).IsEqualTo(expectedKey);
        await Assert.That(optionId).IsEqualTo(expectedOptionId);
    }

    [Test]
    public async Task OptionsMatchAny_WhenCalled_ShouldCreateCorrectFilter()
    {
        // Arrange
        const string expectedNamespace = "test_ns";
        const string expectedKey = "test_key";
        var expectedOptionIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        // Act
        var filter = EventCustomPropertyProjectionFilter.OptionsMatchAny(expectedNamespace, expectedKey, expectedOptionIds);

        // Assert
        await Assert.That(filter.FilterType).IsEqualTo(EventCustomPropertyProjectionFilterType.OptionsMatchAny);
        var (@namespace, key, optionIds) = ((string Namespace, string Key, List<Guid> OptionIds))filter.Value;
        await Assert.That(@namespace).IsEqualTo(expectedNamespace);
        await Assert.That(key).IsEqualTo(expectedKey);
        await Assert.That(optionIds).IsEqualTo(expectedOptionIds);
    }

    [Test]
    public async Task TextSearch_WhenCalled_ShouldCreateCorrectFilter()
    {
        // Arrange
        const string expectedNamespace = "test_ns";
        const string expectedKey = "test_key";
        const string expectedSearchTerm = "search_term";

        // Act
        var filter = EventCustomPropertyProjectionFilter.TextSearch(expectedNamespace, expectedKey, expectedSearchTerm);

        // Assert
        await Assert.That(filter.FilterType).IsEqualTo(EventCustomPropertyProjectionFilterType.TextSearch);
        var (@namespace, key, searchTerm) = ((string Namespace, string Key, string SearchTerm))filter.Value;
        await Assert.That(@namespace).IsEqualTo(expectedNamespace);
        await Assert.That(key).IsEqualTo(expectedKey);
        await Assert.That(searchTerm).IsEqualTo(expectedSearchTerm);
    }

    [Test]
    public async Task GlobalTextSearch_WhenCalled_ShouldCreateCorrectFilter()
    {
        // Arrange
        const string expectedSearchTerm = "global_search";

        // Act
        var filter = EventCustomPropertyProjectionFilter.GlobalTextSearch(expectedSearchTerm);

        // Assert
        await Assert.That(filter.FilterType).IsEqualTo(EventCustomPropertyProjectionFilterType.GlobalTextSearch);
        await Assert.That((string)filter.Value).IsEqualTo(expectedSearchTerm);
    }

    [Test]
    public async Task Exists_WhenCalled_ShouldCreateCorrectFilter()
    {
        // Arrange
        const string expectedNamespace = "test_ns";
        const string expectedKey = "test_key";

        // Act
        var filter = EventCustomPropertyProjectionFilter.Exists(expectedNamespace, expectedKey);

        // Assert
        await Assert.That(filter.FilterType).IsEqualTo(EventCustomPropertyProjectionFilterType.Exists);
        var (@namespace, key) = ((string Namespace, string Key))filter.Value;
        await Assert.That(@namespace).IsEqualTo(expectedNamespace);
        await Assert.That(key).IsEqualTo(expectedKey);
    }

    [Test]
    public async Task BooleanTrue_WhenCalled_ShouldCreateCorrectFilter()
    {
        // Arrange
        const string expectedNamespace = "test_ns";
        const string expectedKey = "test_key";

        // Act
        var filter = EventCustomPropertyProjectionFilter.BooleanTrue(expectedNamespace, expectedKey);

        // Assert
        await Assert.That(filter.FilterType).IsEqualTo(EventCustomPropertyProjectionFilterType.BooleanTrue);
        var (@namespace, key) = ((string Namespace, string Key))filter.Value;
        await Assert.That(@namespace).IsEqualTo(expectedNamespace);
        await Assert.That(key).IsEqualTo(expectedKey);
    }

    [Test]
    public async Task NumberRange_WhenCalled_ShouldCreateCorrectFilter()
    {
        // Arrange
        const string expectedNamespace = "test_ns";
        const string expectedKey = "test_key";
        const decimal expectedMin = 10.5m;
        const decimal expectedMax = 25.75m;

        // Act
        var filter = EventCustomPropertyProjectionFilter.NumberRange(expectedNamespace, expectedKey, expectedMin, expectedMax);

        // Assert
        await Assert.That(filter.FilterType).IsEqualTo(EventCustomPropertyProjectionFilterType.NumberRange);
        var (@namespace, key, min, max) = ((string Namespace, string Key, decimal? Min, decimal? Max))filter.Value;
        await Assert.That(@namespace).IsEqualTo(expectedNamespace);
        await Assert.That(key).IsEqualTo(expectedKey);
        await Assert.That(min).IsEqualTo(expectedMin);
        await Assert.That(max).IsEqualTo(expectedMax);
    }

    [Test]
    public async Task DateRange_WhenCalled_ShouldCreateCorrectFilter()
    {
        // Arrange
        const string expectedNamespace = "test_ns";
        const string expectedKey = "test_key";
        var expectedFrom = new DateTimeOffset(2026, 4, 13, 8, 30, 0, TimeSpan.Zero);
        var expectedTo = new DateTimeOffset(2026, 4, 20, 18, 45, 0, TimeSpan.Zero);

        // Act
        var filter = EventCustomPropertyProjectionFilter.DateRange(expectedNamespace, expectedKey, expectedFrom, expectedTo);

        // Assert
        await Assert.That(filter.FilterType).IsEqualTo(EventCustomPropertyProjectionFilterType.DateRange);
        var (@namespace, key, from, to) = ((string Namespace, string Key, DateTimeOffset? From, DateTimeOffset? To))filter.Value;
        await Assert.That(@namespace).IsEqualTo(expectedNamespace);
        await Assert.That(key).IsEqualTo(expectedKey);
        await Assert.That(from).IsEqualTo(expectedFrom);
        await Assert.That(to).IsEqualTo(expectedTo);
    }

    [Test]
    public async Task FactoryMethods_WhenExposureCeilingOmitted_ShouldDefaultToPublic()
    {
        // Act
        var filter = EventCustomPropertyProjectionFilter.Exists("test_ns", "test_key");

        // Assert
        await Assert.That(filter.ExposureCeiling).IsEqualTo(ExposureLevel.Public);
    }

    [Test]
    public async Task FactoryMethods_WhenExposureCeilingProvided_ShouldPreserveCeiling()
    {
        // Act
        var filter = EventCustomPropertyProjectionFilter.GlobalTextSearch("term", ExposureLevel.TenantAdminOnly);

        // Assert
        await Assert.That(filter.ExposureCeiling).IsEqualTo(ExposureLevel.TenantAdminOnly);
    }
}
