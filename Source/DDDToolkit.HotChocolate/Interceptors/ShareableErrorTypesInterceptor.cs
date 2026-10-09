using DDDToolkit.HotChocolate.Errors;
using HotChocolate.Configuration;
using HotChocolate.Internal;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;

namespace DDDToolkit.HotChocolate.Interceptors;

/// <summary>
/// Marks the toolkit's error types <c>@shareable</c> in a source schema, the schema one module or service
/// serves for a Fusion gateway to compose with others.
/// </summary>
/// <remarks>
/// <para>
/// Every source schema that calls <see cref="DependencyInjection.AddDDDToolkitMutationConventions"/> declares
/// the same <c>RefusalError</c>, <c>InvalidValuesError</c>, <c>BrokenRulesError</c> and
/// <c>ConcurrencyConflictError</c>, and the types they list. To the gateway each is one type that several
/// schemas return, and a field belongs to one source schema unless it says otherwise: without the directive
/// composition refuses the second schema that has the conventions. Like a value object, an error has no owner
/// and no identity, and whichever schema returned one gives the answer for it.
/// </para>
/// <para>
/// Active only in a source schema, which HotChocolate's <c>AddSourceSchemaDefaults()</c> declares; a schema
/// served to clients directly gets no directive it has no use for. Registered by
/// <see cref="DependencyInjection.AddDDDToolkitMutationConventions"/>.
/// </para>
/// </remarks>
public sealed class ShareableErrorTypesInterceptor : TypeInterceptor
{
    /// <summary>The object types the conventions add to a schema. The interface they implement needs no directive.</summary>
    private static readonly HashSet<Type> ErrorTypes =
    [
        typeof(RefusalError),
        typeof(InvalidValuesError),
        typeof(ValueFailure),
        typeof(BrokenRulesError),
        typeof(RuleViolation),
        typeof(ConcurrencyConflictError),
        typeof(FailureArgument),
    ];

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

    /// <summary>Registers the <c>@shareable</c> directive the error types are about to carry.</summary>
    public override IEnumerable<TypeReference> RegisterMoreTypes(IReadOnlyCollection<ITypeDiscoveryContext> discoveryContexts)
    {
        if (_shareable is not null)
        {
            yield return _shareable;
            _shareable = null;
        }
    }

    /// <summary>Puts <c>@shareable</c> on every object type whose runtime type is one of the toolkit's error types.</summary>
    public override void OnBeforeCompleteType(ITypeCompletionContext completionContext, TypeSystemConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(completionContext);

        if (configuration is ObjectTypeConfiguration { IsExtension: false } objectType
            && ErrorTypes.Contains(objectType.RuntimeType))
        {
            objectType.AddDirective(Shareable.Instance, completionContext.TypeInspector);
        }
    }
}
