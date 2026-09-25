using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventResources.Handlers.Queries;
using Explore.Application.Features.EventResources.Requests.Queries;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceManagementPersistenceTests
{
    [Test]
    public async Task ExportStorageFailureProducesClosedUnavailableWithoutMetadata()
    {
        var (scope, actor) = await SeedAsync();
        await using var context = database.CreateContext(new ExportReadFailure());
        var result = await Workflow(context, scope.TenantAId, actor).ExportAsync(scope.EventAId, 1, 20, default);
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Unavailable);
        await Assert.That(result.Value).IsNull();
    }

    [Test]
    public async Task ExportPreservesSemanticIntentWithoutProtectedDeliveryOrAuditState()
    {
        var (scope, actor) = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, actor);
        var id = Guid.CreateVersion7();
        var draft = Draft() with
        {
            DeliveryType = EventResourceDeliveryTypeEnum.ExternalLink, SensitiveNotes = "Authorized management note",
            Availability = new(StartAnchor: EventResourceAvailabilityAnchorEnum.EventEnd, StartOffsetTicks: TimeSpan.FromDays(1).Ticks)
        };
        await Assert.That((await workflow.CreateAsync(scope.EventAId, id, draft, default)).IsSuccess).IsTrue();
        var resource = await context.EventResources.SingleAsync(row => row.Id == id);
        var protectedPayload = Guid.CreateVersion7().ToString("N");
        resource.SetExternalDestination(protectedPayload, 1, "https://private-material.example.test",
            resource.ConcurrencyStamp, actor, Now);
        await context.SaveChangesAsync();
        var result = await new ExportEventResourceMetadataQueryHandler(workflow).QueryAsync(new(scope.EventAId));
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
        var item = result.Value!.Items.Single();
        await Assert.That(item.Id).IsEqualTo(id);
        await Assert.That(item.Title).IsEqualTo(draft.Title);
        await Assert.That(item.SensitiveNotes).IsEqualTo(draft.SensitiveNotes);
        await Assert.That(item.Availability).IsEqualTo(draft.Availability);
        await Assert.That(item.AudienceRules.Select(rule => rule.Kind).SequenceEqual(draft.AudienceRules.Select(rule => rule.Kind))).IsTrue();
        var json = JsonSerializer.SerializeToElement(item, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await Assert.That(json.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
            .SetEquals(["id", "eventSessionId", "publicationState", "title", "publicTitle", "description", "sensitiveNotes",
                "kind", "disclosureMode", "deliveryType", "languageCode", "accessibilityNote", "sortOrder",
                "accessibleAlternativeEventResourceId", "availability", "audienceRules"])).IsTrue();
        await Assert.That(json.GetRawText().Contains(protectedPayload, StringComparison.Ordinal)
            || json.GetRawText().Contains("https://private-material.example.test", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ExportRequiresEachResourceExportDecisionRatherThanManagementOrParentAlone()
    {
        var (scope, actor) = await SeedAsync();
        var id = Guid.CreateVersion7();
        await using var context = database.CreateContext();
        await Assert.That((await Workflow(context, scope.TenantAId, actor)
            .CreateAsync(scope.EventAId, id, Draft(), default)).IsSuccess).IsTrue();
        var provider = Substitute.For<IEventResourceAuthorizationProvider>();
        provider.CheckBatchAsync(Arg.Any<IReadOnlyList<EventResourceProviderInput>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var inputs = call.Arg<IReadOnlyList<EventResourceProviderInput>>();
                ArgumentNullException.ThrowIfNull(inputs);
                return inputs.Select(input => input.Action == "export" && input.Resource.Id == id
                    ? EventResourceProviderDecision.Deny : EventResourceProviderDecision.Allow).ToArray();
            });
        var result = await Workflow(context, scope.TenantAId, actor, provider: provider)
            .ExportAsync(scope.EventAId, 1, 20, default);
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Forbidden);
        await Assert.That(result.Value).IsNull();
    }

    [Test]
    public async Task ExportCannotReleaseMetadataChangedDuringItsProviderCheck()
    {
        var (scope, actor) = await SeedAsync();
        var id = Guid.CreateVersion7();
        await using (var seed = database.CreateContext())
            await Assert.That((await Workflow(seed, scope.TenantAId, actor)
                .CreateAsync(scope.EventAId, id, Draft(), default)).IsSuccess).IsTrue();
        var provider = Substitute.For<IEventResourceAuthorizationProvider>();
        bool changed = false;
        provider.CheckBatchAsync(Arg.Any<IReadOnlyList<EventResourceProviderInput>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var inputs = call.Arg<IReadOnlyList<EventResourceProviderInput>>();
                ArgumentNullException.ThrowIfNull(inputs);
                if (!changed && inputs.Any(input => input.Resource.Id == id))
                {
                    changed = true;
                    await using var writer = database.CreateIndependentContext();
                    var resource = await writer.EventResources.SingleAsync(row => row.Id == id);
                    resource.UpdateMetadata(new EventResourceMetadata
                    {
                        Title = "New semantic revision", Kind = EventResourceKindEnum.GeneralDocument,
                        DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
                    }, resource.ConcurrencyStamp, actor, Now);
                    await writer.SaveChangesAsync();
                }
                return (IReadOnlyList<EventResourceProviderDecision>)inputs.Select(_ => EventResourceProviderDecision.Allow).ToArray();
            });
        await using var context = database.CreateIndependentContext();
        var result = await Workflow(context, scope.TenantAId, actor, provider: provider)
            .ExportAsync(scope.EventAId, 1, 20, default);
        await Assert.That(changed).IsTrue();
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(result.Value).IsNull();
    }

    private sealed class ExportReadFailure : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"ie_event_resources\"", StringComparison.Ordinal)
                && command.CommandText.Contains("LIMIT", StringComparison.Ordinal))
                throw new InvalidOperationException("Export storage is unavailable.");
            return ValueTask.FromResult(result);
        }
    }
}
