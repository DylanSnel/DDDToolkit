using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using Examples.Tenancy.Tenants.Contracts.TokenRoles;
using HotChocolate.Configuration;
using HotChocolate.Language;
using HotChocolate.Types.Descriptors.Configurations;

namespace Examples.Tenancy.Host.Access;

/// <summary>
/// The seat a route inside a tenant requires, for GraphQL: a check in front of every root field of a module's
/// schema, queries, mutations and lookups alike, but the fields named as open and the fields named as the
/// operators', which ask for an operator instead.
/// </summary>
/// <remarks>
/// For REST the same requirement is an authorization policy (<see cref="SamplePolicies.SeatRequired"/>). A policy
/// cannot do it here: HotChocolate's own authorization answers one generic error, which cannot carry the refusal's
/// code, and a client is owed the same <c>tenancy.tenant-required</c> or <c>tenancy.not-seated</c> it gets from a
/// route. So the gate asks the question the policy's handler asks, <see cref="SeatRequirement.RefusalFor"/>, of
/// the caller tenant selection made current, and throws the refusal. A query then answers a coded error, and a
/// mutation a <c>RefusalError</c> in its payload: the gate is put inside what turns a mutation's refusals into
/// typed errors.
/// <para>
/// The application's own staff hold no seat, and the routes they read are mapped outside the group that
/// requires one, under <see cref="SamplePolicies.OperatorRequired"/>. A field of theirs gets the same here: the
/// question that policy's handler asks, and <c>tenancy.operators-only</c> for anybody else, a seat included.
/// </para>
/// <para>
/// It is a check at the door, not the access check: every command and query still passes its module's own on
/// its way to its handler.
/// </para>
/// </remarks>
/// <param name="openFields">The root fields a caller without a seat may ask. A name the schema does not have is ignored.</param>
/// <param name="operatorFields">The root fields only an operator may ask, seat or no seat. A name the schema does not have is ignored.</param>
public sealed class SeatGate(IEnumerable<string> openFields, IEnumerable<string> operatorFields) : TypeInterceptor
{
    private static readonly FieldMiddlewareConfiguration Seated = new(next => context =>
    {
        // The caller the request's flow carries: the gateway calls a module's schema in that flow.
        if (SeatRequirement.RefusalFor(TenancyCallers.Current<TenantId, SeatId>()) is { } refusal)
        {
            throw refusal;
        }

        return next(context);
    });

    private static readonly FieldMiddlewareConfiguration OperatorsOnly = new(next => context =>
    {
        // What the token says, as the routes' policy and the modules' own checks ask it.
        if (!SampleTokenRoles.IsOperator(Callers.Ambient))
        {
            throw TenancyRefusals.Of(TenancyRefusals.OperatorsOnly);
        }

        return next(context);
    });

    private readonly HashSet<string> _open = new(openFields, StringComparer.Ordinal);
    private readonly HashSet<string> _operators = new(operatorFields, StringComparer.Ordinal);
    private ObjectTypeConfiguration? _query;
    private bool _queriesGated;

#pragma warning disable HC8001 // The one hook that says which type is the query type, whatever it is called.
    /// <summary>Remembers which type of the schema is the query type.</summary>
    public override void OnAfterResolveRootType(ITypeCompletionContext completionContext, ObjectTypeConfiguration configuration, OperationType operationType)
    {
        if (operationType == OperationType.Query)
        {
            _query = configuration;
        }
    }
#pragma warning restore HC8001

    /// <summary>Puts the check first on every field of the query type: a refusal there is the field's error.</summary>
    public override void OnBeforeCompleteType(ITypeCompletionContext completionContext, TypeSystemConfiguration configuration)
    {
        if (_query is null || !ReferenceEquals(configuration, _query))
        {
            return;
        }

        foreach (var field in _query.Fields)
        {
            if (!field.IsIntrospectionField && CheckOf(field.Name) is { } check)
            {
                field.MiddlewareConfigurations.Insert(0, check);
            }
        }

        _queriesGated = true;
    }

    /// <summary>
    /// A schema whose query type the gate never saw would serve its fields unchecked, and say nothing. So it does
    /// not build: every source schema has a query type, and the gate is in front of it or the host does not start.
    /// </summary>
    public override void OnAfterCompleteTypes()
    {
        if (!_queriesGated)
        {
            throw new InvalidOperationException(
                "The seat gate found no query type in a schema it was added to, so that schema's fields would be served unchecked. "
                + "It learns the query type from OnAfterResolveRootType and gates it in OnBeforeCompleteType.");
        }
    }

    /// <summary>
    /// Puts the check on a mutation field while it is handed to the interceptors, before the mutation conventions
    /// wrap it: the refusal is then thrown inside them, and becomes a typed error in the payload.
    /// </summary>
    public override void OnBeforeCompleteMutationField(ITypeCompletionContext completionContext, ObjectFieldConfiguration mutationField)
    {
        ArgumentNullException.ThrowIfNull(mutationField);

        if (CheckOf(mutationField.Name) is { } check)
        {
            mutationField.MiddlewareConfigurations.Insert(0, check);
        }
    }

    /// <summary>What stands in front of a root field: nothing for an open one, the operators' check for one of theirs, the seat for every other.</summary>
    private FieldMiddlewareConfiguration? CheckOf(string field)
        => _open.Contains(field) ? null : _operators.Contains(field) ? OperatorsOnly : Seated;
}
