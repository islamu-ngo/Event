// ABOUTME: Private durable issuance budgets containing only real tenant/event scope and the current database minute.
// ABOUTME: One row per scope is reused forever; these counters never reserve tickets or identify requesters.

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
