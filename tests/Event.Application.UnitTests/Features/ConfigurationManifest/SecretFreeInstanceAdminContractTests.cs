using Explore.Application.Features.InstanceAdmin.Handlers.Queries;

namespace Event.Application.UnitTests.Features.ConfigurationManifest;

public sealed class SecretFreeInstanceAdminContractTests
{
    [Test]
    public async Task ExistingOverviewConsumesServerSideSecretAuthorityStatus()
    {
        string[] dependencies = typeof(GetInstanceOverviewQueryHandler)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name)
            .ToArray();

        await Assert.That(dependencies).Contains("ISecretAuthorityStatusReader");
    }

    [Test]
    public async Task SecretAuthorityStatusContractContainsOnlyBoundedFields()
    {
        Type? contract = typeof(GetInstanceOverviewQueryHandler).Assembly.GetType(
            "Explore.Application.Contracts.Secrets.SecretAuthorityStatusSnapshot");
        await Assert.That(contract).IsNotNull();
        string[] propertyNames = contract!
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        await Assert.That(propertyNames)
            .IsEquivalentTo(["Provider", "Status", "RemediationCode"]);
    }
}
