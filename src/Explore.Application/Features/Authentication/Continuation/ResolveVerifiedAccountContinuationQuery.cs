using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Authentication.Continuation;

/// <summary>
/// Carries an exact canonical account key assembled by a verified authentication adapter,
/// and an optional server-resolved tenant target. This is never a browser input contract.
/// </summary>
public sealed record ResolveVerifiedAccountContinuationQuery(
    ProviderAccountKey AccountKey,
    Guid? TargetTenantId = null) : IQuery<VerifiedAccountContinuationResult>;
