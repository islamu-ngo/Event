using Explore.Domain;
using Explore.Persistence.Privacy.ErasureAuthority.Configurations;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Privacy.ErasureAuthority;

public sealed class EmbeddedPrivacyErasureAuthorityDbContext(
    DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options) : DbContext(options)
{
    public const string MigrationsAssembly = "Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite";
    public const string MigrationsHistoryTable =
        RelationalModelNamespace.Prefix + "__EFPrivacyErasureAuthorityMigrationsHistory";

    public DbSet<PrivacyErasureIntent> ErasureIntents => Set<PrivacyErasureIntent>();
    public DbSet<PrivacyErasureCounter> AuthorityCounters => Set<PrivacyErasureCounter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new EmbeddedPrivacyErasureIntentConfiguration());
        modelBuilder.ApplyConfiguration(new EmbeddedPrivacyErasureCounterConfiguration());
    }
}
