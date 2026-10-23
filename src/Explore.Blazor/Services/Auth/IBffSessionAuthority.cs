namespace Explore.Blazor.Services.Auth;

public interface IBffSessionAuthority
{
    Task<bool> IsActiveAsync(BffSessionBinding binding, CancellationToken cancellationToken);
}
