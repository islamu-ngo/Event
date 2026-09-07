using Explore.Blazor.Client.Components.Shell.AiAssistant;

namespace Explore.Blazor.Client.Tests.Components.Shell;

public sealed class AiReferencePickerTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task SelectedChip_CanBeRemovedWithKeyboard()
    {
        var referenceId = Guid.CreateVersion7();
        Guid? removedReferenceId = null;
        var cut = _ctx.RenderMudComponent<AiReferencePicker>(parameters => parameters
            .Add(component => component.SelectedReferences, [new HalResourceOfAiReferenceSearchResultDto
            {
                ReferenceId = referenceId,
                DisplayName = "Community Iftar",
                Kind = "Event"
            }])
            .Add(component => component.OnRemove, EventCallback.Factory.Create<Guid>(this, id => removedReferenceId = id)));

        await cut.Find("[data-testid='ai-reference-chip']").KeyDownAsync(new KeyboardEventArgs { Key = "Delete" });

        await Assert.That(removedReferenceId).IsEqualTo(referenceId);
    }

}
