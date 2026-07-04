using System.Buffers;
using System.Text.Json;

namespace RemoteSupport.ManagedHost.Service;

/// <summary>Byte-identical mirror of src/server/RemoteSupport.Server/ControlPlaneCrypto.cs's
/// Canonicalize/WriteCanonical. Signatures only verify server-side if this produces the exact
/// same bytes the server canonicalizes for the same logical payload.</summary>
public static class JsonCanonicalization
{
    public static byte[] Canonicalize(JsonElement element)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(writer, element);
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException("Unsupported JSON value for canonicalization.");
        }
    }

    public static byte[] DomainSeparated(string domain, ReadOnlySpan<byte> payload)
    {
        byte[] prefix = System.Text.Encoding.ASCII.GetBytes(domain);
        byte[] result = new byte[prefix.Length + 1 + payload.Length];
        prefix.CopyTo(result, 0);
        payload.CopyTo(result.AsSpan(prefix.Length + 1));
        return result;
    }
}
