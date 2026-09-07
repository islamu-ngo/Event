// ABOUTME: Exercises rendered SMTP administration against real generated clients and controlled HTTP responses.
// ABOUTME: Verifies confirmation authority, lock preservation, expiry and failures without timing-based waits.

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Pages.Admin.Components;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Serialization;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class EmailDeliveryAdminSectionTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task RenderedConfirmationUsesExactAcknowledgementAndRefreshesCanonicalState(bool instanceScope)
    {
        using var fixture = new Fixture(instanceScope);
        var cut = fixture.Render();
        await cut.Find("[data-action='preview-disable']").ClickAsync(new MouseEventArgs());

        await Assert.That(int.Parse(cut.Find("[data-field='affected-count']").TextContent, CultureInfo.InvariantCulture)).IsEqualTo(1);
        await Assert.That(long.Parse(cut.Find("[data-field='revision']").TextContent, CultureInfo.InvariantCulture)).IsEqualTo(fixture.Transport.Revision);
        await Assert.That(DateTimeOffset.Parse(cut.Find("time").GetAttribute("datetime")!, CultureInfo.InvariantCulture)).IsEqualTo(fixture.Clock.Now.AddMinutes(5));
        await Assert.That(cut.Find("input").Id).IsNotEmpty();
        await Assert.That(cut.FindAll($"label[for='{cut.Find("input").Id}']").Count).IsEqualTo(1);
        await cut.Find("input").InputAsync(new ChangeEventArgs { Value = "disable email delivery" });
        await Assert.That(cut.Find("[data-action='confirm-disable']").HasAttribute("disabled")).IsTrue();
        await cut.Find("input").InputAsync(new ChangeEventArgs { Value = EmailDeliveryAdminService.Acknowledgement });
        await cut.Find("[data-action='confirm-disable']").ClickAsync(new MouseEventArgs());

        await Assert.That(fixture.Transport.Enabled).IsFalse();
        await Assert.That(fixture.Transport.Submission!.Value.GetProperty("acknowledgement").GetString()).IsEqualTo(EmailDeliveryAdminService.Acknowledgement);
        await Assert.That(fixture.Transport.Submission.Value.GetProperty("expectedRevision").GetInt64()).IsEqualTo(fixture.Transport.Revision);
        await Assert.That(fixture.Transport.Submission.Value.GetProperty("confirmationToken").GetString()).IsEqualTo(fixture.Transport.Token);
        await Assert.That(cut.FindAll("[data-action='confirm-disable']").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[data-action='enable-delivery']").Count).IsEqualTo(1);
        await Assert.That(fixture.Transport.Reads).IsEqualTo(2);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task MissingHalActionsNeverExposeOrSendMutations(bool instanceScope)
    {
        using var fixture = new Fixture(instanceScope);
        fixture.Transport.Advertise = false;
        var cut = fixture.Render();
        await Assert.That(cut.FindAll("[data-action]").Count).IsEqualTo(0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.PreviewAsync(instanceScope, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.EnableAsync(instanceScope, null));
        await Assert.That(fixture.Transport.Writes).IsEqualTo(0);
    }

    [Test]
    public async Task TenantLockRemainsReadOnlyAndDoesNotHideStatus()
    {
        using var fixture = new Fixture(false);
        fixture.Transport.Locked = true;
        var cut = fixture.Render();
        await Assert.That(cut.FindAll("[role='status']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-action]").Count).IsEqualTo(0);
        await Assert.That(fixture.Transport.Locked).IsTrue();
        await Assert.That(fixture.Transport.Writes).IsEqualTo(0);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task EnableUsesOnlyAdvertisedTrueUpdateWithoutChangingLocks(bool instanceScope)
    {
        using var fixture = new Fixture(instanceScope);
        fixture.Transport.Enabled = false;
        var cut = fixture.Render();
        await cut.Find("[data-action='enable-delivery']").ClickAsync(new MouseEventArgs());
        JsonElement body = fixture.Transport.Submission!.Value;
        await Assert.That(body.EnumerateObject().Count(property => property.Value.ValueKind != JsonValueKind.Null)).IsEqualTo(1);
        if (instanceScope)
        {
            await Assert.That(body.GetProperty("deliveryEnabled").GetProperty("hasValue").GetBoolean()).IsTrue();
            await Assert.That(body.GetProperty("deliveryEnabled").GetProperty("value").GetBoolean()).IsTrue();
        }
        else await Assert.That(body.GetProperty("value").GetString()).IsEqualTo("true");
        await Assert.That(fixture.Transport.Enabled).IsTrue();
        await Assert.That(cut.FindAll("[data-action='preview-disable']").Count).IsEqualTo(1);
        await Assert.That(fixture.Transport.Reads).IsEqualTo(2);
    }

    [Test]
    public async Task StaleSubmissionClearsAuthorityAndCannotAutomaticallyRetry()
    {
        using var fixture = new Fixture(true);
        fixture.Transport.DisableStatus = HttpStatusCode.Conflict;
        var cut = fixture.Render();
        await cut.Find("[data-action='preview-disable']").ClickAsync(new MouseEventArgs());
        await cut.Find("input").InputAsync(new ChangeEventArgs { Value = EmailDeliveryAdminService.Acknowledgement });
        await cut.Find("[data-action='confirm-disable']").ClickAsync(new MouseEventArgs());
        await Assert.That(cut.FindAll("[data-action]").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("input").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[role='alert']").Count).IsEqualTo(1);
        await Assert.That(fixture.Transport.DisableRequests).IsEqualTo(1);
        await Assert.That(fixture.Transport.Enabled).IsTrue();
        await cut.Find("button").ClickAsync(new MouseEventArgs());
        await Assert.That(cut.FindAll("[data-action='preview-disable']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("input").Count).IsEqualTo(0);
        await Assert.That(fixture.Transport.DisableRequests).IsEqualTo(1);
    }

    [Test]
    public async Task ExpiredPreviewCannotReachTransportEvenIfButtonWasPreviouslyEnabled()
    {
        using var fixture = new Fixture(false);
        var cut = fixture.Render();
        await cut.Find("[data-action='preview-disable']").ClickAsync(new MouseEventArgs());
        await cut.Find("input").InputAsync(new ChangeEventArgs { Value = EmailDeliveryAdminService.Acknowledgement });
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(6);
        await cut.Find("[data-action='confirm-disable']").ClickAsync(new MouseEventArgs());
        await Assert.That(fixture.Transport.DisableRequests).IsEqualTo(0);
        await Assert.That(cut.FindAll("input").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[role='alert']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task MissingDisableLinkCannotCreateConfirmationAuthority()
    {
        using var fixture = new Fixture(true);
        fixture.Transport.AdvertiseDisable = false;
        var cut = fixture.Render();
        await cut.Find("[data-action='preview-disable']").ClickAsync(new MouseEventArgs());
        await Assert.That(cut.FindAll("time").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("input").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[data-action='confirm-disable']").Count).IsEqualTo(0);
        await Assert.That(fixture.Transport.DisableRequests).IsEqualTo(0);
    }

    [Test]
    public async Task PendingDisableConsumesPreviewAndDisablesKeyboardActionsUntilResponse()
    {
        using var fixture = new Fixture(true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.DisableBarrier = async () =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };
        var cut = fixture.Render();
        await cut.Find("[data-action='preview-disable']").ClickAsync(new MouseEventArgs());
        await cut.Find("input").InputAsync(new ChangeEventArgs { Value = EmailDeliveryAdminService.Acknowledgement });
        var pendingRendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cut.OnMarkupUpdated += (_, _) =>
        {
            if (cut.Find("section").GetAttribute("aria-busy") == "true") pendingRendered.TrySetResult();
        };
        Task click = cut.Find("[data-action='confirm-disable']").ClickAsync(new MouseEventArgs());
        try
        {
            await Task.WhenAll(entered.Task, pendingRendered.Task).WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(cut.FindAll("input").Count).IsEqualTo(0);
            await Assert.That(cut.FindAll("button").All(b => b.HasAttribute("disabled"))).IsTrue();
            await Assert.That(cut.Find("section").GetAttribute("aria-busy")).IsEqualTo("true");
        }
        finally { release.TrySetResult(); }
        await click.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(fixture.Transport.DisableRequests).IsEqualTo(1);
        await Assert.That(fixture.Transport.Enabled).IsFalse();
    }

    [Test]
    public async Task PreviewFailureClearsPreviousTypedAcknowledgement()
    {
        using var fixture = new Fixture(true);
        var cut = fixture.Render();
        await cut.Find("[data-action='preview-disable']").ClickAsync(new MouseEventArgs());
        await cut.Find("input").InputAsync(new ChangeEventArgs { Value = EmailDeliveryAdminService.Acknowledgement });
        fixture.Transport.PreviewFails = true;
        await cut.Find("[data-action='preview-disable']").ClickAsync(new MouseEventArgs());
        await Assert.That(cut.FindAll("input").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[data-action]").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[role='alert']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task CancelRestoresFocusAndRequiresFreshAcknowledgement()
    {
        using var fixture = new Fixture(true);
        var targets = new List<string>();
        fixture.Context.Services.GetRequiredService<IAccessibilityFocusService>()
            .FocusByIdAsync(Arg.Any<string>(), Arg.Any<bool>())
            .Returns(call => { targets.Add(call.ArgAt<string>(0)); return Task.CompletedTask; });
        var cut = fixture.Render();
        await cut.Find("[data-action='preview-disable']").ClickAsync(new MouseEventArgs());
        await Assert.That(targets.Last()).IsEqualTo(cut.Find("input").Id);
        await cut.Find("input").InputAsync(new ChangeEventArgs { Value = EmailDeliveryAdminService.Acknowledgement });
        await cut.Find("[data-action='cancel-disable']").ClickAsync(new MouseEventArgs());
        await Assert.That(targets.Last()).IsEqualTo(cut.Find("button").Id);
        await Assert.That(cut.FindAll("input").Count).IsEqualTo(0);
        await cut.Find("[data-action='preview-disable']").ClickAsync(new MouseEventArgs());
        await Assert.That(cut.Find("[data-action='confirm-disable']").HasAttribute("disabled")).IsTrue();
        await Assert.That(fixture.Transport.DisableRequests).IsEqualTo(0);
    }

    [Test]
    public async Task AotSerializationPreservesTypedRevisionAndHalWithoutRenderingToken()
    {
        using var fixture = new Fixture(true);
        var settings = await fixture.Service.GetInstanceAsync();
        var preview = await fixture.Service.PreviewAsync(true, settings._links);
        string json = JsonSerializer.Serialize(preview, AppJsonSerializerContext.Default.HalResourceOfEmailDeliveryDisablePreviewDto);
        var restored = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.HalResourceOfEmailDeliveryDisablePreviewDto)!;
        await Assert.That(restored.ExpectedRevision).IsEqualTo(fixture.Transport.Revision);
        await Assert.That(restored.ConfirmationToken).IsEqualTo(fixture.Transport.Token);
        await Assert.That(EmailDeliveryAdminService.HasAction(restored._links, "disable", "POST")).IsTrue();
        var cut = fixture.Render();
        await cut.Find("[data-action='preview-disable']").ClickAsync(new MouseEventArgs());
        await Assert.That(cut.FindAll("input").Select(i => i.GetAttribute("value")).Contains(fixture.Transport.Token)).IsFalse();
    }

    private sealed class Fixture : IDisposable
    {
        public BlazorTestContext Context { get; } = new();
        public TestClock Clock { get; } = new();
        public DeliveryTransport Transport { get; }
        public EmailDeliveryAdminService Service { get; }
        private readonly HttpClient _http;
        private readonly bool _instanceScope;

        public Fixture(bool instanceScope)
        {
            _instanceScope = instanceScope;
            Transport = new DeliveryTransport(instanceScope, Clock);
            _http = new HttpClient(Transport) { BaseAddress = new Uri("https://bff.example.test/") };
            Service = new EmailDeliveryAdminService(new InstanceMessagingSettingsClient(_http), new SettingsClient(_http), Clock);
            Context.Services.AddSingleton<IEmailDeliveryAdminService>(Service);
            Context.Services.AddSingleton<TimeProvider>(Clock);
        }
        public IRenderedComponent<EmailDeliveryAdminSection> Render() => Context.Render<EmailDeliveryAdminSection>(p => p.Add(c => c.InstanceScope, _instanceScope));
        public void Dispose() { Context.Dispose(); _http.Dispose(); }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class DeliveryTransport(bool instanceScope, TestClock clock) : HttpMessageHandler
    {
        public bool Enabled { get; set; } = true;
        public bool Locked { get; set; }
        public bool Advertise { get; set; } = true;
        public bool AdvertiseDisable { get; set; } = true;
        public bool PreviewFails { get; set; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public int DisableRequests { get; private set; }
        public long Revision { get; } = 9007199254740993;
        public string Token { get; } = Guid.NewGuid().ToString("N");
        public Guid TenantId { get; } = Guid.NewGuid();
        public JsonElement? Submission { get; private set; }
        public HttpStatusCode DisableStatus { get; set; } = HttpStatusCode.OK;
        public Func<Task>? DisableBarrier { get; set; }
        private string Prefix => instanceScope ? "/api/instance/settings/smtp" : "/api/settings/email-delivery";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                Reads++;
                var links = new Dictionary<string, HalLink>();
                if (Advertise && !Locked)
                {
                    if (Enabled) links["disable-preview"] = new HalLink { Href = Prefix + "/disable-preview", Method = "POST" };
                    else links[instanceScope ? "edit" : "enable"] = new HalLink
                    {
                        Href = instanceScope ? Prefix : "/api/settings/tenant/email.delivery_enabled", Method = instanceScope ? "PATCH" : "PUT"
                    };
                }
                return instanceScope
                    ? Json(new { deliveryEnabled = Enabled, _links = links })
                    : Json(new { tenantId = TenantId, category = "Email", settings = new[] { new { key = EmailDeliveryAdminService.DeliveryEnabledKey, value = Enabled ? "true" : "false", isLocked = Locked } }, _links = links });
            }
            Writes++;
            if (path == Prefix + "/disable-preview")
            {
                if (PreviewFails) return Json(new { status = 403 }, HttpStatusCode.Forbidden);
                return Json(new
                {
                    tenantId = instanceScope ? (Guid?)null : TenantId,
                    expectedRevision = Revision, isLocked = Locked, canDisable = !Locked,
                    affectedScopes = new[] { new { tenantId = instanceScope ? (Guid?)null : TenantId, revision = Revision } },
                    confirmationToken = Token, expiresAtUtc = clock.Now.AddMinutes(5),
                    _links = AdvertiseDisable ? new Dictionary<string, HalLink> { ["disable"] = new() { Href = Prefix + "/disable", Method = "POST" } } : []
                });
            }
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Submission = body.RootElement.Clone();
            if (path == Prefix + "/disable")
            {
                DisableRequests++;
                if (DisableBarrier is not null) await DisableBarrier();
                if (DisableStatus != HttpStatusCode.OK) return Json(new { status = (int)DisableStatus }, DisableStatus);
                if (Submission.Value.GetProperty("acknowledgement").GetString() != EmailDeliveryAdminService.Acknowledgement
                    || Submission.Value.GetProperty("expectedRevision").GetInt64() != Revision
                    || Submission.Value.GetProperty("confirmationToken").GetString() != Token)
                    return Json(new { status = 400 }, HttpStatusCode.BadRequest);
                Enabled = false;
            }
            else if ((instanceScope && path == Prefix && request.Method == HttpMethod.Patch)
                || (!instanceScope && path.EndsWith("/email.delivery_enabled", StringComparison.Ordinal) && request.Method == HttpMethod.Put))
                Enabled = true;
            else return Json(new { status = 404 }, HttpStatusCode.NotFound);
            return Json(new { success = true });
        }
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
