using System.Collections.ObjectModel;
using System.Text.Json;
using Explore.Domain.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class AuthenticationCredentialCutoverConfiguration
    : IEntityTypeConfiguration<AuthenticationCredentialCutover>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<AuthenticationCredentialCutover> builder)
    {
        builder.ToTable("authentication_credential_cutover_receipts", table =>
        {
            table.HasCheckConstraint("CK_AuthenticationCutover_State",
                $"state IN ({string.Join(",", Enum.GetNames<AuthenticationCredentialCutover.CutoverState>().Select(value => $"'{value}'"))})");
            table.HasCheckConstraint("CK_AuthenticationCutover_Authority",
                $"authority IN ({string.Join(",", Enum.GetNames<AuthenticationCredentialCutover.CredentialAuthority>().Select(value => $"'{value}'"))})");
            table.HasCheckConstraint("CK_AuthenticationCutover_Counts",
                "(cleaned_setting_count IS NULL OR cleaned_setting_count >= 0) AND "
                + "(cleaned_binding_count IS NULL OR cleaned_binding_count >= 0)");
        });
        builder.HasKey(operation => operation.Id);
        builder.HasAlternateKey(operation => new { operation.InstanceId, operation.Id });
        builder.Property(operation => operation.Id).ValueGeneratedNever();
        builder.Property(operation => operation.ConcurrencyStamp).IsConcurrencyToken().ValueGeneratedNever();
        builder.Property(operation => operation.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(operation => operation.Authority).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Ignore(operation => operation.RequiredActivationCount);
        builder.Ignore(operation => operation.ActivationCount);

        var replacements = builder.Property(operation => operation.RequiredReplacements)
            .HasConversion(value => JsonSerializer.Serialize(value.ToArray(), JsonOptions),
                value => DeserializeArray<AuthenticationCredentialCutover.Replacement>(value))
            .IsRequired();
        replacements.Metadata.SetValueComparer(
            new ValueComparer<ReadOnlyCollection<AuthenticationCredentialCutover.Replacement>>(
                (left, right) => left != null && right != null && left.SequenceEqual(right),
                value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                value => Array.AsReadOnly(value.ToArray())));

        var activations = builder.Property(operation => operation.Activations)
            .HasConversion(value => JsonSerializer.Serialize(value.ToArray(), JsonOptions),
                value => DeserializeArray<AuthenticationCredentialCutover.ActivationReport>(value))
            .IsRequired();
        activations.Metadata.SetValueComparer(
            new ValueComparer<ReadOnlyCollection<AuthenticationCredentialCutover.ActivationReport>>(
                (left, right) => left != null && right != null && left.SequenceEqual(right),
                value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                value => Array.AsReadOnly(value.ToArray())));

        builder.Property(operation => operation.Fence)
            .HasConversion(value => JsonSerializer.Serialize(value, JsonOptions),
                value => JsonSerializer.Deserialize<AuthenticationCredentialCutover.FenceAttestation>(value, JsonOptions));
    }

    private static ReadOnlyCollection<T> DeserializeArray<T>(string value) =>
        Array.AsReadOnly(JsonSerializer.Deserialize<T[]>(value, JsonOptions)
            ?? throw new InvalidOperationException("authentication_cutover_invalid_evidence"));
}
