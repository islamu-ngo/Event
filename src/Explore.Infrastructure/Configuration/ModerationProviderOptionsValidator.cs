using Explore.Application.Features.EventReporting.Models;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Configuration;

public sealed class ModerationProviderOptionsValidator : IValidateOptions<ModerationProviderOptions>
{
    private static readonly HashSet<string> SupportedModes = new(StringComparer.OrdinalIgnoreCase)
    {
        ModerationProviderOptions.ModeDisabled,
        ModerationProviderOptions.ModeLocalOnly,
        ModerationProviderOptions.ModeOsprey,
        ModerationProviderOptions.ModeCoop,
        ModerationProviderOptions.ModeComposite
    };

    public ValidateOptionsResult Validate(string? name, ModerationProviderOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.Mode) || !SupportedModes.Contains(options.Mode.Trim()))
        {
            failures.Add("Reporting:Mode must be Disabled, LocalOnly, Osprey, Coop, or Composite.");
        }

        if (!Enum.IsDefined(options.EvidenceMode))
        {
            failures.Add("Reporting:EvidenceMode must be a valid event-report provider evidence mode.");
        }

        if ((options.IsDisabled || options.IsLocalOnly)
            && options.EvidenceMode == EventReportProviderEvidenceMode.ReporterText)
        {
            failures.Add("Reporting:EvidenceMode cannot be ReporterText when Reporting:Mode is Disabled or LocalOnly.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
