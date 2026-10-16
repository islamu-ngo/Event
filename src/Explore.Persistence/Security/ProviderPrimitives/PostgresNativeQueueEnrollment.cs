using System.Text;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Security;

/// <summary>
/// Deployment-declared login identities for one PostgreSQL cluster's native queue contracts.
/// Membership options and privileges belong to each source-defined contract, never live ACL discovery.
/// </summary>
public sealed record PostgresNativeQueueEnrollment(
    string MigratorLogin,
    string GlobalLogin,
    string RuntimeLogin)
{
    public static PostgresNativeQueueEnrollment Bind(IConfiguration configuration)
    {
        var enrollment = new PostgresNativeQueueEnrollment(
            PrimaryDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.Migrator),
            PrimaryDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.Global),
            PrimaryDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.Runtime));
        enrollment.Validate();
        return enrollment;
    }

    public void Validate()
    {
        string[] logins = [MigratorLogin, GlobalLogin, RuntimeLogin];
        if (logins.Any(login => string.IsNullOrWhiteSpace(login) ||
                login != login.Trim() || login.Any(char.IsControl) || Encoding.UTF8.GetByteCount(login) > 63) ||
            logins.Distinct(StringComparer.Ordinal).Count() != logins.Length)
            throw new InvalidOperationException("native_queue_login_enrollment_invalid");
    }
}
