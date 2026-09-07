namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Explore.Domain.ValueObjects;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class LocationPiiConfiguration : IEntityTypeConfiguration<LocationPii>
{
    public void Configure(EntityTypeBuilder<LocationPii> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_location_pii_coordinate_shape",
            """
            (latitude IS NULL AND longitude IS NULL)
            OR (latitude IS NOT NULL AND longitude IS NOT NULL
                AND latitude BETWEEN -90 AND 90
                AND longitude BETWEEN -180 AND 180)
            """));

        builder.HasKey(e => e.LocationId);

        builder.Property(e => e.Address)
            .HasMaxLength(LocationTextNormalization.MaximumSourceLength)
            .IsRequired();

        builder.Property(e => e.Postcode)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.AddressSubstringKey)
            .HasMaxLength(LocationTextNormalization.MaximumKeyLength)
            .IsUnicode()
            .IsRequired()
            .UseLocationUnicodeCollation();

        builder.Property(e => e.AddressSubstringKeyVersion)
            .IsRequired();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_location_pii_address_substring_key_version",
            $"address_substring_key_version = {LocationTextNormalization.CurrentRevision} AND address_substring_key <> ''"));
    }
}
