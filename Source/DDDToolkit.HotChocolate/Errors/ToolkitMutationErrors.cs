using DDDToolkit.Exceptions;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;

namespace DDDToolkit.HotChocolate.Errors;

/// <summary>
/// Gives every mutation of a schema the toolkit's four error types, so a resolver declares none of them: a
/// use case refuses, and the payload says so. Registered by
/// <see cref="DependencyInjection.AddDDDToolkitMutationConventions"/>.
/// </summary>
internal sealed class ToolkitMutationErrors : MutationErrorConfiguration
{
    /// <summary>The error types, in the order a mutation is given them.</summary>
    private static readonly Type[] ErrorTypes =
    [
        typeof(RefusalError),
        typeof(InvalidValuesError),
        typeof(BrokenRulesError),
        typeof(ConcurrencyConflictError),
    ];

    /// <summary>
    /// What the error types are made of, which the schema has to know before any mutation is looked at.
    /// </summary>
    private static readonly Type[] MemberTypes =
    [
        typeof(RefusalKind),
        typeof(FailureArgument),
        typeof(ValueFailure),
        typeof(RuleViolation),
    ];

    /// <summary>Has the schema know the types the error types are made of before any mutation is looked at.</summary>
    /// <remarks>
    /// The conventions register an error type late, after the schema's types were discovered, and work out the
    /// types of its members themselves. For an enum they go on into the members of the enum itself, where
    /// <c>GetTypeCode()</c> puts the enum <c>TypeCode</c> in the schema; a type the schema already knows is
    /// left alone, so what the error types are made of is registered here, ahead of them. The cost is that a
    /// schema with the conventions and no mutation declares these four types too; <c>RefusalKind</c> is also
    /// the type of the <c>kind</c> a refused query carries in its extensions.
    /// </remarks>
    public override IEnumerable<TypeReference> OnResolveDependencies(IDescriptorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return [.. MemberTypes.Select(type => context.TypeInspector.GetOutputTypeRef(type))];
    }

    /// <inheritdoc />
    public override void OnConfigure(IDescriptorContext context, ObjectFieldConfiguration mutationField)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mutationField);

        foreach (var errorType in ErrorTypes)
        {
            mutationField.AddErrorType(context, errorType);
        }
    }
}
