namespace Explore.Blazor.Client.Services.Docking;

public interface IDockLayoutPersistence
{
    Task<DockLayoutSnapshot?> LoadAsync(string layoutKey, CancellationToken cancellationToken = default);

    Task<bool> SaveAsync(DockLayoutSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string layoutKey, CancellationToken cancellationToken = default);
}
