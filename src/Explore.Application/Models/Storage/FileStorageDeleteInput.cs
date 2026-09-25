namespace Explore.Application.Models.Storage;

public sealed record FileStorageDeleteInput(string ObjectKey, string? ProviderVersionId = null);
