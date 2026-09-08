using Explore.Blazor.Client.Components.Shell.AiAssistant;
using Explore.Blazor.Client.Tests;

namespace Explore.Blazor.Client.Tests.Components.Shell;

public sealed class AiConversationComposerTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Render_WhenHostedDirectly_EmitsActorPromptAndSendCallbacks()
    {
        var firstActorId = Guid.CreateVersion7();
        var secondActorId = Guid.CreateVersion7();
        Guid? selectedActorId = null;
        string? prompt = null;
        var sent = false;
        var cut = _ctx.RenderMudComponent<AiConversationComposer>(parameters => parameters
            .Add(component => component.ActorOptions,
            [
                new(firstActorId, "User", "Amina Yusuf"),
                new(secondActorId, "Organization", "ISLAMU Center")
            ])
            .Add(component => component.SelectedActorId, firstActorId)
            .Add(component => component.SelectedActorIdChanged,
                EventCallback.Factory.Create<Guid?>(this, value => selectedActorId = value))
            .Add(component => component.OnPromptInput,
                EventCallback.Factory.Create<string>(this, value => prompt = value))
            .Add(component => component.OnSend,
                EventCallback.Factory.Create(this, () => sent = true)));

        await Assert.That(cut.Find("[data-testid='ai-rail-send']").HasAttribute("disabled")).IsFalse();

        await cut.Find("[data-testid='ai-rail-actor-selector']")
            .ChangeAsync(new ChangeEventArgs { Value = secondActorId.ToString() });
        await cut.Find("[data-testid='ai-rail-prompt']")
            .InputAsync(new ChangeEventArgs { Value = "Draft an event" });
        await cut.Find("[data-testid='ai-rail-send']").ClickAsync(new MouseEventArgs());

        await Assert.That(selectedActorId).IsEqualTo(secondActorId);
        await Assert.That(prompt).IsEqualTo("Draft an event");
        await Assert.That(sent).IsTrue();
    }

    [Test]
    public async Task Render_WhenTwoHostsMountComposer_GeneratesDistinctAccessibleIds()
    {
        using var secondContext = new BlazorTestContext();
        var first = _ctx.RenderMudComponent<AiConversationComposer>();
        var second = secondContext.RenderMudComponent<AiConversationComposer>();

        var firstPrompt = first.Find("[data-testid='ai-rail-prompt']");
        var secondPrompt = second.Find("[data-testid='ai-rail-prompt']");
        var firstActor = first.Find("[data-testid='ai-rail-actor-selector']");
        var secondActor = second.Find("[data-testid='ai-rail-actor-selector']");

        await Assert.That(firstPrompt.Id).IsNotEqualTo(secondPrompt.Id);
        await Assert.That(firstActor.Id).IsNotEqualTo(secondActor.Id);
        await Assert.That(first.Find("label").GetAttribute("for")).IsEqualTo(firstActor.Id);
        await Assert.That(second.Find("label").GetAttribute("for")).IsEqualTo(secondActor.Id);
    }
}
