using Explore.Persistence.Schema;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Explore.Persistence;

public sealed class DataProtectionKeyContext(DbContextOptions<DataProtectionKeyContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var schema = this.GetService<IDbContextOptions>()
            .FindExtension<Database.RelationalNamespaceOptionsExtension>()?.ModelSchema
            ?? RelationalModelNamespace.DefaultSchema;
        RelationalModelNamespace.Apply(modelBuilder, Database.ProviderName, schema);
    }
}
