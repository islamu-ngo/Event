using Explore.Application.Responses;

namespace Explore.Application.Exceptions;

public class QuotaExceededException : ApplicationException
{
    public const string Code = FailureCodes.QuotaExceeded;

    public QuotaExceededException(
        string message,
        string quotaKey,
        int limit,
        int? actual,
        int? attempted,
        string scope,
        Guid? tenantId = null)
        : base(message)
    {
        Details = new QuotaExceededDetails(quotaKey, limit, actual, attempted, scope, tenantId);
    }

    public QuotaExceededDetails Details { get; }

    public string QuotaKey => Details.QuotaKey;

    public int Limit => Details.Limit;

    public int? Actual => Details.Actual;

    public int? Attempted => Details.Attempted;

    public string Scope => Details.Scope;

    public Guid? TenantId => Details.TenantId;
}
