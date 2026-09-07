using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;

namespace Event.Architecture.Tests.Privacy;

public sealed class PrivacyErasureContractArchitectureTests
{
    [Test]
    public async Task PrivacyErasureContracts_ExposeOnlyTypedBoundedFields()
    {
        await Assert.That(Enum.GetValues<PrivacyErasureSubjectKind>())
            .IsEquivalentTo([PrivacyErasureSubjectKind.User]);

        string[] forbidden =
        [
            "LocationIds", "OwnerUserId", "Table", "Column", "Sql", "Json", "Metadata", "Instructions"
        ];
        Type[] contracts =
        [
            typeof(PrivacyErasureIntent),
            typeof(PrivacyErasureReplayCheckpoint),
            typeof(PrivacyErasureRequest)
        ];

        await Assert.That(contracts.SelectMany(type => type.GetProperties())
            .Any(property => forbidden.Contains(property.Name, StringComparer.OrdinalIgnoreCase)))
            .IsFalse();
        await Assert.That(contracts.SelectMany(type => type.GetProperties())
            .Any(property => property.PropertyType == typeof(string)))
            .IsFalse();
    }

}
