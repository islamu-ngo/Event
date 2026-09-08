using System.Globalization;
using System.Reflection;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Admissions;
using Explore.Blazor.Client.Pages.Studio;
using ISLAMU.Wire.Contracts.Admissions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.Client.Tests.Pages.Studio;

public sealed class StudioAdmissionCheckInRedTests : IDisposable
{
    private const string CheckInRelation = "check-in-admissions";
    private const string PayloadPrefix = "islamu-admission:v1:";
    private const int QueueCapacity = 100;

    private readonly BlazorTestContext _ctx = new();
    private readonly IAdmissionQrScanner _scanner = Substitute.For<IAdmissionQrScanner>();
    private readonly IAdmissionScannerCapabilityState _scannerCapabilityState = Substitute.For<IAdmissionScannerCapabilityState>();
    private readonly IAccessibilityAnnouncerService _announcer = Substitute.For<IAccessibilityAnnouncerService>();
    private readonly IAccessibilityFocusService _focus = Substitute.For<IAccessibilityFocusService>();
    private readonly Phase21ServiceHandler _service = new();

    public StudioAdmissionCheckInRedTests()
    {
        _scanner.GetCapabilityAsync(Arg.Any<CancellationToken>())
            .Returns(new AdmissionQrScannerCapability(NativeQrAvailable: true));
        _ctx.Services.AddSingleton(_scanner);
        _ctx.Services.AddSingleton(_scannerCapabilityState);
        _ctx.Services.RemoveAll<IAccessibilityAnnouncerService>();
        _ctx.Services.RemoveAll<IAccessibilityFocusService>();
        _ctx.Services.AddSingleton(_announcer);
        _ctx.Services.AddSingleton(_focus);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task ComponentDeclaresEventCheckInRouteAndExplicitCapabilityGeneration()
    {
        Type component = typeof(StudioAdmissionCheckIn);
        string[] routes = component.GetCustomAttributes<RouteAttribute>()
            .Select(attribute => attribute.Template)
            .ToArray();

        await Assert.That(routes).Contains("/studio/events/{EventId:guid}/check-in");
        await Assert.That(component.GetProperty("EventId")?.PropertyType).IsEqualTo(typeof(Guid));
        await Assert.That(component.GetProperty("TargetId")?.PropertyType).IsEqualTo(typeof(Guid));
        await Assert.That(component.GetProperty("Event")?.PropertyType).IsEqualTo(typeof(EventDto));
        await Assert.That(component.GetProperty("CapabilityGeneration")?.PropertyType).IsEqualTo(typeof(long));
    }

    [Test]
    [Arguments(null)]
    [Arguments("view-check-in-summary")]
    [Arguments("check-in-admission")]
    public async Task SurfaceRequiresExactCheckInAdmissionsHalRelation(string? advertisedRelation)
    {
        EventDto resource = Event(advertisedRelation);
        var cut = Render(resource);

        await Assert.That(cut.FindAll("[data-testid='studio-admission-check-in-unavailable']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("h1").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='studio-admission-check-in']")).IsEmpty();
        await Assert.That(_service.Calls).IsEmpty();
    }

    [Test]
    public async Task ExactCheckInAdmissionsHalRelationEnablesTheSurfaceWithoutLocalRoleChecks()
    {
        var cut = Render(Event(CheckInRelation));

        await Assert.That(cut.FindAll("[data-testid='studio-admission-check-in']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("h1").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("h2").Count).IsEqualTo(3);
        await Assert.That(cut.FindAll("h3")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='manual-admission-input']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='hid-admission-input']").Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(CameraFallbackScenario.Unsupported)]
    [Arguments(CameraFallbackScenario.PermissionDenied)]
    [Arguments(CameraFallbackScenario.DetectionFailure)]
    public async Task UnsupportedDeniedOrFailedCameraFallsBackToHidAndManual(CameraFallbackScenario scenario)
    {
        bool nativeAvailable = scenario != CameraFallbackScenario.Unsupported;
        AdmissionQrScanOutcome cameraOutcome = scenario == CameraFallbackScenario.Unsupported
            ? AdmissionQrScanOutcome.Unsupported
            : AdmissionQrScanOutcome.Failure;
        _scanner.GetCapabilityAsync(Arg.Any<CancellationToken>())
            .Returns(new AdmissionQrScannerCapability(nativeAvailable));
        _scanner.DetectAsync(Arg.Any<ElementReference>(), Arg.Any<CancellationToken>())
            .Returns(new AdmissionQrScanResult(cameraOutcome));

        var cut = Render(Event(CheckInRelation));
        if (nativeAvailable)
        {
            await cut.Find("[data-testid='start-camera-scan']").ClickAsync(new MouseEventArgs());
        }

        await Assert.That(cut.FindAll("[data-testid='manual-admission-input']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='hid-admission-input']").Count).IsEqualTo(1);
        await Assert.That(cut.Find("label[for='manual-admission-input']").TextContent.Trim()).IsNotEmpty();
        await Assert.That(cut.FindAll("[data-testid='camera-fallback']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='start-camera-scan']")).IsEmpty();
        await _focus.Received().FocusAsync("#manual-admission-input", Arg.Any<bool>());
        await Assert.That(_service.Calls).IsEmpty();
    }

    [Test]
    [Arguments(
        AdmissionQrScanOutcome.NoCode,
        "No QR code was detected. Reposition the code and try again.")]
    [Arguments(
        AdmissionQrScanOutcome.MultipleAmbiguous,
        "Multiple QR codes were detected. Show one code at a time.")]
    [Arguments(
        AdmissionQrScanOutcome.Invalid,
        "The detected QR code is not a valid admission credential.")]
    public async Task RetriableCameraOutcomesKeepCameraAvailableWithAccurateFeedback(
        AdmissionQrScanOutcome outcome,
        string expectedMessage)
    {
        _scanner.DetectAsync(Arg.Any<ElementReference>(), Arg.Any<CancellationToken>())
            .Returns(new AdmissionQrScanResult(outcome));
        var cut = Render(Event(CheckInRelation));

        await cut.Find("[data-testid='start-camera-scan']").ClickAsync(new MouseEventArgs());

        await Assert.That(cut.FindAll("[data-testid='start-camera-scan']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='camera-fallback']")).IsEmpty();
        await Assert.That(cut.Find("[data-testid='camera-scan-status']").TextContent.Trim())
            .IsEqualTo(expectedMessage);
    }

    [Test]
    public async Task CameraActionUsesAppButtonBusyStateToPreventConcurrentDetection()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<AdmissionQrScanResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _scanner.DetectAsync(Arg.Any<ElementReference>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.TrySetResult();
                return result.Task;
            });
        var cut = Render(Event(CheckInRelation));

        Task click = cut.Find("[data-testid='start-camera-scan']")
            .ClickAsync(new MouseEventArgs());
        await started.Task;

        var button = cut.Find("[data-testid='start-camera-scan']");
        await Assert.That(button.HasAttribute("disabled")).IsTrue();
        await Assert.That(button.GetAttribute("aria-busy")).IsEqualTo("true");
        result.SetResult(new AdmissionQrScanResult(AdmissionQrScanOutcome.NoCode));
        await click;
        await Assert.That(cut.Find("[data-testid='start-camera-scan']")
            .HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task CameraHidAndManualInputsUseTheSameTypedServiceMethod()
    {
        string cameraBearer = Bearer(1);
        string hidBearer = Bearer(2);
        string manualBearer = Bearer(3);
        AdmissionCredentialBearer.TryCreate(cameraBearer, out AdmissionCredentialBearer? cameraCredential);
        _scanner.DetectAsync(Arg.Any<ElementReference>(), Arg.Any<CancellationToken>())
            .Returns(new AdmissionQrScanResult(AdmissionQrScanOutcome.SingleValid, cameraCredential));
        _service.EnqueueResult("CheckedIn");
        _service.EnqueueResult("AlreadyCheckedIn");
        _service.EnqueueResult("Rejected");
        EventDto resource = Event(CheckInRelation);
        Guid eventId = resource.Id!.Value;
        Guid targetId = TargetId(resource);
        var cut = Render(resource);

        await cut.Find("[data-testid='start-camera-scan']").ClickAsync(new MouseEventArgs());
        await Submit(cut, "hid-admission-input", PayloadPrefix + hidBearer);
        await Submit(cut, "manual-admission-input", manualBearer);

        await Assert.That(_service.Calls.All(call => call.EventId == eventId)).IsTrue();
        await Assert.That(_service.Calls.All(call => call.TargetId == targetId)).IsTrue();
        await Assert.That(_service.CredentialValues).IsEquivalentTo([cameraBearer, hidBearer, manualBearer]);
    }

    [Test]
    public async Task RapidDuplicateSubmissionsAreSuppressedBeforeTheServiceBoundary()
    {
        string bearer = Bearer(4);
        _service.HoldResults();
        var cut = Render(Event(CheckInRelation));

        cut.Find("[data-testid='manual-admission-input']").Input(bearer);
        cut.Find("[data-testid='manual-admission-input']").KeyDown(new KeyboardEventArgs { Key = "Enter", Code = "Enter" });
        cut.Find("[data-testid='manual-admission-input']").KeyDown(new KeyboardEventArgs { Key = "Enter", Code = "Enter" });

        await Assert.That(_service.CredentialValues.Count(value => value == bearer)).IsEqualTo(1);
        _service.ReleaseHeldResults("CheckedIn");
    }

    [Test]
    public async Task QueueIsBoundedAndSaturationDoesNotCreateAnOfflineBacklog()
    {
        _service.HoldResults();
        var cut = Render(Event(CheckInRelation));

        for (int index = 0; index < QueueCapacity + 1; index++)
        {
            cut.Find("[data-testid='manual-admission-input']").Input(Bearer(index + 10));
            cut.Find("[data-testid='manual-admission-input']").KeyDown(new KeyboardEventArgs { Key = "Enter", Code = "Enter" });
        }

        int depth = int.Parse(
            cut.Find("[data-testid='admission-queue-depth']").GetAttribute("data-depth")!,
            CultureInfo.InvariantCulture);
        var queueDepth = cut.Find("[data-testid='admission-queue-depth']");
        await Assert.That(depth).IsLessThanOrEqualTo(QueueCapacity);
        await Assert.That(queueDepth.TextContent.Trim()).IsEqualTo($"{depth} pending");
        await Assert.That(queueDepth.HasAttribute("aria-label")).IsFalse();
        await Assert.That(queueDepth.GetAttribute("aria-live")).IsEqualTo("polite");
        await Assert.That(cut.FindAll("[data-testid='admission-queue-saturated'][role='alert']").Count).IsEqualTo(1);
        _service.ReleaseHeldResults("CheckedIn");
    }

    [Test]
    public async Task PerItemResultsRemainInSubmissionOrderAcrossMixedOutcomes()
    {
        _service.EnqueueResult("CheckedIn");
        _service.EnqueueResult("AlreadyCheckedIn");
        _service.EnqueueResult("Rejected");
        var cut = Render(Event(CheckInRelation));

        await Submit(cut, "manual-admission-input", Bearer(120));
        await Submit(cut, "manual-admission-input", Bearer(121));
        await Submit(cut, "manual-admission-input", Bearer(122));

        string[] codes = cut.FindAll("[data-testid='admission-result']")
            .OrderBy(item => int.Parse(item.GetAttribute("data-sequence")!, CultureInfo.InvariantCulture))
            .Select(item => item.GetAttribute("data-result-code")!)
            .ToArray();
        await Assert.That(codes).IsEquivalentTo(["CheckedIn", "AlreadyCheckedIn", "Rejected"]);
    }

    [Test]
    public async Task EveryOutcomeHasVisibleTextAndAccessibleAnnouncementWithoutColorOrSoundDependency()
    {
        _service.EnqueueResult("CheckedIn");
        _service.EnqueueResult("AlreadyCheckedIn");
        _service.EnqueueResult("Rejected");
        var cut = Render(Event(CheckInRelation));

        for (int index = 0; index < 3; index++)
        {
            await Submit(cut, "manual-admission-input", Bearer(130 + index));
        }

        var results = cut.FindAll("[data-testid='admission-result']");
        await Assert.That(results.Count).IsEqualTo(3);
        await Assert.That(results.All(result => !string.IsNullOrWhiteSpace(result.TextContent))).IsTrue();
        await Assert.That(results.Select(result => result.GetAttribute("data-result-code")!).Distinct().ToArray())
            .IsEquivalentTo(["CheckedIn", "AlreadyCheckedIn", "Rejected"]);
        string rejectedFeedback = results.Single(result => result.GetAttribute("data-result-code") == "Rejected").TextContent;
        await Assert.That(ExposesSecurityReason(rejectedFeedback)).IsFalse();
        await Assert.That(cut.FindAll("[data-testid='admission-live-region'][aria-live]").Count).IsEqualTo(1);
        await Assert.That(cut.Find("[data-testid='admission-queue-depth']").TagName).IsEqualTo("SPAN");
        await Assert.That(cut.FindAll("audio, [data-feedback-only='color'], [data-feedback-only='sound']")).IsEmpty();
        await _announcer.DidNotReceive().AnnouncePoliteAsync(Arg.Any<string>());
    }

    [Test]
    public async Task RepeatedIdenticalOutcomesReplaceTheLiveAnnouncementNode()
    {
        _service.EnqueueResult("CheckedIn");
        _service.EnqueueResult("CheckedIn");
        var cut = Render(Event(CheckInRelation));

        await Submit(cut, "manual-admission-input", Bearer(126));
        string firstSequence = cut.Find("[data-testid='admission-live-region']")
            .GetAttribute("data-announcement-sequence")!;
        await Submit(cut, "manual-admission-input", Bearer(127));

        var liveRegion = cut.Find("[data-testid='admission-live-region']");
        await Assert.That(liveRegion.GetAttribute("data-announcement-sequence"))
            .IsNotEqualTo(firstSequence);
        await Assert.That(liveRegion.TextContent.Trim()).IsEqualTo("Admission accepted.");
    }

    [Test]
    public async Task ResultHandlingPreservesInputFocusAndCameraFallbackFocusesManualEntry()
    {
        _service.EnqueueResult("CheckedIn");
        _scanner.DetectAsync(Arg.Any<ElementReference>(), Arg.Any<CancellationToken>())
            .Returns(new AdmissionQrScanResult(AdmissionQrScanOutcome.Failure));
        var cut = Render(Event(CheckInRelation));

        await Submit(cut, "manual-admission-input", Bearer(140));
        await _focus.DidNotReceive().FocusAsync("#admission-check-in-input", Arg.Any<bool>());

        await cut.Find("[data-testid='start-camera-scan']").ClickAsync(new MouseEventArgs());

        await _focus.Received().FocusAsync("#manual-admission-input", Arg.Any<bool>());
    }

    [Test]
    public async Task ControlsUseNativeKeyboardSemanticsAndEnterSubmitsBothTextInputs()
    {
        _service.EnqueueResult("CheckedIn");
        _service.EnqueueResult("CheckedIn");
        var cut = Render(Event(CheckInRelation));

        await Assert.That(cut.Find("[data-testid='start-camera-scan']").TagName).IsEqualTo("BUTTON");
        var manual = cut.Find("[data-testid='manual-admission-input']");
        var hid = cut.Find("[data-testid='hid-admission-input']");
        await Assert.That(manual.TagName).IsEqualTo("INPUT");
        await Assert.That(hid.TagName).IsEqualTo("INPUT");
        await Assert.That(manual.GetAttribute("aria-describedby")).IsEqualTo("manual-admission-help");
        await Assert.That(hid.GetAttribute("aria-describedby")).IsEqualTo("hid-admission-help");

        await hid.InputAsync(new ChangeEventArgs { Value = Bearer(150) });
        await hid.KeyDownAsync(new KeyboardEventArgs { Key = "Enter", Code = "Enter" });
        await manual.InputAsync(new ChangeEventArgs { Value = Bearer(151) });
        await manual.KeyDownAsync(new KeyboardEventArgs { Key = "Enter", Code = "Enter" });
        await Assert.That(_service.Calls.Count).IsEqualTo(2);
    }

    [Test]
    public async Task StructureUsesSemanticRtlNeutralMarkup()
    {
        var cut = Render(Event(CheckInRelation));
        string rootClass = cut.Find("[data-testid='studio-admission-check-in']").ClassName ?? string.Empty;
        string markup = cut.Markup.ToLowerInvariant();

        await Assert.That(rootClass).Contains("studio-admission-check-in");
        await Assert.That(cut.FindAll("[dir='ltr']")).IsEmpty();
        await Assert.That(markup).DoesNotContain("margin-left");
        await Assert.That(markup).DoesNotContain("margin-right");
        await Assert.That(markup).DoesNotContain("text-align: left");
        await Assert.That(markup).DoesNotContain("text-align: right");
    }

    [Test]
    public async Task EventRouteOrCapabilityGenerationChangeCancelsAndRejectsStaleWork()
    {
        _service.HoldResults();
        EventDto first = Event(CheckInRelation);
        EventDto second = Event(CheckInRelation);
        var cut = Render(first, capabilityGeneration: 1);
        cut.Find("[data-testid='manual-admission-input']").Input(Bearer(160));
        cut.Find("[data-testid='manual-admission-input']").KeyDown(new KeyboardEventArgs { Key = "Enter", Code = "Enter" });
        CancellationToken firstToken = _service.Calls.Single().CancellationToken;

        RenderAgain(cut, second, capabilityGeneration: 2);

        await Assert.That(firstToken.IsCancellationRequested).IsTrue();
        await Assert.That(cut.FindAll("[data-testid='admission-result']")).IsEmpty();
        _service.ReleaseHeldResults("CheckedIn");
        await Task.Yield();
        await Assert.That(cut.FindAll("[data-testid='admission-result']")).IsEmpty();
    }

    [Test]
    public async Task TransientCapabilityParameterActivatesInMemoryStateAndDisposalClearsIt()
    {
        const string capability = "transient-scanner-capability";
        EventDto resource = Event(CheckInRelation);
        var cut = Render(resource, scannerCapability: capability);

        _scannerCapabilityState.Received(1).Activate(capability);
        await Assert.That(cut.Markup).DoesNotContain(capability);

        DisposeRenderedPhase21Component(cut);

        _scannerCapabilityState.Received().Clear();
    }

    [Test]
    public async Task DisposalCancelsCapabilityDetectionAndInFlightCheckIn()
    {
        var capability = new TaskCompletionSource<AdmissionQrScannerCapability>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capabilityToken = default;
        _scanner.GetCapabilityAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            capabilityToken = call.Arg<CancellationToken>();
            return capability.Task;
        });
        _service.HoldResults();
        var cut = Render(Event(CheckInRelation));
        cut.Find("[data-testid='manual-admission-input']").Input(Bearer(170));
        cut.Find("[data-testid='manual-admission-input']").KeyDown(new KeyboardEventArgs { Key = "Enter", Code = "Enter" });
        CancellationToken checkInToken = _service.Calls.Single().CancellationToken;

        DisposeRenderedPhase21Component(cut);

        await Assert.That(capabilityToken.IsCancellationRequested).IsTrue();
        await Assert.That(checkInToken.IsCancellationRequested).IsTrue();
        capability.TrySetCanceled(capabilityToken);
        _service.ReleaseHeldResults("CheckedIn");
    }

    [Test]
    [Arguments(AdmissionCheckInUiStatus.OnlineRequired, "admission-online-required")]
    [Arguments(AdmissionCheckInUiStatus.Saturated, "admission-queue-saturated")]
    public async Task FirstPressureResponseStopsAndClearsTheBoundedFifo(
        AdmissionCheckInUiStatus status,
        string expectedStatusTestId)
    {
        _service.HoldResults();
        var cut = Render(Event(CheckInRelation));
        cut.Find("[data-testid='manual-admission-input']").Input(Bearer(175));
        cut.Find("[data-testid='manual-admission-input']").KeyDown(new KeyboardEventArgs { Key = "Enter", Code = "Enter" });
        cut.Find("[data-testid='manual-admission-input']").Input(Bearer(176));
        cut.Find("[data-testid='manual-admission-input']").KeyDown(new KeyboardEventArgs { Key = "Enter", Code = "Enter" });
        cut.Find("[data-testid='manual-admission-input']").Input(Bearer(177));
        cut.Find("[data-testid='manual-admission-input']").KeyDown(new KeyboardEventArgs { Key = "Enter", Code = "Enter" });

        _service.ReleaseHeldResults(AdmissionCheckInUiCodes.Rejected, status);
        await cut.InvokeAsync(() => { });

        await Assert.That(_service.Calls.Count).IsEqualTo(1);
        await Assert.That(cut.Find("[data-testid='admission-queue-depth']").GetAttribute("data-depth")).IsEqualTo("0");
        await Assert.That(cut.FindAll($"[data-testid='{expectedStatusTestId}'][role='alert']").Count).IsEqualTo(1);
        await Assert.That(cut.Find("[data-testid='admission-live-region']").TextContent).IsNotEmpty();
        await Assert.That(cut.FindAll("[data-testid='admission-result']")).IsEmpty();
        await Assert.That(cut.Markup).DoesNotContain("Retry-After");
    }

    [Test]
    public async Task ConnectivityFailurePersistsNoTokenAndQueuesNothingOffline()
    {
        _service.Throw(new HttpRequestException("offline"));
        var cut = Render(Event(CheckInRelation));

        await Submit(cut, "manual-admission-input", Bearer(180));

        await Assert.That(cut.FindAll("[data-testid='admission-online-required'][role='alert']").Count).IsEqualTo(1);
        await Assert.That(cut.Find("[data-testid='admission-queue-depth']").GetAttribute("data-depth")).IsEqualTo("0");
        await Assert.That(_ctx.JSInterop.Invocations.Select(invocation => invocation.Identifier)
            .Any(identifier => identifier.Contains("localStorage", StringComparison.Ordinal)
                || identifier.Contains("sessionStorage", StringComparison.Ordinal))).IsFalse();
        await Assert.That(cut.Markup).DoesNotContain("data-offline-queue");
    }

    [Test]
    public async Task ClientServiceContractRequiresExplicitEventAndTargetThroughOneTypedMethod()
    {
        Type service = typeof(IAdmissionCheckInService);
        MethodInfo[] operations = service.GetMethods()
            .Where(method => !method.IsSpecialName)
            .ToArray();
        MethodInfo operation = operations.Single();
        ParameterInfo[] parameters = operation.GetParameters();

        await Assert.That(operation.Name)
            .IsEqualTo(nameof(IAdmissionCheckInService.CheckInAsync));
        await Assert.That(parameters.Length).IsEqualTo(4);
        await Assert.That((parameters[0].Name, parameters[0].ParameterType)).IsEqualTo(("eventId", typeof(Guid)));
        await Assert.That((parameters[1].Name, parameters[1].ParameterType)).IsEqualTo(("targetId", typeof(Guid)));
        await Assert.That((parameters[2].Name, parameters[2].ParameterType))
            .IsEqualTo(("credential", typeof(AdmissionCredentialBearer)));
        await Assert.That((parameters[3].Name, parameters[3].ParameterType))
            .IsEqualTo(("cancellationToken", typeof(CancellationToken)));
        await Assert.That(operation.ReturnType.IsGenericType
            && operation.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)).IsTrue();
    }

