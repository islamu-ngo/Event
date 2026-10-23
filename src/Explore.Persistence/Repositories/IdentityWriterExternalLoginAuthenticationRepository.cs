using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Resolves one provider binding on the original selected IdentityWriter application owner.</summary>
internal sealed class IdentityWriterExternalLoginAuthenticationRepository : IExternalLoginAuthenticationRepository
{
    private readonly LocalIdentityExecutionLease _execution;

    internal IdentityWriterExternalLoginAuthenticationRepository(LocalIdentityExecutionLease execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        _execution = execution;
    }

    public async Task<UserExternalLogin?> GetByProviderAndKey(
        ProviderAccountKey accountKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        var application = _execution.Application
            ?? throw new UnauthorizedAccessException("The selected application owner is required.");
        var identity = _execution.Identity
            ?? throw new UnauthorizedAccessException("The selected identity owner is required.");
        var applicationTransaction = application.Database.CurrentTransaction;
        var identityTransaction = identity.Database.CurrentTransaction;
        void RequireCurrent()
        {
            _execution.RequireCurrent(application, identity);
            HumanSecuritySourceGate.RequireCurrent(application);
            if (!ReferenceEquals(application.Database.CurrentTransaction, applicationTransaction)
                || !ReferenceEquals(identity.Database.CurrentTransaction, identityTransaction))
                throw new UnauthorizedAccessException("The original binding lookup transaction changed.");
        }

        RequireCurrent();
        var binding = await application.UserExternalLogins.AsNoTracking().SingleOrDefaultAsync(
            row => row.AuthenticationProviderId == (int)accountKey.ProviderKind
                && row.ProviderKey == accountKey.Value, cancellationToken);
        RequireCurrent();
        return binding;
    }
}
