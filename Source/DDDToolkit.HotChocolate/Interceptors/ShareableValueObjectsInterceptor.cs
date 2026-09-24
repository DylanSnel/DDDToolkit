using DDDToolkit.Abstractions.Interfaces;
using HotChocolate.Configuration;
using HotChocolate.Internal;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;

namespace DDDToolkit.HotChocolate.Interceptors;

/// <summary>
/// Marks every value object type <c>@shareable</c> in a source schema, the schema one service serves for a
/// Fusion gateway to compose with others.
/// </summary>
/// <remarks>
/// <para>
/// In a composite schema a field belongs to one source schema unless it says otherwise, so the gateway
/// knows who answers it. An entity has one owner, and other services add to it by its key. A value object
/// has no owner and no identity: <c>Money</c> in Catalog's prices and <c>Money</c> in Payments' amounts
/// are the same type, and any service holding one gives the same answer for it. That is what
/// <c>@shareable</c> says, and without it composition refuses the second service that returns a
/// <c>Money</c>.
/// </para>
/// <para>
/// Active only in a source schema, which HotChocolate's <c>AddSourceSchemaDefaults()</c> declares; a
/// schema served to clients directly gets no directive it has no use for. Registered by
/// <see cref="DependencyInjection.AddDDDToolkitTypes"/>.
/// </para>
/// </remarks>
public sealed class ShareableValueObjectsInterceptor : TypeInterceptor
{
    private TypeReference? _shareable;

    /// <summary>On in a source schema only: <c>AddSourceSchemaDefaults()</c> makes node fields shareable.</summary>
    public override bool IsEnabled(IDescriptorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Options.ApplyShareableToNodeFields)
        {
            return false;
        }

        _shareable = context.TypeInspector.GetTypeRef(typeof(Shareable));
        return true;
    }

    /// <summary>Registers the <c>@shareable</c> directive the value object types are about to carry.</summary>
    public override IEnumerable<TypeReference> RegisterMoreTypes(IReadOnlyCollection<ITypeDiscoveryContext> discoveryContexts)
    {
        if (_shareable is not null)
        {
            yield return _shareable;
            _shareable = null;
        }
    }

    /// <summary>Puts <c>@shareable</c> on every object type whose runtime type is a value object.</summary>
    public override void OnBeforeCompleteType(ITypeCompletionContext completionContext, TypeSystemConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(completionContext);

        if (configuration is ObjectTypeConfiguration { IsExtension: false } objectType
            && typeof(IValueObject).IsAssignableFrom(objectType.RuntimeType))
        {
            objectType.AddDirective(Shareable.Instance, completionContext.TypeInspector);
        }
    }
}
