using Explore.Domain;
using Explore.Domain.CustomProperties;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Event.Persistence.IntegrationTests;

public sealed class CustomPropertyPatternModelTests
{
    [Test]
    [Arguments(typeof(CustomPropertyDefinition))]
    [Arguments(typeof(EventCustomPropertyDefinition))]
    [Arguments(typeof(EventSessionCustomPropertyDefinition))]
    [Arguments(typeof(EventTemplateCustomPropertyDefinition))]
    [Arguments(typeof(EventSessionTemplateCustomPropertyDefinition))]
    public async Task AdmittedPatternBoundaryFitsEveryDefinitionStorageModel(Type definition)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite("Data Source=:memory:").Options;
        await using var context = new ExploreDbContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;
        int? storageLimit = model.FindEntityType(definition)!.FindProperty("RegexPattern")!.GetMaxLength();
        await Assert.That(storageLimit).IsEqualTo(CustomPropertyPatternPolicy.MaximumPatternLength);
        await Assert.That(CustomPropertyPatternPolicy.IsValid("(?#" + new string('a', storageLimit!.Value - 4) + ")"))
            .IsTrue();
    }
}
