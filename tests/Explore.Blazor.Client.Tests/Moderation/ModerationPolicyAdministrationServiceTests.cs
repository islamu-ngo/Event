using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Services.Moderation;

namespace Explore.Blazor.Client.Tests.Moderation;

public sealed class ModerationPolicyAdministrationServiceTests
{
    [Test]
    [Arguments(ModerationPolicyAdministrationScope.Tenant, "tenant", AuthorityScopeKind.Tenant)]
    [Arguments(ModerationPolicyAdministrationScope.Instance, "instance", AuthorityScopeKind.Instance)]
    public async Task ReadsTypedScopeRetainedDefinitionsAndPublicReference(
        ModerationPolicyAdministrationScope scope, string name, AuthorityScopeKind kind)
    {
        using var transport = new Transport { Scope = name };
        using var http = Http(transport);
        var result = await Service(http).ReadAsync(scope, CancellationToken.None);
        await Assert.That(result.Failure).IsEqualTo(ModerationPolicyAdministrationFailure.None);
        await Assert.That(result.View!.Current.Jurisdiction.Kind).IsEqualTo(kind);
        await Assert.That(result.View.Current.Authoring!.Modes.Count).IsEqualTo(2);
        await Assert.That(result.View.Current.Authoring.Profiles.First().Definition.ScopeKinds.Single())
            .IsEqualTo(ModerationScopeKind.Tenant);
        await Assert.That(result.View.Current.Authoring.SelectedProfiles.Single().Code).IsEqualTo("selected");
        await Assert.That(result.View.ReferenceCatalogue.Revision).IsEqualTo(8L);
        await Assert.That(result.View.Current.Authoring.Profiles.First().Consequences.Count).IsEqualTo(2);
        await Assert.That(result.View.Current.Authoring.Profiles.First().Consequences.First().PendingWork)
            .IsEqualTo(ModerationPendingWorkMode.OwnedObjectReviewNeverAutomaticRelease);
        await Assert.That(result.View.ReferenceCatalogue.Profiles.First().ConsequencePlans!.Count).IsEqualTo(2);
        await Assert.That(result.View.ReferenceCatalogue.Profiles.First().Prerequisites!.Single())
            .IsEqualTo(ModerationReferencePrerequisite.ExactResourceAudienceAndVersion);
        await Assert.That(result.View.CanPublishDraft).IsTrue();
        await Assert.That(transport.Requests[0].Path).IsEqualTo("/community/api/moderation/policies/current?scope=" + name);
    }

    [Test]
    public async Task EmptyInstalledSlotIsNotAnEmptyAdoptedPolicy()
    {
        using var transport = new Transport { Uninitialized = true };
        using var http = Http(transport);
        var result = await Service(http).ReadAsync(ModerationPolicyAdministrationScope.Tenant, CancellationToken.None);
        await Assert.That(result.View!.IsUninitialized).IsTrue();
        await Assert.That(result.View.Current.Authoring).IsNull();
        transport.Uninitialized = false;
        transport.NullAuthoring = true;
        var broken = await Service(http).ReadAsync(ModerationPolicyAdministrationScope.Tenant, CancellationToken.None);
        await Assert.That(broken.View).IsNull();
        await Assert.That(broken.Failure).IsEqualTo(ModerationPolicyAdministrationFailure.InvalidRepresentation);
    }

