using Explore.Domain;

namespace Explore.Application.Contracts.PrivacyErasure;

public interface IPrivacyIdentityBindingReader
{
    Task<List<UserExternalLogin>> GetByUser(Guid userId);
    Task<IReadOnlyList<UserExternalLogin>> ReadExternalBindingsAfterAsync(
        Guid? afterId, int limit, CancellationToken cancellationToken);
}
