namespace Explore.Diagnostic.AiReplay;

public enum AiReplayFailureClass
{
    None = 0,
    CatalogAuthorization = 1,
    ProposalValidation = 2,
    Recovery = 3,
    Redaction = 4,
    SideEffectSafety = 5
}
