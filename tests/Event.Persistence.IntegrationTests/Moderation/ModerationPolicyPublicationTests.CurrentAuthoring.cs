using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Features.Moderation.Requests.Queries;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task CurrentAuthoringUsesFreshQualificationAtConsumptionAndDoesNotWrite()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        await database.PublishAsync(request);
        await using var context = database.Context();
        var (handler, _, _) = CurrentAuthoringHandler(database, context);
        var pending = await handler.QueryAsync(new("tenant"));
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        ModerationCurrentPolicyView? delivered = null;
        var outcome = await pending.Projection!.DiscloseAsync(
            (view, _) => Task.FromResult(view),
            (view, _) =>
            {
                delivered = view;
                return Task.CompletedTask;
            });
        await Assert.That(outcome).IsEqualTo(ModerationCurrentPolicyReadOutcome.Allowed);
        await Assert.That(delivered!.ExpectedQualificationRevision).IsEqualTo(2);
        await Assert.That(delivered.ExpectedRevision).IsEqualTo(1);
        await Assert.That(delivered.ExpectedPublicationId).IsEqualTo(request.Proposed.PublicationId);
        await Assert.That(delivered.Authoring!.HistoricalExpectedRevision).IsEqualTo(0);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await database.AssertCountsAsync(1, 1, 2);
    }

    [Test]
    public async Task NativeQualifiedViewWritesTerminalHttpBytesUnderOriginalFencesWithoutPrivateFacts()
    {
        await using var database = await Database.CreateAsync();
        await database.PublishAsync(database.Request());
        await using var context = database.Context();
        var (handler, _, _) = CurrentAuthoringHandler(database, context);
        var pending = await handler.QueryAsync(new("tenant"));
        var http = new DefaultHttpContext();
        await using var body = new MemoryStream();
        http.Response.Body = body;
        var transactionId = Guid.Empty;
        await Assert.That(await pending.Projection!.DiscloseAsync(async (view, _) =>
        {
            transactionId = context.Database.CurrentTransaction!.TransactionId;
            await Assert.That(view.NativeAuthoringQualified).IsTrue();
            return JsonSerializer.SerializeToUtf8Bytes(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }, async (bytes, token) =>
        {
            await Assert.That(context.Database.CurrentTransaction!.TransactionId).IsEqualTo(transactionId);
            await Assert.That(context.ModerationTrustDomainId).IsEqualTo(database.Scope.TrustDomainId);
            await http.Response.Body.WriteAsync(bytes, token);
        })).IsEqualTo(ModerationCurrentPolicyReadOutcome.Allowed);
        using var json = JsonDocument.Parse(body.ToArray());
        await Assert.That(json.RootElement.GetProperty("expectedQualificationRevision").GetInt64()).IsEqualTo(2);
        var fields = json.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet();
        await Assert.That(fields.Overlaps(["nativeAuthoringQualified", "principalId", "preparerId",
            "operationId", "requestDigest", "declaredStateDigest", "evidence", "consumers",
            "publishedActive", "isReady", "isSupported"])).IsFalse();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await database.AssertCountsAsync(1, 1, 2);
    }

    [Test]
    public async Task CurrentAuthoringSelectsLatestDraftNotOlderActivePointerOrUnadoptedCandidate()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var first = database.Request();
        await database.PublishAsync(first);
        var latest = database.Next(first);
        await database.PublishAsync(latest);
        // An older active pointer is adversarial selection metadata, not a deployment claim.
        await using (var change = database.Context())
        {
            change.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await change.ModerationPolicySelections.ExecuteUpdateAsync(setters =>
                setters.SetProperty(row => row.ActivePublicationId, first.Proposed.PublicationId));
        }
        var future = database.Next(latest);
        future = future with { Proposed = future.Proposed with
            { RequestedState = ModerationPolicyRequestedState.Active } };
        await using (var preparation = database.Context())
            await Assert.That((await database.Writer(preparation).PrepareAsync(future, Now.AddHours(1))).Outcome)
                .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using var context = database.Context();
        var (handler, _, _) = CurrentAuthoringHandler(database, context);
        var pending = await handler.QueryAsync(new("tenant"));
        var outcome = await pending.Projection!.DiscloseAsync((view, _) =>
        {
            return Task.FromResult(view);
        }, async (view, _) =>
        {
            await Assert.That(view.ExpectedPublicationId).IsEqualTo(latest.Proposed.PublicationId);
            await Assert.That(view.ExpectedRevision).IsEqualTo(2);
            await Assert.That(view.ExpectedQualificationRevision).IsEqualTo(4);
            await Assert.That(view.Authoring!.PublicationId).IsEqualTo(latest.Proposed.PublicationId);
        });
        await Assert.That(outcome).IsEqualTo(ModerationCurrentPolicyReadOutcome.Allowed);
        await database.AssertCountsAsync(3, 1, 4);
    }

    [Test]
    public async Task InstanceReadUsesCurrentRequestEpochNotHistoricalForeignPublicationAuthority()
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        var request = RebindExportRequest(database.Request(), database.Scope.Parent!);
        await using (var writer = database.Context())
        {
            writer.TenantContext = new ExportTenantContext(foreign.TenantId);
            var epoch = await writer.ModerationQualificationEpochs.SingleAsync();
            request = request with { ExpectedAuthoritySourceId = epoch.Id, ExpectedQualificationRevision = epoch.Revision };
            writer.ChangeTracker.Clear();
            await Assert.That((await database.Writer(writer).PublishAsync(request)).Outcome)
                .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        }
        await using var context = database.Context();
        var (handler, _, _) = CurrentAuthoringHandler(database, context);
        var pending = await handler.QueryAsync(new("instance"));
        await Assert.That(await pending.Projection!.DiscloseAsync((view, _) => Task.FromResult(view),
            async (view, _) =>
            {
                await Assert.That(view.ExpectedAuthoritySourceId).IsEqualTo(database.EpochId);
                await Assert.That(view.ExpectedQualificationRevision).IsEqualTo(1);
                await Assert.That(view.ExpectedPublicationId).IsEqualTo(request.Proposed.PublicationId);
                await Assert.That(view.ExpectedRevision).IsEqualTo(1);
            })).IsEqualTo(ModerationCurrentPolicyReadOutcome.Allowed);
    }

    [Test]
    public async Task InstalledEmptySlotHasUnreservedIdentityAndNoReservationOrEpochAdvance()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var (handler, _, _) = CurrentAuthoringHandler(database, context);
        var pending = await handler.QueryAsync(new("tenant"));
        await Assert.That(await pending.Projection!.DiscloseAsync((view, _) => Task.FromResult(view),
            async (view, _) =>
            {
                await Assert.That(view.State).IsEqualTo("uninitialized");
                await Assert.That(view.PolicyId.Version).IsEqualTo(7);
                await Assert.That(view.ExpectedRevision).IsEqualTo(0);
                await Assert.That(view.ExpectedPublicationId).IsNull();
                await Assert.That(view.Authoring).IsNull();
            })).IsEqualTo(ModerationCurrentPolicyReadOutcome.Allowed);
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    [Arguments("host")]
    [Arguments("epoch")]
    public async Task MissingInstallationIsUnavailableNotUninitializedAndIsNeverCreated(string missing)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        await context.ModerationQualificationEpochs.ExecuteDeleteAsync();
        if (missing == "host") await context.ModerationHostBindings.ExecuteDeleteAsync();
        var (handler, _, _) = CurrentAuthoringHandler(database, context);
        var pending = await handler.QueryAsync(new("tenant"));
        var prepared = false;
        await Assert.That(await pending.Projection!.DiscloseAsync((view, _) =>
        {
            prepared = true;
            return Task.FromResult(view);
        }, (_, _) => Task.CompletedTask)).IsEqualTo(ModerationCurrentPolicyReadOutcome.Unavailable);
        await Assert.That(prepared).IsFalse();
        await Assert.That(await context.ModerationQualificationEpochs.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProspectiveIdentityStaysUnreservedAndCannotReplaceAnAdoptedSelection(bool adopted)
    {
        await using var database = await Database.CreateAsync();
        var publication = database.Request();
        if (adopted) await database.PublishAsync(publication);
        var prospective = Guid.CreateVersion7();
        for (var read = 0; read < 2; read++)
        {
            await using var context = database.Context();
            var (handler, _, _) = CurrentAuthoringHandler(database, context);
            var pending = await handler.QueryAsync(new("tenant", prospective));
            await Assert.That(await pending.Projection!.DiscloseAsync((view, _) => Task.FromResult(view),
                async (view, _) =>
                {
                    await Assert.That(view.PolicyId).IsEqualTo(adopted ? publication.Proposed.PolicyId : prospective);
                    await Assert.That(view.ExpectedRevision).IsEqualTo(adopted ? 1L : 0L);
                    await Assert.That(view.State).IsEqualTo(adopted ? "adopted" : "uninitialized");
                    await Assert.That(view.ExpectedPublicationId)
                        .IsEqualTo(adopted ? publication.Proposed.PublicationId : (Guid?)null);
                })).IsEqualTo(ModerationCurrentPolicyReadOutcome.Allowed);
            await Assert.That(context.Database.CurrentTransaction).IsNull();
        }
        await database.AssertCountsAsync(adopted ? 1 : 0, adopted ? 1 : 0, adopted ? 2 : 1);
    }

    [Test]
    [Arguments("00000000-0000-0000-0000-000000000000")]
    [Arguments("0199aabb-1234-4000-8000-000000000001")]
    [Arguments("0199aabb-1234-7000-0000-000000000001")]
    public async Task InvalidProspectiveIdentityNeverAcquiresAReadLease(string invalid)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var (handler, _, _) = CurrentAuthoringHandler(database, context);
        var result = await handler.QueryAsync(new("tenant", Guid.Parse(invalid)));
        await Assert.That(result.Outcome).IsEqualTo(ModerationCurrentPolicyReadOutcome.Invalid);
        await Assert.That(result.Projection).IsNull();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    public async Task InventoryDriftKeepsAllDisabledMeaningsAndCompleteRetainedPlansSeparate()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request(Enum.GetValues<ModerationReferenceProfileId>());
        request = request with { Proposed = request.Proposed with { SupportedProfiles = [] } };
        await database.PublishAsync(request);
        var json = JsonNode.Parse(ModerationCapabilityRegistry.ReadInventory())!.AsObject();
        json["revision"] = database.Inventory.Revision + 1;
        var installed = ModerationCapabilityRegistry.Parse(json.ToJsonString());
        await using var context = database.Context();
        var (handler, _, _) = CurrentAuthoringHandler(database, context, inventory: installed);
        var pending = await handler.QueryAsync(new("tenant"));
        await Assert.That(await pending.Projection!.DiscloseAsync((view, _) => Task.FromResult(view),
            async (view, _) =>
            {
                await Assert.That(view.InstalledInventoryRevision).IsEqualTo(installed.Revision);
                await Assert.That(view.Authoring!.InventoryRevision).IsEqualTo(database.Inventory.Revision);
                await Assert.That(view.Authoring.Profiles.Length).IsEqualTo(27);
                await Assert.That(view.Authoring.Profiles.All(row => !row.Consequences.IsEmpty)).IsTrue();
                await Assert.That(view.Authoring.SelectedProfiles.IsEmpty).IsTrue();
                await Assert.That(view.Authoring.Classes.Select(row => row.Meaning).ToHashSet()
                    .SetEquals(request.Proposed.Classes.Select(row => row.Meaning))).IsTrue();
            })).IsEqualTo(ModerationCurrentPolicyReadOutcome.Allowed);
    }

    [Test]
    public async Task MissingRetainedBasePlanCannotBeDisclosedAsACompleteDefinition()
    {
        await using var database = await Database.CreateAsync();
        await database.PublishAsync(database.Request());
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await context.Set<ModerationPolicyPublicationPlan>().Where(row => row.Strategy == 0).ExecuteDeleteAsync();
        context.ModerationTrustDomainId = null;
        var (handler, _, _) = CurrentAuthoringHandler(database, context);
        var pending = await handler.QueryAsync(new("tenant"));
        var prepared = false;
        await Assert.That(await pending.Projection!.DiscloseAsync((view, _) =>
        {
            prepared = true;
            return Task.FromResult(view);
        }, (_, _) => Task.CompletedTask)).IsEqualTo(ModerationCurrentPolicyReadOutcome.Unavailable);
        await Assert.That(prepared).IsFalse();
    }

    [Test]
    [Arguments("tenant", ModerationCurrentPolicyReadOutcome.Allowed)]
    [Arguments("instance", ModerationCurrentPolicyReadOutcome.Denied)]
    public async Task NativeTenantAdministratorCannotUseInstanceAuthoringScope(
        string scope, ModerationCurrentPolicyReadOutcome expected)
    {
        await using var database = await Database.CreateAsync();
        await DemoteExportAdministratorAsync(database);
        await using var context = database.Context();
        var (handler, _, _) = CurrentAuthoringHandler(database, context);
        var pending = await handler.QueryAsync(new(scope));
        await Assert.That(await pending.Projection!.DiscloseAsync(
            (view, _) => Task.FromResult(view), (_, _) => Task.CompletedTask)).IsEqualTo(expected);
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    [Arguments("role")]
    [Arguments("human")]
    [Arguments("machine")]
    [Arguments("tenant")]
    [Arguments("trust")]
    [Arguments("epoch")]
    [Arguments("slot")]
    [Arguments("dirty")]
    [Arguments("transaction")]
    public async Task OriginalNativeFrameChangeDuringPreparationSuppressesTerminalBytes(string change)
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        await database.PublishAsync(request);
        await using var context = database.Context();
        var tenantFrame = context.TenantContext;
        var (handler, http, machine) = CurrentAuthoringHandler(database, context);
        var pending = await handler.QueryAsync(new("tenant"));
        var delivered = false;
        var outcome = await pending.Projection!.DiscloseAsync(async (view, _) =>
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(view);
            switch (change)
            {
                case "role": await context.PlatformUserRoles.ExecuteDeleteAsync(); break;
                case "human": http.HttpContext!.User = new ClaimsPrincipal(); break;
                case "machine":
                    machine.SetPrincipal(new Explore.Application.Authentication.ApiKeyPrincipalContext(
                        Guid.CreateVersion7().ToString(), database.TenantId, ExternalApiKeyOwnerType.User, database.AdminId, []));
                    break;
                case "tenant": context.TenantContext = new ExportTenantContext(Guid.CreateVersion7()); break;
                case "trust": context.ModerationTrustDomainId = Guid.CreateVersion7(); break;
                case "epoch": await context.ModerationQualificationEpochs.ExecuteUpdateAsync(setters =>
                    setters.SetProperty(row => row.Revision, row => row.Revision + 1)); break;
                case "slot": await context.ModerationPolicySelections.ExecuteUpdateAsync(setters =>
                    setters.SetProperty(row => row.Revision, row => row.Revision + 1)); break;
                case "dirty": (await context.Tenants.SingleAsync()).FullName = "Unflushed preparation write"; break;
                case "transaction": await context.Database.CurrentTransaction!.DisposeAsync(); break;
                default: throw new ArgumentOutOfRangeException(nameof(change));
            }
            return bytes;
        }, (_, _) => { delivered = true; return Task.CompletedTask; });
        await Assert.That(outcome == ModerationCurrentPolicyReadOutcome.Allowed).IsFalse();
        await Assert.That(delivered).IsFalse();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(ReferenceEquals(context.TenantContext, tenantFrame)).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        context.ChangeTracker.Clear();
        await database.AssertCountsAsync(1, 1, 2);
    }

    [Test]
    public async Task EmptySlotMustRemainAbsentAfterPrivateRepresentation()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        var publication = (await database.PublishAsync(request)).Publication!;
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await context.ModerationPolicySelections.ExecuteDeleteAsync();
        context.ModerationTrustDomainId = null;
        var (handler, _, _) = CurrentAuthoringHandler(database, context);
        var pending = await handler.QueryAsync(new("tenant"));
        var delivered = false;
        await Assert.That(await pending.Projection!.DiscloseAsync(async (view, token) =>
        {
            context.ModerationPolicySelections.Add(ModerationPolicySelection.Start(publication));
            await context.SaveChangesAsync(token);
            return JsonSerializer.SerializeToUtf8Bytes(view);
        }, (_, _) => { delivered = true; return Task.CompletedTask; }))
            .IsEqualTo(ModerationCurrentPolicyReadOutcome.Unavailable);
        await Assert.That(delivered).IsFalse();
        await database.AssertCountsAsync(1, 0, 2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PendingReadIsSingleUseAndDisposalDoesNotAcquireAuthority(bool dispose)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var (handler, _, _) = CurrentAuthoringHandler(database, context);
        var pending = await handler.QueryAsync(new("tenant"));
        var calls = 0;
        if (dispose) await pending.DisposeAsync();
        else await pending.Projection!.DiscloseAsync((view, _) => Task.FromResult(view),
            (_, _) => { calls++; return Task.CompletedTask; });
        await Assert.That(await pending.Projection!.DiscloseAsync((view, _) => Task.FromResult(view),
            (_, _) => { calls++; return Task.CompletedTask; }))
            .IsEqualTo(ModerationCurrentPolicyReadOutcome.Unavailable);
        await Assert.That(calls).IsEqualTo(dispose ? 0 : 1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    public async Task CancellationAfterPreparationReleasesOriginalTransactionWithoutDelivery()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var (handler, _, _) = CurrentAuthoringHandler(database, context);
        using var cancellation = new CancellationTokenSource();
        var pending = await handler.QueryAsync(new("tenant"), cancellation.Token);
        var delivered = false;
        await Assert.That(async () => await pending.Projection!.DiscloseAsync((view, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(view);
        }, (_, _) => { delivered = true; return Task.CompletedTask; })).Throws<OperationCanceledException>();
        await Assert.That(delivered).IsFalse();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellationAtTerminalDeliveryCannotReportAllowedAndReleasesOriginalFences(bool origin)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var (handler, _, _) = CurrentAuthoringHandler(database, context);
        using var cancellation = new CancellationTokenSource();
        var pending = await handler.QueryAsync(new("tenant"),
            origin ? cancellation.Token : CancellationToken.None);
        var http = new DefaultHttpContext();
        await using var body = new MemoryStream();
        http.Response.Body = body;
        var transactionId = Guid.Empty;
        await Assert.That(async () => await pending.Projection!.DiscloseAsync((view, _) =>
        {
            transactionId = context.Database.CurrentTransaction!.TransactionId;
            return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(view));
        }, async (bytes, token) =>
        {
            await Assert.That(context.Database.CurrentTransaction!.TransactionId).IsEqualTo(transactionId);
            await Assert.That(context.ModerationTrustDomainId).IsEqualTo(database.Scope.TrustDomainId);
            await http.Response.Body.WriteAsync(bytes, token);
            cancellation.Cancel();
        }, origin ? CancellationToken.None : cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(body.Length).IsGreaterThan(0L);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    [Arguments("anonymous", ModerationCurrentPolicyReadOutcome.Unauthenticated)]
    [Arguments("revoked", ModerationCurrentPolicyReadOutcome.Denied)]
    [Arguments("machine", ModerationCurrentPolicyReadOutcome.Denied)]
    public async Task TerminalReadDoesNotBorrowEarlierIdentityOrExportAuthority(
        string change, ModerationCurrentPolicyReadOutcome expected)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var (handler, http, machine) = CurrentAuthoringHandler(database, context);
        var pending = await handler.QueryAsync(new("tenant"));
        if (change == "anonymous") http.HttpContext!.User = new ClaimsPrincipal();
        if (change == "revoked") await context.PlatformUserRoles.ExecuteDeleteAsync();
        if (change == "machine")
            machine.SetPrincipal(new Explore.Application.Authentication.ApiKeyPrincipalContext(
                Guid.CreateVersion7().ToString(), database.TenantId, ExternalApiKeyOwnerType.User, database.AdminId, []));
        await Assert.That(await pending.Projection!.DiscloseAsync(
            (view, _) => Task.FromResult(view), (_, _) => Task.CompletedTask)).IsEqualTo(expected);
        await database.AssertCountsAsync(0, 0, 1);
    }

}
