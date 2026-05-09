using System.Globalization;
using System.Text.Json;
using HotChocolate.Features;
using HotChocolate.Language;
using HotChocolate.Text.Json;
using HotChocolate.Types;

namespace HoldFast.GraphQL.Private;

/// <summary>
/// Custom scalar that maps C# DateTime to the Go schema's Timestamp scalar.
/// The Go/gqlgen backend exposes all date/time fields as the Timestamp scalar
/// (an ISO 8601 string). HC's built-in DateTime scalar uses the name "DateTime",
/// causing schema mismatches when the frontend sends variables typed as Timestamp.
///
/// HOL-16: rewritten for HotChocolate 16's ScalarType API. The four override
/// points changed:
///
///   HC 15                                    HC 16
///   ──────────────────────────────────────   ──────────────────────────────────
///   ParseLiteral(StringValueNode)            OnCoerceInputLiteral(StringValueNode)
///   ParseValue(DateTime)                     OnValueToLiteral(DateTime)
///   ParseResult(object?)                     (removed; folded into OnCoerceOutputValue)
///   Serialize(object?)                       OnCoerceOutputValue(DateTime, ResultElement)
///   TryDeserialize(object?, out object?)     OnCoerceInputValue(JsonElement, IFeatureProvider)
///
/// Accepts common ISO 8601 formats including milliseconds; serializes as ISO 8601 UTC.
/// </summary>
public sealed class TimestampType : ScalarType<DateTime, StringValueNode>
{
    public TimestampType() : base("Timestamp") { }

    /// <summary>
    /// HC 16 input-coercion path: literal AST node → runtime value. Called when
    /// the GraphQL document contains a string literal for a Timestamp arg.
    /// </summary>
    protected override DateTime OnCoerceInputLiteral(StringValueNode valueSyntax)
    {
        if (DateTimeOffset.TryParse(
                valueSyntax.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var dto))
        {
            return dto.UtcDateTime;
        }
        throw new LeafCoercionException(
            $"Cannot parse '{valueSyntax.Value}' as Timestamp.", this, HotChocolate.Path.Root);
    }

    /// <summary>
    /// HC 16 input-coercion path: JSON variable → runtime value. Called when
    /// the variable is supplied via the variables map (most common case from
    /// the dashboard frontend, which sends timestamps as JSON strings).
    /// </summary>
    protected override DateTime OnCoerceInputValue(JsonElement value, IFeatureProvider features)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var s = value.GetString();
            if (s is null)
                throw new LeafCoercionException("Timestamp string is null.", this, HotChocolate.Path.Root);
            if (DateTimeOffset.TryParse(
                    s,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var dto))
            {
                return dto.UtcDateTime;
            }
            throw new LeafCoercionException(
                $"Cannot parse '{s}' as Timestamp.", this, HotChocolate.Path.Root);
        }
        throw new LeafCoercionException(
            $"Cannot coerce {value.ValueKind} to Timestamp; expected string.", this, HotChocolate.Path.Root);
    }

    /// <summary>
    /// HC 16 literal-build path: runtime value → literal AST. Used when HC
    /// needs to inline a Timestamp into a printed query (e.g., introspection
    /// default values).
    /// </summary>
    protected override StringValueNode OnValueToLiteral(DateTime runtimeValue)
        => new(runtimeValue.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));

    /// <summary>
    /// HC 16 output-coercion path: runtime value is written into the result
    /// element directly. Replaces the old Serialize/ParseResult split — HC 16
    /// inverted the contract from "return a value" to "write a value into the
    /// destination" so the executor can avoid intermediate boxing.
    ///
    /// SetStringValue produces the same JSON output as the old
    /// `Serialize(...) → string` path: a quoted ISO 8601 string matching the
    /// Go schema's Timestamp wire format.
    /// </summary>
    protected override void OnCoerceOutputValue(DateTime runtimeValue, ResultElement element)
        => element.SetStringValue(runtimeValue.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
}
