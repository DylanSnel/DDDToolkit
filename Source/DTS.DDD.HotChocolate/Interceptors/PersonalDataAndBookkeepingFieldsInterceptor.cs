using DTS.DDD.Abstractions.Attributes;
using HotChocolate.Configuration;
using HotChocolate.Types.Descriptors.Configurations;
using System.Reflection;

namespace DTS.DDD.HotChocolate.Interceptors;

/// <summary>
/// Leaves the members marked <see cref="PersonalDataAttribute">[PersonalData]</see> or
/// <see cref="BookkeepingAttribute">[Bookkeeping]</see> out of a type that binds its fields by convention.
/// </summary>
/// <remarks>
/// <para>
/// HotChocolate binds implicitly by default, so every public property becomes a field. Tenancy's seat has the
/// <c>Identity</c> of its person's account, and Tenancy's role has the permissions its starter role gave it. Both are
/// stored. Neither belongs in an API that the application did not choose to show them in.
/// </para>
/// <para>
/// The application shows such a member in one of two ways:
/// </para>
/// <list type="bullet">
/// <item>The type binds explicitly with <c>BindFieldsExplicitly()</c> and lists the member. This interceptor leaves a
/// type that binds explicitly alone.</item>
/// <item>The type class has a resolver for the field, such as <c>GetIdentity([Parent] Seat seat)</c>. A field with a
/// resolver of its own stays.</item>
/// </list>
/// <para>
/// The interceptor works on object types, interface types and input object types, so a marked member can be neither
/// read nor written by convention. It removes the fields before HotChocolate registers what they depend on, as
/// <see cref="IgnoreInternalFieldsInterceptor"/> does, so a type that only a removed field mentions stays out of the
/// schema. Registered by <see cref="DependencyInjection.AddDDDToolkitTypes"/>.
/// </para>
/// </remarks>
public sealed class PersonalDataAndBookkeepingFieldsInterceptor : TypeInterceptor
{
    /// <summary>Removes the marked fields of the types that bind by convention.</summary>
    public override void OnBeforeRegisterDependencies(ITypeDiscoveryContext discoveryContext, TypeSystemConfiguration configuration)
    {
        switch (configuration)
        {
            case ObjectTypeConfiguration objectType when objectType.Fields.IsImplicitBinding():
                Remove(objectType.Fields, static field => IsMarked(field.Member) && HasNoResolver(field));
                break;

            case InterfaceTypeConfiguration interfaceType when interfaceType.Fields.IsImplicitBinding():
                Remove(interfaceType.Fields, static field => IsMarked(field.Member) && field.ResolverMember is null);
                break;

            case InputObjectTypeConfiguration inputType when inputType.Fields.IsImplicitBinding():
                Remove(inputType.Fields, static field => IsMarked(field.Property));
                break;
        }
    }

    private static void Remove<TField>(IList<TField> fields, Func<TField, bool> marked)
        where TField : FieldConfiguration
    {
        for (var index = fields.Count - 1; index >= 0; index--)
        {
            if (marked(fields[index]))
            {
                fields.RemoveAt(index);
            }
        }
    }

    // A field from a type class's method has that method as its member, or a resolver set by HotChocolate's generator.
    private static bool HasNoResolver(ObjectFieldConfiguration field)
        => field is { Resolver: null, PureResolver: null, ResolverMember: null };

    private static bool IsMarked(MemberInfo? member)
        => member is not null
            && (Attribute.IsDefined(member, typeof(PersonalDataAttribute), inherit: true)
                || Attribute.IsDefined(member, typeof(BookkeepingAttribute), inherit: true));
}
