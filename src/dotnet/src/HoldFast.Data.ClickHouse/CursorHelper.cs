using System.Globalization;
using System.Text;

namespace HoldFast.Data.ClickHouse;

/// <summary>
/// Encodes and decodes pagination cursors matching Go's cursor.go format.
/// Format: base64("{RFC3339},{uuid}")
/// </summary>
public static class CursorHelper
{
    private const string Rfc3339Format = "yyyy-MM-ddTHH:mm:ssK";

    public static string Encode(DateTime timestamp, string uuid)
    {
        var utc = timestamp.Kind == DateTimeKind.Utc
            ? timestamp
            : timestamp.ToUniversalTime();
        var key = $"{utc.ToString(Rfc3339Format, CultureInfo.InvariantCulture)},{uuid}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(key));
    }

    public static (DateTime Timestamp, string Uuid) Decode(string encodedCursor)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encodedCursor);
        }
        catch (FormatException)
        {
            throw new ArgumentException("Cursor is not valid base64", nameof(encodedCursor));
        }

        var parts = Encoding.UTF8.GetString(bytes).Split(',');
        if (parts.Length != 2)
            throw new ArgumentException("Cursor is invalid — expected format: timestamp,uuid", nameof(encodedCursor));

        if (!DateTime.TryParseExact(parts[0], Rfc3339Format, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            throw new ArgumentException($"Cursor contains invalid timestamp: {parts[0]}", nameof(encodedCursor));
        }

        return (timestamp, parts[1]);
    }

    /// <summary>
    /// Try to decode without throwing.
    /// </summary>
    public static bool TryDecode(string? encodedCursor, out DateTime timestamp, out string uuid)
    {
        timestamp = default;
        uuid = string.Empty;

        if (string.IsNullOrEmpty(encodedCursor))
            return false;

        try
        {
            (timestamp, uuid) = Decode(encodedCursor);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
