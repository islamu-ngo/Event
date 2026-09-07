namespace Explore.Application.Exceptions;

using System.Collections.Immutable;

public sealed class TenantDirectoryOperatorIdentityReadinessException(
    string failureCode,
    IEnumerable<string> reasonCodes)
    : InvalidOperationException("Tenant directory operator identity is not ready.")
{
    public string FailureCode { get; } = failureCode;

    public ImmutableArray<string> ReasonCodes { get; } = [.. reasonCodes];
}
