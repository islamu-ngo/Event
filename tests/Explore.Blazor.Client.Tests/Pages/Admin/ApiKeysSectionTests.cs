using Explore.Blazor.Client.Pages.Admin.Components;
using Explore.Blazor.Client.Pages.Admin.Dialogs;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class ApiKeysSectionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CreateCompletion_RefreshesKeysOnlyOnSuccess(bool success)
    {
        using var ctx = new BlazorTestContext();
        var service = Substitute.For<IExternalApiKeyService>();
        service.GetApiKeysAsync().Returns(
            new List<ExternalApiKeyListDto>(),
            new List<ExternalApiKeyListDto> { new() { Name = "Created key", ExternalApiKeyOwnerTypeId = 1 } });
        var dialogs = Substitute.For<IDialogService>();
        var dialog = Substitute.For<IDialogReference>();
        dialog.Result.Returns(Task.FromResult<DialogResult?>(success ? DialogResult.Ok(true) : null));
        dialogs.ShowAsync<CreateApiKeyDialog>(
            Arg.Any<string>(), Arg.Any<DialogParameters>(), Arg.Any<DialogOptions>()).Returns(dialog);
        ctx.Services.AddSingleton(service);
        ctx.Services.AddSingleton(dialogs);
        var cut = ctx.RenderMudComponent<ApiKeysSection>();

        await cut.FindAll("button").Single(button => button.TextContent.Contains("Create API Key")).ClickAsync(new());

        await Assert.That(cut.Markup.Contains("Created key", StringComparison.Ordinal)).IsEqualTo(success);
        await Assert.That(cut.Markup.Contains("No API keys", StringComparison.Ordinal)).IsEqualTo(!success);
    }
}
