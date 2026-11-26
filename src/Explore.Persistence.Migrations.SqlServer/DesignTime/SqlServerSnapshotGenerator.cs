using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Design;

namespace Explore.Persistence.Migrations.SqlServer.DesignTime;

/// <summary>
/// Retains explicit non-cascading ownership metadata after the standard owned mapping is emitted.
/// </summary>
public sealed class SqlServerSnapshotGenerator(CSharpSnapshotGeneratorDependencies dependencies)
    : CSharpSnapshotGenerator(dependencies)
{
    protected override void GenerateOwnedType(
        string entityTypeBuilderName,
        IForeignKey ownership,
        IndentedStringBuilder stringBuilder)
    {
        base.GenerateOwnedType(entityTypeBuilderName, ownership, stringBuilder);

        if (ownership.DeleteBehavior == DeleteBehavior.NoAction
            && ownership.PrincipalToDependent is { } navigation)
        {
            stringBuilder.AppendLine()
                .Append(entityTypeBuilderName)
                .Append(ownership.PrincipalEntityType.IsOwned() ? ".OwnedEntityType" : ".Metadata")
                .Append(".FindNavigation(")
                .Append(Dependencies.CSharpHelper.Literal(navigation.Name))
                .AppendLine(")!.ForeignKey.DeleteBehavior = DeleteBehavior.NoAction;");
        }
    }
}
