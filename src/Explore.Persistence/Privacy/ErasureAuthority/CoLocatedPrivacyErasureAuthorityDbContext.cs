using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Privacy.ErasureAuthority.Configurations;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Explore.Persistence.Privacy.ErasureAuthority;

public sealed class CoLocatedPrivacyErasureAuthorityDbContext(
    DbContextOptions<CoLocatedPrivacyErasureAuthorityDbContext> options) : DbContext(options)
{
    public DbSet<PrivacyErasureIntent> ErasureIntents => Set<PrivacyErasureIntent>();
    public DbSet<PrivacyErasureCounter> AuthorityCounters => Set<PrivacyErasureCounter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        string schema = this.GetService<IDbContextOptions>()
            .FindExtension<RelationalNamespaceOptionsExtension>()?.ModelSchema
            ?? RelationalModelNamespace.DefaultSchema;
        modelBuilder.HasDefaultSchema(schema);
        modelBuilder.ApplyConfiguration(new PrivacyErasureIntentConfiguration());
        modelBuilder.ApplyConfiguration(new PrivacyErasureCounterConfiguration());
    }
}
