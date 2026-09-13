using HotChocolate.Configuration;
using HotChocolate.Types.Descriptors.Configurations;
using System.Reflection;

// HotChocolate 16 ships a HotChocolate.Types.Composite.InternalAttribute that is in scope here;
// alias the toolkit's attribute so the two can never be confused.
using DddInternalAttribute = DDDToolkit.Abstractions.Attributes.InternalAttribute;

namespace DDDToolkit.HotChocolate.Interceptors;

/// <summary>
/// Keeps every field whose backing member carries
/// <see cref="DDDToolkit.Abstractions.Attributes.InternalAttribute">[Internal]</see> out of the schema.
/// </summary>
/// <remarks>
/// <para>
/// The toolkit marks auxiliary members — <c>IsValid</c>, <c>IsValidated</c>, the validation
/// <c>Errors</c> and the pending <c>DomainEvents</c> of an aggregate, for example — with
/// <c>[Internal]</c>. Those are plumbing, not API, and HotChocolate's convention-based binding would
/// otherwise publish them.
/// </para>
/// <para>
/// Registered by <see cref="DDDToolkit.HotChocolate.DependencyInjection.AddDDDToolkitTypes"/>. It
/// runs for object types, interface types and input object types, so a member stays hidden whether
/// it is read or written. Covering interface fields as well as object fields matters: a field kept
/// on an interface but dropped from an implementing object would make the schema invalid.
/// </para>
/// <para>
/// HotChocolate 15 renamed the type-system configuration classes (<c>DefinitionBase</c> became
/// <see cref="TypeSystemConfiguration"/>, <c>ObjectTypeDefinition</c> became
/// <see cref="ObjectTypeConfiguration"/>); this interceptor is written against the 16.x names.
/// </para>
/// </remarks>
public sealed class IgnoreInternalFieldsInterceptor : TypeInterceptor
{
    /// <summary>
    /// Drops the internal fields before HotChocolate works out what each field depends on, so the
    /// types they mention are never pulled into the schema either.
    /// </summary>
    /// <remarks>
    /// This is the hook that matters for a field such as the FluentValidation integration's
    /// <c>Errors</c>. Only flagging it as ignored — which is what happens at completion time —
    /// leaves FluentValidation's <c>ValidationFailure</c> registered as a schema type that no field
    /// can reach.
    /// </remarks>
    public override void OnBeforeRegisterDependencies(
        ITypeDiscoveryContext discoveryContext,
        TypeSystemConfiguration configuration)
        => Apply(configuration, remove: true);

    /// <summary>
    /// Flags anything internal that appeared after discovery — from a merged type extension, for
    /// example — so it still stays out of the schema.
    /// </summary>
    public override void OnBeforeCompleteType(
        ITypeCompletionContext completionContext,
        TypeSystemConfiguration configuration)
        => Apply(configuration, remove: false);

    private static void Apply(TypeSystemConfiguration configuration, bool remove)
    {
        switch (configuration)
        {
            case ObjectTypeConfiguration objectType:
                Apply(objectType.Fields, static field => IsInternal(field.Member) || IsInternal(field.ResolverMember), remove);
                break;

            case InterfaceTypeConfiguration interfaceType:
                Apply(interfaceType.Fields, static field => IsInternal(field.Member) || IsInternal(field.ResolverMember), remove);
                break;

            case InputObjectTypeConfiguration inputType:
                Apply(inputType.Fields, static field => IsInternal(field.Property), remove);
                break;
        }
    }

    private static void Apply<TField>(IList<TField> fields, Func<TField, bool> isInternal, bool remove)
        where TField : FieldConfiguration
    {
        for (var index = fields.Count - 1; index >= 0; index--)
        {
            var field = fields[index];
            if (!isInternal(field))
            {
                continue;
            }

            if (remove)
            {
                fields.RemoveAt(index);
            }
            else
            {
                field.Ignore = true;
            }
        }
    }

    private static bool IsInternal(MemberInfo? member)
        => member is not null && member.IsDefined(typeof(DddInternalAttribute), inherit: true);
}
