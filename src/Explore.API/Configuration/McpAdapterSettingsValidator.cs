using Microsoft.Extensions.Options;

namespace Explore.API.Configuration;

public sealed class McpAdapterSettingsValidator : IValidateOptions<McpAdapterSettings>
{
    public ValidateOptionsResult Validate(string? name, McpAdapterSettings options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.EndpointPath))
        {
            failures.Add("Mcp:EndpointPath is required.");
        }
        else if (!options.EndpointPath.StartsWith('/'))
        {
            failures.Add("Mcp:EndpointPath must start with '/'.");
        }

        if (!options.Stateless)
        {
            failures.Add("Mcp:Stateless must remain true for the initial API-hosted adapter.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
