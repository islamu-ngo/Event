namespace Explore.Blazor.Client.Services.Shell;

public interface IWorkspaceRegistry
{
    IReadOnlyList<WorkspaceDescriptor> Workspaces { get; }
}
