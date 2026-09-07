namespace Explore.Domain;

public sealed class EventContactShareExportItem
{
    private EventContactShareExportItem()
    {
    }

    public Guid ExportId { get; private set; }
    public EventContactShareExport? Export { get; private set; }
    public Guid ConsentId { get; private set; }
    public EventContactShareConsent? Consent { get; private set; }
    public string ExportedFieldSnapshot { get; private set; } = string.Empty;

    public static EventContactShareExportItem Create(Guid exportId, Guid consentId, string exportedFieldSnapshot)
    {
        if (exportId == Guid.Empty || consentId == Guid.Empty || string.IsNullOrWhiteSpace(exportedFieldSnapshot))
        {
            throw new ArgumentException("Export item requires export, consent, and immutable field snapshot.");
        }

        return new EventContactShareExportItem
        {
            ExportId = exportId,
            ConsentId = consentId,
            ExportedFieldSnapshot = exportedFieldSnapshot.Trim()
        };
    }
}
