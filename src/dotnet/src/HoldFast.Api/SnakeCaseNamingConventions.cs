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
        var baseName = base.GetMemberName(member, kind);
        // Methods are query/mutation resolvers — the Go/gqlgen schema uses camelCase for
        // operation names (createProject, editProjectSettings, etc.), which matches HC's
        // default camelCase output. Only properties (entity fields) need snake_case.
        if (member.MemberType == MemberTypes.Method)
            return baseName;
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

    public new string FormatFieldName(string fieldName)
    {
        return ToSnakeCase(fieldName);
    }
}
