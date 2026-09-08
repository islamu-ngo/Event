using System.Net;
using Event.Api.IntegrationTests.Fixtures;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Fast)]
[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public sealed class AdminMigrationEndpointRetirementTests(ContractApiFixture fixture)
{
    [Test]
    public async Task AdminMigrate_Post_IsNotMappedInTestingHost()
    {
        using var response = await fixture.Client.PostAsync("/admin/migrate", content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }
}
