using Explore.Blazor.Client.Pages.User.Components;
using Explore.Blazor.Client.Pages.User.Components.Dialogs;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.User;

public sealed class SettingsPersonalInfoTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    [Test]
    [Arguments(false, "null")]
    [Arguments(true, "null")]
    [Arguments(false, "cancel")]
    [Arguments(true, "cancel")]
    [Arguments(false, "success")]
    [Arguments(true, "success")]
    public async Task DialogCompletion_SavesAndRefreshesOnlyOnSuccess(bool profileImage, string completion)
    {
        var user = new UserDto
        {
            Id = Guid.NewGuid(),
            ConcurrencyStamp = Guid.NewGuid(),
            FirstName = "Original",
            LastName = "User",
            ProfileImageUri = "/original.png"
        };
        var refreshed = user with { FirstName = "Updated", ProfileImageUri = "/updated.png" };
        var imageId = Guid.NewGuid();
        UpdateUserDto? saved = null;
        var users = Substitute.For<IUserService>();
        users.GetCurrentUserAsync().Returns(_ => saved is null ? user : refreshed);
        users.UpdateUserAsync(user.Id!.Value, user.ConcurrencyStamp!.Value, Arg.Any<UpdateUserDto>())
            .Returns(call =>
            {
                saved = call.Arg<UpdateUserDto>();
                return new BaseCommandResponseOfGuid { Success = true };
            });
        _ctx.Services.AddSingleton(users);
        var reference = Substitute.For<IDialogReference>();
        object data = profileImage
            ? new EditUserProfileImageDialog.ProfileImageUploadResult(imageId, "/uploaded.png")
            : new UpdateUserNamesDto { FirstName = "Updated", LastName = "User" };
        reference.Result.Returns(Task.FromResult<DialogResult?>(completion switch
        {
            "null" => null,
            "cancel" => DialogResult.Cancel(),
            _ => DialogResult.Ok(data)
        }));
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowAsync<EditUserNamesDialog>(Arg.Any<string>(), Arg.Any<DialogOptions>())
            .Returns(Task.FromResult(reference));
        dialogs.ShowAsync<EditUserProfileImageDialog>(Arg.Any<string>(), Arg.Any<DialogOptions>())
            .Returns(Task.FromResult(reference));
        _ctx.Services.AddSingleton(dialogs);
        var cut = _ctx.RenderMudComponent<SettingsPersonalInfo>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await cut.FindAll("button").Single(button => button.TextContent.Trim() == (profileImage ? "Change" : "Edit"))
            .ClickAsync(new MouseEventArgs()).WaitAsync(timeout.Token);

        var state = _ctx.Services.GetRequiredService<CurrentUserState>();
        if (completion == "success")
        {
            await Assert.That(saved).IsNotNull();
            if (profileImage)
            {
                await Assert.That(saved!.ProfileImage?.ProfilePictureId).IsEqualTo(imageId);
                await Assert.That(saved.Names).IsNull();
            }
            else
            {
                await Assert.That(saved!.Names?.FirstName).IsEqualTo("Updated");
                await Assert.That(saved.ProfileImage).IsNull();
            }
            await Assert.That(cut.Markup).Contains("Updated User");
            await Assert.That(cut.Find("img").GetAttribute("src")).IsEqualTo("/updated.png");
            await Assert.That(cut.Markup).Contains("Profile updated successfully!");
            await Assert.That(state.Current).IsEqualTo(refreshed);
        }
        else
        {
            await Assert.That(saved).IsNull();
            await Assert.That(state.Current).IsNull();
            await Assert.That(cut.Markup).Contains("Original User");
            await Assert.That(cut.Find("img").GetAttribute("src")).IsEqualTo("/original.png");
            await Assert.That(cut.Markup).DoesNotContain("Profile updated successfully!");
        }
    }
}
