using System.Text;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Security;

/// <summary>Deployment identities, not passwords or catalogue-discovered authority.</summary>
public sealed record PlatformApiKeySourceEnrollment(
    PostgresNativeQueueEnrollment Original, string ReadLogin, string WriteLogin, string ReportLogin)
{
    public static PlatformApiKeySourceEnrollment Bind(IConfiguration configuration)
    {
        var result = new PlatformApiKeySourceEnrollment(PostgresNativeQueueEnrollment.Bind(configuration),
            PrimaryDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.InstanceKeyRead),
            PrimaryDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.InstanceKeyWrite),
            PrimaryDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.PlatformKeyReport));
        result.Validate();
        return result;
    }

    public void Validate()
    {
        Original.Validate();
        string[] logins = [Original.MigratorLogin, Original.GlobalLogin, Original.RuntimeLogin,
            Original.EmailInstanceMutationLogin, Original.EmailTenantMutationLogin, ReadLogin, WriteLogin, ReportLogin];
        if (logins.Any(login => string.IsNullOrWhiteSpace(login) || login != login.Trim()
                || login.Any(char.IsControl) || Encoding.UTF8.GetByteCount(login) > 63)
            || logins.Distinct(StringComparer.Ordinal).Count() != logins.Length)
            throw new InvalidOperationException("platform_key_source_enrollment_invalid");
    }
}
