namespace Explore.Persistence;

public partial class ExploreDbContext
{
    /// <summary>Only the co-located SQLite authority gate may own this enlistment.</summary>
    internal bool IdentityFenceOwnsTransaction { get; set; }
    internal bool IdentityFenceTransactionFailed { get; set; }
}
