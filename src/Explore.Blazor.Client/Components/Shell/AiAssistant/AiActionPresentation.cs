namespace Explore.Blazor.Client.Components.Shell.AiAssistant;

public sealed record AiActionResultPresentation(
    string Heading,
    string Description,
    string? Destination = null,
    string? LinkText = null);

/// <summary>
/// Host-owned copy and affordances. Live hosts derive permissions from AI HAL;
/// simulation hosts derive them from private learning HAL and authored transitions.
/// </summary>
public sealed record AiActionPresentation(
    string Title,
    string Description,
    bool CanConfirm = false,
    bool CanReject = false,
    string ConfirmLabel = "Confirm",
    string RejectLabel = "Reject",
    AiActionResultPresentation? Result = null);
