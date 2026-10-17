namespace Explore.Persistence;

public sealed partial class EfCoreUnitOfWork
{
    internal void RequireTrustedSetupSettingsOwner(ExploreDbContext context)
    {
        if (!ReferenceEquals(_dbContext, context))
            throw new UnauthorizedAccessException("Setup settings must use the enrollment data owner.");
    }
}
