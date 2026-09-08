using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CarpaNet.Cbor;
using CarpaNet.Json;

namespace Explore.Infrastructure.Services.Federation;

public sealed record AtprotoEncodedSizeValidationResult(
    int JsonBytes,
    int DagCborBytes,
    ImmutableArray<string> Errors)
{
    public bool IsValid => Errors.IsEmpty;
}

public static class AtprotoRecordSizeValidator
{
    public const int MaximumJsonBytes = 2_097_152;
    public const int MaximumDagCborBytes = 1_048_576;

    public static AtprotoEncodedSizeValidationResult Validate<T>(T record)
    {
        ArgumentNullException.ThrowIfNull(record);
        JsonElement json = JsonSerializer.SerializeToElement(
            record,
            ATProtoJsonContext.DefaultOptions.GetTypeInfo(typeof(T))!);
        int jsonBytes = Encoding.UTF8.GetByteCount(json.GetRawText());
        int dagCborBytes = ATProtoCborContext.Default.Serialize(record).Length;
        return ValidateEncodedLengths(jsonBytes, dagCborBytes);
    }

    public static AtprotoEncodedSizeValidationResult ValidateEncodedLengths(
        int jsonBytes,
        int dagCborBytes)
    {
        if (jsonBytes < 0 || dagCborBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(jsonBytes), "Encoded byte lengths cannot be negative.");
        }

        var errors = ImmutableArray.CreateBuilder<string>();
        if (jsonBytes > MaximumJsonBytes)
        {
            errors.Add($"The complete JSON record is {jsonBytes} bytes; the limit is {MaximumJsonBytes} bytes.");
        }

        if (dagCborBytes > MaximumDagCborBytes)
        {
            errors.Add($"The complete DAG-CBOR record is {dagCborBytes} bytes; the limit is {MaximumDagCborBytes} bytes.");
        }

        return new(jsonBytes, dagCborBytes, errors.ToImmutable());
    }
}
