using System.Reflection;
using Explore.Blazor.Client.Components.Moderation;

namespace Explore.Blazor.Client.Tests.Components.Moderation;

public sealed class ModerationReportActionDialogTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task Submit_WhenAssignUserIdInvalid_ShowsValidationError()
    {
        var cut = _ctx.RenderMudComponent<ModerationReportActionDialog>(parameters => parameters
            .Add(component => component.ActionKind, ModerationReportActionKind.Assign)
            .Add(component => component.ReportId, Guid.NewGuid()));

        SetPrivateField(cut.Instance, "_assigneeUserId", "not-a-guid");
        InvokePrivateVoid(cut.Instance, "Submit");

        await Assert.That(GetPrivateField<string?>(cut.Instance, "_errorMessage"))
            .IsEqualTo("Assignee user id is required.");
    }

    [Test]
    public async Task Submit_WhenHeavyRedactionUnconfirmed_ShowsIrreversibleValidationError()
    {
        var cut = _ctx.RenderMudComponent<ModerationReportActionDialog>(parameters => parameters
            .Add(component => component.ActionKind, ModerationReportActionKind.Decide)
            .Add(component => component.ReportId, Guid.NewGuid())
            .Add(component => component.DefaultReasonCode, "spam"));

        SetPrivateField(cut.Instance, "_decisionKind", EventReportDecisionKind.HeavyRedact);
        InvokePrivateVoid(cut.Instance, "Submit");

        await Assert.That(GetPrivateField<string?>(cut.Instance, "_errorMessage"))
            .IsEqualTo("Irreversible confirmation is required.");
    }

    public void Dispose() => _ctx.Dispose();

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");

        return (T?)field.GetValue(instance)
            ?? throw new InvalidOperationException($"Field {fieldName} returned null.");
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");

        field.SetValue(instance, value);
    }

    private static void InvokePrivateVoid(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");

        method.Invoke(instance, null);
    }
}
