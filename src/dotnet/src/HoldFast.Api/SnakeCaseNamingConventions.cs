using System.Reflection;
using System.Text.RegularExpressions;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;

namespace HoldFast.Api;

/// <summary>
/// HC naming convention that produces snake_case field/argument names to match
/// the original Go/gqlgen schema the frontend was built against.
/// </summary>
public sealed class SnakeCaseNamingConventions : DefaultNamingConventions
{
    private static readonly Regex PascalToSnake = new(
        @"(?<=[a-z0-9])([A-Z])|(?<=[A-Z])([A-Z][a-z])",
        RegexOptions.Compiled);

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        // Insert underscore before uppercase runs
        var snake = PascalToSnake.Replace(name, m => "_" + m.Value);
        return snake.ToLowerInvariant();
    }

    public override string GetMemberName(MemberInfo member, MemberKind kind)
    {
        // Honour explicit [GraphQLName] — don't apply the naming convention on top of it.
        // (The base DefaultNamingConventions returns the [GraphQLName] value, then we would
        // wrongly snake_case it. Check the attribute ourselves and short-circuit instead.)
        var nameAttr = member.GetCustomAttribute<GraphQLNameAttribute>();
        if (nameAttr != null)
            return nameAttr.Name;

        var baseName = base.GetMemberName(member, kind);
        if (member.MemberType == MemberTypes.Method)
        {
            // Go/gqlgen schema naming convention:
            //   Mutations → camelCase  (createProject, editProjectSettings, updateAdminAboutYouDetails)
            //   Queries   → snake_case (admin_role_by_project, session_intervals, error_groups)
            // HC base already strips the "Get" prefix from query resolver names.
            var declaringType = member.DeclaringType?.Name ?? "";
            if (declaringType.EndsWith("Mutation", StringComparison.Ordinal))
                return baseName; // keep camelCase
            return ToSnakeCase(baseName); // snake_case for queries
        }
        // Properties (entity fields like photo_url, slack_im_channel_id) → snake_case
        return ToSnakeCase(baseName);
    }

    public override string GetArgumentName(ParameterInfo parameter)
    {
        // [GraphQLName] on a parameter is not honoured by the base GetArgumentName,
        // so we check it ourselves before applying snake_case.
        var attr = parameter.GetCustomAttribute<GraphQLNameAttribute>();
        if (attr != null)
            return attr.Name;
        var baseName = base.GetArgumentName(parameter);
        return ToSnakeCase(baseName);
    }

    public override string GetTypeName(Type type, TypeKind kind)
    {
        // HC default appends "Input" suffix to all InputObject types. The Go/gqlgen schema
        // does NOT auto-append — types that need "Input" already have it in their C# name
        // (e.g. SamplingInput, SessionAlertInput). Types without it (AdminAboutYouDetails)
        // should keep the name as-is, matching the Go schema exactly.
        if (kind == TypeKind.InputObject)
            return type.Name;
        return base.GetTypeName(type, kind);
    }

    public override string GetEnumValueName(object value)
    {
        // Go/gqlgen schema uses PascalCase for enum values (SixMonths, ThreeMonths, etc.).
        // HC default converts to UPPER_SNAKE_CASE (SIX_MONTHS) — override to preserve the
        // C# member name (which already matches the Go schema naming).
        return value.ToString()!;
    }

    public new string FormatFieldName(string fieldName)
    {
        return ToSnakeCase(fieldName);
    }
}
