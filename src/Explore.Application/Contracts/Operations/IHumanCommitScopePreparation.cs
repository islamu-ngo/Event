using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Contracts.Operations;

/// <summary>
/// Resolves trusted human identity and binds the original selected database owner before
/// constructing consumers. Machine and internal callers retain their separate authority.
/// </summary>
public interface IHumanCommitScopePreparation
{
    HumanCommitSubject? Subject { get; }
    ValueTask PrepareAsync(CancellationToken cancellationToken);
}
