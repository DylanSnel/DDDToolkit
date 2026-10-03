using HotChocolate.Configuration;
using HotChocolate.Internal;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;

namespace DDDToolkit.HotChocolate.Interceptors;

/// <summary>
/// Makes every field of an entity nullable in the schema, except the fields of its key, whatever the type it is
/// declared over says. An entity is an object type that carries a key: <c>[EntityKey("id")]</c>.
/// </summary>
/// <remarks>
/// <para>
/// A gateway resolves a reference to an entity by asking the schema that owns it. When the owner answers
/// nothing, because the entity is not there or the caller may not read it, the gateway has only the key the
/// naming schema gave. Asked for a field the owner declares as never null, it nulls the whole reference and
/// adds an entry to <c>errors</c>. Asked only for fields that may be null, it answers the object with its key
/// and the other fields null, and no error: the nulls tell a client there is nothing for it to read. So every
/// field a gateway may have to leave empty has to be nullable, and that is every field but the key.
/// </para>
/// <para>
/// HotChocolate takes a field's nullability from the member it is declared over, which is right for a type
/// served on its own and wrong here: the record a module's application layer answers says a name is a
/// <see cref="string"/>, never null, and it is right about itself. Saying otherwise per field is one
/// <c>descriptor.Field(...).Type&lt;...&gt;()</c> for every field of every entity, and one forgotten field is an
/// error at run time. This interceptor says it once: a host declares its GraphQL types over its own records,
/// puts the key on them, and writes nothing per field.
/// </para>
/// <para>
/// Only the outermost nullability changes: <c>[Loan!]!</c> becomes <c>[Loan!]</c>. A field named in any key of
/// the type keeps what it declared; a key that reaches into an object, <c>"id owner { id }"</c>, keeps
/// <c>owner</c>. A type without a key is not touched. An interface the entity implements is not touched
/// either, so a field it shares with the entity has to be nullable there as well, or HotChocolate refuses the
/// schema. Registered by <see cref="DependencyInjection.AddDDDToolkitEntityNullability"/>.
/// </para>
/// </remarks>
public sealed class EntityFieldsNullableInterceptor : TypeInterceptor
{
    /// <summary>
    /// Runs when extensions have been merged into their type, so the key and the fields are all there, wherever
    /// each was declared, and before the fields' types are resolved.
    /// </summary>
    public override void OnBeforeCompleteType(ITypeCompletionContext completionContext, TypeSystemConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(completionContext);

        if (configuration is not ObjectTypeConfiguration type || KeyFieldsOf(type) is not { } keyFields)
        {
            return;
        }

        foreach (var field in type.Fields)
        {
            if (field.IsIntrospectionField || field.Type is null || keyFields.Contains(field.Name))
            {
                continue;
            }

            field.Type = Nullable(field.Type, completionContext.TypeInspector);
        }
    }

    /// <summary>The names of the fields in the type's keys, or null when it has no key and so is no entity.</summary>
    private static HashSet<string>? KeyFieldsOf(ObjectTypeConfiguration type)
    {
        if (!type.HasDirectives)
        {
            return null;
        }

        HashSet<string>? fields = null;
        foreach (var directive in type.Directives)
        {
            if (directive.Value is not EntityKey key)
            {
                continue;
            }

            fields ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (var selection in key.Fields.Selections)
            {
                if (selection is FieldNode field)
                {
                    fields.Add(field.Name.Value);
                }
            }
        }

        return fields;
    }

    /// <summary>The same type, allowed to be null. Each way HotChocolate writes a field's type down is unwrapped in its own terms.</summary>
    private static TypeReference Nullable(TypeReference type, ITypeInspector inspector)
        => type switch
        {
            // Inferred from a member, or named with Type<T>(): the runtime or schema type, with its nullability.
            ExtendedTypeReference inferred => inferred.WithType(inspector.ChangeNullability(inferred.Type, true)),

            // A resolver of a type class HotChocolate's generator wrote: the type's structure, spelled out.
            FactoryTypeReference { TypeStructure: NonNullTypeNode written } generated => new FactoryTypeReference(generated.TypeDefinition, written.Type),

            // Written as schema syntax: Type("String!").
            SyntaxTypeReference { Type: NonNullTypeNode written } syntax => syntax.WithType(written.Type),

            // Handed over as a type instance: Type(new NonNullType(...)).
            SchemaTypeReference { Type: NonNullType written } instance => instance.WithType(written.NullableType),

            _ => type,
        };
}
