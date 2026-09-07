using System.Reflection;
using Explore.Application.Authorization;
using Explore.Application.Features.StorageObjects.Requests.Queries;

namespace Event.Application.UnitTests.Features.StorageObjects.Queries;

public sealed class StorageObjectQueryAuthorizationMetadataTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid StorageObjectId = Guid.NewGuid();

    public static IEnumerable<(Type RequestType, string ExpectedAction)> StorageObjectQueries()
    {
        yield return (typeof(GetStorageObjectListRequest), AuthorizationActions.StorageObjects.View);
        yield return (typeof(GetStorageObjectDetailsRequest), AuthorizationActions.StorageObjects.View);
        yield return (typeof(GetStorageObjectContentRequest), AuthorizationActions.StorageObjects.Download);
        yield return (typeof(GetPresignedDownloadUrlRequest), AuthorizationActions.StorageObjects.PresignedDownload);
    }

    [Test]
    [MethodDataSource(nameof(StorageObjectQueries))]
    public async Task StorageObjectReadQueriesRequireStorageObjectAuthorization(
        (Type RequestType, string ExpectedAction) testCase)
    {
        var attribute = testCase.RequestType.GetCustomAttribute<AuthorizeResourceAttribute>();

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Resource).IsEqualTo(ResourceKinds.StorageObject);
        await Assert.That(attribute.Action).IsEqualTo(testCase.ExpectedAction);
        await Assert.That(typeof(ISecureRequest).IsAssignableFrom(testCase.RequestType)).IsTrue();
    }

    public static IEnumerable<(ISecureRequest Request, string ExpectedResourceId)> AuthorizedStorageObjectRequests()
    {
        yield return (new GetStorageObjectListRequest { TenantId = TenantId }, TenantId.ToString("D"));
        yield return (new GetStorageObjectDetailsRequest { Id = StorageObjectId, TenantId = TenantId }, StorageObjectId.ToString("D"));
        yield return (new GetStorageObjectContentRequest { StorageObjectId = StorageObjectId, TenantId = TenantId }, StorageObjectId.ToString("D"));
        yield return (new GetPresignedDownloadUrlRequest { Id = StorageObjectId, TenantId = TenantId }, StorageObjectId.ToString("D"));
    }

    [Test]
    [MethodDataSource(nameof(AuthorizedStorageObjectRequests))]
    public async Task StorageObjectReadQueriesExposeTenantAuthorizationContext(
        (ISecureRequest Request, string ExpectedResourceId) testCase)
    {
        await Assert.That(testCase.Request.ResourceId).IsEqualTo(testCase.ExpectedResourceId);
        await Assert.That(testCase.Request.AuthorizationFacts)
            .IsEqualTo(new StorageObjectCollectionAuthorizationFacts(TenantId));
    }
}
