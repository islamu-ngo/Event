namespace Explore.Application.Models.Storage;

public sealed record FileStorageReadInput(
    string ObjectKey,
    string? ContentType);
