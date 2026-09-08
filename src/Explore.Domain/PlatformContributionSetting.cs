using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class PlatformContributionSetting : IAuditableEntity
{
    private readonly List<PlatformContributionOption> _options = [];

    private PlatformContributionSetting()
    {
    }

    private PlatformContributionSetting(int versionNumber, bool isEnabled, string heading, string body, IEnumerable<PlatformContributionOption> options)
    {
        Id = Guid.CreateVersion7();
        VersionNumber = versionNumber;
        IsActive = true;
        IsEnabled = isEnabled;
        Heading = heading;
        Body = body;
        _options.AddRange(options.Select(static option => option.Clone()));
    }

    public Guid Id { get; private set; }

    public int VersionNumber { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsEnabled { get; private set; }

    public string Heading { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public IReadOnlyCollection<PlatformContributionOption> Options => _options.AsReadOnly();

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static PlatformContributionSetting CreateInitial(
        bool isEnabled,
        string heading,
        string body,
        IEnumerable<PlatformContributionOption> options)
    {
        PlatformContributionOption[] materializedOptions = options.ToArray();
        (string normalizedHeading, string normalizedBody) = NormalizeAndValidate(isEnabled, heading, body, materializedOptions);
        return new PlatformContributionSetting(1, isEnabled, normalizedHeading, normalizedBody, materializedOptions);
    }

    public PlatformContributionSetting CreateRevision(
        bool isEnabled,
        string heading,
        string body,
        IEnumerable<PlatformContributionOption> options)
    {
        EnsureActive();
        PlatformContributionOption[] materializedOptions = options.ToArray();
        (string normalizedHeading, string normalizedBody) = NormalizeAndValidate(isEnabled, heading, body, materializedOptions);
        IsActive = false;
        return new PlatformContributionSetting(checked(VersionNumber + 1), isEnabled, normalizedHeading, normalizedBody, materializedOptions);
    }

    public void Retire()
    {
        EnsureActive();
        IsActive = false;
    }

    private void EnsureActive()
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Only the active platform contribution version can be revised or retired.");
        }
    }

    private static (string Heading, string Body) NormalizeAndValidate(bool isEnabled, string heading, string body, IEnumerable<PlatformContributionOption> options)
    {
        ArgumentNullException.ThrowIfNull(heading);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(options);

        PlatformContributionOption[] materializedOptions = options.ToArray();
        if (materializedOptions.Length == 0 || materializedOptions.Count(static option => option.IsDefault) != 1)
        {
            throw new ArgumentException("Contribution options must contain exactly one default.", nameof(options));
        }

        if (materializedOptions.Any(static option => option.ContributionBasisPoints == 0 && !option.IsDefault) || materializedOptions.Single(static option => option.IsDefault).ContributionBasisPoints != 0)
        {
            throw new ArgumentException("The zero-percent contribution option must be the default.", nameof(options));
        }

        if (materializedOptions.Select(static option => option.ContributionBasisPoints).Distinct().Count() != materializedOptions.Length ||
            materializedOptions.Select(static option => option.SortOrder).Distinct().Count() != materializedOptions.Length)
        {
            throw new ArgumentException("Contribution percentages and sort orders must be unique.", nameof(options));
        }

        string normalizedHeading = heading.Trim();
        string normalizedBody = body.Trim();
        if (isEnabled && (string.IsNullOrWhiteSpace(normalizedHeading) || string.IsNullOrWhiteSpace(normalizedBody)))
        {
            throw new ArgumentException("Enabled contribution settings require stored heading and body text.");
        }

        return (normalizedHeading, normalizedBody);
    }
}
