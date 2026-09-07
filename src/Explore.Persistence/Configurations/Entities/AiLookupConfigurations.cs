using Explore.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class AiConversationStatusLookupConfiguration : IEntityTypeConfiguration<AiConversationStatusLookup>
{
    public void Configure(EntityTypeBuilder<AiConversationStatusLookup> builder)
    {
        ConfigureLookup(builder, "ai_conversation_statuses", "ux_ai_conversation_statuses_master_code");
    }

    private static void ConfigureLookup<TLookup>(EntityTypeBuilder<TLookup> builder, string tableName, string masterCodeIndexName)
        where TLookup : class
    {
        builder.ToTable(tableName);
        builder.Property<int>("Id").ValueGeneratedNever();
        builder.Property<string>("MasterCode").IsRequired().HasMaxLength(100);
        builder.Property<string>("FullName").IsRequired().HasMaxLength(200);
        builder.Property<string?>("Description").HasMaxLength(500);
        builder.HasIndex("MasterCode").IsUnique().HasDatabaseName(masterCodeIndexName);
    }
}

public sealed class AiMessageRoleLookupConfiguration : IEntityTypeConfiguration<AiMessageRoleLookup>
{
    public void Configure(EntityTypeBuilder<AiMessageRoleLookup> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique().HasDatabaseName("ux_ai_message_roles_master_code");
    }
}

public sealed class AiRunStatusLookupConfiguration : IEntityTypeConfiguration<AiRunStatusLookup>
{
    public void Configure(EntityTypeBuilder<AiRunStatusLookup> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique().HasDatabaseName("ux_ai_run_statuses_master_code");
    }
}

public sealed class AiReferenceKindLookupConfiguration : IEntityTypeConfiguration<AiReferenceKindLookup>
{
    public void Configure(EntityTypeBuilder<AiReferenceKindLookup> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique().HasDatabaseName("ux_ai_reference_kinds_master_code");
    }
}

public sealed class AiProposedActionKindLookupConfiguration : IEntityTypeConfiguration<AiProposedActionKindLookup>
{
    public void Configure(EntityTypeBuilder<AiProposedActionKindLookup> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique().HasDatabaseName("ux_ai_proposed_action_kinds_master_code");
    }
}

public sealed class AiProposedActionStatusLookupConfiguration : IEntityTypeConfiguration<AiProposedActionStatusLookup>
{
    public void Configure(EntityTypeBuilder<AiProposedActionStatusLookup> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique().HasDatabaseName("ux_ai_proposed_action_statuses_master_code");
    }
}

public sealed class AiProviderKindLookupConfiguration : IEntityTypeConfiguration<AiProviderKindLookup>
{
    public void Configure(EntityTypeBuilder<AiProviderKindLookup> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique().HasDatabaseName("ux_ai_provider_kinds_master_code");
    }
}
