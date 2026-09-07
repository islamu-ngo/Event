namespace Explore.Application.Contracts.Secrets;

public interface ISetupSecretBindingCommitBarrier
{
    Task WaitBeforeProviderDispatchAsync(
        CancellationToken cancellationToken);
}
