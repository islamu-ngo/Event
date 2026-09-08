using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore.Design;

namespace Explore.Persistence.Migrations.SqlServer;

public sealed class SqlServerExternalIdentityDbContextFactory
    : IDesignTimeDbContextFactory<ExternalIdentityDbContext>
{
    public ExternalIdentityDbContext CreateDbContext(string[] args) =>
        new ExternalIdentityDbContextFactory().CreateDbContext(args);
}
