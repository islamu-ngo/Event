// ABOUTME: Dedicated EF Core context for operators hosting Local Identity in an external database.
// ABOUTME: Applies only Identity entity mappings and never exposes platform Domain aggregates.

using Explore.Persistence.Schema;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Explore.Persistence.Identity;

public sealed class ExternalIdentityDbContext(
    DbContextOptions<ExternalIdentityDbContext> options)
    : DbContext(options)
{
    public DbSet<LocalIdentityUser> LocalIdentityUsers => Set<LocalIdentityUser>();
    public DbSet<LocalIdentityRole> LocalIdentityRoles => Set<LocalIdentityRole>();
    public DbSet<LocalIdentityLifecycleOperation> LocalIdentityLifecycleOperations => Set<LocalIdentityLifecycleOperation>();
    public DbSet<IdentityUserRole<Guid>> IdentityUserRoles => Set<IdentityUserRole<Guid>>();
    public DbSet<IdentityUserClaim<Guid>> IdentityUserClaims => Set<IdentityUserClaim<Guid>>();
    public DbSet<IdentityRoleClaim<Guid>> IdentityRoleClaims => Set<IdentityRoleClaim<Guid>>();
    public DbSet<IdentityUserLogin<Guid>> IdentityUserLogins => Set<IdentityUserLogin<Guid>>();
    public DbSet<IdentityUserToken<Guid>> IdentityUserTokens => Set<IdentityUserToken<Guid>>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ExternalIdentityDbContext).Assembly,
            configurationType => configurationType.Namespace
                is "Explore.Persistence.Identity.Configurations");
        PortableRelationalModelPolicy.Apply(modelBuilder, Database.ProviderName);
        string schema = this.GetService<IDbContextOptions>()
            .FindExtension<Database.RelationalNamespaceOptionsExtension>()?.ModelSchema
            ?? IdentityDatabaseConfiguration.DefaultSchema;
        RelationalModelNamespace.Apply(modelBuilder, Database.ProviderName, schema);
        MySqlModelIdentifierPolicy.Apply(modelBuilder, Database.ProviderName);
    }
}
