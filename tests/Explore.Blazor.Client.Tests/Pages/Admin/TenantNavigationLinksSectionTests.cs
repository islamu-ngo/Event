using Explore.Blazor.Client.Pages.Admin.Components;
using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class TenantNavigationLinksSectionTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task DialogCompletion_MutatesAndRefreshesOnlyOnTypedSuccess(bool edit, bool success)
    {
        using var ctx = new BlazorTestContext();
        using var state = new TenantNavLinksState();
        var original = new TenantNavigationLinkDto { Id = Guid.NewGuid(), Label = "Original", Url = "/original", Order = 1 };
        var changed = new TenantNavigationLinkDto { Id = original.Id, Label = "Changed", Url = "/changed", Order = 1 };
        var service = Substitute.For<ITenantNavigationService>();
        var persisted = false;
        service.GetNavigationLinksAsync().Returns(_ =>
            new List<TenantNavigationLinkDto> { persisted ? changed : original });
        service.CreateNavigationLinkAsync(Arg.Any<CreateTenantNavigationLinkDto>()).Returns(call =>
        {
            persisted = call.Arg<CreateTenantNavigationLinkDto>()?.Label == "Changed";
            return new BaseCommandResponseOfGuid { Success = true };
        });
        service.UpdateNavigationLinkAsync(original.Id.Value, Arg.Any<UpdateTenantNavigationLinkDto>()).Returns(call =>
        {
            persisted = call.Arg<UpdateTenantNavigationLinkDto>()?.Label?.Value == "Changed";
            return new BaseCommandResponseOfboolean { Success = true };
        });
        var dialogs = Substitute.For<IDialogService>();
        var dialog = Substitute.For<IDialogReference>();
        object payload = edit
            ? new UpdateTenantNavigationLinkDto { Label = new() { Value = "Changed" }, Url = new() { Value = "/changed" } }
            : new CreateTenantNavigationLinkDto { Label = "Changed", Url = "/changed" };
        dialog.Result.Returns(Task.FromResult<DialogResult?>(success ? DialogResult.Ok(payload) : null));
        dialogs.ShowAsync<TenantNavigationDialog>(
            Arg.Any<string>(), Arg.Any<DialogParameters>(), Arg.Any<DialogOptions>()).Returns(dialog);
        ctx.Services.AddSingleton(service);
        ctx.Services.AddSingleton(dialogs);
        ctx.Services.AddSingleton(state);
        state.SetLinks([original]);
        var cut = ctx.RenderMudComponent<TenantNavigationLinksSection>();

        await (edit ? cut.Find("button[title='Edit']") : cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Create Link"))).ClickAsync(new());

        await Assert.That(persisted).IsEqualTo(success);
        await Assert.That(state.Links.Single().Label).IsEqualTo(success ? "Changed" : "Original");
        await Assert.That(cut.Markup).Contains(success ? "Changed" : "Original");
    }
}
