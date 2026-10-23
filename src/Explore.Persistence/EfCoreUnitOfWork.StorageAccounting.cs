namespace Explore.Persistence;

public sealed partial class EfCoreUnitOfWork
{
    internal void RequireStorageAccountingOwner(ExploreDbContext context)
    {
        if (!ReferenceEquals(_dbContext, context))
            throw new UnauthorizedAccessException("Accounting must retain its original physical data owner.");
    }
}
