using System.Reflection;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Commands;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Commands;

namespace Event.Application.UnitTests.Features.EventCustomPropertyProjections.Commands;

public sealed class ProjectionCommandAuthorizationMetadataTests
{
    public static IEnumerable<Type> ProjectionMutationCommandTypes()
    {
        yield return typeof(RebuildEventCustomPropertyProjectionCommand);
        yield return typeof(RebuildEventSessionCustomPropertyProjectionCommand);
        yield return typeof(DrainCustomPropertyProjectionDirtyScopesCommand);
        yield return typeof(RebuildSingleEventCustomPropertyProjectionCommand);
        yield return typeof(RebuildSingleEventSessionCustomPropertyProjectionCommand);
    }

    [Test]
    [MethodDataSource(nameof(ProjectionMutationCommandTypes))]
    public async Task ProjectionMutationCommandsRequireCustomPropertyProjectionUpdatePermission(Type commandType)
    {
        var attribute = commandType.GetCustomAttribute<AuthorizeResourceAttribute>();

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Resource).IsEqualTo(ResourceKinds.CustomPropertyProjection);
        await Assert.That(attribute.Action).IsEqualTo(AuthorizationActions.CustomPropertyProjections.Update);
        await Assert.That(typeof(ISecureRequest).IsAssignableFrom(commandType)).IsTrue();
    }

    public static IEnumerable<(ISecureRequest Request, string ExpectedResourceId, string ExpectedContextKey, string ExpectedContextValue)> AuthorizedProjectionCommands()
    {
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var eventSessionId = Guid.NewGuid();

        yield return (
            new RebuildEventCustomPropertyProjectionCommand
            {
                RequestDto = new RebuildProjectionRequestDto { TenantId = tenantId }
            },
            tenantId.ToString("D"),
            "projectionName",
            IEventCustomPropertyProjectionUpdater.ProjectionName);
        yield return (
            new RebuildEventSessionCustomPropertyProjectionCommand
            {
                RequestDto = new RebuildProjectionRequestDto { TenantId = tenantId }
            },
            tenantId.ToString("D"),
            "projectionName",
            IEventSessionCustomPropertyProjectionUpdater.ProjectionName);
        yield return (
            new DrainCustomPropertyProjectionDirtyScopesCommand
            {
                RequestDto = new DrainDirtyScopesRequestDto
                {
                    TenantId = tenantId,
                    ProjectionName = IEventCustomPropertyProjectionUpdater.ProjectionName
                }
            },
            $"{tenantId:D}:{IEventCustomPropertyProjectionUpdater.ProjectionName}",
            "projectionName",
            IEventCustomPropertyProjectionUpdater.ProjectionName);
        yield return (
            new RebuildSingleEventCustomPropertyProjectionCommand { EventId = eventId },
            eventId.ToString("D"),
            "eventId",
            eventId.ToString("D"));
        yield return (
            new RebuildSingleEventSessionCustomPropertyProjectionCommand { EventSessionId = eventSessionId },
            eventSessionId.ToString("D"),
            "eventSessionId",
            eventSessionId.ToString("D"));
    }

    [Test]
    [MethodDataSource(nameof(AuthorizedProjectionCommands))]
    public async Task ProjectionMutationCommandsExposeResourceAuthorizationContext(
        (ISecureRequest Request, string ExpectedResourceId, string ExpectedContextKey, string ExpectedContextValue) testCase)
    {
        await Assert.That(testCase.Request.ResourceId).IsEqualTo(testCase.ExpectedResourceId);
        // Projection administration is tenant-scoped. The event or session in the resource id selects the
        // projection scope; it never adds an authority zone of its own.
        await Assert.That(testCase.Request.AuthorizationFacts)
            .IsTypeOf<CustomPropertyProjectionAuthorizationFacts>();
    }
}
