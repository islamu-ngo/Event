using System.Text.Json;
using Explore.Application.Models.Common;

namespace Explore.Application.UnitTests.Models.Common;

public class OptionalUpdateTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task DefaultValueIsUnspecified()
    {
        OptionalUpdate<string?> update = default;

        await Assert.That(update.HasValue).IsFalse();
        await Assert.That(update.Value).IsNull();
    }

    [Test]
    public async Task UnspecifiedReturnsNoFieldOperation()
    {
        var update = OptionalUpdate<Guid?>.Unspecified();

        await Assert.That(update.HasValue).IsFalse();
        await Assert.That(update.Value).IsNull();
    }

    [Test]
    public async Task SetWithValueRecordsExplicitUpdate()
    {
        var update = OptionalUpdate<string?>.Set("Updated bio");

        await Assert.That(update.HasValue).IsTrue();
        await Assert.That(update.Value).IsEqualTo("Updated bio");
    }

    [Test]
    public async Task SetWithNullRecordsExplicitClear()
    {
        var update = OptionalUpdate<string?>.Set(null);

        await Assert.That(update.HasValue).IsTrue();
        await Assert.That(update.Value).IsNull();
    }

    [Test]
    public async Task DeserializeWhenBodyContainsHasValueAndNullValueRecordsExplicitClear()
    {
        var update = JsonSerializer.Deserialize<OptionalUpdate<string?>>(
            """
            {
              "hasValue": true,
              "value": null
            }
            """,
            JsonOptions);

        await Assert.That(update.HasValue).IsTrue();
        await Assert.That(update.Value).IsNull();
    }

    [Test]
    public async Task DeserializeWhenBodyOmitsFieldsRemainsUnspecified()
    {
        var update = JsonSerializer.Deserialize<OptionalUpdate<string?>>("{}", JsonOptions);

        await Assert.That(update.HasValue).IsFalse();
        await Assert.That(update.Value).IsNull();
    }

    [Test]
    public async Task SerializeUsesCamelCaseContractNames()
    {
        var json = JsonSerializer.Serialize(OptionalUpdate<string?>.Set("Updated bio"), JsonOptions);

        await Assert.That(json).IsEqualTo("""{"hasValue":true,"value":"Updated bio"}""");
    }
}
