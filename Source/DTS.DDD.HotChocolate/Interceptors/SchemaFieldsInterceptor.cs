using DTS.DDD.HotChocolate.Attributes;
using HotChocolate.Configuration;
using HotChocolate.Types.Descriptors.Configurations;
using System.Reflection;

namespace DTS.DDD.HotChocolate.Interceptors;

/// <summary>
/// Keeps the fields of a class marked <c>[GraphQLSchema(name)]</c> in the schema of that name, and removes them from
/// every other schema. The fields are methods marked <c>[Query]</c>, <c>[Mutation]</c> or <c>[Subscription]</c>, which
/// HotChocolate's generator registers in every schema a project is added to.
/// </summary>
/// <remarks>
/// <para>
/// The interceptor reads the attribute when the schema is built, with inheritance, so an attribute of the application
/// that derives from <see cref="GraphQLSchemaAttribute"/>, such as
/// <c>public sealed class OperatorSchemaAttribute() : GraphQLSchemaAttribute("operator");</c>, marks a class as well. A
/// class with two marks is in both schemas. A class without a mark is in every schema.
/// </para>
/// <para>
/// Only marks without an operation type count here. A mark with one, <c>[GraphQLSchema("admin", OperationType.Query)]</c>,
/// is read by the toolkit's generator, which registers the class's fields in that schema only.
/// </para>
/// <para>
/// Registered by <see cref="DependencyInjection.AddDDDToolkitTypes"/>, with the kind of the builder's schema: its name, or
/// the <c>schemaKind</c> the host gives it, so several schemas of one kind, an operator schema per module, share a mark.
/// </para>
/// </remarks>
/// <param name="schemaKind">What the marks are compared with: the schema's name, or the kind the host gave it.</param>
public sealed class SchemaFieldsInterceptor(string schemaKind) : TypeInterceptor
{
    /// <summary>Removes the fields of another schema before HotChocolate works out what they depend on.</summary>
    public override void OnBeforeRegisterDependencies(ITypeDiscoveryContext discoveryContext, TypeSystemConfiguration configuration)
        => Apply(configuration, remove: true);

    /// <summary>Ignores a field of another schema that a type extension merged in after discovery.</summary>
    public override void OnBeforeCompleteType(ITypeCompletionContext completionContext, TypeSystemConfiguration configuration)
        => Apply(configuration, remove: false);

    private void Apply(TypeSystemConfiguration configuration, bool remove)
    {
        if (configuration is not ObjectTypeConfiguration type)
        {
            return;
        }

        for (var index = type.Fields.Count - 1; index >= 0; index--)
        {
            var field = type.Fields[index];
            if (!OfAnotherSchema(field.ResolverMember ?? field.Member))
            {
                continue;
            }

            if (remove)
            {
                type.Fields.RemoveAt(index);
            }
            else
            {
                field.Ignore = true;
            }
        }
    }

    // A field of a marked class whose marks name no schema of this kind.
    private bool OfAnotherSchema(MemberInfo? member)
    {
        if (member?.DeclaringType is not { } declaring)
        {
            return false;
        }

        var marks = declaring.GetCustomAttributes<GraphQLSchemaAttribute>(inherit: true).Where(mark => mark.Operation is null).ToList();
        return marks.Count > 0 && !marks.Any(mark => string.Equals(mark.Name, schemaKind, StringComparison.Ordinal));
    }
}
