namespace Explore.Blazor.Client.Services.Scheduling;

/// <summary>
/// Explanations for a refused scheduler action. Each states what happened and what to do about it, because an
/// operator seeing "409" alone cannot tell a read-only host apart from a scheduler that is switched off.
/// </summary>
internal static class SchedulerAdminFailureMessages
{
    public static string Describe(int statusCode) => statusCode switch
    {
        401 => "Your session expired. Sign in again and retry the action.",
        403 => "You do not have permission to control the scheduler.",
        404 => "The scheduler administration API is not enabled on this host.",
        409 => "The scheduler refused the action. It may be read-only or disabled on this host.",
        429 => "Too many scheduler actions in a short time. Wait a moment and retry.",
        _ => "The scheduler action could not be completed."
    };
}