    private IRenderedComponent<StudioAdmissionCheckIn> Render(
        EventDto resource,
        long capabilityGeneration = 1,
        string? scannerCapability = null)
    {
        _ctx.Services.AddSingleton<IAdmissionCheckInService>(_service);
        NavigationManager navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("targetId", TargetId(resource)));
        return _ctx.RenderMudComponent<StudioAdmissionCheckIn>(parameters => parameters
            .Add(component => component.EventId, resource.Id!.Value)
            .Add(component => component.Event, resource)
            .Add(component => component.CapabilityGeneration, capabilityGeneration)
            .Add(component => component.ScannerCapability, scannerCapability));
    }

    private void RenderAgain(
        IRenderedComponent<StudioAdmissionCheckIn> cut,
        EventDto resource,
        long capabilityGeneration)
    {
        NavigationManager navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("targetId", TargetId(resource)));
        cut.Render(parameters => parameters
            .Add(component => component.EventId, resource.Id!.Value)
            .Add(component => component.Event, resource)
            .Add(component => component.CapabilityGeneration, capabilityGeneration));
    }

    private static async Task Submit(
        IRenderedComponent<StudioAdmissionCheckIn> cut,
        string testId,
        string value)
    {
        var input = cut.Find($"[data-testid='{testId}']");
        await input.InputAsync(new ChangeEventArgs { Value = value });
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Enter", Code = "Enter" });
    }

    private static void DisposeRenderedPhase21Component(
        IRenderedComponent<StudioAdmissionCheckIn> cut) =>
        cut.Instance.Dispose();

    private static EventDto Event(string? relation)
    {
        Guid eventId = Guid.CreateVersion7();
        var resource = new EventDto { Id = eventId, Title = "Door operations" };
        resource.AdditionalProperties["_links"] = System.Text.Json.JsonSerializer.SerializeToElement(
            relation is null
                ? new Dictionary<string, object>()
                : new Dictionary<string, object>
                {
                    [relation] = new
                    {
                        href = $"/api/events/{eventId:D}/admission-check-ins",
                        method = "POST"
                    }
                });
        return resource;
    }

    private static bool ExposesSecurityReason(string feedback) =>
        feedback.Contains("scope", StringComparison.OrdinalIgnoreCase)
        || feedback.Contains("revok", StringComparison.OrdinalIgnoreCase)
        || feedback.Contains("tenant", StringComparison.OrdinalIgnoreCase);

    private static Guid TargetId(EventDto resource)
    {
        byte[] bytes = resource.Id!.Value.ToByteArray();
        bytes[^1] ^= 0x01;
        return new Guid(bytes);
    }

    private static string Bearer(int seed)
    {
        var bytes = new byte[AdmissionCredentialBearer.ByteLength];
        BitConverter.TryWriteBytes(bytes, seed);
        return AdmissionCredentialBearer.FromBytes(bytes).Value;
    }

    public enum CameraFallbackScenario
    {
        Unsupported,
        PermissionDenied,
        DetectionFailure
    }

    private sealed record ServiceCall(
        Guid EventId,
        Guid TargetId,
        AdmissionCredentialBearer Credential,
        CancellationToken CancellationToken);

    private sealed class Phase21ServiceHandler : IAdmissionCheckInService
    {
        private readonly Queue<string> _resultCodes = new();
        private readonly List<TaskCompletionSource<AdmissionCheckInUiResult>> _held = [];
        private Exception? _exception;
        private bool _hold;

        public List<ServiceCall> Calls { get; } = [];

        public IReadOnlyList<string> CredentialValues => Calls
            .Select(call => call.Credential.Value)
            .ToArray();

        public void EnqueueResult(string code) => _resultCodes.Enqueue(code);

        public void HoldResults() => _hold = true;

        public void Throw(Exception exception) => _exception = exception;

        public Task<AdmissionCheckInUiResult> CheckInAsync(
            Guid eventId,
            Guid targetId,
            AdmissionCredentialBearer credential,
            CancellationToken cancellationToken)
        {
            Calls.Add(new ServiceCall(eventId, targetId, credential, cancellationToken));
            if (_exception is not null)
            {
                return Task.FromException<AdmissionCheckInUiResult>(_exception);
            }

            if (_hold)
            {
                var completion = new TaskCompletionSource<AdmissionCheckInUiResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _held.Add(completion);
                return completion.Task;
            }

            return Task.FromResult(Result(
                _resultCodes.Count > 0 ? _resultCodes.Dequeue() : "CheckedIn",
                AdmissionCheckInUiStatus.Completed));
        }

        public void ReleaseHeldResults(
            string code,
            AdmissionCheckInUiStatus status = AdmissionCheckInUiStatus.Completed)
        {
            _hold = false;
            foreach (TaskCompletionSource<AdmissionCheckInUiResult> completion in _held)
            {
                completion.TrySetResult(Result(code, status));
            }
            _held.Clear();
        }

        private static AdmissionCheckInUiResult Result(
            string code,
            AdmissionCheckInUiStatus status) => new()
            {
                Code = code,
                Message = code,
                Status = status
            };
    }
}
