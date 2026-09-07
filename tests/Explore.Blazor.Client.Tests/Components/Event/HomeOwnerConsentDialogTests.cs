using System.Reflection;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Pages.Events.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Components.Event;

public sealed class HomeOwnerConsentDialogTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private IRenderedComponent<MudDialogProvider>? _dialogProvider;

    [Test]
    public async Task Render_LeavesConsentUncheckedAndConfirmDisabled()
    {
        RegisterService();
        var cut = RenderDialog();

        // Exactly one consent control exists, and it starts unticked: consent is never pre-granted.
        var checkboxes = _dialogProvider!.FindAll("input[type='checkbox']");
        await Assert.That(checkboxes.Count).IsEqualTo(1);
        await Assert.That(checkboxes[0].HasAttribute("checked")).IsFalse();

        var confirm = _dialogProvider.Find("[data-testid='private-home-consent-confirm']");
        await Assert.That(confirm.HasAttribute("disabled")).IsTrue();
    }

    [Test]
    public async Task Confirm_WithoutConsent_NeverReachesTheApi()
    {
        var service = RegisterService();
        var cut = RenderDialog();

        await cut.InvokeAsync(() => InvokePrivateTaskAsync(cut.Instance, "ConfirmAsync"));

        await service.DidNotReceive().ClassifyAsPrivateHomeAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<PrivateHomeOwnershipConsentDto>(), Arg.Any<CancellationToken>());
        await service.DidNotReceive().AcceptOwnershipAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<PrivateHomeOwnershipConsentDto>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Confirm_AfterConsent_ClassifiesWithTheVersionedAcknowledgement()
    {
        var locationId = Guid.NewGuid();
        var stamp = Guid.NewGuid();
        var service = RegisterService();
        PrivateHomeOwnershipConsentDto? captured = null;
        service.ClassifyAsPrivateHomeAsync(
                locationId,
                stamp,
                Arg.Do<PrivateHomeOwnershipConsentDto>(consent => captured = consent),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var cut = RenderDialog(locationId, stamp);
        SetPrivateField(cut.Instance, "_consentAcknowledged", true);

        await cut.InvokeAsync(() => InvokePrivateTaskAsync(cut.Instance, "ConfirmAsync"));

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.ConsentAcknowledged).IsEqualTo(true);
        await Assert.That(captured.ConsentVersion).IsEqualTo(PrivateHomeConsentStatement.CurrentVersion);
    }

    [Test]
    public async Task TransferMode_PromptsForOwnershipAndCallsTheAcceptanceOperation()
    {
        var locationId = Guid.NewGuid();
        var stamp = Guid.NewGuid();
        var service = RegisterService();
        service.AcceptOwnershipAsync(
                locationId, stamp, Arg.Any<PrivateHomeOwnershipConsentDto>(), Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var cut = RenderDialog(locationId, stamp, HomeOwnerConsentMode.Transfer);
        await Assert.That(_dialogProvider!.Markup).Contains("Take ownership of this home");

        SetPrivateField(cut.Instance, "_consentAcknowledged", true);
        await cut.InvokeAsync(() => InvokePrivateTaskAsync(cut.Instance, "ConfirmAsync"));

        await service.Received(1).AcceptOwnershipAsync(
            locationId, stamp, Arg.Any<PrivateHomeOwnershipConsentDto>(), Arg.Any<CancellationToken>());
        await service.DidNotReceive().ClassifyAsPrivateHomeAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<PrivateHomeOwnershipConsentDto>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Confirm_WhenServerRejects_KeepsTheDialogOpenWithTheReason()
    {
        var service = RegisterService();
        service.ClassifyAsPrivateHomeAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<PrivateHomeOwnershipConsentDto>(), Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "Private Home ownership changes require explicit owner consent."
            });

        var cut = RenderDialog();
        SetPrivateField(cut.Instance, "_consentAcknowledged", true);

        await cut.InvokeAsync(() => InvokePrivateTaskAsync(cut.Instance, "ConfirmAsync"));
        cut.Render();

        await Assert.That(GetPrivateField<string?>(cut.Instance, "_error"))
            .IsEqualTo("Private Home ownership changes require explicit owner consent.");
        await Assert.That(_dialogProvider!.FindAll("[role='alert']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task Render_ShowsTheConsentVersionTheOwnerIsAgreeingTo()
    {
        RegisterService();
        var cut = RenderDialog();

        await Assert.That(cut.Find("[data-testid='private-home-consent-version']").TextContent)
            .Contains(PrivateHomeConsentStatement.CurrentVersion);
    }

    public void Dispose() => _ctx.Dispose();

    private IPrivateHomeOwnershipService RegisterService()
    {
        var service = Substitute.For<IPrivateHomeOwnershipService>();
        _ctx.Services.AddSingleton(service);
        return service;
    }

    private IRenderedComponent<HomeOwnerConsentDialog> RenderDialog(
        Guid? locationId = null,
        Guid? stamp = null,
        HomeOwnerConsentMode mode = HomeOwnerConsentMode.Classify)
    {
        _dialogProvider = _ctx.Render<MudDialogProvider>();
        var parameters = new DialogParameters<HomeOwnerConsentDialog>
        {
            { component => component.LocationId, locationId ?? Guid.NewGuid() },
            { component => component.ExpectedConcurrencyStamp, stamp ?? Guid.NewGuid() },
            { component => component.Mode, mode }
        };
        _ = _ctx.Services.GetRequiredService<IDialogService>()
            .ShowAsync<HomeOwnerConsentDialog>("Private home consent", parameters);
        _dialogProvider.WaitForState(
            () => _dialogProvider.FindComponents<HomeOwnerConsentDialog>().Count == 1,
            TimeSpan.FromSeconds(3));
        return _dialogProvider.FindComponent<HomeOwnerConsentDialog>();
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");
        return (T)field.GetValue(instance)!;
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");
        field.SetValue(instance, value);
    }

    private static Task InvokePrivateTaskAsync(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");
        return (Task)method.Invoke(instance, null)!;
    }
}
