using MudBlazor;

namespace Explore.Blazor.Client.Services;

/// <summary>
/// Standard <see cref="DialogOptions"/> presets.
/// Use these instead of manually constructing DialogOptions to ensure
/// consistent dialog behavior across the application.
/// </summary>
public static class DialogOptionsFactory
{
    /// <summary>
    /// Small dialog — confirmations, simple selections, delete prompts.
    /// CloseOnEscapeKey + MaxWidth.Small + FullWidth.
    /// </summary>
    public static DialogOptions Small() => new()
    {
        CloseOnEscapeKey = true,
        MaxWidth = MaxWidth.Small,
        FullWidth = true
    };

    /// <summary>
    /// Medium dialog — forms, registration, aspect editing.
    /// CloseOnEscapeKey + MaxWidth.Medium + FullWidth.
    /// </summary>
    public static DialogOptions Medium() => new()
    {
        CloseOnEscapeKey = true,
        MaxWidth = MaxWidth.Medium,
        FullWidth = true
    };

    /// <summary>
    /// Centered confirmation — session selection, centered prompts.
    /// Small + Position.Center.
    /// </summary>
    public static DialogOptions Confirmation() => new()
    {
        CloseOnEscapeKey = true,
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        Position = DialogPosition.Center
    };

    /// <summary>
    /// Borderless sign-in prompt — no title chrome and custom backdrop.
    /// </summary>
    public static DialogOptions LoginPrompt() => new()
    {
        CloseOnEscapeKey = true,
        MaxWidth = MaxWidth.ExtraSmall,
        FullWidth = true,
        CloseButton = false,
        NoHeader = true,
        BackgroundClass = "login-prompt-backdrop"
    };

    /// <summary>
    /// Editor dialog — description editing, rich content.
    /// Medium + CloseButton + BackdropClick.
    /// </summary>
    public static DialogOptions Editor() => new()
    {
        MaxWidth = MaxWidth.Medium,
        FullWidth = true,
        CloseButton = true,
        BackdropClick = true
    };

    /// <summary>
    /// Full-viewport media lightbox — transparent chrome with backdrop and Escape dismissal.
    /// </summary>
    public static DialogOptions ImageLightbox() => new()
    {
        CloseOnEscapeKey = true,
        MaxWidth = MaxWidth.False,
        FullWidth = true,
        NoHeader = true,
        BackdropClick = true,
        Position = DialogPosition.Center
    };
}
