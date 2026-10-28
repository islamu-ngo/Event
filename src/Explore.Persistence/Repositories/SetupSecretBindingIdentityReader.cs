using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class SetupSecretBindingIdentityReader(ExploreDbContext context) : ISetupSecretBindingIdentityReader
{
    public Task<Guid?> ReadAsync(string bindingKey, CancellationToken cancellationToken)
    {
        if (bindingKey is not ("setup.signing" or "setup.encryption"))
            throw new ArgumentOutOfRangeException(nameof(bindingKey));
        return context.SecretBindings
            .Where(binding => binding.SettingKey == bindingKey
                && binding.Qualifier == string.Empty
                && binding.SettingScopeId == (int)ConfigurationScopeEnum.Instance
                && binding.ScopeId == null)
            .Select(binding => (Guid?)binding.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
