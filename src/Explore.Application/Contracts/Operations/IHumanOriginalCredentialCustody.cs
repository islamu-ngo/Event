using Explore.Application.Authentication;

namespace Explore.Application.Contracts.Operations;

/// <summary>Requires the exact request, principal and validated observation retained by physical owner selection.</summary>
public interface IHumanOriginalCredentialCustody
{
    void RequireOriginalCredential(HumanAuthenticationLifetime lifetime);
}
