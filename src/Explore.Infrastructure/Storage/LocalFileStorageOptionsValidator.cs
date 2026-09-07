using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Storage;

public sealed class LocalFileStorageOptionsValidator : IValidateOptions<LocalFileStorageOptions>
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
                _ = Path.GetFullPath(options.RootPath);
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
