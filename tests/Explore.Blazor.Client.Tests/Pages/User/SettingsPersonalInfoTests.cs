using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Pages.User.Components;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Services.Accessibility;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.Client.Tests.Pages.User;

public sealed class SettingsPersonalInfoTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    public void Dispose() => _ctx.Dispose();

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task SensitiveFormsAndImageActionsFollowTheirIndependentHalLinks(bool collection, bool image)
    {
        var transport = Register(collection, image);
        var cut = _ctx.RenderMudComponent<SettingsPersonalInfo>();
        await Assert.That(cut.FindAll("button").Any(button => button.TextContent.Trim() == "Edit")).IsEqualTo(collection);
        await Assert.That(cut.FindAll("button").Any(button => button.TextContent.Trim() == "Change")).IsEqualTo(image);
        await Assert.That(cut.FindAll("input[type='date']").Count).IsEqualTo(0);
        await Assert.That(transport.Writes).IsEqualTo(0);
    }

    [Test]
    public async Task BirthDateInputIsOpenedOnlyAfterFreshReadinessAndDiscardedOnCancel()
    {
        var transport = Register(true, false);
        var cut = _ctx.RenderMudComponent<SettingsPersonalInfo>();
        await cut.Find("#open-private-birth-date").ClickAsync(new MouseEventArgs());
        await Assert.That(transport.ReadinessReads).IsEqualTo(2);
        await Assert.That(cut.Find("input[type='date']").GetAttribute("id")).IsEqualTo("private-birth-date");
        cut.Find("input[type='date']").Change("2000-02-29");
        await cut.Find("#cancel-private-birth-date").ClickAsync(new MouseEventArgs());
        await Assert.That(cut.FindAll("input[type='date']").Count).IsEqualTo(0);
        await Assert.That(transport.Writes).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DeclaredThresholdIsPrivateReadOnlyMetadataNotAnActionPermit(bool declared)
    {
        var transport = Register(false, false);
        transport.DeclaredAge = new
        {
            kind = declared ? "Declared" : "Unknown",
            outcome = declared ? "AtLeastThreshold" : "Unknown", minimumAge = 18,
            birthDateRevision = "1", evaluatedAt = DateTimeOffset.UtcNow,
            validUntil = DateTimeOffset.UtcNow.AddMinutes(1)
        };
        var cut = _ctx.RenderMudComponent<SettingsPersonalInfo>();
        var summary = cut.Find("#declared-age-summary").TextContent;
        await Assert.That(summary.Contains(declared ? "at least the adopted threshold of 18" : "unknown",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(summary.Contains("not verified age evidence or access permission", StringComparison.Ordinal)).IsTrue();
        await Assert.That(cut.FindAll("input[type='date']").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("#edit-profile-names, #open-private-birth-date, #change-profile-image").Count).IsEqualTo(0);
        await Assert.That(transport.Writes).IsEqualTo(0);
    }

    [Test]
    public async Task RefreshPreservesTheExactSelectedAssessmentNotAnotherContextForTheSameProfile()
    {
        var transport = Register(true, false, true);
        var cut = _ctx.RenderMudComponent<SettingsPersonalInfo>();
        cut.Find("#profile-collection-context").Change("1");
        await cut.Find("#open-private-birth-date").ClickAsync(new MouseEventArgs());
        cut.Find("input[type='date']").Change("2000-02-29");
        await cut.Find("form").SubmitAsync();
        await Assert.That(transport.WrittenAssessment).IsEqualTo(transport.SecondAssessment);
    }

    [Test]
    public async Task AReplacementSingletonRequiresExplicitSelectionBeforeCollecting()
    {
        var transport = Register(true, false, true);
        var cut = _ctx.RenderMudComponent<SettingsPersonalInfo>();
        cut.Find("#profile-collection-context").Change("1");
        transport.RefreshedAssessments = [transport.FirstAssessment];
        await cut.Find("#open-private-birth-date").ClickAsync(new MouseEventArgs());
        await Assert.That(cut.FindAll("input[type='date']").Count).IsEqualTo(0);
        await Assert.That(cut.Find("#profile-collection-context").GetAttribute("value")).IsEqualTo("-1");
        cut.Find("#profile-collection-context").Change("0");
        await cut.Find("#open-private-birth-date").ClickAsync(new MouseEventArgs());
        await Assert.That(cut.FindAll("input[type='date']").Count).IsEqualTo(1);
        await Assert.That(transport.Writes).IsEqualTo(0);
    }

    [Test]
    public async Task PendingReadinessDisablesSelectionAndCannotOverwriteItsContext()
    {
        var transport = Register(true, false, true);
        var cut = _ctx.RenderMudComponent<SettingsPersonalInfo>();
        cut.Find("#profile-collection-context").Change("1");
        transport.PauseNextReadiness();
        var opening = cut.Find("#open-private-birth-date").ClickAsync(new MouseEventArgs());
        await transport.ReadinessStarted.Task;
        await Assert.That(cut.Find("#profile-collection-context").HasAttribute("disabled")).IsTrue();
        transport.ReleaseReadiness.TrySetResult(true);
        await opening;
        cut.Find("input[type='date']").Change("2000-02-29");
        await cut.Find("form").SubmitAsync();
        await Assert.That(transport.WrittenAssessment).IsEqualTo(transport.SecondAssessment);
    }

    [Test]
    public async Task WithdrawingTheCollectionLinkScrubsAnAlreadyOpenDraft()
    {
        var transport = Register(true, false);
        var cut = _ctx.RenderMudComponent<SettingsPersonalInfo>();
        await cut.Find("#open-private-birth-date").ClickAsync(new MouseEventArgs());
        cut.Find("input[type='date']").Change("2000-02-29");
        transport.CollectionAvailable = false;
        await cut.Find("#edit-profile-names").ClickAsync(new MouseEventArgs());
        await Assert.That(cut.FindAll("input[type='date']").Count).IsEqualTo(0);
        await Assert.That(transport.Writes).IsEqualTo(0);
    }

    [Test]
    public async Task SelectingAContextDoesNotMoveFocusToTheBirthDateButton()
    {
        Register(true, false, true);
        _ctx.Services.AddScoped<IAccessibilityFocusService, AccessibilityFocusService>();
        var focus = _ctx.JSInterop.SetupModule("/js/accessibility.js");
        focus.SetupVoid("setFocusById", _ => true).SetVoidResult();
        var cut = _ctx.RenderMudComponent<SettingsPersonalInfo>();
        cut.Find("#profile-collection-context").Change("1");
        await Assert.That(focus.Invocations.Any(invocation => invocation.Identifier == "setFocusById")).IsFalse();
    }

    private ProfileTransport Register(bool collection, bool image, bool multiple = false)
    {
        var transport = new ProfileTransport(collection, image, multiple);
        var http = new HttpClient(transport) { BaseAddress = new Uri("https://event.example.test") };
        _ctx.Services.AddSingleton<IUserService>(new UserService(new UserClient(http), NullLogger<UserService>.Instance));
        _ctx.Services.AddSingleton<IUserProfileCompletionService>(new UserProfileCompletionService(new UserClient(http),
            new UserProfileCompletionClient(http), new UserProfileImageClient(http)));
        return transport;
    }

    private sealed class ProfileTransport(bool collection, bool image, bool multiple) : HttpMessageHandler
    {
        private readonly Guid _user = Guid.CreateVersion7();
        private readonly Guid _profile = Guid.CreateVersion7();
        private readonly Guid _firstAssessment = Guid.CreateVersion7();
        public Guid FirstAssessment => _firstAssessment;
        public Guid SecondAssessment { get; } = Guid.CreateVersion7();
        public Guid[]? RefreshedAssessments { get; set; }
        public bool CollectionAvailable { get; set; } = collection;
        public object? DeclaredAge { get; set; }
        public TaskCompletionSource<bool> ReadinessStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseReadiness { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _pauseReadiness;
        public void PauseNextReadiness() => _pauseReadiness = true;
        public Guid? WrittenAssessment { get; private set; }
        public int Writes { get; private set; }
        public int ReadinessReads { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string json;
            if (request.Method != HttpMethod.Get)
            {
                Writes++;
                if (request.Headers.TryGetValues("X-Profile-Collection", out var values))
                {
                    using var nomination = JsonDocument.Parse(values.Single());
                    WrittenAssessment = nomination.RootElement.GetProperty("assessment").GetProperty("sourceId").GetGuid();
                }
                json = "{\"success\":true}";
            }
            else if (request.RequestUri!.AbsolutePath.EndsWith("profile-completion", StringComparison.Ordinal))
            {
                ReadinessReads++;
                if (_pauseReadiness)
                {
                    _pauseReadiness = false;
                    ReadinessStarted.TrySetResult(true);
                    await ReleaseReadiness.Task.WaitAsync(token);
                }
                var links = new Dictionary<string, object>();
                if (CollectionAvailable)
                {
                    links["edit"] = new { href = "/profile", method = "PATCH" };
                    links["record-birth-date"] = new { href = "/private-birth-date", method = "PUT" };
                }
                json = JsonSerializer.Serialize(new
                {
                    options = (RefreshedAssessments ?? (multiple ? new[] { _firstAssessment, SecondAssessment } : new[] { _firstAssessment }))
                        .Select(sourceId => new { assessment = new { sourceId, original = new { profileId = _profile } },
                            validUntil = DateTimeOffset.UtcNow.AddMinutes(5), declaredAge = DeclaredAge }),
                    birthDateStatus = new { revision = 0, hasDeclaration = false }, _links = links
                });
            }
            else json = JsonSerializer.Serialize(new
            {
                id = _user, concurrencyStamp = Guid.CreateVersion7(), firstName = "Original", lastName = "User",
                _links = image ? new Dictionary<string, object> { ["profile-image"] = new { href = "/profile-image", method = "PATCH" } } : []
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/hal+json") };
        }
    }
}
