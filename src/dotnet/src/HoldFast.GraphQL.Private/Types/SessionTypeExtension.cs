using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using HoldFast.Data.ClickHouse.Models;
using HotChocolate;
using HotChocolate.Types;

namespace HoldFast.GraphQL.Private.Types;

/// <summary>
/// Adds fields to Session that are missing from the entity or need typed overrides.
/// The Go schema exposes several session-level fields (fields list, user_properties, etc.)
/// that are not stored as simple columns in PostgreSQL.
/// </summary>
[ExtendObjectType(typeof(Session))]
public class SessionTypeExtension
{
    /// <summary>
    /// Related Field entities for this session (search filter metadata).
    /// Returns empty list — field data is project-scoped, not session-scoped in HoldFast.
    /// </summary>
    [GraphQLName("fields")]
    public List<Field> GetFields([Parent] Session s) => [];
}

/// <summary>
/// Overrides QueryKey.type to expose KeyType enum instead of String.
/// The ClickHouse model uses string for portability, but the GraphQL schema requires the enum.
/// </summary>
[ExtendObjectType(typeof(QueryKey))]
public class QueryKeyTypeExtension
{
    [GraphQLName("type")]
    public KeyType GetType([Parent] QueryKey k)
    {
        if (Enum.TryParse<KeyType>(k.Type, ignoreCase: true, out var result))
            return result;
        return KeyType.String;
    }
}