    [Test]
    public async Task NewIntentUsesFreshCoordinatesButDoesNotMigrateHistoricalInventory()
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        _ = await service.ReadAsync(ModerationPolicyAdministrationScope.Tenant, CancellationToken.None);
        transport.Revision = 19;
        var intent = Proposed();
        var result = await service.CreateDraftAsync(ModerationPolicyAdministrationScope.Tenant,
            intent, OperationId, CancellationToken.None);
        await Assert.That(result.Failure).IsEqualTo(ModerationPolicyAdministrationFailure.None);
        _ = await service.SubmitAsync(result.Operation!, CancellationToken.None);
        var body = Body(transport.Requests.Last().Body!).GetProperty("request");
        await Assert.That(body.GetProperty("expectedHostBindingId").GetGuid()).IsEqualTo(HostId);
        await Assert.That(body.GetProperty("expectedAuthoritySourceId").GetGuid()).IsEqualTo(SourceId);
        await Assert.That(body.GetProperty("expectedQualificationRevision").GetInt64()).IsEqualTo(23L);
        await Assert.That(body.GetProperty("expectedWriterGeneration").GetString()).IsEqualTo("writer-current");
        await Assert.That(body.GetProperty("expectedPublicationId").GetGuid()).IsEqualTo(PredecessorId);
        var proposed = body.GetProperty("proposed");
        await Assert.That(proposed.GetProperty("expectedRevision").GetInt64()).IsEqualTo(19L);
        await Assert.That(proposed.GetProperty("policyId").GetGuid()).IsEqualTo(PolicyId);
        await Assert.That(proposed.GetProperty("jurisdiction").GetProperty("id").GetGuid()).IsEqualTo(TenantId);
        await Assert.That(proposed.GetProperty("inventoryRevision").GetInt64()).IsEqualTo(3L);
        await Assert.That(proposed.GetProperty("inventoryFingerprint").GetString()).IsEqualTo("retained");
        await Assert.That(proposed.GetProperty("publicationId").GetGuid()).IsEqualTo(PublicationId);
    }

    [Test]
    [Arguments(ModerationPolicyAdministrationScope.Tenant, "tenant")]
    [Arguments(ModerationPolicyAdministrationScope.Instance, "instance")]
    public async Task EmptySlotPreparationReacquiresNativeLinksForTheSameUnreservedIntent(
        ModerationPolicyAdministrationScope scope, string name)
    {
        using var transport = new Transport { Scope = name, Uninitialized = true, MintProspectiveIdentity = true };
        using var http = Http(transport);
        var service = Service(http);
        var current = (await service.ReadAsync(scope)).View!.Current;
        var proposed = Proposed();
        proposed.PolicyId = current.PolicyId;
        var result = await service.CreateDraftAsync(scope, proposed, OperationId);
        await Assert.That(result.Failure).IsEqualTo(ModerationPolicyAdministrationFailure.None);
        await Assert.That(result.Operation!.PolicyId).IsEqualTo(current.PolicyId);
        await Assert.That(result.Operation.ExpectedRevision).IsEqualTo(0L);
        await Assert.That(transport.Requests.Last().Path)
            .IsEqualTo($"/community/api/moderation/policies/current?scope={name}&prospectivePolicyId={current.PolicyId:D}");
        var submitted = await service.SubmitAsync(result.Operation);
        await Assert.That(submitted.Failure).IsEqualTo(ModerationPolicyAdministrationFailure.None);
        await Assert.That(submitted.Publication!.Value.Id).IsEqualTo(current.PolicyId);
        await Assert.That(transport.Requests.Last().Path)
            .IsEqualTo($"/community/api/moderation/policies/{current.PolicyId:D}/publications");
        var request = Body(transport.Requests.Last().Body!).GetProperty("request");
        await Assert.That(request.GetProperty("proposed").GetProperty("policyId").GetGuid()).IsEqualTo(current.PolicyId);
        await Assert.That(!request.TryGetProperty("expectedPublicationId", out var predecessor)
            || predecessor.ValueKind == JsonValueKind.Null).IsTrue();
    }

    [Test]
    [Arguments(null, "POST")]
    [Arguments("https://foreign.example/api/moderation/policies/{id}/publications", "POST")]
    [Arguments("//foreign.example/api/moderation/policies/{id}/publications", "POST")]
    [Arguments("/api/moderation/policies/{id}/publications", "GET")]
    [Arguments("/api/moderation/policies/{id}/activation", "POST")]
    [Arguments("/api/moderation/policies/{id}/publications?tenant=other", "POST")]
    [Arguments("/api/moderation/policies/{id}/../publications", "POST")]
    [Arguments("/api/moderation/policies/0199aabb-1234-7000-8000-000000000099/publications", "POST")]
    public async Task MissingOrUnsafeLinksNeverBecomeWrites(string? href, string method)
    {
        using var transport = new Transport { DraftHref = href?.Replace("{id}", PolicyId.ToString("D")), Method = method };
        using var http = Http(transport);
        var service = Service(http);
        var view = (await service.ReadAsync(ModerationPolicyAdministrationScope.Tenant, CancellationToken.None)).View!;
        await Assert.That(view.CanPublishDraft).IsFalse();
        var result = await service.CreateDraftAsync(ModerationPolicyAdministrationScope.Tenant,
            Proposed(), OperationId, CancellationToken.None);
        await Assert.That(result.Failure).IsEqualTo(ModerationPolicyAdministrationFailure.ActionUnavailable);
        await Assert.That(transport.Requests.All(r => r.Method == "GET")).IsTrue();
    }

    [Test]
    [Arguments(null)]
    [Arguments("https://foreign.example/api/moderation/policies/{id}/publication-preparations")]
    public async Task CandidatePreparationRequiresItsOwnSafeNativeLink(string? href)
    {
        using var transport = new Transport
        {
            PreparationHref = href?.Replace("{id}", PolicyId.ToString("D"))
        };
        using var http = Http(transport);
        var service = Service(http);
        var view = (await service.ReadAsync(ModerationPolicyAdministrationScope.Tenant, CancellationToken.None)).View!;
        await Assert.That(view.CanPublishDraft).IsTrue();
        await Assert.That(view.CanPreparePublication).IsFalse();
        var proposed = Proposed();
        proposed.RequestedState = ModerationPolicyRequestedState.Active;
        var result = await service.CreatePreparationAsync(ModerationPolicyAdministrationScope.Tenant,
            proposed, OperationId, DateTimeOffset.Parse("2026-10-14T00:00:00Z"), CancellationToken.None);
        await Assert.That(result.Failure).IsEqualTo(ModerationPolicyAdministrationFailure.ActionUnavailable);
        await Assert.That(transport.Requests.All(r => r.Method == "GET")).IsTrue();
    }

    [Test]
    public async Task UnsupportedScopeAndNonVersionSevenIdentityNeverReachHttp()
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        await Assert.That((await service.ReadAsync((ModerationPolicyAdministrationScope)99, CancellationToken.None)).Failure)
            .IsEqualTo(ModerationPolicyAdministrationFailure.InvalidIntent);
        await Assert.That((await service.CreateDraftAsync(ModerationPolicyAdministrationScope.Tenant,
            Proposed(), Guid.Empty, CancellationToken.None)).Failure)
            .IsEqualTo(ModerationPolicyAdministrationFailure.InvalidIntent);
        await Assert.That(transport.Requests.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CompletePayloadAndExactExplicitRetrySurviveCallerMutationAndConflict()
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        var proposed = Proposed();
        var operation = (await service.CreateDraftAsync(ModerationPolicyAdministrationScope.Tenant,
            proposed, OperationId, CancellationToken.None)).Operation!;
        proposed.Profiles.Clear();
        proposed.SupportedProfiles.Clear();
        proposed.Modes.Clear();
        transport.PostStatus = HttpStatusCode.Conflict;
        var conflict = await service.SubmitAsync(operation, CancellationToken.None);
        await Assert.That(conflict.Failure).IsEqualTo(ModerationPolicyAdministrationFailure.Conflict);
        await Assert.That(transport.Requests.Count(r => r.Method == "POST")).IsEqualTo(1);
        transport.Revision = 30;
        transport.PostStatus = HttpStatusCode.OK;
        _ = await service.SubmitAsync(operation, CancellationToken.None);
        var posts = transport.Requests.Where(r => r.Method == "POST").ToArray();
        await Assert.That(posts[0].Body).IsEqualTo(posts[1].Body);
        await Assert.That(transport.Requests.Count(r => r.Method == "GET")).IsEqualTo(1);
        var request = Body(posts[0].Body!).GetProperty("request");
        await Assert.That(request.GetProperty("operationId").GetGuid()).IsEqualTo(OperationId);
        var payload = request.GetProperty("proposed");
        await Assert.That(payload.GetProperty("profiles").GetArrayLength()).IsEqualTo(2);
        await Assert.That(payload.GetProperty("supportedProfiles").GetArrayLength()).IsEqualTo(1);
        await Assert.That(payload.GetProperty("modes")[0].GetString()).IsEqualTo("Embedded");
        await Assert.That(payload.GetProperty("profiles")[1].GetProperty("rule").GetProperty("code").GetString())
            .IsEqualTo("disabled");
        await Assert.That(payload.GetProperty("profiles")[0].GetProperty("strategies")[0].GetString())
            .IsEqualTo("PublicationIntegrity");
        await Assert.That(payload.GetProperty("profiles")[0].GetProperty("effects").GetArrayLength()).IsEqualTo(1);
        await Assert.That(payload.GetProperty("profiles")[0].GetProperty("timing").GetProperty("duration").GetString())
            .IsEqualTo("01:00:00");
        await Assert.That(payload.GetProperty("profiles")[0].GetProperty("contestationCalendarMonths").GetInt32()).IsEqualTo(6);
    }

    [Test]
    public async Task DetachedReadCopiesCannotAlterOwnedBoundsDefinitionsOrLinks()
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var view = (await Service(http).ReadAsync(ModerationPolicyAdministrationScope.Tenant, CancellationToken.None)).View!;
        view.Current.Authoring!.Profiles.Clear();
        view.Current._links!.Clear();
        view.Current.AuthoringBounds.ScopeKinds.Clear();
        view.ReferenceCatalogue.Profiles.Clear();
        await Assert.That(view.Current.Authoring!.Profiles.Count).IsEqualTo(2);
        await Assert.That(view.Current.AuthoringBounds.ScopeKinds.Single()).IsEqualTo(ModerationScopeKind.Tenant);
        await Assert.That(view.CanPublishDraft).IsTrue();
        await Assert.That(view.ReferenceCatalogue.Profiles.Count).IsEqualTo(1);
    }

    [Test]
    public async Task PreparationReturnsCandidateNotPublicationAndNeverActivates()
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        var proposed = Proposed();
        proposed.RequestedState = ModerationPolicyRequestedState.Active;
        var deadline = DateTimeOffset.Parse("2026-10-14T00:00:00Z");
        var operation = (await service.CreatePreparationAsync(ModerationPolicyAdministrationScope.Tenant,
            proposed, OperationId, deadline, CancellationToken.None)).Operation!;
        var result = await service.SubmitAsync(operation, CancellationToken.None);
        await Assert.That(result.Candidate!.Value.Id).IsEqualTo(PublicationId);
        await Assert.That(result.Candidate.Value.ValidUntil).IsEqualTo(deadline);
        await Assert.That(result.Publication).IsNull();
        await Assert.That(transport.Requests.Last().Path).IsEqualTo(
            $"/community/api/moderation/policies/{PolicyId:D}/publication-preparations");
        await Assert.That(Body(transport.Requests.Last().Body!).GetProperty("validUntil").GetDateTimeOffset()).IsEqualTo(deadline);
        await Assert.That(transport.Requests.Count(r => r.Method == "POST")).IsEqualTo(1);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task PreparationRejectsAnotherIdentityOrChangedValidity(bool wrongIdentity)
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        var proposed = Proposed();
        proposed.RequestedState = ModerationPolicyRequestedState.Active;
        var operation = (await service.CreatePreparationAsync(ModerationPolicyAdministrationScope.Tenant,
            proposed, OperationId, DateTimeOffset.Parse("2026-10-14T00:00:00Z"), CancellationToken.None)).Operation!;
        transport.PostBody = $$"""
            {"success":true,"id":"{{(wrongIdentity ? CandidateId : PublicationId)}}","revision":12,
             "validUntil":"{{(wrongIdentity ? "2026-10-14T00:00:00Z" : "2026-10-15T00:00:00Z")}}"}
            """;
        var result = await service.SubmitAsync(operation, CancellationToken.None);
        await Assert.That(result.Candidate).IsNull();
        await Assert.That(result.Failure).IsEqualTo(ModerationPolicyAdministrationFailure.InvalidRepresentation);
    }

    [Test]
    public async Task CallerMutationDuringFreshReadCannotChangeFrozenAuthoringIntent()
    {
        using var transport = new Transport { BlockGet = true };
        using var http = Http(transport);
        var service = Service(http);
        var proposed = Proposed();
        var pending = service.CreateDraftAsync(ModerationPolicyAdministrationScope.Tenant,
            proposed, OperationId, CancellationToken.None);
        await transport.GetEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        proposed.Profiles.Clear();
        proposed.InventoryFingerprint = "changed";
        transport.ReleaseGet.TrySetResult();
        var result = await pending;
        _ = await service.SubmitAsync(result.Operation!, CancellationToken.None);
        var payload = Body(transport.Requests.Last().Body!).GetProperty("request").GetProperty("proposed");
        await Assert.That(payload.GetProperty("profiles").GetArrayLength()).IsEqualTo(2);
        await Assert.That(payload.GetProperty("inventoryFingerprint").GetString()).IsEqualTo("retained");
    }

    [Test]
    public async Task SameOriginAbsoluteNativeLinkIsRemappedUnderCurrentBffBase()
    {
        using var transport = new Transport { DraftHref = $"https://event.example/api/moderation/policies/{PolicyId:D}/publications" };
        using var http = Http(transport);
        var service = Service(http);
        var operation = (await service.CreateDraftAsync(ModerationPolicyAdministrationScope.Tenant,
            Proposed(), OperationId, CancellationToken.None)).Operation!;
        _ = await service.SubmitAsync(operation, CancellationToken.None);
        await Assert.That(transport.Requests.Last().Path)
            .IsEqualTo($"/community/api/moderation/policies/{PolicyId:D}/publications");
    }

    [Test]
    public async Task DraftSubmissionCannotRequestActivation()
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var proposed = Proposed();
        proposed.RequestedState = ModerationPolicyRequestedState.Active;
        var result = await Service(http).CreateDraftAsync(ModerationPolicyAdministrationScope.Tenant,
            proposed, OperationId, CancellationToken.None);
        await Assert.That(result.Failure).IsEqualTo(ModerationPolicyAdministrationFailure.InvalidIntent);
        await Assert.That(transport.Requests.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(400, ModerationPolicyAdministrationFailure.InvalidIntent)]
    [Arguments(401, ModerationPolicyAdministrationFailure.SignInRequired)]
    [Arguments(403, ModerationPolicyAdministrationFailure.Forbidden)]
    [Arguments(409, ModerationPolicyAdministrationFailure.Conflict)]
    [Arguments(429, ModerationPolicyAdministrationFailure.RateLimited)]
    [Arguments(503, ModerationPolicyAdministrationFailure.Unavailable)]
    public async Task HttpFailuresRemainDistinctAndNeverRefreshMutationCoordinates(
        int status, ModerationPolicyAdministrationFailure expected)
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        var operation = (await service.CreateDraftAsync(ModerationPolicyAdministrationScope.Tenant,
            Proposed(), OperationId, CancellationToken.None)).Operation!;
        transport.PostStatus = (HttpStatusCode)status;
        await Assert.That((await service.SubmitAsync(operation, CancellationToken.None)).Failure).IsEqualTo(expected);
        transport.GetStatus = (HttpStatusCode)status;
        await Assert.That((await service.ReadAsync(ModerationPolicyAdministrationScope.Tenant, CancellationToken.None)).Failure)
            .IsEqualTo(expected);
        await Assert.That(transport.Requests.Count(r => r.Method == "POST")).IsEqualTo(1);
        await Assert.That(transport.Requests.Count(r => r.Method == "GET")).IsEqualTo(2);
    }

    [Test]
    public async Task LostMutationResponseIsUncertainAndOnlyExplicitRetryResendsExactIntent()
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        var operation = (await service.CreateDraftAsync(ModerationPolicyAdministrationScope.Tenant,
            Proposed(), OperationId, CancellationToken.None)).Operation!;
        transport.LosePostResponse = true;
        await Assert.That((await service.SubmitAsync(operation, CancellationToken.None)).Failure)
            .IsEqualTo(ModerationPolicyAdministrationFailure.Uncertain);
        await Assert.That(transport.Requests.Count(r => r.Method == "POST")).IsEqualTo(1);
        transport.LosePostResponse = false;
        var retry = await service.SubmitAsync(operation, CancellationToken.None);
        await Assert.That(retry.Publication!.Value.PublicationId).IsEqualTo(PublicationId);
        await Assert.That(transport.Requests[1].Body).IsEqualTo(transport.Requests[2].Body);
    }

    [Test]
    public async Task MalformedSuccessIsNotReadinessOrAnAdoptedPolicy()
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        var operation = (await service.CreateDraftAsync(ModerationPolicyAdministrationScope.Tenant,
            Proposed(), OperationId, CancellationToken.None)).Operation!;
        transport.PostBody = """{"success":true,"publishedActive":true}""";
        var result = await service.SubmitAsync(operation, CancellationToken.None);
        await Assert.That(result.Failure).IsEqualTo(ModerationPolicyAdministrationFailure.InvalidRepresentation);
        await Assert.That(result.Publication).IsNull();
        transport.PostBody = "not JSON";
        await Assert.That((await service.SubmitAsync(operation, CancellationToken.None)).Failure)
            .IsEqualTo(ModerationPolicyAdministrationFailure.InvalidRepresentation);
    }

    [Test]
    public async Task IntentCannotBeSubmittedThroughAnotherTenantBaseAddress()
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        var operation = (await service.CreateDraftAsync(ModerationPolicyAdministrationScope.Tenant,
            Proposed(), OperationId, CancellationToken.None)).Operation!;
        using var otherHttp = new HttpClient(transport, disposeHandler: false) { BaseAddress = new Uri("https://event.example/other/") };
        var result = await Service(otherHttp).SubmitAsync(operation, CancellationToken.None);
        await Assert.That(result.Failure).IsEqualTo(ModerationPolicyAdministrationFailure.ActionUnavailable);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CallerCancellationPropagatesThroughExactPostEventWithoutRetry()
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        var operation = (await service.CreateDraftAsync(ModerationPolicyAdministrationScope.Tenant,
            Proposed(), OperationId, CancellationToken.None)).Operation!;
        transport.BlockPost = true;
        using var cancellation = new CancellationTokenSource();
        var pending = service.SubmitAsync(operation, cancellation.Token);
        await transport.PostEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.That(async () => await pending).Throws<OperationCanceledException>();
        await Assert.That(transport.Requests.Count(r => r.Method == "POST")).IsEqualTo(1);
    }

    private static readonly Guid PolicyId = Guid.Parse("0199aabb-1234-7000-8000-000000000001");
    private static readonly Guid TenantId = Guid.Parse("0199aabb-1234-7000-8000-000000000002");
    private static readonly Guid HostId = Guid.Parse("0199aabb-1234-7000-8000-000000000003");
    private static readonly Guid SourceId = Guid.Parse("0199aabb-1234-7000-8000-000000000004");
    private static readonly Guid PredecessorId = Guid.Parse("0199aabb-1234-7000-8000-000000000005");
    private static readonly Guid PublicationId = Guid.Parse("0199aabb-1234-7000-8000-000000000006");
    private static readonly Guid OperationId = Guid.Parse("0199aabb-1234-7000-8000-000000000007");
    private static readonly Guid CandidateId = Guid.Parse("0199aabb-1234-7000-8000-000000000008");
    private static readonly JsonSerializerOptions Json = EventApiJsonSerializerSettings.Configure(new());
    private static HttpClient Http(Transport transport) => new(transport) { BaseAddress = new Uri("https://event.example/community/") };
    private static ModerationPolicyAdministrationService Service(HttpClient http) =>
        new(new ModerationCurrentPolicyClient(http), new ModerationReferenceCatalogueClient(http), http);
    private static ModerationPolicyProposedState Proposed() => JsonSerializer.Deserialize<ModerationPolicyProposedState>(ProposalJson, Json)!;
    private static JsonElement Body(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    // Literal native wire fixtures exercise named enum collection ingestion independently of serialization.
    private static readonly string ProposalJson = $$"""
        {
          "policyId":"{{SourceId}}","expectedRevision":2,"publicationId":"{{PublicationId}}",
          "jurisdiction":{"kind":"Tenant","id":"{{SourceId}}"},
          "catalogueRevision":8,"inventoryRevision":3,"inventoryFingerprint":"retained","requestedState":"Draft",
          "modes":["Embedded","RemoteStrict"],"classes":[
            {"reference":{"ownerId":"{{PolicyId}}","code":"class","revision":2},"meaning":"Retained class","ground":"CompatibleConfirmedConduct","basis":"ConfirmedWarning"}
          ],"reasons":[{"reference":{"ownerId":"{{PolicyId}}","code":"reason","revision":2},
            "meaning":"Retained reason","class":{"ownerId":"{{PolicyId}}","code":"class","revision":2} }],
          "profiles":[
            {"id":"{{PolicyId}}","rule":{"ownerId":"{{PolicyId}}","code":"selected","revision":4},
             "reason":{"ownerId":"{{PolicyId}}","code":"reason","revision":2},
             "referenceProfile":"CFG01","referenceRevision":8,"grade":"G0",
             "scopeKinds":["Tenant"],"subjectKinds":["Content"],"effects":[{"kind":"ReasonedNotice","mode":"Notice"}],
             "bundle":"Unaffected","strategies":["PublicationIntegrity"],"timing":{"kind":"Finite","duration":"01:00:00"},
             "requiredApprovals":2,"contestationCalendarMonths":6,"recognition":"Never"},
            {"id":"{{SourceId}}","rule":{"ownerId":"{{PolicyId}}","code":"disabled","revision":5},
             "reason":{"ownerId":"{{PolicyId}}","code":"reason","revision":2},
             "referenceProfile":"CFG01","referenceRevision":8,"grade":"G0",
             "scopeKinds":["Tenant"],"subjectKinds":["Content"],"effects":[],"bundle":"Unaffected","strategies":[],
             "timing":{"kind":"Finite","duration":"00:30:00"},"requiredApprovals":3,"contestationCalendarMonths":9,"recognition":"Never"}
          ],"supportedProfiles":[{"ownerId":"{{PolicyId}}","code":"selected","revision":4}]
        }
        """;

    private const string PlansJson = """
        [
          {"content":"QuarantineNamedVersion","management":"CurrentAuthority",
           "resources":"QuarantineNamedVersionSafeStatusOnly","newAdmission":"CurrentEligibility",
           "existingAdmission":"CurrentEligibility","privateDisclosure":"MaskQuarantinedEmbeddedDetails",
           "settlement":"NoBookingChange","capacity":"NoSeatChange","credentials":"CurrentCredentialLifecycle",
           "notifications":"DurableSafeSubjectAndAdmin","recognition":"NoCaptureFromThisControl",
           "pendingWork":"OwnedObjectReviewNeverAutomaticRelease","restoration":"AuthorizedObjectDecision",
           "safeguards":{"automaticHistoricalRestoration":false,"preserveProtectedRemedies":true}},
          {"content":"QuarantineNamedVersion","management":"CurrentAuthority",
           "resources":"QuarantineNamedVersionSafeStatusOnly","newAdmission":"CurrentEligibility",
           "existingAdmission":"CurrentEligibility","privateDisclosure":"MaskQuarantinedEmbeddedDetails",
           "settlement":"NoBookingChange","capacity":"NoSeatChange","credentials":"CurrentCredentialLifecycle",
           "notifications":"DurableSafeSubjectAndAdmin","recognition":"NoCaptureFromThisControl",
           "pendingWork":"OwnedObjectReviewNeverAutomaticRelease","restoration":"AuthorizedObjectDecision",
           "safeguards":{"automaticHistoricalRestoration":false,"preserveBothProtectivePurposes":true}}
        ]
        """;

    private sealed class Transport : HttpMessageHandler
    {
        internal string Scope { get; set; } = "tenant";
        internal bool Uninitialized { get; set; }
        internal bool MintProspectiveIdentity { get; set; }
        internal bool NullAuthoring { get; set; }
        internal long Revision { get; set; } = 11;
        internal string? DraftHref { get; set; } = $"/api/moderation/policies/{PolicyId:D}/publications";
        internal string? PreparationHref { get; set; } = $"/api/moderation/policies/{PolicyId:D}/publication-preparations";
        internal string Method { get; set; } = "POST";
        internal HttpStatusCode GetStatus { get; set; } = HttpStatusCode.OK;
        internal HttpStatusCode PostStatus { get; set; } = HttpStatusCode.OK;
        internal string? PostBody { get; set; }
        internal bool LosePostResponse { get; set; }
        internal bool BlockPost { get; set; }
        internal bool BlockGet { get; set; }
        internal TaskCompletionSource PostEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource GetEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseGet { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<(string Method, string Path, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add((request.Method.Method, path,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(token)));
            if (request.Method == HttpMethod.Post)
            {
                PostEntered.TrySetResult();
                if (BlockPost)
                {
                    var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var registration = token.Register(() => canceled.TrySetCanceled(token));
                    await canceled.Task;
                }
                if (LosePostResponse) throw new HttpRequestException("Response lost after delivery.");
                var publishedPolicy = Body(Requests.Last().Body!).GetProperty("request").GetProperty("proposed")
                    .GetProperty("policyId").GetGuid();
                return Response(PostBody ?? (path.EndsWith("publication-preparations", StringComparison.Ordinal)
                    ? $$"""{"success":true,"id":"{{PublicationId}}","revision":12,"validUntil":"2026-10-14T00:00:00Z"}"""
                    : $$"""{"success":true,"id":"{{publishedPolicy}}","publicationId":"{{PublicationId}}","revision":{{(Uninitialized ? 1 : 12)}},"publishedActive":false}"""), PostStatus);
            }
            if (GetStatus != HttpStatusCode.OK) return Response("""{"title":"Unavailable"}""", GetStatus);
            if (path.EndsWith("reference-catalogue", StringComparison.Ordinal))
                return Response($$"""
                    {"revision":8,"profiles":[{"id":"CFG09","code":"CFG09","revision":8,
                    "basis":"VerifiedUnsafeObject","ground":"VerifiedUnsafeResource","bundle":"ObjectQuarantine",
                    "recognition":"Never","scopeKinds":["Resource"],"subjectKinds":["Content"],
                    "effects":[{"kind":"UnsafeResourceVersion","mode":"Quarantine","target":"SelectedResourceVersion","execution":"NativeOwner"}],
                    "prerequisites":["ExactResourceAudienceAndVersion"],"strategies":["PublicationIntegrity"],
                    "consequencePlans":{{PlansJson}}}]}
                    """);
            if (BlockGet)
            {
                GetEntered.TrySetResult();
                await ReleaseGet.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            }
            var proposed = Proposed();
            var prospective = request.RequestUri.Query.Split('&')
                .Select(part => part.TrimStart('?'))
                .FirstOrDefault(part => part.StartsWith("prospectivePolicyId=", StringComparison.Ordinal));
            var currentPolicyId = Uninitialized
                ? prospective is not null
                    ? Guid.Parse(Uri.UnescapeDataString(prospective["prospectivePolicyId=".Length..]))
                    : MintProspectiveIdentity ? Guid.CreateVersion7() : PolicyId
                : PolicyId;
            var authoring = Uninitialized || NullAuthoring ? "null" : JsonSerializer.Serialize(new ModerationCurrentPolicyAuthoringState
            {
                PublicationId = PredecessorId, HistoricalExpectedRevision = 2,
                CatalogueRevision = 8, InventoryRevision = 3, InventoryFingerprint = "retained",
                Modes = proposed.Modes, Classes = proposed.Classes, Reasons = proposed.Reasons,
                Profiles = proposed.Profiles.Select(p => new ModerationCurrentPolicyProfileView
                {
                    Definition = p,
                    Consequences = JsonSerializer.Deserialize<ModerationConsequenceModes[]>(PlansJson, Json)!
                }).ToArray(),
                SelectedProfiles = proposed.SupportedProfiles
            }, Json);
            var links = new Dictionary<string, HalLink>();
            if (PreparationHref is not null)
                links["publication-preparation"] = new()
                {
                    Href = Uninitialized ? PreparationHref.Replace(PolicyId.ToString("D"), currentPolicyId.ToString("D")) : PreparationHref,
                    Method = "POST"
                };
            if (DraftHref is not null) links["draft-publication"] = new()
            {
                Href = Uninitialized ? DraftHref.Replace(PolicyId.ToString("D"), currentPolicyId.ToString("D")) : DraftHref,
                Method = Method
            };
            return Response($$"""
                {"policyId":"{{currentPolicyId}}","scope":"{{Scope}}","state":"{{(Uninitialized ? "uninitialized" : "adopted")}}",
                 "jurisdiction":{"kind":"{{(Scope == "tenant" ? "Tenant" : "Instance")}}","id":"{{TenantId}}"},
                 "expectedHostBindingId":"{{HostId}}","expectedAuthoritySourceId":"{{SourceId}}",
                 "expectedQualificationRevision":23,"expectedWriterGeneration":"writer-current",
                 "expectedRevision":{{(Uninitialized ? 0 : Revision)}},"expectedPublicationId":{{(Uninitialized ? "null" : $"\"{PredecessorId}\"")}},
                 "installedCatalogueRevision":8,"installedInventoryRevision":17,"installedInventoryFingerprint":"installed",
                 "authoringBounds":{"jurisdiction":{"kind":"Tenant","id":"{{TenantId}}"},"scopeKinds":["Tenant"],
                 "subjectKinds":["Content"],"effects":[],"timingKinds":["Finite"],"maximumFiniteDuration":"1.00:00:00",
                 "maximumReviewDelay":"01:00:00","minimumApprovals":1,"maximumApprovals":4,"minimumContestationCalendarMonths":6},
                 "authoring":{{authoring}},"_links":{{JsonSerializer.Serialize(links, Json)}}}
                """);
        }

        private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/hal+json") };
    }
}
