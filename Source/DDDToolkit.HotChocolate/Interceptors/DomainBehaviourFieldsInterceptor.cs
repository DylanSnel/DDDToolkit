using DDDToolkit.Abstractions.Interfaces;
using HotChocolate.Configuration;
using HotChocolate.Types.Descriptors.Configurations;
using System.Reflection;

namespace DDDToolkit.HotChocolate.Interceptors;

/// <summary>
/// Keeps the methods of entities, aggregates and value objects out of a schema that binds their fields by
/// convention: such a type publishes its properties, not its behaviour.
/// </summary>
/// <remarks>
/// <para>
/// HotChocolate binds implicitly by default. Every public property, and every public method that returns
/// something, becomes a field, and a method's parameters become the field's arguments. On a domain type the
/// methods are its behaviour: <c>Money.Times(int)</c> would be published as <c>times(quantity: Int!)</c>,
/// and a method on an aggregate that changes state and returns a result would run inside an ordinary
/// query. Neither GraphQL nor HotChocolate can tell a method with side effects from one without.
/// </para>
/// <para>
/// So on an implicitly bound object type whose runtime type implements <see cref="IEntity"/> or
/// <see cref="IValueObject"/>, the fields that come from the type's own methods are removed. What stays:
/// </para>
/// <list type="bullet">
/// <item>a type declared with <c>BindFieldsExplicitly()</c>, untouched: it publishes what it lists, methods included;</item>
/// <item>fields a type extension adds, and fields with a resolver of their own;</item>
/// <item>properties. A computed value that belongs in the schema is best made one.</item>
/// </list>
/// <para>
/// On an implicitly bound type a method named with <c>descriptor.Field(order =&gt; order.Total())</c> is
/// removed as well, because HotChocolate records it the same way as one it found itself. Declare that type
/// with <c>BindFieldsExplicitly()</c>. Registered by <see cref="DependencyInjection.AddDDDToolkitTypes"/>.
/// </para>
/// </remarks>
public sealed class DomainBehaviourFieldsInterceptor : TypeInterceptor
{
    /// <summary>
    /// Removes the fields before HotChocolate works out what they depend on, so the types only their
    /// arguments mention, such as a <c>MoneyInput</c> for <c>plus(other:)</c>, never enter the schema.
    /// </summary>
    public override void OnBeforeRegisterDependencies(ITypeDiscoveryContext discoveryContext, TypeSystemConfiguration configuration)
    {
        if (configuration is not ObjectTypeConfiguration { IsExtension: false } objectType
            || !objectType.Fields.IsImplicitBinding()
            || !IsDomainType(objectType.RuntimeType))
        {
            return;
        }

        for (var index = objectType.Fields.Count - 1; index >= 0; index--)
        {
            var field = objectType.Fields[index];

            if (field is { Member: MethodInfo method, Resolver: null, PureResolver: null }
                && IsDomainType(method.ReflectedType ?? method.DeclaringType))
            {
                objectType.Fields.RemoveAt(index);
            }
        }
    }

    private static bool IsDomainType(Type? type)
        => type is not null
            && (typeof(IEntity).IsAssignableFrom(type) || typeof(IValueObject).IsAssignableFrom(type));
}
