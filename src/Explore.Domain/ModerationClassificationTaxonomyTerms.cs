namespace Explore.Domain;

/// <summary>
/// Substantive obligations recorded by the adopted coordinator and accepted by a separately
/// nominated qualified reviewer. Native governance must authenticate both canonical controllers,
/// roster competence and current availability; these statements alone confer no authority.
/// </summary>
public sealed record ModerationClassificationTaxonomyMandate(
    Guid OwnerHumanId, Guid OwnerControllerHumanId, Guid ReviewerHumanId, Guid ReviewerControllerHumanId,
    string OwnershipGrounds, string ReviewMandateGrounds)
{
    public void Validate()
    {
        EventContentClassificationVersion.RequireIndependentReview(
            OwnerHumanId, OwnerControllerHumanId, ReviewerHumanId, ReviewerControllerHumanId);
        if (!ModerationChildAgePolicyTerms.Grounds(OwnershipGrounds)
            || !ModerationChildAgePolicyTerms.Grounds(ReviewMandateGrounds))
            throw new ArgumentException("Taxonomy requires substantive ownership and independent classification review obligations.");
    }
}

public sealed record ModerationClassificationTaxonomyCategoryTerms(
    Guid VersionId, int CategoryId, string Code, EventContentClassificationVersion.CategoryKind Kind,
    int MinimumAge, string Meaning, string ReviewRationale)
{
    public void Validate(int adoptedDebateMinimumAge)
    {
        ModerationHostBinding.RequireUuidV7(VersionId, nameof(VersionId));
        if (CategoryId < 1 || !Enum.IsDefined(Kind) || MinimumAge is < 0 or > 130
            || string.IsNullOrEmpty(Code) || Code.Length > 100 || !char.IsAsciiLetterLower(Code[0])
            || Code.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character)
                && character is not '-' and not '.')
            || !ModerationChildAgePolicyTerms.Grounds(Meaning)
            || !ModerationChildAgePolicyTerms.Grounds(ReviewRationale)
            || adoptedDebateMinimumAge is < 1 or > 130
            || Kind == EventContentClassificationVersion.CategoryKind.Debate && MinimumAge < adoptedDebateMinimumAge)
            throw new ArgumentException("Category requires an int lookup, stable code, explicit meaning and reviewed adopted floor.");
    }
}

public sealed class ModerationClassificationTaxonomyCategory
{
    private ModerationClassificationTaxonomyCategory() { }
    public Guid Id { get; private set; }
    public Guid SourceId { get; private set; }
    public int Ordinal { get; private set; }
    public int CategoryId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public EventContentClassificationVersion.CategoryKind Kind { get; private set; }
    public int MinimumAge { get; private set; }
    public string Meaning { get; private set; } = string.Empty;
    public string ReviewRationale { get; private set; } = string.Empty;
    public ModerationClassificationTaxonomyCategoryTerms Terms =>
        new(Id, CategoryId, Code, Kind, MinimumAge, Meaning, ReviewRationale);

    internal static ModerationClassificationTaxonomyCategory Capture(Guid sourceId, int ordinal,
        ModerationClassificationTaxonomyCategoryTerms terms) => new()
        {
            Id = terms.VersionId,
            SourceId = sourceId,
            Ordinal = ordinal,
            CategoryId = terms.CategoryId,
            Code = terms.Code,
            Kind = terms.Kind,
            MinimumAge = terms.MinimumAge,
            Meaning = terms.Meaning,
            ReviewRationale = terms.ReviewRationale
        };
}
