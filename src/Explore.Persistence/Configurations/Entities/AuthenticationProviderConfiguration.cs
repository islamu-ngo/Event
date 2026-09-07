using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class AuthenticationProviderConfiguration : IEntityTypeConfiguration<AuthenticationProvider>
{
    public void Configure(EntityTypeBuilder<AuthenticationProvider> builder)
    {
        builder.Property(provider => provider.Id).ValueGeneratedNever();
        builder.Property(provider => provider.MasterCode).HasMaxLength(100).IsRequired();
        builder.Property(provider => provider.FullName).HasMaxLength(200).IsRequired();
        builder.Property(provider => provider.Description).HasMaxLength(500);
        builder.HasIndex(provider => provider.MasterCode).IsUnique();
    }
}
