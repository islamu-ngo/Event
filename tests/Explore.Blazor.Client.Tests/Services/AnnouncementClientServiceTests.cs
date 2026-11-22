using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Contracts.Services;
using Microsoft.JSInterop;
using Wire = ISLAMU.Wire.Contracts.Announcements;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class AnnouncementClientServiceTests
{
    [Test]
    public async Task GeneratedDismissalTransportPreservesExplicitAbsenceAndTheWholeWitness()
    {
        using var handler = new SerializedDismissalHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://announcement.test/") };
        var api = new AnnouncementsClient(http);
        var observed = AnnouncementTestData.Observation();

        await api.DismissAnnouncementAsync(new() { Observed = observed, ExpectedMarker = null });

        using var body = JsonDocument.Parse(handler.Body
            ?? throw new InvalidOperationException("The generated client must send its request."));
        await Assert.That(body.RootElement.GetProperty("expectedMarker").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(body.RootElement.TryGetProperty("actorUserId", out _)).IsFalse();
        var sent = body.RootElement.GetProperty("observed");
        await Assert.That(sent.GetProperty("tenantId").GetGuid()).IsEqualTo(observed.TenantId);
        await Assert.That(sent.GetProperty("edition").GetGuid()).IsEqualTo(observed.Edition);
        await Assert.That(sent.GetProperty("owner").GetProperty("tenantId").ValueKind).IsEqualTo(JsonValueKind.Null);
        var witness = sent.GetProperty("sourceWitness");
        await Assert.That(witness.GetProperty("instanceSelectionEpoch").GetGuid())
            .IsEqualTo(observed.SourceWitness.InstanceSelectionEpoch);
        await Assert.That(witness.GetProperty("instanceIsLocked").GetBoolean()).IsFalse();
        await Assert.That(witness.GetProperty("tenantSelectionEpoch").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(witness.GetProperty("tenantIsLocked").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task EpochOnlyChangeDoesNotRedisplayDismissedMaterial()
    {
        await using var harness = new Harness();
        harness.Settings = AnnouncementTestData.Settings(epoch: AnnouncementTestData.OtherEpoch);
        harness.Self = AnnouncementTestData.Self(harness.Settings,
            new() { Observation = AnnouncementTestData.Observation() });

        var display = await harness.Service.LoadAsync();

        await Assert.That(display).IsNotNull();
        await Assert.That(display!.Observation!.Edition).IsEqualTo(AnnouncementTestData.Edition);
        await Assert.That(display.Observation.SourceWitness.InstanceSelectionEpoch).IsEqualTo(AnnouncementTestData.OtherEpoch);
        await Assert.That(display.IsDismissed).IsTrue();
        await Assert.That(display.CanDismiss).IsFalse();
    }

    [Test]
    public async Task AlreadyDismissedAcceptsAnOlderWitnessOnlyForTheSameIdentity()
    {
        await using var harness = new Harness();
        harness.Settings = AnnouncementTestData.Settings(epoch: AnnouncementTestData.OtherEpoch);
        harness.Self = AnnouncementTestData.Self(harness.Settings);
        var retained = new AnnouncementDismissalMarkerDto { Observation = AnnouncementTestData.Observation() };
        harness.Api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                harness.Self = AnnouncementTestData.Self(harness.Settings, retained);
                return new HalResourceOfAnnouncementDismissalReceiptDto { Decision = 1, Marker = retained };
            });
        await harness.Service.LoadAsync();

        var dismissed = await harness.Service.DismissAsync();

        await Assert.That(dismissed).IsTrue();
        await Assert.That(harness.Service.Display!.IsDismissed).IsTrue();
        await Assert.That(retained.Observation.SourceWitness.InstanceSelectionEpoch).IsEqualTo(AnnouncementTestData.Epoch);
    }

    [Test]
    public async Task ApplyRequiresTheExactObservedWitnessAndCannotBeReplayedWithoutReconciliation()
    {
        await using var harness = new Harness();
        harness.Api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfAnnouncementDismissalReceiptDto
            {
                Decision = 0, Marker = new() { Observation = AnnouncementTestData.Observation(epoch: AnnouncementTestData.OtherEpoch) }
            });
        await harness.Service.LoadAsync();

        await Assert.That(await harness.Service.DismissAsync()).IsFalse();
        await Assert.That(harness.Service.RequiresReconciliation).IsTrue();
        await Assert.That(harness.Service.Display!.IsDismissed).IsFalse();
        bool replayed = false;
        harness.Api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { replayed = true; return harness.Applied(); });

        await Assert.That(await harness.Service.DismissAsync()).IsFalse();
        await Assert.That(replayed).IsFalse();
        await harness.Service.LoadAsync(reconcile: true);
        await Assert.That(await harness.Service.DismissAsync()).IsTrue();
    }

    [Test]
    public async Task MissingDismissalHalCannotInvokeTheMutationPort()
    {
        await using var harness = new Harness();
        harness.Self._links = new Dictionary<string, HalLink>();
        bool attempted = false;
        harness.Api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { attempted = true; return harness.Applied(); });

        var display = await harness.Service.LoadAsync();
        await Assert.That(display!.CanDismiss).IsFalse();
        await Assert.That(await harness.Service.DismissAsync()).IsFalse();
        await Assert.That(attempted).IsFalse();
    }

    [Test]
    public async Task AnOlderPrivateReadCannotResurfacePreferencesAfterConfirmedDismissal()
    {
        await using var harness = new Harness();
        await harness.Service.LoadAsync();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = new TaskCompletionSource<HalResourceOfAnnouncementSelfStateDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Api.GetMyAnnouncementDismissalAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { requested.TrySetResult(); return old.Task; }, _ => Task.FromResult(harness.Self));
        var read = harness.Service.LoadAsync();
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(await harness.Service.DismissAsync()).IsTrue();
        old.SetResult(AnnouncementTestData.Self(harness.Settings));

        await Assert.That(await read.WaitAsync(TimeSpan.FromSeconds(10))).IsNull();
        await Assert.That(harness.Service.Display!.IsDismissed).IsTrue();
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(true, true)]
    [Arguments(false, false)]
    [Arguments(false, true)]
    public async Task PublicRefreshCannotReplayPendingDismissal(bool authenticated, bool reconcile)
    {
        await using var harness = new Harness();
        harness.Authentication.SetAuthenticated(authenticated);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HalResourceOfAnnouncementDismissalReceiptDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stored = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        harness.Api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { attempts++; started.TrySetResult(); return response.Task; });
        harness.Preferences.SaveAnonymousAnnouncementMarkerAsync(AnnouncementTestData.Tenant,
                Arg.Any<Wire.AnnouncementDismissalMarkerDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => { attempts++; started.TrySetResult(); return stored.Task; });
        await harness.Service.LoadAsync();
        var saving = harness.Service.DismissAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<bool>? replay = null;
        try
        {
            harness.Settings = AnnouncementTestData.Settings(epoch: AnnouncementTestData.OtherEpoch);
            harness.Self = AnnouncementTestData.Self(harness.Settings);
            harness.Experience.SettingsChanged += Raise.Event<Action>();
            await harness.Service.LoadAsync(reconcile);
            replay = harness.Service.DismissAsync();

            await Assert.That(attempts).IsEqualTo(1);
            await Assert.That(harness.Service.IsSaving).IsTrue();
        }
        finally
        {
            response.TrySetResult(harness.Applied());
            stored.TrySetResult(true);
            await saving.WaitAsync(TimeSpan.FromSeconds(10));
            if (replay is not null)
                await replay.WaitAsync(TimeSpan.FromSeconds(10));
        }
        await Assert.That(harness.Service.IsSaving).IsFalse();
        await Assert.That(harness.Service.RequiresReconciliation).IsTrue();
    }

    [Test]
    public async Task PublicRefreshCannotReplayPendingPublication()
    {
        await using var harness = new Harness();
        var original = await harness.Service.LoadSourceAsync()
            ?? throw new InvalidOperationException("The original source must load.");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HalResourceOfAnnouncementPublicationReceiptDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        harness.Api.UpdateTenantAnnouncementAsync(Arg.Any<AnnouncementUpdateDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { attempts++; started.TrySetResult(); return response.Task; });
        var saving = harness.Service.PublishAsync(original, PublishChange());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<AnnouncementPublicationClientResult>? replay = null;
        try
        {
            harness.Settings = AnnouncementTestData.Settings(epoch: AnnouncementTestData.OtherEpoch);
            harness.Source = AnnouncementTestData.Source(
                AnnouncementTestData.Edition, AnnouncementTestData.OtherEpoch);
            harness.Experience.SettingsChanged += Raise.Event<Action>();
            var refreshed = await harness.Service.LoadSourceAsync();
            if (refreshed is not null)
                replay = harness.Service.PublishAsync(refreshed, PublishChange());

            await Assert.That(attempts).IsEqualTo(1);
            await Assert.That(refreshed).IsNull();
        }
        finally
        {
            harness.Source = AnnouncementTestData.Source(
                AnnouncementTestData.OtherEdition, AnnouncementTestData.OtherEpoch, "Replacement");
            response.TrySetResult(new() { Outcome = 0, Source = harness.Source.Source });
            await saving.WaitAsync(TimeSpan.FromSeconds(10));
            if (replay is not null)
                await replay.WaitAsync(TimeSpan.FromSeconds(10));
        }
        var reconciled = await harness.Service.LoadSourceAsync()
            ?? throw new InvalidOperationException("Settled publication must allow a fresh source read.");
        await Assert.That(harness.Service.CanPublish(reconciled)).IsTrue();
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, true, false)]
    [Arguments(true, false, false)]
    [Arguments(true, true, false)]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    public async Task DelayedRefreshRetainsUncertainDismissalOnlyForItsOriginalIdentity(
        bool replacement, bool reconcile, bool refreshCompletesFirst)
    {
        await using var harness = new Harness();
        await harness.Service.LoadAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HalResourceOfAnnouncementDismissalReceiptDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        harness.Api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { attempts++; started.TrySetResult(); return response.Task; });
        var saving = harness.Service.DismissAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        harness.Settings = replacement
            ? AnnouncementTestData.Settings(edition: AnnouncementTestData.OtherEdition, message: "Tenant override",
                epoch: AnnouncementTestData.OtherEpoch, tenantOwner: true)
            : AnnouncementTestData.Settings(epoch: AnnouncementTestData.OtherEpoch);
        harness.Experience.SettingsChanged += Raise.Event<Action>();
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stateRead = new TaskCompletionSource<HalResourceOfAnnouncementSelfStateDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Api.GetMyAnnouncementDismissalAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { reading.TrySetResult(); return stateRead.Task; });
        var loading = harness.Service.LoadAsync(reconcile);
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var refreshedSelf = AnnouncementTestData.Self(harness.Settings);
            if (refreshCompletesFirst)
            {
                stateRead.SetResult(refreshedSelf);
                await loading.WaitAsync(TimeSpan.FromSeconds(10));
            }

            response.SetException(new HttpRequestException("The original dismissal receipt was lost."));
            await saving.WaitAsync(TimeSpan.FromSeconds(10));
            if (!refreshCompletesFirst)
                stateRead.SetResult(refreshedSelf);
            var state = await loading.WaitAsync(TimeSpan.FromSeconds(10));

            await Assert.That(state).IsNotNull();
            await Assert.That(state!.CanDismiss).IsEqualTo(replacement);
            await Assert.That(harness.Service.RequiresReconciliation).IsEqualTo(!replacement);
            if (replacement)
            {
                await Assert.That(harness.Service.Display!.CanDismiss).IsTrue();
                harness.Settings = AnnouncementTestData.Settings(epoch: AnnouncementTestData.OtherEpoch);
                harness.Api.GetMyAnnouncementDismissalAsync(
                        Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                    .Returns(_ => Task.FromResult(AnnouncementTestData.Self(harness.Settings)));
                harness.Experience.SettingsChanged += Raise.Event<Action>();
                var returned = await harness.Service.LoadAsync();
                await Assert.That(returned).IsNotNull();
                await Assert.That(returned!.CanDismiss).IsFalse();
                await Assert.That(harness.Service.RequiresReconciliation).IsTrue();
            }
            await Assert.That(await harness.Service.DismissAsync()).IsFalse();
            await Assert.That(attempts).IsEqualTo(1);
            var current = await harness.Service.LoadAsync(reconcile: true);
            await Assert.That(current!.CanDismiss).IsTrue();
            await Assert.That(harness.Service.RequiresReconciliation).IsFalse();
        }
        finally
        {
            response.TrySetException(new HttpRequestException("Fixture cleanup ended the transport."));
            stateRead.TrySetResult(AnnouncementTestData.Self(harness.Settings));
            await saving.WaitAsync(TimeSpan.FromSeconds(10));
            await loading.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Test]
    public async Task AccountSwitchRevokesAnOutstandingDismissalWithoutPoisoningTheNewAccount()
    {
        await using var harness = new Harness();
        await harness.Service.LoadAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = new TaskCompletionSource<HalResourceOfAnnouncementDismissalReceiptDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { started.TrySetResult(); return settled.Task; });
        var saving = harness.Service.DismissAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        harness.Authentication.SetAuthenticated(true, AnnouncementTestData.OtherTenant);
        await harness.Service.LoadAsync();
        settled.SetResult(new() { Decision = 0, Marker = new() { Observation = AnnouncementTestData.Observation() } });

        await Assert.That(await saving.WaitAsync(TimeSpan.FromSeconds(10))).IsFalse();
        await Assert.That(harness.Service.Display!.IsDismissed).IsFalse();
        await Assert.That(harness.Service.Display.CanDismiss).IsTrue();
        await Assert.That(harness.Service.RequiresReconciliation).IsFalse();
    }

    [Test]
    public async Task DisposedServiceDoesNotPublishAnOutstandingMutationResult()
    {
        await using var harness = new Harness();
        await harness.Service.LoadAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = new TaskCompletionSource<HalResourceOfAnnouncementDismissalReceiptDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { started.TrySetResult(); return settled.Task; });
        var saving = harness.Service.DismissAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await harness.Service.DisposeAsync();
        int lateNotifications = 0;
        harness.Service.Changed += () => lateNotifications++;
        settled.SetResult(new() { Decision = 0, Marker = new() { Observation = AnnouncementTestData.Observation() } });

        await Assert.That(await saving.WaitAsync(TimeSpan.FromSeconds(10))).IsFalse();
        await Assert.That(harness.Service.Display).IsNull();
        await Assert.That(lateNotifications).IsEqualTo(0);
    }

    [Test]
    public async Task AnonymousDismissalIsNeverMergedIntoAuthenticatedSelfState()
    {
        await using var harness = new Harness();
        harness.Authentication.SetAuthenticated(false);
        Wire.AnnouncementDismissalMarkerDto? stored = null;
        harness.Preferences.GetAnonymousAnnouncementMarkerAsync(AnnouncementTestData.Tenant, Arg.Any<CancellationToken>())
            .Returns(_ => new AnnouncementMarkerReadResult(true, stored));
        harness.Preferences.SaveAnonymousAnnouncementMarkerAsync(AnnouncementTestData.Tenant,
                Arg.Any<Wire.AnnouncementDismissalMarkerDto>(), Arg.Any<CancellationToken>())
            .Returns(call => { stored = call.Arg<Wire.AnnouncementDismissalMarkerDto>(); return true; });
        await harness.Service.LoadAsync();
        await Assert.That(await harness.Service.DismissAsync()).IsTrue();

        harness.Authentication.SetAuthenticated(true);
        var account = await harness.Service.LoadAsync();
        await Assert.That(account!.IsDismissed).IsFalse();
        await Assert.That(account.CanDismiss).IsTrue();
        harness.Authentication.SetAuthenticated(false);
        var anonymous = await harness.Service.LoadAsync();

        await Assert.That(anonymous!.IsDismissed).IsTrue();
        await Assert.That(stored!.Observation.Edition).IsEqualTo(AnnouncementTestData.Edition);
    }

    [Test]
    public async Task FailedGuestReconciliationReadKeepsAnUnconfirmedDismissalFenced()
    {
        var browser = new GuestAnnouncementStorage
        {
            CompleteFirstWrite = commit =>
            {
                commit();
                throw new JSException("The browser committed the marker but its acknowledgement was lost.");
            }
        };
        await using var harness = new Harness(browser.Runtime);
        harness.Authentication.SetAuthenticated(false);
        var service = harness.Service;
        var guest = await service.LoadAsync();
        await Assert.That(guest!.CanDismiss).IsTrue();
        await Assert.That(await service.DismissAsync()).IsFalse();
        await Assert.That(browser.StoredMarker).IsEqualTo(AnnouncementTestData.AnonymousMarker());
        await Assert.That(service.RequiresReconciliation).IsTrue();

        browser.ReadUnavailable = true;
        await service.LoadAsync(reconcile: true);

        await Assert.That(service.RequiresReconciliation).IsTrue();
        await Assert.That(service.Display?.CanDismiss == true).IsFalse();
        await Assert.That(await service.DismissAsync()).IsFalse();

        browser.ReadUnavailable = false;
        var reconciled = await service.LoadAsync(reconcile: true);

        await Assert.That(reconciled!.IsDismissed).IsTrue();
        await Assert.That(service.RequiresReconciliation).IsFalse();
        await Assert.That(await service.DismissAsync()).IsFalse();
        await Assert.That(browser.StoredMarker).IsEqualTo(AnnouncementTestData.AnonymousMarker());
    }

    [Test]
    public async Task ReturningToTheSameGuestStorageDoesNotReplayAnOutstandingDismissal()
    {
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeout = TimeSpan.FromSeconds(10);
        var browser = new GuestAnnouncementStorage
        {
            CompleteFirstWrite = async commit =>
            {
                dispatched.SetResult();
                await releaseCommit.Task;
                commit();
                return true;
            }
        };
        await using var harness = new Harness(browser.Runtime);
        harness.Authentication.SetAuthenticated(false);
        var service = harness.Service;
        var guest = await service.LoadAsync();
        await Assert.That(guest!.CanDismiss).IsTrue();

        var saving = service.DismissAsync();
        try
        {
            await dispatched.Task.WaitAsync(timeout);
            harness.Authentication.SetAuthenticated(true, AnnouncementTestData.OtherTenant);
            var account = await service.LoadAsync().WaitAsync(timeout);
            await Assert.That(account!.CanDismiss).IsTrue();
            await Assert.That(service.RequiresReconciliation).IsFalse();

            harness.Authentication.SetAuthenticated(false);
            await service.LoadAsync().WaitAsync(timeout);
            await Assert.That(await service.DismissAsync().WaitAsync(timeout)).IsFalse();
            await Assert.That(browser.StoredMarker).IsNull();
        }
        finally
        {
            releaseCommit.TrySetResult();
            await saving.WaitAsync(timeout);
        }

        await Assert.That(await saving).IsFalse();
        await Assert.That(browser.StoredMarker).IsEqualTo(AnnouncementTestData.AnonymousMarker());
        await service.LoadAsync();
        await Assert.That(service.RequiresReconciliation).IsTrue();

        var reconciled = await service.LoadAsync(reconcile: true);

        await Assert.That(reconciled!.IsDismissed).IsTrue();
        await Assert.That(service.RequiresReconciliation).IsFalse();
        await Assert.That(browser.StoredMarker).IsEqualTo(AnnouncementTestData.AnonymousMarker());
    }

    [Test]
    public async Task ReplacementGuestEditionWaitsForTheOriginalBrowserSlotWrite()
    {
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeout = TimeSpan.FromSeconds(10);
        var browser = new GuestAnnouncementStorage
        {
            CompleteFirstWrite = async commit =>
            {
                dispatched.SetResult();
                await releaseCommit.Task;
                commit();
                return true;
            }
        };
        await using var harness = new Harness(browser.Runtime);
        harness.Authentication.SetAuthenticated(false);
        var service = harness.Service;
        await service.LoadAsync();
        var saving = service.DismissAsync();
        try
        {
            await dispatched.Task.WaitAsync(timeout);
            harness.Settings = AnnouncementTestData.Settings(
                edition: AnnouncementTestData.OtherEdition, epoch: AnnouncementTestData.OtherEpoch);
            harness.Experience.SettingsChanged += Raise.Event<Action>();
            var replacement = await service.LoadAsync().WaitAsync(timeout);

            await Assert.That(replacement!.IsVisible).IsTrue();
            await Assert.That(await service.DismissAsync().WaitAsync(timeout)).IsFalse();
            await Assert.That(browser.StoredMarker).IsNull();
        }
        finally
        {
            releaseCommit.TrySetResult();
            await saving.WaitAsync(timeout);
        }

        await Assert.That(await saving).IsFalse();
        await Assert.That(service.IsSaving).IsFalse();
        await Assert.That(service.Display!.CanDismiss).IsTrue();
        await Assert.That(service.RequiresReconciliation).IsFalse();
        await Assert.That(await service.DismissAsync()).IsTrue();
        await Assert.That(browser.StoredMarker!.Observation.Edition).IsEqualTo(AnnouncementTestData.OtherEdition);
        await Assert.That((await service.LoadAsync())!.IsDismissed).IsTrue();
    }

    [Test]
    public async Task FreshSourceReadRatherThanTheReceiptRestoresPublicationHal()
    {
        await using var harness = new Harness();
        var original = await harness.Service.LoadSourceAsync()
            ?? throw new InvalidOperationException("The source must load.");
        var fresh = AnnouncementTestData.Source(AnnouncementTestData.OtherEdition, AnnouncementTestData.OtherEpoch, "Replacement");
        harness.Api.UpdateTenantAnnouncementAsync(Arg.Any<AnnouncementUpdateDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                harness.Source = fresh;
                return new HalResourceOfAnnouncementPublicationReceiptDto
                {
                    Outcome = 0, Source = fresh.Source, _links = new Dictionary<string, HalLink>()
                };
            });

        var result = await harness.Service.PublishAsync(original, PublishChange());

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Source).IsSameReferenceAs(fresh);
        await Assert.That(harness.Service.CanPublish(fresh)).IsTrue();
        await Assert.That(harness.Service.CanPublish(original)).IsFalse();
    }

    [Test]
    public async Task MutatingGeneratedDraftObjectsCannotChangeAnAlreadyStartedPublication()
    {
        await using var harness = new Harness();
        var original = await harness.Service.LoadSourceAsync()
            ?? throw new InvalidOperationException("The source must load.");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = new TaskCompletionSource<HalResourceOfAnnouncementPublicationReceiptDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        AnnouncementUpdateDto? sent = null;
        harness.Api.UpdateTenantAnnouncementAsync(Arg.Any<AnnouncementUpdateDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sent = call.Arg<AnnouncementUpdateDto>();
                started.TrySetResult();
                return settled.Task;
            });
        var change = PublishChange();
        var saving = harness.Service.PublishAsync(original, change);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        change.Material!.Message = "Later draft";
        var originalPublication = original.Source.Group?.Publication
            ?? throw new InvalidOperationException("The source must contain its publication.");
        originalPublication.Material.Message = "Tampered source";
        var fresh = AnnouncementTestData.Source(AnnouncementTestData.OtherEdition, AnnouncementTestData.OtherEpoch, "Replacement");
        harness.Source = fresh;
        settled.SetResult(new() { Outcome = 0, Source = fresh.Source });

        var result = await saving.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(result.Success).IsTrue();
        await Assert.That(sent!.Change.Material!.Message).IsEqualTo("Replacement");
        await Assert.That(sent.ExpectedSource.Group!.Publication!.Material.Message).IsEqualTo("Announcement");
        await Assert.That(change.Material.Message).IsEqualTo("Later draft");
    }

    [Test]
    public async Task SourceChangeAfterPublicRefreshCannotReturnStalePublicationSuccess()
    {
        await using var harness = new Harness();
        var original = await harness.Service.LoadSourceAsync()
            ?? throw new InvalidOperationException("The source must load.");
        var receiptSource = AnnouncementTestData.Source(AnnouncementTestData.OtherEdition, AnnouncementTestData.OtherEpoch, "Replacement");
        harness.Api.UpdateTenantAnnouncementAsync(Arg.Any<AnnouncementUpdateDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfAnnouncementPublicationReceiptDto { Outcome = 0, Source = receiptSource.Source });
        var refreshing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshed = new TaskCompletionSource<PublicExperienceSettingsDto?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Experience.GetSettingsAsync().Returns(_ => { refreshing.TrySetResult(); return refreshed.Task; });
        var saving = harness.Service.PublishAsync(original, PublishChange());
        await refreshing.Task.WaitAsync(TimeSpan.FromSeconds(10));
        harness.Source = AnnouncementTestData.Source(epoch: AnnouncementTestData.OtherEpoch, message: "Concurrent publication");
        refreshed.SetResult(harness.Settings);

        var result = await saving.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.RequiresReconciliation).IsTrue();
        await Assert.That(harness.Service.CanPublish(original)).IsFalse();
    }

    [Test]
    public async Task PostNotificationSourceReplacementCannotEscapeTheFinalPublicationFence()
    {
        await using var harness = new Harness();
        var original = await harness.Service.LoadSourceAsync()
            ?? throw new InvalidOperationException("The source must load.");
        var fresh = AnnouncementTestData.Source(AnnouncementTestData.OtherEdition, AnnouncementTestData.OtherEpoch, "Replacement");
        harness.Api.UpdateTenantAnnouncementAsync(Arg.Any<AnnouncementUpdateDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { harness.Source = fresh; return new HalResourceOfAnnouncementPublicationReceiptDto { Outcome = 0, Source = fresh.Source }; });
        bool invalidated = false;
        harness.Service.Changed += () =>
        {
            if (!invalidated && harness.Service.CanPublish(fresh))
            {
                invalidated = true;
                harness.Experience.SettingsChanged += Raise.Event<Action>();
            }
        };

        var result = await harness.Service.PublishAsync(original, PublishChange());

        await Assert.That(invalidated).IsTrue();
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.RequiresReconciliation).IsTrue();
        await Assert.That(harness.Service.CanPublish(fresh)).IsFalse();
    }

    [Test]
    public async Task CancellationDuringFinalPublicationNotificationRequiresAReadBeforePublishingAgain()
    {
        await using var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        var original = await harness.Service.LoadSourceAsync()
            ?? throw new InvalidOperationException("The original source must load.");
        var fresh = AnnouncementTestData.Source(AnnouncementTestData.OtherEdition, AnnouncementTestData.OtherEpoch, "Replacement");
        harness.Api.UpdateTenantAnnouncementAsync(Arg.Any<AnnouncementUpdateDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                harness.Source = fresh;
                return new HalResourceOfAnnouncementPublicationReceiptDto { Outcome = 0, Source = fresh.Source };
            });
        void CancelWhenPublishable()
        {
            if (!harness.Service.CanPublish(fresh))
                return;
            harness.Service.Changed -= CancelWhenPublishable;
            cancellation.Cancel();
        }
        harness.Service.Changed += CancelWhenPublishable;

        var result = await harness.Service.PublishAsync(original, PublishChange(), cancellation.Token);

        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.RequiresReconciliation).IsTrue();
        await Assert.That(harness.Service.CanPublish(fresh)).IsFalse();

        var reconciled = await harness.Service.LoadSourceAsync();
        await Assert.That(harness.Service.CanPublish(reconciled)).IsTrue();
    }

    [Test]
    public async Task MissingPublicationHalCannotInvokeTheMutationPort()
    {
        await using var harness = new Harness();
        harness.Source = AnnouncementTestData.Source(canPublish: false);
        var source = await harness.Service.LoadSourceAsync()
            ?? throw new InvalidOperationException("The read-only source must load.");
        bool attempted = false;
        harness.Api.UpdateTenantAnnouncementAsync(Arg.Any<AnnouncementUpdateDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { attempted = true; return new HalResourceOfAnnouncementPublicationReceiptDto(); });

        var result = await harness.Service.PublishAsync(source, PublishChange());

        await Assert.That(result.Success).IsFalse();
        await Assert.That(attempted).IsFalse();
        await Assert.That(harness.Service.CanPublish(source)).IsFalse();
    }

    [Test]
    public async Task CancellationAfterTransportRequiresAReadAndNeverReplaysTheDismissal()
    {
        await using var harness = new Harness();
        await harness.Service.LoadAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = new TaskCompletionSource<HalResourceOfAnnouncementDismissalReceiptDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { started.TrySetResult(); return settled.Task; });
        using var cancellation = new CancellationTokenSource();
        var saving = harness.Service.DismissAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        settled.SetResult(harness.Applied());

        await Assert.That(await saving.WaitAsync(TimeSpan.FromSeconds(10))).IsFalse();
        await Assert.That(harness.Service.RequiresReconciliation).IsTrue();
        bool replayed = false;
        harness.Api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { replayed = true; return harness.Applied(); });
        var reconciled = await harness.Service.LoadAsync(reconcile: true);

        await Assert.That(reconciled!.IsDismissed).IsTrue();
        await Assert.That(await harness.Service.DismissAsync()).IsFalse();
        await Assert.That(replayed).IsFalse();
    }

    [Test]
    [Arguments(AnnouncementEditScope.Instance)]
    [Arguments(AnnouncementEditScope.ManagedTenant)]
    public async Task PublicationRetainsTheActualServerSelectedOwnerAndTypedContext(AnnouncementEditScope scope)
    {
        await using var harness = new Harness();
        harness.Api.GetTenantAnnouncementSourceAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<HalResourceOfAnnouncementSourceResource>(
                new InvalidOperationException("This case requires its instance or managed source query.")));
        Guid? managed = scope == AnnouncementEditScope.ManagedTenant ? AnnouncementTestData.OtherTenant : null;
        harness.Source = AnnouncementTestData.Source(scope: scope, tenant: managed);
        var original = await harness.Service.LoadSourceAsync(scope, managed)
            ?? throw new InvalidOperationException("The selected source must load.");
        var fresh = AnnouncementTestData.Source(AnnouncementTestData.OtherEdition,
            AnnouncementTestData.OtherEpoch, "Replacement", scope: scope, tenant: managed);
        AnnouncementUpdateDto? sent = null;
        Guid? target = null;
        harness.Api.UpdateInstanceAnnouncementAsync(Arg.Any<AnnouncementUpdateDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sent = call.Arg<AnnouncementUpdateDto>();
                harness.Source = fresh;
                return new HalResourceOfAnnouncementPublicationReceiptDto { Outcome = 0, Source = fresh.Source };
            });
        harness.Api.UpdateManagedTenantAnnouncementAsync(Arg.Any<Guid>(), Arg.Any<AnnouncementUpdateDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                target = call.Arg<Guid>();
                sent = call.Arg<AnnouncementUpdateDto>();
                harness.Source = fresh;
                return new HalResourceOfAnnouncementPublicationReceiptDto { Outcome = 0, Source = fresh.Source };
            });

        var result = await harness.Service.PublishAsync(original, PublishChange());

        await Assert.That(result.Success).IsTrue();
        await Assert.That(sent!.ExpectedSource.Owner.TenantId).IsEqualTo(managed);
        await Assert.That(target).IsEqualTo(managed);
        await Assert.That(result.Source).IsSameReferenceAs(fresh);
    }

    [Test]
    public async Task APostNotificationSourceChangeCannotReturnStaleDismissalSuccess()
    {
        await using var harness = new Harness();
        await harness.Service.LoadAsync();
        bool invalidated = false;
        harness.Service.Changed += () =>
        {
            if (!invalidated && harness.Service.Display?.IsDismissed == true)
            {
                invalidated = true;
                harness.Experience.SettingsChanged += Raise.Event<Action>();
            }
        };

        var dismissed = await harness.Service.DismissAsync();

        await Assert.That(invalidated).IsTrue();
        await Assert.That(dismissed).IsFalse();
        await Assert.That(harness.Service.Display).IsNull();
    }

    [Test]
    public async Task ANewerSameIdentityReadDuringDismissalNotificationSupersedesSuccess()
    {
        await using var harness = new Harness();
        await harness.Service.LoadAsync();
        bool reloadedInline = false;
        void ReloadAfterDismissal()
        {
            if (harness.Service.Display?.IsDismissed != true)
                return;
            harness.Service.Changed -= ReloadAfterDismissal;
            harness.Self = AnnouncementTestData.Self(harness.Settings);
            var reload = harness.Service.LoadAsync();
            reloadedInline = reload.IsCompletedSuccessfully && reload.Result is { IsDismissed: false };
        }
        harness.Service.Changed += ReloadAfterDismissal;

        var dismissed = await harness.Service.DismissAsync();

        await Assert.That(reloadedInline).IsTrue();
        await Assert.That(harness.Service.Display!.IsDismissed).IsFalse();
        await Assert.That(dismissed).IsFalse();
    }

    [Test]
    public async Task UnconfirmedPublicationNeedsAFreshSourceReadBeforeAnotherAttempt()
    {
        await using var harness = new Harness();
        var original = await harness.Service.LoadSourceAsync()
            ?? throw new InvalidOperationException("The original source must load.");
        harness.Api.UpdateTenantAnnouncementAsync(Arg.Any<AnnouncementUpdateDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<HalResourceOfAnnouncementPublicationReceiptDto>(
                new HttpRequestException("The transport ended without its receipt.")));

        var uncertain = await harness.Service.PublishAsync(original, PublishChange());
        await Assert.That(uncertain.Success).IsFalse();
        await Assert.That(uncertain.RequiresReconciliation).IsTrue();
        bool attempted = false;
        var fresh = AnnouncementTestData.Source(AnnouncementTestData.OtherEdition, AnnouncementTestData.OtherEpoch, "Replacement");
        harness.Api.UpdateTenantAnnouncementAsync(Arg.Any<AnnouncementUpdateDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempted = true;
                harness.Source = fresh;
                return new HalResourceOfAnnouncementPublicationReceiptDto { Outcome = 0, Source = fresh.Source };
            });
        await Assert.That((await harness.Service.PublishAsync(original, PublishChange())).Success).IsFalse();
        await Assert.That(attempted).IsFalse();

        var reconciled = await harness.Service.LoadSourceAsync()
            ?? throw new InvalidOperationException("Explicit reconciliation must read the source.");
        var saved = await harness.Service.PublishAsync(reconciled, PublishChange());

        await Assert.That(saved.Success).IsTrue();
        await Assert.That(harness.Service.CanPublish(saved.Source)).IsTrue();
    }

    private static AnnouncementChangeDto PublishChange() => new()
    {
        Kind = "publish", Enabled = true, Redisplay = false,
        Material = new() { Message = "Replacement", LinkText = string.Empty, LinkUrl = string.Empty }
    };

    private sealed class Harness : IAsyncDisposable
    {
        private readonly UserSettingsService? _browserPreferences;
        internal IAnnouncementsClient Api { get; } = Substitute.For<IAnnouncementsClient>();
        internal IPublicExperienceService Experience { get; } = Substitute.For<IPublicExperienceService>();
        internal IUserSettingsService Preferences { get; } = Substitute.For<IUserSettingsService>();
        internal Authentication Authentication { get; } = new();
        internal PublicExperienceSettingsDto Settings { get; set; } = AnnouncementTestData.Settings();
        internal HalResourceOfAnnouncementSelfStateDto Self { get; set; } =
            AnnouncementTestData.Self(AnnouncementTestData.Settings());
        internal HalResourceOfAnnouncementSourceResource Source { get; set; } = AnnouncementTestData.Source();
        internal AnnouncementClientService Service { get; }

        internal Harness(IJSRuntime? browser = null)
        {
            Experience.GetCachedSettingsAsync().Returns(_ => Task.FromResult<PublicExperienceSettingsDto?>(Settings));
            Experience.GetSettingsAsync().Returns(_ => Task.FromResult<PublicExperienceSettingsDto?>(Settings));
            Preferences.GetAnonymousAnnouncementMarkerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(new AnnouncementMarkerReadResult(true, null));
            Api.GetMyAnnouncementDismissalAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(_ => Self);
            Api.GetTenantAnnouncementSourceAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(_ => Source);
            Api.GetInstanceAnnouncementSourceAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(_ => Source);
            Api.GetManagedTenantAnnouncementSourceAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => Source);
            Api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                    Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(_ => Applied());
            if (browser is not null)
                _browserPreferences = new(Substitute.For<ISettingsClient>(), Authentication, browser,
                    Substitute.For<ILogger<UserSettingsService>>(), Experience);
            Service = new(Api, Experience, (IUserSettingsService?)_browserPreferences ?? Preferences, Authentication,
                Substitute.For<ILogger<AnnouncementClientService>>());
        }

        internal HalResourceOfAnnouncementDismissalReceiptDto Applied()
        {
            var marker = new AnnouncementDismissalMarkerDto
            {
                Observation = Self.CurrentObservation
                    ?? throw new InvalidOperationException("The private source must contain its observation.")
            };
            Self = AnnouncementTestData.Self(Settings, marker);
            return new() { Decision = 0, Marker = marker, _links = new Dictionary<string, HalLink>() };
        }

        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            if (_browserPreferences is not null)
                await _browserPreferences.DisposeAsync();
        }
    }

    private sealed class GuestAnnouncementStorage
    {
        private readonly Dictionary<string, Wire.AnnouncementDismissalMarkerDto> _markers = [];
        internal IJSRuntime Runtime { get; } = Substitute.For<IJSRuntime>();
        internal bool ReadUnavailable { get; set; }
        internal Func<Action, ValueTask<bool>>? CompleteFirstWrite { get; init; }
        internal Wire.AnnouncementDismissalMarkerDto? StoredMarker =>
            _markers.GetValueOrDefault(AnnouncementTestData.Tenant.ToString("N"));

        internal GuestAnnouncementStorage()
        {
            var module = Substitute.For<IJSObjectReference>();
            bool firstWrite = true;
            Runtime.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>())
                .Returns(new ValueTask<IJSObjectReference>(module));
            module.InvokeAsync<Wire.AnnouncementDismissalMarkerDto?>("getAnnouncement", Arg.Any<CancellationToken>(),
                    Arg.Any<object?[]?>())
                .Returns(call =>
                {
                    if (ReadUnavailable)
                        throw new JSException("The browser could not read announcement storage.");
                    var args = call.ArgAt<object?[]>(2);
                    return new ValueTask<Wire.AnnouncementDismissalMarkerDto?>(_markers.GetValueOrDefault((string)args[0]!));
                });
            module.InvokeAsync<bool>("setAnnouncement", Arg.Any<CancellationToken>(), Arg.Any<object?[]?>())
                .Returns(call =>
                {
                    var args = call.ArgAt<object?[]>(2);
                    var tenant = (string)args[0]!;
                    var marker = (Wire.AnnouncementDismissalMarkerDto)args[1]!;
                    void Commit() => _markers[tenant] = marker;

                    if (firstWrite && CompleteFirstWrite is { } complete)
                    {
                        firstWrite = false;
                        return complete(Commit);
                    }
                    Commit();
                    return new ValueTask<bool>(true);
                });
        }
    }

    private sealed class Authentication : AuthenticationStateProvider
    {
        private AuthenticationState _state = new(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, AnnouncementTestData.Tenant.ToString("D"))], "TestAuth")));
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(_state);
        internal void SetAuthenticated(bool authenticated, Guid? subject = null)
        {
            _state = new(new ClaimsPrincipal(authenticated
                ? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, (subject ?? AnnouncementTestData.Tenant).ToString("D"))], "TestAuth")
                : new ClaimsIdentity()));
            NotifyAuthenticationStateChanged(Task.FromResult(_state));
        }
    }

    private sealed class SerializedDismissalHandler : HttpMessageHandler
    {
        internal string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Body = await (request.Content ?? throw new InvalidOperationException("Dismissal requires a body."))
                .ReadAsStringAsync(token);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new HalResourceOfAnnouncementDismissalReceiptDto
                {
                    Decision = 0,
                    Marker = new() { Observation = AnnouncementTestData.Observation() },
                    _links = new Dictionary<string, HalLink>()
                }), Encoding.UTF8, "application/json")
            };
        }
    }
}

