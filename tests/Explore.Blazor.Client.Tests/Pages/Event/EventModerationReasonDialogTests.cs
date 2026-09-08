using System.Reflection;
using Explore.Blazor.Client.Pages.Events.Dialogs;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Event;

public sealed class EventModerationReasonDialogTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task Render_WhenHeavyConfirmationRequired_DisablesSubmitUntilCheckboxIsChecked()
    {
        var cut = RenderDialog(requiresIrreversibleConfirmation: true);

        await Assert.That(GetPrivateProperty<bool>(cut.Instance, "CanSubmit")).IsFalse();

        SetPrivateField(cut.Instance, "_confirmedIrreversible", true);

        await Assert.That(GetPrivateProperty<bool>(cut.Instance, "CanSubmit")).IsTrue();
    }

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<EventModerationReasonDialog> RenderDialog(bool requiresIrreversibleConfirmation) =>
        _ctx.RenderMudComponent<EventModerationReasonDialog>(parameters => parameters
            .Add(component => component.DialogTitle, "Heavy Redact Event")
            .Add(component => component.Message, "Permanently redact this event?")
            .Add(component => component.ConfirmText, "Redact Event")
            .Add(component => component.CancelText, "Keep Event")
            .Add(component => component.ConfirmIcon, Icons.Material.Filled.DeleteForever)
            .Add(component => component.TitleIcon, Icons.Material.Filled.Report)
            .Add(component => component.ConfirmColor, Color.Error)
            .Add(component => component.AlertSeverity, Severity.Error)
            .Add(component => component.RequiresIrreversibleConfirmation, requiresIrreversibleConfirmation)
            .Add(component => component.ReasonOptions, [new EventModerationReasonOption("illegal_image", "Illegal image")]));

    private static T GetPrivateProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Property {propertyName} was not found.");

        return (T?)property.GetValue(instance)
            ?? throw new InvalidOperationException($"Property {propertyName} returned null.");
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");

        field.SetValue(instance, value);
    }
}
