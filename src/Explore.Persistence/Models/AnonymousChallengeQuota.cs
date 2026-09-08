
namespace Explore.Persistence.Models;

internal sealed class AnonymousChallengeTenantQuota
{
    public Guid TenantId { get; set; }
    public long WindowMinute { get; set; }
    public int Issued { get; set; }
}

internal sealed class AnonymousChallengeEventQuota
{
    public Guid TenantId { get; set; }
    public Guid EventId { get; set; }
    public long WindowMinute { get; set; }
    public int Issued { get; set; }
}
