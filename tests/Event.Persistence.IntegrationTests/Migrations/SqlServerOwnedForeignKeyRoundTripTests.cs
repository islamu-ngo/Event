using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Event.Persistence.IntegrationTests.Migrations;

public sealed class SqlServerOwnedForeignKeyRoundTripTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GeneratedModel_PreservesSqlServerOwnershipDeletePolicies(bool snapshot)
    {
        await using ExploreDbContext context = new ExploreDbContextFactory().CreateDbContext(
        [
            "--DOTNET_ENVIRONMENT=Development",
            "--DesignTime:ModelOnly=true",
            "--Database:Provider=SqlServer"
        ]);
        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();
        IModel generated;
        if (snapshot)
        {
            generated = migrations.ModelSnapshot?.Model
                ?? throw new InvalidOperationException("ExploreDbContext migration snapshot was not found.");
        }
        else
        {
            var latest = migrations.Migrations.OrderBy(entry => entry.Key, StringComparer.Ordinal).Last();
            generated = migrations.CreateMigration(latest.Value, context.Database.ProviderName!).TargetModel;
        }

        generated = context.GetService<IModelRuntimeInitializer>()
            .Initialize(generated, designTime: true, validationLogger: null);
        IModel current = context.GetService<IDesignTimeModel>().Model;
        IForeignKey evidence = current.GetEntityTypes()
            .Single(entity => entity.GetTableName() == "moderation_case_evidence")
            .GetForeignKeys().Single(foreignKey => foreignKey.IsOwnership);
        IForeignKey receipt = current.FindEntityType(typeof(UserBirthDateCollectionReceipt))!
            .GetForeignKeys().Single(foreignKey =>
                foreignKey.PrincipalEntityType.Name == typeof(UserPii).FullName
                && foreignKey.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(UserBirthDateCollectionReceipt.UserId)]));
        string[] policyConstraints = [evidence.GetConstraintName()!, receipt.GetConstraintName()!];
        MigrationOperation[] policyDifferences = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(generated.GetRelationalModel(), current.GetRelationalModel())
            .Where(operation => operation switch
            {
                AddForeignKeyOperation added => policyConstraints.Contains(added.Name, StringComparer.Ordinal),
                DropForeignKeyOperation dropped => policyConstraints.Contains(dropped.Name, StringComparer.Ordinal),
                _ => false
            })
            .ToArray();

        await Assert.That(evidence.DeleteBehavior).IsEqualTo(DeleteBehavior.NoAction);
        await Assert.That(receipt.DeleteBehavior).IsEqualTo(DeleteBehavior.Cascade);
        await Assert.That(policyDifferences).IsEmpty();
        await Assert.That(FindGeneratedForeignKey(generated, evidence).DeleteBehavior)
            .IsEqualTo(DeleteBehavior.NoAction);
        await Assert.That(FindGeneratedForeignKey(generated, receipt).DeleteBehavior)
            .IsEqualTo(DeleteBehavior.Cascade);
    }

    private static IForeignKey FindGeneratedForeignKey(IModel generated, IForeignKey current)
    {
        StoreObjectIdentifier dependentTable = StoreObjectIdentifier.Table(
            current.DeclaringEntityType.GetTableName()!, current.DeclaringEntityType.GetSchema());
        StoreObjectIdentifier principalTable = StoreObjectIdentifier.Table(
            current.PrincipalEntityType.GetTableName()!, current.PrincipalEntityType.GetSchema());

        return generated.GetEntityTypes()
            .Where(entity => entity.GetTableName() == dependentTable.Name
                && entity.GetSchema() == dependentTable.Schema)
            .SelectMany(entity => entity.GetForeignKeys())
            .Single(foreignKey =>
                foreignKey.GetConstraintName() == current.GetConstraintName()
                && foreignKey.IsOwnership == current.IsOwnership
                && foreignKey.PrincipalEntityType.GetTableName() == principalTable.Name
                && foreignKey.PrincipalEntityType.GetSchema() == principalTable.Schema
                && foreignKey.Properties.Select(property => property.GetColumnName(dependentTable))
                    .SequenceEqual(current.Properties.Select(property => property.GetColumnName(dependentTable)))
                && foreignKey.PrincipalKey.Properties.Select(property => property.GetColumnName(principalTable))
                    .SequenceEqual(current.PrincipalKey.Properties.Select(property => property.GetColumnName(principalTable))));
    }
}
