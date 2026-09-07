using Explore.Application.Features.EventReporting.Models;

namespace Explore.Infrastructure.Configuration;

public sealed class ModerationProviderOptions
{
    public const string SectionName = "Reporting";
    public const string ModeDisabled = "Disabled";
    public const string ModeLocalOnly = "LocalOnly";
    public const string ModeOsprey = "Osprey";
    public const string ModeCoop = "Coop";
    public const string ModeComposite = "Composite";

    public bool Enabled { get; set; } = true;
    public string Mode { get; set; } = ModeLocalOnly;
    public bool SyncReports { get; set; } = true;
    public bool EvaluateSignals { get; set; }
    public bool MirrorReviewQueue { get; set; }
    public bool ExecuteDecisions { get; set; } = true;
    public EventReportProviderEvidenceMode EvidenceMode { get; set; } = EventReportProviderEvidenceMode.MetadataOnly;

    public bool IsDisabled => !Enabled || IsMode(ModeDisabled);
    public bool IsLocalOnly => IsMode(ModeLocalOnly);
    public bool UsesOsprey => IsMode(ModeOsprey) || IsMode(ModeComposite);
    public bool UsesCoop => IsMode(ModeCoop) || IsMode(ModeComposite);

    public bool ShouldEvaluateSignals => !IsDisabled && EvaluateSignals && UsesOsprey;
    public bool ShouldMirrorReviewQueue => !IsDisabled && MirrorReviewQueue && UsesCoop;

    public bool IsMode(string mode) =>
        string.Equals(Mode?.Trim(), mode, StringComparison.OrdinalIgnoreCase);
}
