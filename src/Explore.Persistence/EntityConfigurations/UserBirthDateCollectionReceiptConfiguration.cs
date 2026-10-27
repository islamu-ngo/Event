using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.EntityConfigurations;

public sealed class UserBirthDateCollectionReceiptConfiguration : IEntityTypeConfiguration<UserBirthDateCollectionReceipt>
{
    public void Configure(EntityTypeBuilder<UserBirthDateCollectionReceipt> builder)
    {
        builder.ToTable("user_birth_date_collection_receipts");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.HasIndex(row => row.UserId).IsUnique();
        builder.HasOne<UserPii>().WithMany().HasForeignKey(row => row.UserId)
            .HasPrincipalKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
