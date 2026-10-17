using System.Text.Json;

namespace Explore.Infrastructure.Http;

/// <summary>Reads at most the byte ceiling plus one overflow byte before JSON materialization.</summary>
public static class BoundedResponseReader
{
    public static async Task<byte[]> ReadJsonAsync(
        HttpContent content,
        int maximumBytes,
        int maximumDepth = 32,
        int maximumCollectionItems = 256,
        int maximumScalarBytes = 256 * 1024,
        CancellationToken cancellationToken = default,
        bool allowEmptyDocument = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCollectionItems);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumScalarBytes);
        cancellationToken.ThrowIfCancellationRequested();

        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new ProviderResponseRejectedException("response_too_large");
        }

        // Compressed input is not admitted without independently bounded encoded and decoded streams.
        if (content.Headers.ContentEncoding.Any(encoding =>
                !string.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ProviderResponseRejectedException("response_encoding_denied");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        byte[] bytes = new byte[checked(maximumBytes + 1)];
        int length = 0;
        while (length < bytes.Length)
        {
            int count = await stream.ReadAsync(bytes.AsMemory(length), cancellationToken);
            if (count == 0)
            {
                break;
            }

            length += count;
        }

        if (length > maximumBytes)
        {
            throw new ProviderResponseRejectedException("response_too_large");
        }

        if (allowEmptyDocument && IsJsonWhitespace(bytes.AsSpan(0, length)))
        {
            return [];
        }
        ValidateJson(bytes.AsSpan(0, length), maximumDepth, maximumCollectionItems, maximumScalarBytes);
        return bytes.AsSpan(0, length).ToArray();
    }

    private static bool IsJsonWhitespace(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            if (value is not (0x20 or 0x09 or 0x0A or 0x0D))
            {
                return false;
            }
        }
        return true;
    }

    private static void ValidateJson(ReadOnlySpan<byte> bytes, int maximumDepth, int maximumItems, int maximumScalar)
    {
        // Inspect every token, including unknown fields, before an adapter or SDK builds its object graph.
        // Enforce depth at each opening token so classification never depends on JsonException text or cursor state.
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = int.MaxValue });
        var arrays = new Stack<(int Depth, int Items)>();
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject
                    && reader.CurrentDepth >= maximumDepth)
                    throw new ProviderResponseRejectedException("response_depth_limit");
                bool startsValue = reader.TokenType is not (JsonTokenType.EndArray or JsonTokenType.EndObject or JsonTokenType.PropertyName);
                if (startsValue && arrays.TryPeek(out var array) && reader.CurrentDepth == array.Depth + 1)
                {
                    arrays.Pop();
                    if (++array.Items > maximumItems)
                    {
                        throw new ProviderResponseRejectedException("response_item_limit");
                    }
                    arrays.Push(array);
                }

                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    arrays.Push((reader.CurrentDepth, 0));
                }
                else if (reader.TokenType == JsonTokenType.EndArray)
                {
                    arrays.Pop();
                }
                else if (reader.HasValueSequence ? reader.ValueSequence.Length > maximumScalar : reader.ValueSpan.Length > maximumScalar)
                {
                    throw new ProviderResponseRejectedException("response_scalar_limit");
                }
            }

            if (reader.BytesConsumed == 0)
            {
                throw new ProviderResponseRejectedException("response_invalid_json");
            }
        }
        catch (JsonException)
        {
            throw new ProviderResponseRejectedException("response_invalid_json");
        }
    }
}

public sealed class ProviderResponseRejectedException(string category)
    : Exception("The provider response exceeded the permitted response contract.")
{
    public string Category { get; } = category;
}
