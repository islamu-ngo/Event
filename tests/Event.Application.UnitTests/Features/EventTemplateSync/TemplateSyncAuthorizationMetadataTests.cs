using System.Reflection;
using Explore.Application.Authorization;
using Explore.Application.Features.EventSessionTemplateSync.Commands.ApplyEventSessionTemplateSync;
using Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateDiff;
using Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateSyncHistory;
using Explore.Application.Features.EventTemplateSync.Commands.ApplyEventTemplateSync;
using Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateDiff;
using Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateSyncHistory;

namespace Event.Application.UnitTests.Features.EventTemplateSync;

public class TemplateSyncAuthorizationMetadataTests
{
    [Test]
    [Arguments(typeof(GetEventTemplateDiffQuery), AuthorizationActions.CustomPropertyTemplates.SyncDiff)]
    [Arguments(typeof(ApplyEventTemplateSyncCommand), AuthorizationActions.CustomPropertyTemplates.SyncApply)]
    [Arguments(typeof(GetEventTemplateSyncHistoryQuery), AuthorizationActions.CustomPropertyTemplates.View)]
    [Arguments(typeof(GetEventSessionTemplateDiffQuery), AuthorizationActions.CustomPropertyTemplates.SyncDiff)]
    [Arguments(typeof(ApplyEventSessionTemplateSyncCommand), AuthorizationActions.CustomPropertyTemplates.SyncApply)]
    [Arguments(typeof(GetEventSessionTemplateSyncHistoryQuery), AuthorizationActions.CustomPropertyTemplates.View)]
    public async Task TemplateSyncRequestsRequireCustomPropertyTemplatePermission(
        Type requestType,
        string expectedAction)
    {
        var attribute = requestType.GetCustomAttribute<AuthorizeResourceAttribute>();

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Resource).IsEqualTo(ResourceKinds.CustomPropertyTemplate);
        await Assert.That(attribute.Action).IsEqualTo(expectedAction);
        await Assert.That(typeof(ISecureRequest).IsAssignableFrom(requestType)).IsTrue();
    }
}
