using System.Globalization;
using HotChocolate.Language;
using HotChocolate.Types;

namespace HoldFast.GraphQL.Private;

/// <summary>
/// Custom scalar that maps C# DateTime to the Go schema's Timestamp scalar.
/// The Go/gqlgen backend exposes all date/time fields as the Timestamp scalar (an ISO 8601 string).
/// HC's built-in DateTime scalar uses the name "DateTime", causing schema mismatches when the
/// frontend sends variables typed as Timestamp.
/// Accepts ISO 8601 strings from query variables and inline literals.
/// Serializes as ISO 8601 UTC string (e.g., "2024-01-15T10:00:00.000Z").
/// </summary>
public sealed class TimestampType : ScalarType<DateTime, StringValueNode>
{
    public TimestampType() : base("Timestamp") { }

    protected override DateTime ParseLiteral(StringValueNode valueSyntax)
    {
        if (DateTime.TryParse(
            valueSyntax.Value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal,
            out var dt))
            return dt.ToUniversalTime();
        throw new SerializationException(
            $"Cannot parse '{valueSyntax.Value}' as Timestamp.", this);
    }

    protected override StringValueNode ParseValue(DateTime runtimeValue)
        => new(runtimeValue.ToUniversalTime().ToString("o"));

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
        if (resultValue is string s && DateTime.TryParse(
            s,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal,
            out var parsed))
        {
            runtimeValue = parsed.ToUniversalTime();
            return true;
        }
        runtimeValue = null;
        return false;
    }
}
