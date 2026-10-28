using Explore.Domain.Learning;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>
/// Fixed storage rights for C# learning operations. Runtime retains tenant RLS;
/// the existing protected erasure purpose can only select/delete learner roots.
/// </summary>
public static class PostgresLearningPermissions
{
    private const string ErasurePolicy = "learning_subject_erasure";

    internal static string GlobalPolicyException(ExploreDbContext context, string login)
    {
        var entity = context.Model.FindEntityType(typeof(UserLearningState))
            ?? throw new InvalidOperationException("The learning model is incomplete.");
        string expression = $"pg_has_role(CURRENT_USER, '{PostgresSubjectPrivacyErasureContract.GlobalRole}'::name, 'MEMBER'::text)";
        return $"""
            (session_user={Text(login)} AND current_user=session_user
                AND n.nspname={Text(entity.GetSchema() ?? RelationalModelNamespace.DefaultSchema)}
                AND c.relname={Text(entity.GetTableName()!)} AND p.polname={Text(ErasurePolicy)}
                AND p.polcmd='*' AND p.polpermissive
                AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})]
                AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)={Text(expression)}
                AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)='false')
            """;
    }

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var names = context.GetService<ISqlGenerationHelper>();
        string runtime = names.DelimitIdentifier(enrollment.RuntimeLogin);
        string global = names.DelimitIdentifier(enrollment.GlobalLogin);
        string erasure = names.DelimitIdentifier(PostgresSubjectPrivacyErasureContract.GlobalRole);
        string Table(Type type)
        {
            var entity = context.Model.FindEntityType(type)
                ?? throw new InvalidOperationException("The learning model is incomplete.");
            return names.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        }
        string[] tables = new[] { typeof(TenantLearningSpace), typeof(UserLearningState),
            typeof(LearningTourProgress), typeof(LearningPracticeState) }.Select(Table).ToArray();
        string learner = Table(typeof(UserLearningState));
        string policy = names.DelimitIdentifier(ErasurePolicy);
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using (var identity = context.Database.GetDbConnection().CreateCommand())
        {
            identity.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
            identity.CommandText = "SELECT session_user = @migrator AND current_user = session_user";
            var parameter = identity.CreateParameter();
            parameter.ParameterName = "migrator";
            parameter.Value = enrollment.MigratorLogin;
            identity.Parameters.Add(parameter);
            if (await identity.ExecuteScalarAsync(cancellationToken) is not true)
                throw new UnauthorizedAccessException("Learning permissions require the declared migrator.");
        }
        await context.Database.ExecuteSqlRawAsync($"""
            GRANT SELECT, INSERT, UPDATE, DELETE ON {string.Join(", ", tables)} TO {runtime};
            GRANT SELECT, DELETE ON {learner} TO {erasure}, {global};
            DROP POLICY IF EXISTS {policy} ON {learner};
            CREATE POLICY {policy} ON {learner} TO {global}
                USING (pg_catalog.pg_has_role(current_user, '{PostgresSubjectPrivacyErasureContract.GlobalRole}'::name, 'MEMBER'::text))
                WITH CHECK (false);
            """, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
