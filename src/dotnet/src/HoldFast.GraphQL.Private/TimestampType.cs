using System.Globalization;
using HotChocolate.Language;
using HotChocolate.Types;

namespace HoldFast.GraphQL.Private;

/// <summary>
/// Custom scalar that maps C# DateTime to the Go schema's Timestamp scalar.
/// The Go/gqlgen backend exposes all date/time fields as the Timestamp scalar (an ISO 8601 string).
/// HC's built-in DateTime scalar uses the name "DateTime", causing schema mismatches when the
/// frontend sends variables typed as Timestamp.
///
/// HC variable coercion flow for input scalars:
///   JSON string → ParseResult(string) → StringValueNode → ParseLiteral(StringValueNode) → DateTime
///
/// Accepts common ISO 8601 formats including milliseconds ("2024-01-15T10:00:00.000Z").
/// Serializes as ISO 8601 UTC string ("2024-01-15T10:00:00.0000000Z").
/// </summary>
public sealed class TimestampType : ScalarType<DateTime, StringValueNode>
{
    public TimestampType() : base("Timestamp") { }

    protected override DateTime ParseLiteral(StringValueNode valueSyntax)
    {
        // Use DateTimeOffset.TryParse for robust ISO 8601 parsing (handles Z, +00:00, etc.)
        if (DateTimeOffset.TryParse(
            valueSyntax.Value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var dto))
            return dto.UtcDateTime;
        throw new SerializationException(
            $"Cannot parse '{valueSyntax.Value}' as Timestamp.", this);
    }

    protected override StringValueNode ParseValue(DateTime runtimeValue)
        => new(runtimeValue.ToUniversalTime().ToString("o"));

    /// <summary>
    /// Called by HC when coercing result values (e.g., from JSON variable strings).
    /// Wraps the raw value as a StringValueNode so ParseLiteral can handle it.
    /// </summary>
    public override IValueNode ParseResult(object? resultValue)
    {
        if (resultValue is DateTime dt)
            return new StringValueNode(dt.ToUniversalTime().ToString("o"));
        if (resultValue is DateTimeOffset dto)
            return new StringValueNode(dto.UtcDateTime.ToString("o"));
        if (resultValue is string s)
            return new StringValueNode(s);
        if (resultValue is null)
            return NullValueNode.Default;
        throw new SerializationException(
            $"Cannot serialize {resultValue.GetType().Name} as Timestamp.", this);
    }

    public override object? Serialize(object? runtimeValue)
        => runtimeValue is DateTime dt
            ? dt.ToUniversalTime().ToString("o")
            : null;

    public override bool TryDeserialize(object? resultValue, out object? runtimeValue)
    {
        if (resultValue is DateTime d)
        {
            runtimeValue = d;
            return true;
        }
        if (resultValue is DateTimeOffset dto)
        {
            runtimeValue = dto.UtcDateTime;
            return true;
        }
        if (resultValue is string s && DateTimeOffset.TryParse(
            s,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed))
        {
            runtimeValue = parsed.UtcDateTime;
            return true;
        }
        runtimeValue = null;
        return false;
    }
}
