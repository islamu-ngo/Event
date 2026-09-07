namespace Explore.Blazor.Hosting;

public enum BlazorHostProfile
{
    Split,
    Combined
}

internal sealed record BlazorHostProfileRegistration(BlazorHostProfile Profile);
