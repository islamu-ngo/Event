namespace Explore.Application.DTOs.ContactShareConsent;

public sealed record SharedContactExportResultDto
{
    private ReadOnlyMemory<byte>? _fileContent = ReadOnlyMemory<byte>.Empty;

    public Guid ExportId { get; init; }
    public int RowCount { get; init; }
    public string Format { get; init; } = string.Empty;
    public ReadOnlyMemory<byte>? FileContent
    {
        get => _fileContent is { } content ? new ReadOnlyMemory<byte>(content.ToArray()) : null;
        init => _fileContent = value is { } content ? new ReadOnlyMemory<byte>(content.ToArray()) : null;
    }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
}
