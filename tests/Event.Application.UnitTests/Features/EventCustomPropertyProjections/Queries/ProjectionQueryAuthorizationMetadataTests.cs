using System.Reflection;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Queries;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Queries;

namespace Event.Application.UnitTests.Features.EventCustomPropertyProjections.Queries;

public class ProjectionQueryAuthorizationMetadataTests
{
    [Test]
    [Arguments(typeof(GetEventCustomPropertyProjectionStatusQuery))]
    [Arguments(typeof(GetEventSessionCustomPropertyProjectionStatusQuery))]
    [Arguments(typeof(GetCustomPropertyProjectionDirtyScopesQuery))]
    [Arguments(typeof(GetEventCustomPropertyProjectionsForEventQuery))]
    [Arguments(typeof(GetEventSessionCustomPropertyProjectionsForSessionQuery))]
    public async Task ProjectionInspectionQueriesRequireCustomPropertyProjectionViewPermission(Type queryType)
    {
        var attribute = queryType.GetCustomAttribute<AuthorizeResourceAttribute>();

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Resource).IsEqualTo(ResourceKinds.CustomPropertyProjection);
        await Assert.That(attribute.Action).IsEqualTo(AuthorizationActions.CustomPropertyProjections.View);
        await Assert.That(typeof(ISecureRequest).IsAssignableFrom(queryType)).IsTrue();
    }

    public static IEnumerable<(ISecureRequest Request, string ExpectedResourceId, string ExpectedContextKey, string ExpectedContextValue)> AuthorizedProjectionQueries()
    {
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var eventSessionId = Guid.NewGuid();

        yield return (
            new GetEventCustomPropertyProjectionStatusQuery { TenantId = tenantId },
            tenantId.ToString("D"),
            "projectionName",
            IEventCustomPropertyProjectionUpdater.ProjectionName);
        yield return (
            new GetEventSessionCustomPropertyProjectionStatusQuery { TenantId = tenantId },
            tenantId.ToString("D"),
            "projectionName",
            IEventSessionCustomPropertyProjectionUpdater.ProjectionName);
        yield return (
            new GetCustomPropertyProjectionDirtyScopesQuery
            {
                TenantId = tenantId,
                ProjectionName = IEventCustomPropertyProjectionUpdater.ProjectionName
            },
            $"{tenantId:D}:{IEventCustomPropertyProjectionUpdater.ProjectionName}",
            "projectionName",
            IEventCustomPropertyProjectionUpdater.ProjectionName);
        yield return (
            new GetEventCustomPropertyProjectionsForEventQuery { EventId = eventId },
            eventId.ToString("D"),
            "eventId",
            eventId.ToString("D"));
        yield return (
            new GetEventSessionCustomPropertyProjectionsForSessionQuery { EventSessionId = eventSessionId },
            eventSessionId.ToString("D"),
            "eventSessionId",
            eventSessionId.ToString("D"));
    }

    [Test]
    [MethodDataSource(nameof(AuthorizedProjectionQueries))]
    public async Task ProjectionInspectionQueriesExposeResourceAuthorizationContext(
        (ISecureRequest Request, string ExpectedResourceId, string ExpectedContextKey, string ExpectedContextValue) testCase)
    {
        await Assert.That(testCase.Request.ResourceId).IsEqualTo(testCase.ExpectedResourceId);
        // Projection administration is tenant-scoped. The event or session in the resource id selects the
        // projection scope; it never adds an authority zone of its own.
        await Assert.That(testCase.Request.AuthorizationFacts)
            .IsTypeOf<CustomPropertyProjectionAuthorizationFacts>();
    }
}