internal static class AnnouncementTestData
{
    internal static readonly Guid Tenant = Guid.Parse("019a3b40-0000-7000-8000-000000000001");
    internal static readonly Guid OtherTenant = Guid.Parse("019a3b40-0000-7000-8000-000000000002");
    internal static readonly Guid Edition = Guid.Parse("019a3b40-0000-7000-8000-000000000003");
    internal static readonly Guid OtherEdition = Guid.Parse("019a3b40-0000-7000-8000-000000000004");
    internal static readonly Guid Epoch = Guid.Parse("019a3b40-0000-7000-8000-000000000005");
    internal static readonly Guid OtherEpoch = Guid.Parse("019a3b40-0000-7000-8000-000000000006");

    internal static AnnouncementObservationDto Observation(Guid? tenant = null, Guid? edition = null,
        Guid? epoch = null, bool tenantOwner = false) => new()
    {
        TenantId = tenant ?? Tenant, Owner = new() { TenantId = tenantOwner ? tenant ?? Tenant : null },
        Edition = edition ?? Edition,
        SourceWitness = new()
        {
            InstanceSelectionEpoch = epoch ?? Epoch, InstanceIsLocked = false,
            TenantSelectionEpoch = tenantOwner ? epoch ?? Epoch : null, TenantIsLocked = false
        }
    };

