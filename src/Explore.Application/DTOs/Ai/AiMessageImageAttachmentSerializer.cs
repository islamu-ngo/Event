using System.Text.Json;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Services;

namespace Explore.Application.DTOs.Ai;

internal static class AiMessageImageAttachmentSerializer
{
    public const int MaxImageCount = 4;
    public const int MaxImageBytes = 5 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool TrySerializeValidated(
        IEnumerable<AiMessageImageInputDto>? images,
        out string? imageAttachmentsJson,
        out string? error)
    {
        imageAttachmentsJson = null;
        error = null;
        var attachments = new List<StoredAiMessageImageDto>();

        foreach (AiMessageImageInputDto image in images ?? [])
        {
            string mediaType = image.MediaType.Trim();
            if (!SafeRasterContentPolicy.IsBrowserImageMimeType(mediaType))
            {
                error = "AI message images must use JPEG, PNG, GIF, or WebP.";
                return false;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(image.Data.Trim());
            }
            catch (FormatException)
            {
                error = "AI message image data must be valid base64.";
                return false;
            }

            if (bytes.Length == 0
                || bytes.Length > MaxImageBytes
                || image.SizeBytes is { } declaredSize && declaredSize != bytes.LongLength
                || !SafeRasterContentPolicy.MatchesContainer(bytes, mediaType))
            {
                error = "AI message image bytes do not match the declared image metadata.";
                return false;
            }

            string? fileName = string.IsNullOrWhiteSpace(image.FileName) ? null : image.FileName.Trim();
            if (fileName is not null
                && !SafeRasterContentPolicy.MatchesExtension(mediaType, Path.GetExtension(fileName)))
            {
                error = "AI message image filename extension does not match its media type.";
                return false;
            }

            attachments.Add(new StoredAiMessageImageDto
            {
                MediaType = mediaType.ToLowerInvariant(),
                Data = image.Data.Trim(),
                FileName = fileName,
                SizeBytes = bytes.LongLength
            });
        }

        imageAttachmentsJson = attachments.Count == 0
            ? null
            : JsonSerializer.Serialize(attachments, JsonOptions);
        return true;
    }

    public static IReadOnlyList<AiChatImage> DeserializeForProvider(string? imageAttachmentsJson) =>
        Deserialize(imageAttachmentsJson)
            .Where(image => !string.IsNullOrWhiteSpace(image.MediaType) && !string.IsNullOrWhiteSpace(image.Data))
            .Select(image => new AiChatImage(image.MediaType.Trim(), image.Data.Trim()))
            .ToList();

    public static IReadOnlyList<AiMessageImageDto> DeserializeMetadata(string? imageAttachmentsJson) =>
        Deserialize(imageAttachmentsJson)
            .Where(image => !string.IsNullOrWhiteSpace(image.MediaType))
            .Select(image => new AiMessageImageDto
            {
                MediaType = image.MediaType.Trim(),
                FileName = string.IsNullOrWhiteSpace(image.FileName) ? null : image.FileName.Trim(),
                SizeBytes = image.SizeBytes
            })
            .ToList();

    public static IReadOnlyList<StoredAiMessageImageAttachmentDto> DeserializeForStorage(string? imageAttachmentsJson) =>
        Deserialize(imageAttachmentsJson)
            .Where(image => !string.IsNullOrWhiteSpace(image.MediaType) && !string.IsNullOrWhiteSpace(image.Data))
            .Select(image => new StoredAiMessageImageAttachmentDto(
                image.MediaType.Trim(),
                image.Data.Trim(),
                string.IsNullOrWhiteSpace(image.FileName) ? null : image.FileName.Trim(),
                image.SizeBytes))
            .ToList();

    private static IReadOnlyList<StoredAiMessageImageDto> Deserialize(string? imageAttachmentsJson)
    {
        if (string.IsNullOrWhiteSpace(imageAttachmentsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<StoredAiMessageImageDto>>(imageAttachmentsJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record StoredAiMessageImageDto
    {
        public string MediaType { get; init; } = string.Empty;
        public string Data { get; init; } = string.Empty;
        public string? FileName { get; init; }
        public long? SizeBytes { get; init; }
    }
}

internal sealed record StoredAiMessageImageAttachmentDto(
    string MediaType,
    string Data,
    string? FileName,
    long? SizeBytes);
