using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Services.Moderation;

namespace Explore.Blazor.Client.Tests.Moderation;

public sealed class ModerationClassificationMaterialServiceTests
{
    [Test]
    public async Task GeneratedInventoryReadPreservesNativeCoordinatesAndCandidateMandate()
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var read = await Service(http).ReadInventoryAsync(Query());
        await Assert.That(read.Failure).IsEqualTo(ModerationClassificationMaterialFailure.None);
        var view = read.View!.Value;
        var material = Material(view.Materials.Single());
        await Assert.That(material.EventStamp).IsEqualTo(NativeStamp);
        await Assert.That(material.SessionId).IsNull();
        await Assert.That(material.ResourceStamp).IsNull();
        await Assert.That(view.Components.Single().NativeStamp).IsEqualTo(NativeStamp);
        await Assert.That(view.Categories.Single().VersionId).IsEqualTo(Id);
        var request = Body(transport.Requests.Single().Body!);
        await Assert.That(transport.Requests.Single().Path).IsEqualTo("/community/api/moderation/classification-materials/inventory");
        await Assert.That(request.GetProperty("mode").GetString()).IsEqualTo("Embedded");
        await Assert.That(request.GetProperty("mandate").GetProperty("reviewerHumanId").GetGuid()).IsEqualTo(ReviewerId);
        await Assert.That(request.EnumerateObject().Count()).IsEqualTo(5);
        await Assert.That(request.GetProperty("mandate").EnumerateObject().Count()).IsEqualTo(8);
    }

    [Test]
    public async Task SourceUsesOriginalDisplayedInventoryAndExactGeneratedSelection()
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        var displayed = (await service.ReadInventoryAsync(Query())).View!;
        var detached = displayed.Value;
        detached.ExpectedMaterialGeneration = 999;
        detached.Components.Single().Digest = "changed";
        var selections = Selections();
        selections.Single().AdditionalProperties["privateAge"] = 99;
        var mandate = Query().Mandate;
        mandate.AdditionalProperties["approved"] = true;
        var prepared = service.CreateSource(displayed, mandate, selections, SourceId, OperationId, Deadline);
        selections.Single().Material.EventStamp = Guid.Empty;
        mandate.ReviewGrounds = "changed";
        transport.Generation = 99;
        _ = await service.ReadInventoryAsync(Query());
        var result = await service.SubmitAsync(prepared.Operation!);
        await Assert.That(result.Failure).IsEqualTo(ModerationClassificationMaterialFailure.None);
        await Assert.That(result.Receipt!.Value.Id).IsEqualTo(SourceId);
        var body = Body(transport.Requests.Last().Body!);
        await Assert.That(body.GetProperty("expectedMaterialGeneration").GetInt64()).IsEqualTo(17L);
        await Assert.That(body.GetProperty("expectedQualificationRevision").GetInt64()).IsEqualTo(23L);
        await Assert.That(body.GetProperty("expectedPolicyRevision").GetInt64()).IsEqualTo(11L);
        await Assert.That(body.GetProperty("expectedTaxonomyAdoptionId").GetGuid()).IsEqualTo(Id);
        await Assert.That(body.GetProperty("components")[0].GetProperty("digest").GetString()).IsEqualTo("original");
        await Assert.That(body.GetProperty("materials")[0].GetProperty("reviewId").GetGuid()).IsEqualTo(ReviewId);
        await Assert.That(body.GetProperty("materials")[0].GetProperty("categoryVersionIds")[0].GetGuid()).IsEqualTo(Id);
        var material = body.GetProperty("materials")[0].GetProperty("material");
        await Assert.That(material.GetProperty("eventStamp").GetGuid()).IsEqualTo(NativeStamp);
        await Assert.That(material.TryGetProperty("parent", out _)).IsFalse();
        await Assert.That(body.GetProperty("materials")[0].TryGetProperty("privateAge", out _)).IsFalse();
        await Assert.That(body.GetProperty("mandate").TryGetProperty("approved", out _)).IsFalse();
        await Assert.That(body.GetProperty("mandate").GetProperty("reviewGrounds").GetString()).IsEqualTo("Independent review");
        await Assert.That(body.TryGetProperty("claims", out _)).IsFalse();
        await Assert.That(transport.Requests.Last().NoStore).IsTrue();
    }

    [Test]
    public async Task NominatedAcceptancePreservesDisplayedSourceRevisionAndWriter()
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        var displayed = (await service.ReadReviewAsync(SourceId)).View!;
        await Assert.That(displayed.Value.Materials.Single().CategoryVersionIds.Single()).IsEqualTo(Id);
        var detached = displayed.Value;
        detached.ExpectedSourceWriterGeneration = 999;
        var operation = service.CreateAcceptance(displayed, ReviewId, OperationId, Deadline).Operation!;
        transport.Writer = 99;
        _ = await service.ReadReviewAsync(SourceId);
        var result = await service.SubmitAsync(operation);
        await Assert.That(result.Failure).IsEqualTo(ModerationClassificationMaterialFailure.None);
        var body = Body(transport.Requests.Last().Body!);
        await Assert.That(body.GetProperty("sourceId").GetGuid()).IsEqualTo(SourceId);
        await Assert.That(body.GetProperty("expectedSourceRevision").GetInt64()).IsEqualTo(2L);
        await Assert.That(body.GetProperty("expectedSourceWriterGeneration").GetInt64()).IsEqualTo(31L);
        await Assert.That(body.GetProperty("expectedQualificationRevision").GetInt64()).IsEqualTo(23L);
        await Assert.That(body.GetProperty("reviewId").GetGuid()).IsEqualTo(ReviewId);
        await Assert.That(body.GetProperty("operationId").GetGuid()).IsEqualTo(OperationId);
        await Assert.That(body.EnumerateObject().Count()).IsEqualTo(8);
        await Assert.That(result.Receipt!.Value.Id).IsEqualTo(ReviewId);
    }

    [Test]
    [Arguments(false, null, "POST", false)]
    [Arguments(true, null, "POST", false)]
    [Arguments(false, "https://foreign.example/api/moderation/classification-materials/sources", "POST", false)]
    [Arguments(true, "//foreign.example/api/moderation/classification-materials/reviews", "POST", false)]
    [Arguments(false, "/api/moderation/classification-materials/sources", "GET", false)]
    [Arguments(true, "/api/moderation/classification-materials/reviews", "GET", false)]
    [Arguments(false, "/api/moderation/classification-materials/reviews", "POST", false)]
    [Arguments(true, "/api/moderation/classification-materials/sources", "POST", false)]
    [Arguments(false, "/api/moderation/classification-materials/sources?tenant=other", "POST", false)]
    [Arguments(true, "/api/moderation/classification-materials/reviews#other", "POST", false)]
    [Arguments(false, "/api/moderation/classification-materials/../sources", "POST", false)]
    [Arguments(true, "/api/moderation/classification-materials/%72eviews", "POST", false)]
    [Arguments(false, "/api/moderation/classification-materials/sources", "POST", true)]
    public async Task MissingMismatchedOrUnsafeActionsNeverWrite(bool acceptance, string? href, string method, bool templated)
    {
        using var transport = new Transport { OverrideLink = true, Href = href, Method = method, Templated = templated };
        using var http = Http(transport);
        var service = Service(http);
        var result = acceptance
            ? service.CreateAcceptance((await service.ReadReviewAsync(SourceId)).View!, ReviewId, OperationId, Deadline)
            : service.CreateSource((await service.ReadInventoryAsync(Query())).View!, Query().Mandate, Selections(), SourceId, OperationId, Deadline);
        await Assert.That(result.Failure).IsEqualTo(ModerationClassificationMaterialFailure.ActionUnavailable);
        await Assert.That(result.Operation).IsNull();
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UncertainReplayRetainsExactBodyIdentityAndDoesNotRefresh(bool acceptance)
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        var operation = acceptance
            ? service.CreateAcceptance((await service.ReadReviewAsync(SourceId)).View!, ReviewId, OperationId, Deadline).Operation!
            : service.CreateSource((await service.ReadInventoryAsync(Query())).View!, Query().Mandate, Selections(), SourceId, OperationId, Deadline).Operation!;
        transport.LoseResponse = true;
        var lost = await service.SubmitAsync(operation);
        await Assert.That(lost.Failure).IsEqualTo(ModerationClassificationMaterialFailure.Uncertain);
        await Assert.That(lost.Receipt).IsNull();
        transport.LoseResponse = false;
        transport.Generation = 99;
        transport.Writer = 99;
        var retry = await service.SubmitAsync(operation);
        await Assert.That(retry.Failure).IsEqualTo(ModerationClassificationMaterialFailure.None);
        await Assert.That(transport.Requests.Count).IsEqualTo(3);
        await Assert.That(transport.Requests[1].Body).IsEqualTo(transport.Requests[2].Body);
        await Assert.That(transport.Requests[1].Path).IsEqualTo(transport.Requests[2].Path);
    }

    [Test]
    [Arguments(400, ModerationClassificationMaterialFailure.InvalidIntent)]
    [Arguments(401, ModerationClassificationMaterialFailure.SignInRequired)]
    [Arguments(403, ModerationClassificationMaterialFailure.Forbidden)]
    [Arguments(404, ModerationClassificationMaterialFailure.Unavailable)]
    [Arguments(409, ModerationClassificationMaterialFailure.Conflict)]
    [Arguments(429, ModerationClassificationMaterialFailure.RateLimited)]
    [Arguments(503, ModerationClassificationMaterialFailure.Unavailable)]
    public async Task NativeProblemDetailsNeverFabricateReadOrMutationSuccess(int status, ModerationClassificationMaterialFailure expected)
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        var operation = service.CreateAcceptance((await service.ReadReviewAsync(SourceId)).View!, ReviewId, OperationId, Deadline).Operation!;
        transport.Status = (HttpStatusCode)status;
        var submitted = await service.SubmitAsync(operation);
        await Assert.That(submitted.Failure).IsEqualTo(expected);
        await Assert.That(submitted.Receipt).IsNull();
        await Assert.That((await service.ReadReviewAsync(SourceId)).Failure).IsEqualTo(expected);
        await Assert.That((await service.ReadInventoryAsync(Query())).Failure).IsEqualTo(expected);
        await Assert.That(Body(transport.Requests[1].Body!).GetProperty("expectedSourceWriterGeneration").GetInt64()).IsEqualTo(31L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CallerCancellationPropagatesAtHttpWithoutAutomaticRetry(bool mutation)
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        var operation = service.CreateAcceptance((await service.ReadReviewAsync(SourceId)).View!, ReviewId, OperationId, Deadline).Operation!;
        transport.Block = true;
        using var cancellation = new CancellationTokenSource();
        var pending = mutation ? service.SubmitAsync(operation, cancellation.Token) : CancelRead(service, cancellation.Token);
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
        await Assert.That(transport.Requests.Count).IsEqualTo(2);
    }

    private static async Task<ModerationClassificationMaterialSubmissionResult> CancelRead(
        ModerationClassificationMaterialService service, CancellationToken token)
    {
        _ = await service.ReadInventoryAsync(Query(), token);
        throw new InvalidOperationException("Canceled inventory unexpectedly completed.");
    }

    [Test]
    public async Task SelectionOutsideDisplayedMaterialOrCategoryCannotPrepareSource()
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        var view = (await service.ReadInventoryAsync(Query())).View!;
        var selection = Selections();
        selection.Single().Material.EventStamp = Guid.Empty;
        await Assert.That(service.CreateSource(view, Query().Mandate, selection, SourceId, OperationId, Deadline).Failure)
            .IsEqualTo(ModerationClassificationMaterialFailure.InvalidIntent);
        selection = Selections();
        selection.Single().CategoryVersionIds = [ReviewerId];
        await Assert.That(service.CreateSource(view, Query().Mandate, selection, SourceId, OperationId, Deadline).Failure)
            .IsEqualTo(ModerationClassificationMaterialFailure.InvalidIntent);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CrossOriginReplayAndNonFiniteAcceptanceAreRejected()
    {
        using var transport = new Transport();
        using var http = Http(transport);
        var service = Service(http);
        var view = (await service.ReadReviewAsync(SourceId)).View!;
        await Assert.That(service.CreateAcceptance(view, ReviewId, OperationId, DateTimeOffset.MaxValue).Failure)
            .IsEqualTo(ModerationClassificationMaterialFailure.InvalidIntent);
        var operation = service.CreateAcceptance(view, ReviewId, OperationId, Deadline).Operation!;
        using var otherHttp = new HttpClient(transport, disposeHandler: false) { BaseAddress = new Uri("https://other.example/") };
        await Assert.That(Service(otherHttp).CreateAcceptance(view, ReviewId, OperationId, Deadline).Failure)
            .IsEqualTo(ModerationClassificationMaterialFailure.ActionUnavailable);
        await Assert.That((await Service(otherHttp).SubmitAsync(operation)).Failure).IsEqualTo(ModerationClassificationMaterialFailure.ActionUnavailable);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WrongReceiptIdentityOrFalseSuccessCannotConfirmMutation(bool falseSuccess)
    {
        using var transport = new Transport
        {
            ReceiptBody = falseSuccess
            ? """{"success":false,"id":null,"revision":null,"qualificationRevision":null}"""
            : $$"""{"success":true,"id":"{{Id}}","revision":1,"qualificationRevision":23}"""
        };
        using var http = Http(transport);
        var service = Service(http);
        var operation = service.CreateAcceptance((await service.ReadReviewAsync(SourceId)).View!, ReviewId, OperationId, Deadline).Operation!;
        var result = await service.SubmitAsync(operation);
        await Assert.That(result.Failure).IsEqualTo(ModerationClassificationMaterialFailure.InvalidRepresentation);
        await Assert.That(result.Receipt).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeSessionAndResourceCoordinatesAndNullableComponentStampRoundTrip(bool session)
    {
        var materialJson = $$"""
            {"eventId":"{{NativeStamp}}","eventStamp":"{{NativeStamp}}",
             "sessionId":{{(session ? JsonSerializer.Serialize(NativeStamp) : "null")}},
             "sessionStamp":{{(session ? JsonSerializer.Serialize(NativeStamp) : "null")}},
             "resourceId":"{{NativeStamp}}","resourceStamp":"{{NativeStamp}}"}
            """;
        using var transport = new Transport { MaterialBody = materialJson, NullComponentStamp = true };
        using var http = Http(transport);
        var service = Service(http);
        var view = (await service.ReadInventoryAsync(Query())).View!;
        var selections = Selections();
        selections.Single().Material = Material(Body(materialJson));
        var operation = service.CreateSource(view, Query().Mandate, selections, SourceId, OperationId, Deadline).Operation!;
        await Assert.That((await service.SubmitAsync(operation)).Failure).IsEqualTo(ModerationClassificationMaterialFailure.None);
        var captured = JsonSerializer.Deserialize<RecordModerationClassificationMaterialCommand>(transport.Requests.Last().Body!, Json)!;
        await Assert.That(captured.Materials.Single().Material.ResourceStamp).IsEqualTo((Guid?)NativeStamp);
        await Assert.That(captured.Materials.Single().Material.SessionStamp).IsEqualTo(session ? (Guid?)NativeStamp : null);
        await Assert.That(captured.Components.Single().NativeStamp).IsNull();
    }

    [Test]
    public async Task SameOriginAbsoluteNativeActionWorksAndRetainedAcceptanceClosesRead()
    {
        using var transport = new Transport
        {
            OverrideLink = true,
            Href = "https://event.example/api/moderation/classification-materials/reviews"
        };
        using var http = Http(transport);
        var service = Service(http);
        var view = (await service.ReadReviewAsync(SourceId)).View!;
        var operation = service.CreateAcceptance(view, ReviewId, OperationId, Deadline).Operation!;
        await Assert.That((await service.SubmitAsync(operation)).Failure).IsEqualTo(ModerationClassificationMaterialFailure.None);
        await Assert.That(transport.Requests.Last().Path).IsEqualTo("/community/api/moderation/classification-materials/reviews");
        transport.Status = HttpStatusCode.NotFound;
        var closed = await service.ReadReviewAsync(SourceId);
        await Assert.That(closed.View).IsNull();
        await Assert.That(closed.Failure).IsEqualTo(ModerationClassificationMaterialFailure.Unavailable);
        await Assert.That(view.Value.ExpectedSourceWriterGeneration).IsEqualTo(31L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TransportLossOnKnownReadIsUnavailableWithoutCachedProposal(bool review)
    {
        using var transport = new Transport { LoseRead = true };
        using var http = Http(transport);
        var service = Service(http);
        if (review)
        {
            var result = await service.ReadReviewAsync(SourceId);
            await Assert.That(result.View).IsNull();
            await Assert.That(result.Failure).IsEqualTo(ModerationClassificationMaterialFailure.Unavailable);
        }
        else
        {
            var result = await service.ReadInventoryAsync(Query());
            await Assert.That(result.View).IsNull();
            await Assert.That(result.Failure).IsEqualTo(ModerationClassificationMaterialFailure.Unavailable);
        }
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    private static readonly Guid Id = Guid.Parse("0199aabb-1234-7000-8000-000000000001");
    private static readonly Guid SourceId = Guid.Parse("0199aabb-1234-7000-8000-000000000002");
    private static readonly Guid ReviewerId = Guid.Parse("0199aabb-1234-7000-8000-000000000003");
    private static readonly Guid ReviewId = Guid.Parse("0199aabb-1234-7000-8000-000000000004");
    private static readonly Guid OperationId = Guid.Parse("0199aabb-1234-7000-8000-000000000005");
    private static readonly Guid NativeStamp = Guid.Parse("e138697d-04b0-4f6b-9e2e-a3de14a6706e");
    private static readonly DateTimeOffset Deadline = DateTimeOffset.Parse("2026-11-01T00:00:00Z");
    private static readonly JsonSerializerOptions Json = EventApiJsonSerializerSettings.Configure(new());
    private static HttpClient Http(Transport transport) => new(transport) { BaseAddress = new Uri("https://event.example/community/") };
    private static ModerationClassificationMaterialService Service(HttpClient http) =>
        new(new ModerationClassificationMaterialInventoryClient(http), new ModerationClassificationMaterialReviewClient(http), http);
    private static JsonElement Body(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
    private static ModerationClassificationMaterial Material(object value) =>
        JsonSerializer.Deserialize<ModerationClassificationMaterial>(JsonSerializer.Serialize(value, Json), Json)!;
    private static GetModerationClassificationMaterialInventoryQuery Query() =>
        JsonSerializer.Deserialize<GetModerationClassificationMaterialInventoryQuery>($$"""
        {"eventGenerationId":"{{Id}}","taxonomyId":"{{Id}}","profileId":"{{Id}}","mode":"Embedded",
         "mandate":{"ownerHumanId":"{{Id}}","ownerControllerHumanId":"{{Id}}","ownerAssignmentId":"{{Id}}",
         "ownerAssignmentRevision":1,"reviewerHumanId":"{{ReviewerId}}","reviewerControllerHumanId":"{{ReviewerId}}",
         "ownershipGrounds":"Native owner","reviewGrounds":"Independent review"} }
        """, Json)!;
    private static List<ModerationClassificationMaterialSelection> Selections() =>
        [new() { ReviewId = ReviewId, Material = Material(Body(MaterialJson)), CategoryVersionIds = [Id] }];
    private static string MaterialJson => $$"""
        {"eventId":"{{NativeStamp}}","eventStamp":"{{NativeStamp}}","sessionId":null,"sessionStamp":null,"resourceId":null,"resourceStamp":null}
        """;

    private sealed class Transport : HttpMessageHandler
    {
        internal long Generation { get; set; } = 17;
        internal long Writer { get; set; } = 31;
        internal bool OverrideLink { get; set; }
        internal string? Href { get; set; }
        internal string Method { get; set; } = "POST";
        internal bool Templated { get; set; }
        internal bool LoseResponse { get; set; }
        internal bool LoseRead { get; set; }
        internal string MaterialBody { get; set; } = MaterialJson;
        internal bool NullComponentStamp { get; set; }
        internal bool Block { get; set; }
        internal string? ReceiptBody { get; set; }
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<(string Path, string? Body, bool NoStore)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add((path, request.Content is null ? null : await request.Content.ReadAsStringAsync(token),
                request.Headers.CacheControl?.NoStore == true));
            if (Block)
            {
                Entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
            if (Status != HttpStatusCode.OK)
                return Response($$"""{"title":"Native material unavailable","status":{{(int)Status}}}""", Status, "application/problem+json");
            if (path.EndsWith("/sources", StringComparison.Ordinal) || path.EndsWith("/reviews", StringComparison.Ordinal))
            {
                if (LoseResponse) throw new HttpRequestException("Committed response lost.");
                var id = path.EndsWith("/sources", StringComparison.Ordinal) ? SourceId : ReviewId;
                return Response(ReceiptBody ?? $$"""{"success":true,"id":"{{id}}","revision":1,"qualificationRevision":23}""");
            }
            if (LoseRead) throw new HttpRequestException("Read unavailable.");
            var acceptance = request.Method == HttpMethod.Get;
            var href = OverrideLink ? Href : "/api/moderation/classification-materials/" + (acceptance ? "reviews" : "sources");
            var link = href is null ? "{}" : $$"""
                {"{{(acceptance ? "accept-material-source" : "record-material-source")}}":
                 {"href":{{JsonSerializer.Serialize(href)}},"method":"{{Method}}","templated":{{Templated.ToString().ToLowerInvariant()}} } }
                """;
            var common = $$"""
                "target":{"kind":"Tenant","id":"{{Id}}"},"expectedQualificationRevision":23,
                "eventGenerationId":"{{Id}}","taxonomyId":"{{Id}}","expectedTaxonomySourceId":"{{Id}}",
                "expectedTaxonomyAdoptionId":"{{Id}}",
                "components":[{"kind":"Event","nativeId":"{{NativeStamp}}","nativeStamp":{{(NullComponentStamp ? "null" : JsonSerializer.Serialize(NativeStamp))}},"digest":"original"}],
                "categories":[{"versionId":"{{Id}}","categoryId":1,"revision":2,"code":"general","kind":"General",
                "minimumAge":0,"meaning":"General","reviewRationale":"Native rationale"}],
                "validUntil":"2026-11-01T00:00:00Z","_links":{{link}}
                """;
            return Response(acceptance ? $$"""
                { {{common}},"sourceId":"{{SourceId}}","expectedSourceRevision":2,"expectedSourceWriterGeneration":{{Writer}},
                  "mandate":{{JsonSerializer.Serialize(Query().Mandate, Json)}},
                  "materials":[{"reviewId":"{{ReviewId}}","material":{{MaterialBody}},"categoryVersionIds":["{{Id}}"]}] }
                """ : $$"""
                { {{common}},"expectedMaterialGeneration":{{Generation}},"policyId":"{{Id}}","expectedPolicyRevision":11,
                  "publicationId":"{{Id}}","profileId":"{{Id}}","mode":"Embedded","materials":[{{MaterialBody}}] }
                """);
        }
        private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK, string media = "application/json")
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, media) };
            response.Headers.CacheControl = new() { NoStore = true };
            return response;
        }
    }
}
