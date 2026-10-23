using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Pages.Moderation;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Services.Accessibility;
using Explore.Blazor.Client.Services.Moderation;

namespace Explore.Blazor.Client.Tests.Moderation;

/// <summary>Real generated HTTP transport through the guided editor; no native authority substitutes.</summary>
public sealed class PolicyAdministrationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PresentationChangesPreserveCompleteRetainedWireMeaning(bool advanced)
    {
        using var fixture = new Fixture();
        var page = fixture.Render();
        await page.Find("#policy-mode").ChangeAsync(new ChangeEventArgs { Value = advanced ? "Advanced" : "Simple" });
        await page.Find("#publish-draft").ClickAsync(new MouseEventArgs());
        var proposed = fixture.Transport.Proposed();
        await Assert.That(proposed.GetProperty("profiles").GetArrayLength()).IsEqualTo(2);
        await Assert.That(proposed.GetProperty("supportedProfiles").GetArrayLength()).IsEqualTo(1);
        await Assert.That(proposed.GetProperty("inventoryFingerprint").GetString()).IsEqualTo("historical");
        await Assert.That(proposed.GetProperty("modes")[1].GetString()).IsEqualTo("RemoteStrict");
        await Assert.That(proposed.GetProperty("profiles")[1].GetProperty("rule").GetProperty("code").GetString())
            .IsEqualTo("disabled");
        await Assert.That(proposed.GetProperty("profiles")[1].GetProperty("contestationCalendarMonths").GetInt32())
            .IsEqualTo(9);
        foreach (var name in new[] { "classes", "reasons", "modes" })
            await Assert.That(proposed.GetProperty(name).GetRawText())
                .IsEqualTo(fixture.Transport.InitialAuthoring.GetProperty(name).GetRawText());
        for (var index = 0; index < 2; index++)
            await Assert.That(proposed.GetProperty("profiles")[index].GetRawText())
                .IsEqualTo(fixture.Transport.InitialAuthoring.GetProperty("profiles")[index].GetProperty("definition").GetRawText());
        await Assert.That(fixture.Transport.Posts.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ChangedDisplayedHeadSuppressesPostWithoutReplacingEdits(bool policyChanged)
    {
        using var fixture = new Fixture();
        var page = fixture.Render();
        if (policyChanged) fixture.Transport.PolicyId = Guid.CreateVersion7();
        else fixture.Transport.Revision++;
        await page.Find("#publish-draft").ClickAsync(new MouseEventArgs());
        await Assert.That(fixture.Transport.Posts).IsEmpty();
        await Assert.That(page.Find("[data-failure]").GetAttribute("data-failure")).IsEqualTo("Conflict");
        await Assert.That(page.FindAll("[data-authoring-profile]").Count).IsEqualTo(2);
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(false, false)]
    public async Task DraftAndPreparationAffordancesAreIndependent(bool draft, bool preparation)
    {
        using var fixture = new Fixture();
        fixture.Transport.Draft = draft;
        fixture.Transport.Preparation = preparation;
        var page = fixture.Render();
        await Assert.That(page.FindAll("#publish-draft").Count).IsEqualTo(draft ? 1 : 0);
        await Assert.That(page.FindAll("#prepare-publication").Count).IsEqualTo(preparation ? 1 : 0);
    }

    [Test]
    public async Task PreparationDisplaysCandidateWithoutAdoptingOrRefreshing()
    {
        using var fixture = new Fixture();
        var page = fixture.Render();
        var reads = fixture.Transport.Reads;
        await page.Find("#candidate-deadline").ChangeAsync(new ChangeEventArgs { Value = "2030-01-01T00:00:00Z" });
        await page.Find("#prepare-publication").ClickAsync(new MouseEventArgs());
        await Assert.That(page.FindAll("[data-candidate]").Count).IsEqualTo(1);
        await Assert.That(page.FindAll("[data-publication]").Count).IsEqualTo(0);
        await Assert.That(fixture.Transport.Reads).IsEqualTo(reads + 1);
        await Assert.That(fixture.Transport.Proposed().GetProperty("requestedState").GetString()).IsEqualTo("Active");
        await Assert.That(page.Find("[data-current-revision]").GetAttribute("data-current-revision")).IsEqualTo("7");
        await Assert.That(fixture.Transport.Posts.Single().Path.EndsWith("publication-preparations", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task UncertainReceiptRequiresDeliberateExactRetry()
    {
        using var fixture = new Fixture();
        fixture.Transport.LoseResponse = true;
        var page = fixture.Render();
        await page.Find("#publish-draft").ClickAsync(new MouseEventArgs());
        await Assert.That(fixture.Transport.Posts.Count).IsEqualTo(1);
        await Assert.That(page.FindAll("[data-authoring-profile]").Count).IsEqualTo(2);
        fixture.Transport.LoseResponse = false;
        await page.Find("#retry-receipt").ClickAsync(new MouseEventArgs());
        await Assert.That(fixture.Transport.Posts[1].Body).IsEqualTo(fixture.Transport.Posts[0].Body);
        await Assert.That(fixture.Transport.Reads).IsEqualTo(2);
        await Assert.That(page.FindAll("[data-publication]").Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(HttpStatusCode.Forbidden)]
    [Arguments(HttpStatusCode.Conflict)]
    [Arguments(HttpStatusCode.BadRequest)]
    [Arguments(HttpStatusCode.ServiceUnavailable)]
    public async Task RejectedPublicationRetainsAllWorkWithoutAutomaticRetry(HttpStatusCode status)
    {
        using var fixture = new Fixture();
        fixture.Transport.PostStatus = status;
        var page = fixture.Render();
        await page.Find("#publish-draft").ClickAsync(new MouseEventArgs());
        await Assert.That(fixture.Transport.Posts.Count).IsEqualTo(1);
        await Assert.That(page.FindAll("[data-authoring-profile]").Count).IsEqualTo(2);
        await Assert.That(page.FindAll("[data-failure]").Count).IsEqualTo(1);
    }

    [Test]
    public async Task CancellationAfterPostKeepsIdentityAndBlocksDuplicateSubmission()
    {
        using var fixture = new Fixture();
        fixture.Transport.BlockPost = true;
        var page = fixture.Render();
        var entered = fixture.Transport.PostEntered.Task;
        var pending = page.Find("#publish-draft").ClickAsync(new MouseEventArgs());
        await entered.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(page.Find("#publish-draft").HasAttribute("disabled")).IsTrue();
        await page.Find("#cancel-operation").ClickAsync(new MouseEventArgs());
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(fixture.Transport.Posts.Count).IsEqualTo(1);
        await Assert.That(page.Find("[data-failure]").GetAttribute("data-failure")).IsEqualTo("Uncertain");
        await Assert.That(page.FindAll("#retry-receipt").Count).IsEqualTo(1);
    }

    [Test]
    public async Task MeaningChangesAppendRevisionAndRetainOriginalClass()
    {
        using var fixture = new Fixture();
        var page = fixture.Render();
        await page.Find("[data-revise-class]").ClickAsync(new MouseEventArgs());
        await page.Find("#class-meaning").ChangeAsync(new ChangeEventArgs { Value = "Explicitly revised class meaning" });
        await page.Find("form").SubmitAsync();
        await page.Find("#publish-draft").ClickAsync(new MouseEventArgs());
        var classes = fixture.Transport.Proposed().GetProperty("classes");
        await Assert.That(classes.GetArrayLength()).IsEqualTo(2);
        await Assert.That(classes[0].GetProperty("meaning").GetString()).IsEqualTo("Original class");
        await Assert.That(classes[0].GetProperty("reference").GetProperty("revision").GetInt64()).IsEqualTo(1L);
        await Assert.That(classes[1].GetProperty("reference").GetProperty("revision").GetInt64()).IsEqualTo(2L);
    }

    [Test]
    public async Task DisabledProfileCanBeRevisedWithoutLosingHistoricalDefinition()
    {
        using var fixture = new Fixture();
        var page = fixture.Render();
        await page.Find("[data-authoring-profile='disabled'] [data-revise-profile]").ClickAsync(new MouseEventArgs());
        await page.Find("#profile-months").ChangeAsync(new ChangeEventArgs { Value = "10" });
        await page.Find(".policy-authoring-profile form").SubmitAsync();
        await page.Find("#publish-draft").ClickAsync(new MouseEventArgs());
        var profiles = fixture.Transport.Proposed().GetProperty("profiles");
        await Assert.That(profiles.GetArrayLength()).IsEqualTo(3);
        await Assert.That(profiles[1].GetProperty("contestationCalendarMonths").GetInt32()).IsEqualTo(9);
        await Assert.That(profiles[2].GetProperty("contestationCalendarMonths").GetInt32()).IsEqualTo(10);
        await Assert.That(profiles[2].GetProperty("id").GetGuid()).IsNotEqualTo(profiles[1].GetProperty("id").GetGuid());
        await Assert.That(fixture.Transport.Proposed().GetProperty("supportedProfiles").GetArrayLength()).IsEqualTo(1);
    }

    [Test]
    public async Task UninitializedSlotRequiresExplicitLocalCreationAndNeverReservesOnRead()
    {
        using var fixture = new Fixture();
        fixture.Transport.Uninitialized = true;
        var page = fixture.Render();
        await Assert.That(page.FindAll("#create-policy").Count).IsEqualTo(1);
        await Assert.That(page.FindAll("#publish-draft").Count).IsEqualTo(0);
        await Assert.That(fixture.Transport.Posts).IsEmpty();
        await page.Find("#create-policy").ClickAsync(new MouseEventArgs());
        await Assert.That(page.FindAll("[data-authoring-profile]").Count).IsEqualTo(0);
        await Assert.That(fixture.Transport.Posts).IsEmpty();
    }

    [Test]
    public async Task ScopeReadUsesActualRequestContextAndControlsHaveConnectedLabels()
    {
        using var fixture = new Fixture();
        var page = fixture.Render();
        await page.Find("#policy-scope").ChangeAsync(new ChangeEventArgs { Value = "Instance" });
        await Assert.That(fixture.Transport.LastScope).IsEqualTo("instance");
        await Assert.That(page.FindAll("h1").Count).IsEqualTo(1);
        foreach (var control in page.FindAll("input, select, textarea"))
        {
            var id = control.Id;
            await Assert.That(page.FindAll($"label[for='{id}']").Count).IsEqualTo(1);
            foreach (var description in (control.GetAttribute("aria-describedby") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                await Assert.That(page.FindAll($"#{description}").Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task InvalidResultCanReturnToEditingWithoutDroppingWorkOrReceiptIdentity()
    {
        using var fixture = new Fixture();
        fixture.Transport.PostStatus = HttpStatusCode.BadRequest;
        var page = fixture.Render();
        await page.Find("#publish-draft").ClickAsync(new MouseEventArgs());
        var first = fixture.Transport.Proposed().GetProperty("publicationId").GetGuid();
        await page.Find("#resume-authoring").ClickAsync(new MouseEventArgs());
        await page.Find("[data-revise-class]").ClickAsync(new MouseEventArgs());
        await page.Find("#class-meaning").ChangeAsync(new ChangeEventArgs { Value = "Corrected proposal meaning" });
        await page.Find("form").SubmitAsync();
        await Assert.That(page.FindAll("[data-authoring-class]").Count).IsEqualTo(2);
        await Assert.That(page.Find("[data-receipt-publication]").GetAttribute("data-receipt-publication")).IsEqualTo(first.ToString());
        await Assert.That(fixture.Transport.Posts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task NewProfileRequiresDeliberateResponseAndKeepsEveryChosenField()
    {
        using var fixture = new Fixture();
        var page = fixture.Render();
        await page.Find("#add-profile").ClickAsync(new MouseEventArgs());
        await page.Find("#profile-code").ChangeAsync(new ChangeEventArgs { Value = "new.warning" });
        await page.Find("#profile-template").ChangeAsync(new ChangeEventArgs { Value = "CFG01" });
        await page.Find(".policy-authoring-profile form").SubmitAsync();
        await Assert.That(page.FindAll("[data-authoring-profile]").Count).IsEqualTo(2);
        var reason = page.FindAll("#profile-reason option").Last().GetAttribute("value");
        await page.Find("#profile-reason").ChangeAsync(new ChangeEventArgs { Value = reason });
        await page.Find("#profile-grade").ChangeAsync(new ChangeEventArgs { Value = "G0" });
        await page.Find("#profile-scope-Tenant").ChangeAsync(new ChangeEventArgs { Value = true });
        await page.Find("#profile-subject-Content").ChangeAsync(new ChangeEventArgs { Value = true });
        await page.Find("#profile-select").ChangeAsync(new ChangeEventArgs { Value = true });
        await page.Find(".policy-authoring-profile form").SubmitAsync();
        await page.Find("#publish-draft").ClickAsync(new MouseEventArgs());
        var proposed = fixture.Transport.Proposed();
        var added = proposed.GetProperty("profiles")[2];
        await Assert.That(added.GetProperty("rule").GetProperty("code").GetString()).IsEqualTo("new.warning");
        await Assert.That(added.GetProperty("grade").GetString()).IsEqualTo("G0");
        await Assert.That(added.GetProperty("recognition").GetString()).IsEqualTo("Never");
        await Assert.That(added.GetProperty("timing").GetProperty("duration").GetString()).IsEqualTo("00:00:00");
        await Assert.That(added.GetProperty("scopeKinds")[0].GetString()).IsEqualTo("Tenant");
        await Assert.That(added.GetProperty("subjectKinds")[0].GetString()).IsEqualTo("Content");
        await Assert.That(proposed.GetProperty("supportedProfiles").GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task InventoryAlignmentIsExplicitAndDoesNotMigrateHistoricalDefinitions()
    {
        using var fixture = new Fixture();
        var page = fixture.Render();
        await page.Find("#align-inventory").ClickAsync(new MouseEventArgs());
        await page.Find("#publish-draft").ClickAsync(new MouseEventArgs());
        var proposed = fixture.Transport.Proposed();
        await Assert.That(proposed.GetProperty("inventoryRevision").GetInt64()).IsEqualTo(9L);
        await Assert.That(proposed.GetProperty("inventoryFingerprint").GetString()).IsEqualTo("installed");
        await Assert.That(proposed.GetProperty("profiles").GetArrayLength()).IsEqualTo(2);
        await Assert.That(proposed.GetProperty("classes")[0].GetProperty("meaning").GetString()).IsEqualTo("Original class");
    }

    [Test]
    public async Task NativeModeChangesAreExplicitAndPresentationDoesNotRestoreThem()
    {
        using var fixture = new Fixture();
        var page = fixture.Render();
        await page.Find("#native-mode-RemoteStrict").ChangeAsync(new ChangeEventArgs { Value = false });
        await page.Find("#policy-mode").ChangeAsync(new ChangeEventArgs { Value = "Advanced" });
        await page.Find("#publish-draft").ClickAsync(new MouseEventArgs());
        var modes = fixture.Transport.Proposed().GetProperty("modes");
        await Assert.That(modes.GetArrayLength()).IsEqualTo(1);
        await Assert.That(modes[0].GetString()).IsEqualTo("Embedded");
    }

    [Test]
    public async Task UnsavedMeaningEditPreventsScopeReplacement()
    {
        using var fixture = new Fixture();
        var page = fixture.Render();
        await page.Find("[data-revise-class]").ClickAsync(new MouseEventArgs());
        await page.Find("#class-meaning").ChangeAsync(new ChangeEventArgs { Value = "Unsaved intent" });
        await Assert.That(page.Find("#policy-scope").HasAttribute("disabled")).IsTrue();
        await page.Find("#policy-mode").ChangeAsync(new ChangeEventArgs { Value = "Advanced" });
        await Assert.That(fixture.Transport.Reads).IsEqualTo(1);
        await page.Find("form").SubmitAsync();
        await page.Find("#publish-draft").ClickAsync(new MouseEventArgs());
        await Assert.That(fixture.Transport.Proposed().GetProperty("classes")[1].GetProperty("meaning").GetString()).IsEqualTo("Unsaved intent");
    }

    [Test]
    [Arguments("class")]
    [Arguments("reason")]
    [Arguments("profile")]
    public async Task StagedEditorControlsHaveConnectedLabelsAndDescriptions(string editor)
    {
        using var fixture = new Fixture();
        var page = fixture.Render();
        await page.Find("#add-" + editor).ClickAsync(new MouseEventArgs());
        foreach (var control in page.FindAll("input, select, textarea"))
        {
            await Assert.That(page.FindAll($"label[for='{control.Id}']").Count).IsEqualTo(1);
            var descriptions = (control.GetAttribute("aria-describedby") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            await Assert.That(descriptions.Length).IsGreaterThan(0);
            foreach (var description in descriptions)
                await Assert.That(page.FindAll("#" + description).Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task CancellingExplicitRefreshDoesNotEraseRetainedDefinitions()
    {
        using var fixture = new Fixture();
        var page = fixture.Render();
        fixture.Transport.BlockRead = true;
        var entered = fixture.Transport.ReadEntered.Task;
        var pending = page.Find("#read-current").ClickAsync(new MouseEventArgs());
        await entered.WaitAsync(TimeSpan.FromSeconds(5));
        await page.Find("#cancel-operation").ClickAsync(new MouseEventArgs());
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(page.FindAll("[data-authoring-profile]").Count).IsEqualTo(2);
        await Assert.That(fixture.Transport.Posts).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnreservedCreationRequiresNativeEchoWithoutBypassingHeadGuard(bool ignoreProspectiveIdentity)
    {
        using var fixture = new Fixture();
        fixture.Transport.Uninitialized = true;
        fixture.Transport.RotateProspectiveIdentity = true;
        fixture.Transport.IgnoreProspectiveIdentity = ignoreProspectiveIdentity;
        var page = fixture.Render();
        var prospective = fixture.Transport.PolicyId;
        await page.Find("#create-policy").ClickAsync(new MouseEventArgs());
        await page.Find("#native-mode-Embedded").ChangeAsync(new ChangeEventArgs { Value = true });
        await page.Find("#add-class").ClickAsync(new MouseEventArgs());
        await page.Find("#class-code").ChangeAsync(new ChangeEventArgs { Value = "warning.class" });
        await page.Find("#class-meaning").ChangeAsync(new ChangeEventArgs { Value = "A deliberately authored class" });
        await page.Find("#class-template").ChangeAsync(new ChangeEventArgs { Value = "CFG01" });
        await page.Find("form").SubmitAsync();
        await page.Find("#add-reason").ClickAsync(new MouseEventArgs());
        await page.Find("#reason-code").ChangeAsync(new ChangeEventArgs { Value = "warning.reason" });
        await page.Find("#reason-meaning").ChangeAsync(new ChangeEventArgs { Value = "A deliberately authored reason" });
        await page.Find("#reason-class").ChangeAsync(new ChangeEventArgs
            { Value = page.FindAll("#reason-class option").Last().GetAttribute("value") });
        await page.Find("form").SubmitAsync();
        await page.Find("#add-profile").ClickAsync(new MouseEventArgs());
        await page.Find("#profile-code").ChangeAsync(new ChangeEventArgs { Value = "warning.profile" });
        await page.Find("#profile-template").ChangeAsync(new ChangeEventArgs { Value = "CFG01" });
        await page.Find("#profile-reason").ChangeAsync(new ChangeEventArgs
            { Value = page.FindAll("#profile-reason option").Last().GetAttribute("value") });
        await page.Find("#profile-grade").ChangeAsync(new ChangeEventArgs { Value = "G0" });
        await page.Find("#profile-scope-Tenant").ChangeAsync(new ChangeEventArgs { Value = true });
        await page.Find("#profile-subject-Content").ChangeAsync(new ChangeEventArgs { Value = true });
        await page.Find(".policy-authoring-profile form").SubmitAsync();
        await page.Find("#publish-draft").ClickAsync(new MouseEventArgs());
        await Assert.That(fixture.Transport.Reads).IsEqualTo(2);
        await Assert.That(fixture.Transport.Posts.Count).IsEqualTo(ignoreProspectiveIdentity ? 0 : 1);
        if (ignoreProspectiveIdentity)
            await Assert.That(page.Find("[data-failure]").GetAttribute("data-failure")).IsEqualTo("Conflict");
        else
        {
            await Assert.That(page.FindAll("[data-failure]").Count).IsEqualTo(0);
            await Assert.That(fixture.Transport.Proposed().GetProperty("policyId").GetGuid()).IsEqualTo(prospective);
            await Assert.That(page.FindAll("[data-publication]").Count).IsEqualTo(1);
        }
        await Assert.That(page.FindAll("[data-authoring-profile]").Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("class")]
    [Arguments("reason")]
    [Arguments("profile")]
    public async Task CancellingAnEditorReturnsFocusToItsSectionWithoutChangingPolicy(string editor)
    {
        using var fixture = new Fixture();
        fixture.Context.Services.AddScoped<IAccessibilityFocusService, AccessibilityFocusService>();
        var focus = fixture.Context.JSInterop.SetupModule("/js/accessibility.js");
        focus.SetupVoid("setFocusById", _ => true).SetVoidResult();
        var page = fixture.Render();
        await page.Find("#add-" + editor).ClickAsync(new MouseEventArgs());
        await page.Find("form button[type='button']").ClickAsync(new MouseEventArgs());

        var target = (string)focus.Invocations.Last(invocation => invocation.Identifier == "setFocusById").Arguments[0]!;
        await Assert.That(target).IsEqualTo("add-" + editor);
        await Assert.That(page.Find("#" + target).HasAttribute("disabled")).IsFalse();
        await Assert.That(page.FindAll("form")).IsEmpty();
        await Assert.That(page.FindAll("[data-authoring-profile]").Count).IsEqualTo(2);
        await Assert.That(fixture.Transport.Posts).IsEmpty();
    }

    private sealed class Fixture : IDisposable
    {
        internal BlazorTestContext Context { get; } = new();
        internal PolicyAdministrationHttp Transport { get; } = new();
        private readonly HttpClient http;

        internal Fixture()
        {
            http = new(Transport) { BaseAddress = new Uri("https://event.example/community/") };
            Context.Services.AddSingleton(new ModerationPolicyAdministrationService(
                new ModerationCurrentPolicyClient(http), new ModerationReferenceCatalogueClient(http), http));
        }

        internal IRenderedComponent<PolicyAdministration> Render() => Context.Render<PolicyAdministration>();
        public void Dispose() { Context.Dispose(); http.Dispose(); }
    }

    private sealed class PolicyAdministrationHttp : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions Json = EventApiJsonSerializerSettings.Configure(new());
        internal Guid PolicyId { get; set; } = Guid.CreateVersion7();
        private readonly Guid owner = Guid.CreateVersion7();
        private readonly Guid previous = Guid.CreateVersion7();
        internal long Revision { get; set; } = 7;
        internal bool Draft { get; set; } = true;
        internal bool Preparation { get; set; } = true;
        internal bool Uninitialized { get; set; }
        internal bool RotateProspectiveIdentity { get; set; }
        internal bool IgnoreProspectiveIdentity { get; set; }
        internal bool LoseResponse { get; set; }
        internal bool BlockPost { get; set; }
        internal bool BlockRead { get; set; }
        internal HttpStatusCode PostStatus { get; set; } = HttpStatusCode.OK;
        internal int Reads { get; private set; }
        internal JsonElement InitialAuthoring { get; private set; }
        internal string LastScope { get; private set; } = "tenant";
        internal List<(string Path, string Body)> Posts { get; } = [];
        internal TaskCompletionSource PostEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal JsonElement Proposed()
        {
            using var document = JsonDocument.Parse(Posts.Last().Body);
            return document.RootElement.GetProperty("request").GetProperty("proposed").Clone();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadAsStringAsync(token);
                Posts.Add((request.RequestUri!.AbsolutePath, body));
                PostEntered.TrySetResult();
                if (BlockPost)
                {
                    var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var registration = token.Register(() => cancelled.TrySetCanceled(token));
                    await cancelled.Task;
                }
                if (LoseResponse) throw new HttpRequestException("Delivered response lost");
                using var document = JsonDocument.Parse(body);
                var proposed = document.RootElement.GetProperty("request").GetProperty("proposed");
                var publication = proposed.GetProperty("publicationId").GetGuid();
                var revision = proposed.GetProperty("expectedRevision").GetInt64() + 1;
                return request.RequestUri.AbsolutePath.EndsWith("publication-preparations", StringComparison.Ordinal)
                    ? Response(new { success = true, id = publication, revision, validUntil = document.RootElement.GetProperty("validUntil").GetDateTimeOffset() }, PostStatus)
                    : Response(new { success = true, id = PolicyId, publicationId = publication, revision, publishedActive = false }, PostStatus);
            }
            if (request.RequestUri!.AbsolutePath.EndsWith("reference-catalogue", StringComparison.Ordinal))
                return Response(new { revision = 8, profiles = new[] { Reference() } });
            if (BlockRead)
            {
                ReadEntered.TrySetResult();
                var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = token.Register(() => cancelled.TrySetCanceled(token));
                await cancelled.Task;
            }
            Reads++;
            if (Uninitialized && RotateProspectiveIdentity)
            {
                var prospective = request.RequestUri.Query.Split('&')
                    .Select(part => part.TrimStart('?'))
                    .FirstOrDefault(part => part.StartsWith("prospectivePolicyId=", StringComparison.Ordinal));
                PolicyId = !IgnoreProspectiveIdentity && prospective is not null
                    ? Guid.Parse(Uri.UnescapeDataString(prospective["prospectivePolicyId=".Length..]))
                    : Guid.CreateVersion7();
            }
            LastScope = request.RequestUri.Query.Contains("instance", StringComparison.Ordinal) ? "instance" : "tenant";
            var classification = new ModerationPolicyClassDefinition
            {
                Reference = Rule("class"), Meaning = "Original class",
                Ground = ModerationReferenceGround.CompatibleConfirmedConduct, Basis = ModerationReferenceBasis.ConfirmedWarning
            };
            var reason = new ModerationPolicyReasonDefinition { Reference = Rule("reason"), Meaning = "Original reason", Class = classification.Reference };
            var links = new Dictionary<string, HalLink>();
            if (Draft) links["draft-publication"] = new() { Href = $"/api/moderation/policies/{PolicyId:D}/publications", Method = "POST" };
            if (Preparation) links["publication-preparation"] = new() { Href = $"/api/moderation/policies/{PolicyId:D}/publication-preparations", Method = "POST" };
            var current = new HalResourceOfModerationCurrentPolicyView
            {
                PolicyId = PolicyId, Scope = LastScope, State = Uninitialized ? "uninitialized" : "adopted",
                Jurisdiction = new() { Id = owner, Kind = LastScope == "tenant" ? AuthorityScopeKind.Tenant : AuthorityScopeKind.Instance },
                ExpectedHostBindingId = owner, ExpectedAuthoritySourceId = owner, ExpectedQualificationRevision = 1,
                ExpectedWriterGeneration = "current", ExpectedRevision = Uninitialized ? 0 : Revision,
                ExpectedPublicationId = Uninitialized ? null : previous,
                InstalledCatalogueRevision = 8, InstalledInventoryRevision = 9, InstalledInventoryFingerprint = "installed",
                AuthoringBounds = new()
                {
                    Jurisdiction = new() { Id = owner, Kind = LastScope == "tenant" ? AuthorityScopeKind.Tenant : AuthorityScopeKind.Instance },
                    ScopeKinds = [ModerationScopeKind.Tenant], SubjectKinds = [ModerationSubjectKind.Content],
                    Effects = [NoticeEffect()],
                    TimingKinds = [ModerationReferenceTimingKind.NoEnforcementInterval, ModerationReferenceTimingKind.Finite],
                    MaximumFiniteDuration = "1.00:00:00", MaximumReviewDelay = "01:00:00",
                    MinimumApprovals = 0, MaximumApprovals = 2, MinimumContestationCalendarMonths = 6
                },
                Authoring = Uninitialized ? null : new()
                {
                    PublicationId = previous, CatalogueRevision = 8, InventoryRevision = 3, InventoryFingerprint = "historical",
                    Modes = [ModerationNativeMode.Embedded, ModerationNativeMode.RemoteStrict],
                    Classes = [classification], Reasons = [reason],
                    Profiles = [new() { Definition = Profile("selected", 6), Consequences = [Plan()] },
                        new() { Definition = Profile("disabled", 9), Consequences = [Plan()] }],
                    SelectedProfiles = [Rule("selected")]
                },
                _links = links
            };
            if (Reads == 1 && current.Authoring is not null)
                InitialAuthoring = JsonSerializer.SerializeToElement(current.Authoring, Json);
            return Response(current);
        }

        private ModerationRuleReference Rule(string code) => new() { OwnerId = owner, Code = code, Revision = 1 };
        private ModerationPolicyProfileDefinition Profile(string code, int months) => new()
        {
            Id = Guid.CreateVersion7(), Rule = Rule(code), Reason = Rule("reason"),
            ReferenceProfile = ModerationReferenceProfileId.CFG01, ReferenceRevision = 8,
            Grade = ModerationReferenceGrade.G0, ScopeKinds = [ModerationScopeKind.Tenant],
            SubjectKinds = [ModerationSubjectKind.Content], Effects = [NoticeEffect()], Bundle = ModerationConsequenceBundleKind.Unaffected,
            Strategies = [], Timing = new() { Kind = ModerationReferenceTimingKind.NoEnforcementInterval, Duration = "00:00:00" },
            RequiredApprovals = 0, ContestationCalendarMonths = months, Recognition = ModerationReferenceRecognition.Never
        };
        private static ModerationReferenceProfile Reference() => new()
        {
            Id = ModerationReferenceProfileId.CFG01, Code = "CFG01", Revision = 8,
            Ground = ModerationReferenceGround.CompatibleConfirmedConduct, Basis = ModerationReferenceBasis.ConfirmedWarning,
            Bundle = ModerationConsequenceBundleKind.Unaffected, Recognition = ModerationReferenceRecognition.Never,
            ScopeKinds = [ModerationScopeKind.Tenant], SubjectKinds = [ModerationSubjectKind.Content], Effects = [NoticeEffect()], Strategies = [],
            Responses = [new() { Grade = ModerationReferenceGrade.G0, RequiredApprovals = 0,
                Timing = new() { Kind = ModerationReferenceTimingKind.NoEnforcementInterval, Duration = "00:00:00" } }],
            ConsequencePlans = [Plan()], Prerequisites =
            [
                ModerationReferencePrerequisite.ExplicitPolicyAdoption, ModerationReferencePrerequisite.CurrentNativeConformance,
                ModerationReferencePrerequisite.CurrentOrdinaryAuthorization, ModerationReferencePrerequisite.QualifiedSubjectAndTarget,
                ModerationReferencePrerequisite.ProtectedRemedyRoutes, ModerationReferencePrerequisite.QualifiedUnconflictedReviewers,
                ModerationReferencePrerequisite.NoMandatoryExternalProvider, ModerationReferencePrerequisite.CompatibleFinalFinding,
                ModerationReferencePrerequisite.ResponseOpportunityOrQualifiedUrgency
            ]
        };
        private static ModerationReferenceEffect NoticeEffect() => new()
        {
            Kind = ModerationReferenceEffectKind.ReasonedNotice, Mode = ModerationReferenceEffectMode.Notice,
            Target = ModerationReferenceTarget.SelectedOperations, Execution = ModerationReferenceExecution.NativeOwner
        };
        private static ModerationConsequenceModes Plan() => new()
        {
            Content = ModerationContentMode.PreserveSafePublished, Management = ModerationManagementMode.CurrentAuthority,
            Resources = ModerationResourceReadMode.CurrentAudience, NewAdmission = ModerationNewAdmissionMode.CurrentEligibility,
            ExistingAdmission = ModerationExistingAdmissionMode.CurrentEligibility, PrivateDisclosure = ModerationPrivateDisclosureMode.CurrentAudience,
            Settlement = ModerationSettlementMode.NoBookingChange, Capacity = ModerationCapacityMode.NoSeatChange,
            Credentials = ModerationCredentialMode.CurrentCredentialLifecycle, Notifications = ModerationNotificationMode.DurableSafeSubjectAndAdmin,
            Recognition = ModerationRecognitionMode.NoCaptureFromThisControl, PendingWork = ModerationPendingWorkMode.RecheckCurrentEffectAtUseAndDispatch,
            Restoration = ModerationRestorationMode.RecomputeCurrentRightsOnly
        };
        private static HttpResponseMessage Response(object body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/hal+json") };
    }
}
