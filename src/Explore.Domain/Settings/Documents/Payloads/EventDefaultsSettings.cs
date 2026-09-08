namespace Explore.Domain.Settings.Documents.Payloads;

public sealed record EventDefaultsSettings
{
    public bool RequireApproval { get; init; } = true;

    public bool UserSubmissionEnabled { get; init; }
}
