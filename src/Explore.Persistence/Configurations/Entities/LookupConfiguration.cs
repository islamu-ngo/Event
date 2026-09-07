using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public abstract class LookupConfiguration<TLookup> : IEntityTypeConfiguration<TLookup>
    where TLookup : class
{
    protected abstract string TableName { get; }

    public virtual void Configure(EntityTypeBuilder<TLookup> builder)
    {
        builder.ToTable(TableName);
        builder.Property("Id").ValueGeneratedNever();
        builder.Property("MasterCode").IsRequired().HasMaxLength(100);
        builder.Property("FullName").IsRequired().HasMaxLength(200);
        builder.Property("Description").HasMaxLength(500);
        builder.HasIndex("MasterCode").IsUnique();
    }
}
