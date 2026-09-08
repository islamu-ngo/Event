using Explore.Application.Authentication;

namespace Explore.Application.Contracts.Identity;

public interface IMachinePrincipalExecutionAccessor
{
    void SetPrincipal(ApiKeyPrincipalContext principal);

    void Clear();
}
