using System.Text.Json.Serialization;

namespace Explore.Application.Responses;

public sealed record QuotaExceededDetails(
    string QuotaKey,
    int Limit,
    int? Actual,
    int? Attempted,
    string Scope,
    [property: JsonIgnore] Guid? TenantId = null)
{
    public string ToErrorMessage()
    {
        var observed = Attempted ?? Actual;
        return observed.HasValue
            ? $"quota_exceeded: quota '{QuotaKey}' limit {Limit} was exceeded by {observed.Value} in scope '{Scope}'."
            : $"quota_exceeded: quota '{QuotaKey}' limit {Limit} was exceeded in scope '{Scope}'.";
    }
}
