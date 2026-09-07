using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore.Design;

namespace Explore.Persistence.Migrations.MySql;

public sealed class MySqlExternalIdentityDbContextFactory
    : IDesignTimeDbContextFactory<ExternalIdentityDbContext>
{
    public ExternalIdentityDbContext CreateDbContext(string[] args) =>
        new ExternalIdentityDbContextFactory().CreateDbContext(args);
}
