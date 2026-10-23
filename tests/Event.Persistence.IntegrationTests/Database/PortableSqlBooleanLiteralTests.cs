using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class PortableSqlBooleanLiteralTests
{
    [Test]
    [Arguments("value IN ('true', 'false') AND enabled = true", "value IN ('true', 'false') AND enabled = 1")]
    [Arguments("value = 'false' OR enabled = FALSE", "value = 'false' OR enabled = 0")]
    [Arguments("value = 'operator''s true value' AND enabled = TRUE", "value = 'operator''s true value' AND enabled = 1")]
    [Arguments("\"true\" = 'true' AND enabled = false", "\"true\" = 'true' AND enabled = 0")]
    [Arguments("[false] = 'true' AND enabled = TRUE", "[false] = 'true' AND enabled = 1")]
    [Arguments("\"escaped\"\"true\" = N'false' AND enabled = false", "\"escaped\"\"true\" = N'false' AND enabled = 0")]
    [Arguments("[escaped]]false] = 'true' AND enabled = true", "[escaped]]false] = 'true' AND enabled = 1")]
    public async Task SqlServerChecksPreserveQuotedValuesWhileTranslatingBooleanTokens(
        string source, string expected)
    {
        var model = CreateModel(source, filteredIndex: false);

        PortableRelationalModelPolicy.Apply(model, "Microsoft.EntityFrameworkCore.SqlServer");

        string actual = model.Model.FindEntityType("ScalarConstraint")!.GetCheckConstraints().Single().Sql;
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task SqlServerIndexFiltersPreserveTheSerializedValuePredicate()
    {
        var model = CreateModel("value = 'false' AND enabled = true", filteredIndex: true);

        PortableRelationalModelPolicy.Apply(model, "Microsoft.EntityFrameworkCore.SqlServer");

        string? actual = model.Model.FindEntityType("ScalarConstraint")!.GetIndexes().Single().GetFilter();
        await Assert.That(actual).IsEqualTo("value = 'false' AND enabled = 1");
    }

    private static ModelBuilder CreateModel(string predicate, bool filteredIndex)
    {
        var model = new ModelBuilder();
        var row = model.SharedTypeEntity<Dictionary<string, object>>("ScalarConstraint");
        row.Property<int>("Id");
        row.HasKey("Id");
        row.Property<string>("Value").HasColumnName("value");
        row.Property<string>("TrueColumn").HasColumnName("true");
        row.Property<bool>("Enabled").HasColumnName("enabled");
        row.ToTable("scalar_constraints", table =>
        {
            if (!filteredIndex)
                table.HasCheckConstraint("ck_scalar_value", predicate);
        });
        if (filteredIndex)
            row.HasIndex("Value").HasFilter(predicate);
        return model;
    }
}
