namespace ISLAMU.Moderation.Domain;

/// <summary>Identifies immutable rule meaning within its owning authority.</summary>
public sealed record ModerationRuleReference
{
    private ModerationRuleReference(Guid ownerId, string code, long revision)
    {
        OwnerId = ownerId;
        Code = code;
        Revision = revision;
    }

    public Guid OwnerId { get; }
    public string Code { get; }
    public long Revision { get; }

    public static ModerationRuleReference Create(Guid ownerId, string code, long revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (ownerId == Guid.Empty || revision < 1)
        {
            throw new ArgumentException("A qualified owner and positive rule revision are required.", nameof(ownerId));
        }

        if (code.Length > 100 || !code.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            throw new ArgumentException("A rule requires a bounded stable code.", nameof(code));
        }

        return new ModerationRuleReference(ownerId, code, revision);
    }

    public override string ToString() => "ModerationRuleReference";
}
