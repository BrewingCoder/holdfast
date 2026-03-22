using HotChocolate.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors.Definitions;

namespace HoldFast.GraphQL.Private;

/// <summary>
/// HC TypeInterceptor that changes all output fields named "id" from Int! to ID!.
///
/// The Go/gqlgen schema exposes all entity primary keys as `ID!` scalars (serialized as strings).
/// HC by default maps C# `int Id` to `Int!`, causing frontend comparisons like
/// `currentProject.id !== projectId` (where projectId is a string route param) to fail
/// due to JS strict equality (1 !== "1").
///
/// This interceptor ensures that any field named exactly "id" on any object type
/// is typed as `ID!`, matching the Go schema convention.
/// </summary>
public class IdFieldTypeInterceptor : TypeInterceptor
{
    public override void OnBeforeCompleteType(
        ITypeCompletionContext completionContext,
        DefinitionBase definition)
    {
        if (definition is not ObjectTypeDefinition typeDef)
            return;

        foreach (var field in typeDef.Fields)
        {
            if (field.Name == "id")
            {
                field.Type = completionContext.TypeInspector
                    .GetTypeRef(typeof(NonNullType<IdType>));
            }
        }
    }
}
