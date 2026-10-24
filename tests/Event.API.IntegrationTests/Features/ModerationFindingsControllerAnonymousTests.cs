using System.Net;
using Event.Api.IntegrationTests.Fixtures;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public sealed class ModerationFindingsControllerAnonymousTests(ApiTestFixture fixture)
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AnonymousCallerCannotReachFindingWrites(bool finalize)
    {
        var findingId = Guid.CreateVersion7();
        var path = finalize
            ? $"/api/moderation/cases/{Guid.CreateVersion7()}/findings/{findingId}/finalize"
            : $"/api/moderation/findings/{findingId}/reverse";
        using var response = await fixture.Client.PostAsync(path, null);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}
