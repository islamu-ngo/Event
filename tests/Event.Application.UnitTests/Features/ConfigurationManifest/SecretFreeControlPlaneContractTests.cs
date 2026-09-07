using Explore.Application.Features.ControlPlane.Handlers.Queries;

namespace Event.Application.UnitTests.Features.ConfigurationManifest;

public sealed class SecretFreeControlPlaneContractTests
{
    [Test]
    public async Task ExistingOverviewConsumesServerSideSecretAuthorityStatus()
    {
        string[] dependencies = typeof(GetControlPlaneOverviewQueryHandler)
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
        Type? contract = typeof(GetControlPlaneOverviewQueryHandler).Assembly.GetType(
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
