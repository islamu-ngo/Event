using Explore.Domain;
using Explore.Persistence.Security;

namespace Explore.Persistence.Repositories;

public partial class EventRepository
{
    internal Task<Event?> GetPublicDetailNativeAsync(Guid id, CancellationToken cancellationToken) =>
        ReadPublicDetailNativeAsync(id, null, cancellationToken);

    internal Task<Event?> GetPublicDetailByCodeNativeAsync(string publicCode, CancellationToken cancellationToken) =>
        ReadPublicDetailNativeAsync(null, publicCode, cancellationToken);

    private Task<Event?> ReadPublicDetailNativeAsync(Guid? id, string? code, CancellationToken cancellationToken)
    {
        var binding = PublicDiscoveryBinding();
        if (_nativeEnrollment is null || _nativeEnrollment != binding.Enrollment)
            throw new UnauthorizedAccessException("public_detail_original_enrollment_required");
        return PostgresPublicEventDetailContract.ReadAsync(_dbContext, binding, id, code, cancellationToken);
    }
}