    internal static PublicExperienceSettingsDto Settings(Guid? tenant = null, Guid? edition = null,
        string message = "Announcement", Guid? epoch = null, bool tenantOwner = false) => new()
    {
        TenantId = tenant ?? Tenant,
        Announcement = new()
        {
            TenantId = tenant ?? Tenant, Observation = Observation(tenant, edition, epoch, tenantOwner), IsVisible = true,
            Material = new() { Message = message, LinkText = string.Empty, LinkUrl = string.Empty }
        },
        DirectoryOperator = new() { PublicName = "Directory", LegalName = "Directory legal name" }
    };

    internal static HalResourceOfAnnouncementSelfStateDto Self(PublicExperienceSettingsDto settings,
        AnnouncementDismissalMarkerDto? marker = null) => new()
    {
        TenantId = settings.Announcement!.TenantId, CurrentObservation = settings.Announcement.Observation,
        Marker = marker, _links = new Dictionary<string, HalLink>
        {
            ["self"] = new() { Href = "/api/announcements/self", Method = "GET" },
            ["dismiss"] = new() { Href = "/api/announcements/self/dismiss", Method = "POST" }
        }
    };

    internal static HalResourceOfAnnouncementSourceResource Source(
        Guid? edition = null, Guid? epoch = null, string message = "Announcement", bool canPublish = true,
        AnnouncementEditScope scope = AnnouncementEditScope.Tenant, Guid? tenant = null) => new()
    {
        Target = (int)scope,
        Source = new()
        {
            Owner = new() { TenantId = scope == AnnouncementEditScope.Instance ? null : tenant ?? Tenant },
            IsLocked = false,
            Group = new()
            {
                SelectionEpoch = epoch ?? Epoch, Enabled = true,
                Publication = new()
                {
                    Edition = edition ?? Edition,
                    Material = new() { Message = message, LinkText = string.Empty, LinkUrl = string.Empty }
                }
            }
        },
        _links = canPublish
            ? new Dictionary<string, HalLink> { ["publish"] = new()
            {
                Href = scope switch
                {
                    AnnouncementEditScope.Instance => "/api/announcements/instance",
                    AnnouncementEditScope.Tenant => "/api/announcements/tenant",
                    AnnouncementEditScope.ManagedTenant => "/api/announcements/tenants/" + (tenant ?? Tenant).ToString("D"),
                    _ => throw new ArgumentOutOfRangeException(nameof(scope))
                },
                Method = "PUT"
            } }
            : new Dictionary<string, HalLink>()
    };

    internal static Wire.AnnouncementDismissalMarkerDto AnonymousMarker(Guid? tenant = null, Guid? edition = null,
        Guid? epoch = null) => new()
    {
        Observation = new()
        {
            TenantId = tenant ?? Tenant, Owner = new() { TenantId = null }, Edition = edition ?? Edition,
            SourceWitness = new()
            {
                InstanceSelectionEpoch = epoch ?? Epoch, InstanceIsLocked = false,
                TenantSelectionEpoch = null, TenantIsLocked = false
            }
        }
    };
}
