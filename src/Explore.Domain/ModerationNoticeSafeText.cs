namespace Explore.Domain;

/// <summary>
/// Plain text explicitly approved for the subject view, never a conversion from evidence custody.
/// This type validates representation only; the committing source owner must prove disclosure approval.
/// </summary>
public sealed record ModerationNoticeSafeText
{
    public ModerationNoticeSafeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4000
            || value.Any(character => character is '<' or '>'
                || char.IsControl(character) && character is not ('\r' or '\n' or '\t'))
            || !value.Any(char.IsLetterOrDigit))
            throw new ArgumentException("A notice requires substantive, bounded approved plain text.", nameof(value));
        Value = value.Trim();
    }

    public string Value { get; }
    public override string ToString() => nameof(ModerationNoticeSafeText);
}
