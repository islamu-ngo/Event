using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;

namespace Explore.Infrastructure.Storage;

public sealed class LocalFileStorageOptionsValidator(IWebHostEnvironment? environment = null)
    : IValidateOptions<LocalFileStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, LocalFileStorageOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            failures.Add($"{nameof(LocalFileStorageOptions.RootPath)} is required.");
        }
        else
        {
            try
            {
                string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RootPath));
                if (string.Equals(root, Path.GetPathRoot(root),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    failures.Add($"{nameof(LocalFileStorageOptions.RootPath)} must be a dedicated storage directory.");
                if (environment is not null)
                {
                    string publicPath = !string.IsNullOrWhiteSpace(environment.WebRootPath)
                        ? environment.WebRootPath
                        : Path.Combine(environment.ContentRootPath, "wwwroot");
                    string publicRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(publicPath));
                    var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    if (string.Equals(root, publicRoot, comparison)
                        || root.StartsWith(publicRoot + Path.DirectorySeparatorChar, comparison)
                        || publicRoot.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                        failures.Add($"{nameof(LocalFileStorageOptions.RootPath)} must not overlap the host public asset root.");
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                failures.Add($"{nameof(LocalFileStorageOptions.RootPath)} is not a valid filesystem path.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
