using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Decides Tenancy's operator cases before a handler runs (<see cref="TenancyRequirement.Operator"/> and
/// <see cref="TenancyRequirement.OperatorIn{TTenantId}"/>), and begins the operator work a command's handler runs in.
/// <c>AddTenancyAccess</c> registers it for a module's requests, beside the check of Tenancy's other cases.
/// </summary>
/// <remarks>
/// It reads nothing. Whether the caller is an operator is the toolkit's current user's to say, by its token's role. The tenant
/// the request names beside the command is the Tenancy caller's, as tenant selection found it
/// (<see cref="TenancyCaller{TTenantId, TSeatId}.NamedTenant"/>).
/// <para>
/// The work it begins is the only way operator work is begun. It is system work recorded as the operator, whose identity
/// is the current user's id. The class is internal, since an application names <see cref="OperatorAttribute"/>, never the
/// check.
/// </para>
/// </remarks>
/// <param name="options">Which token roles are operators'.</param>
/// <param name="user">Who is calling, as the toolkit says: what says whether the caller is an operator, and who.</param>
internal sealed class TenancyOperatorCheck<TTenantId, TSeatId, TUnitId, TRoleId>(
    TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId> options,
    ICurrentUser user) : IAccessCheck, IBeginsWork
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <inheritdoc />
    public bool Decides(AccessRequirement requirement) => IsOperatorCase(requirement);

    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.operators-only</c> for a caller who is no operator; <c>tenancy.tenant-mismatch</c> for a request that names
    /// another tenant than its <c>[ForTenant]</c> member; for <see cref="OperatorTenant.FromRequest"/>, the refusal tenant
    /// selection gave a request that names no tenant, or one nobody has.
    /// </exception>
    /// <exception cref="InvalidOperationException">The case names a tenant by another type than the application's tenant id.</exception>
    public ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(request);

        // Asked again inside the work it began for this very request, as the toolkit does when the database refuses a
        // save: the caller is that work now, recorded as the operator who was let through.
        if (InWorkBegunFor(request))
        {
            return ValueTask.CompletedTask;
        }

        // The token's own role, never the Tenancy caller: an operator is nobody in every tenant.
        if (!options.IsOperator(user.Snapshot))
        {
            throw TenancyRefusals.Refuse(TenancyRefusals.OperatorsOnly);
        }

        switch (requirement)
        {
            case TenancyRequirement.Operator { Tenant: OperatorTenant.FromRequest }:
                // The tenant the work acts in, which the request names: refused before the handler where it names none.
                _ = TenantTheRequestNames();
                break;

            case TenancyRequirement.Operator:
                break;

            case TenancyRequirement.OperatorIn<TTenantId> required:
                if (TenancyCallers.Current<TTenantId, TSeatId>().NamedTenant is { } named && !named.Equals(required.Tenant))
                {
                    throw TenancyRefusals.Refuse(TenancyRefusals.TenantMismatch);
                }

                break;

            default:
                // A case of Tenancy's this check cannot decide: a tenant known by another type than the application's tenants.
                throw new InvalidOperationException(
                    $"{request.GetType().Name} declares '{requirement}', which Tenancy's access check does not decide: the application's tenants are known by "
                    + $"{typeof(TTenantId).Name}. A requirement nothing checks lets nobody through.");
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A query begins nothing. An operator reads across tenants through the database role of its token. A caller who is
    /// no operator passed another case of the request's requirement, and begins nothing here either.
    /// </remarks>
    public IDisposable? BeginWork(AccessRequirement requirement, IRequireAccess request, bool isQuery)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(request);

        var caller = user.Snapshot;
        if (isQuery || !options.IsOperator(caller) || caller.Id is not { } identity)
        {
            return null;
        }

        return requirement switch
        {
            TenancyRequirement.Operator { Tenant: OperatorTenant.FromRequest } required
                => TenancyWork.BeginOperatorIn<TTenantId, TSeatId>(TenantTheRequestNames(), identity, required.Scope),
            TenancyRequirement.Operator required => TenancyWork.BeginOperator<TTenantId, TSeatId>(identity, required.Scope),
            TenancyRequirement.OperatorIn<TTenantId> required => TenancyWork.BeginOperatorIn<TTenantId, TSeatId>(required.Tenant, identity, required.Scope),
            _ => null,
        };
    }

    /// <summary>Whether <paramref name="requirement"/> is one of the operator cases, over any tenant id.</summary>
    internal static bool IsOperatorCase(AccessRequirement requirement)
        => requirement is TenancyRequirement.Operator
           || (requirement is TenancyRequirement && requirement.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(TenancyRequirement.OperatorIn<>));

    /// <summary>
    /// The tenant the request names beside the command, as tenant selection found it for an operator; the refusal it gave
    /// otherwise, <c>tenancy.tenant-required</c> for a request that names none.
    /// </summary>
    private static TTenantId TenantTheRequestNames()
    {
        var caller = TenancyCallers.Current<TTenantId, TSeatId>();
        return caller.NamedTenant ?? throw TenancyRefusals.Refuse(caller is { Kind: TenancyCallerKind.Nobody, Refusal: { } refusal } ? refusal : TenancyRefusals.TenantRequired);
    }

    /// <summary>
    /// Whether this flow is inside the operator work begun for <paramref name="request"/>: the request in hand, let through
    /// already, and the Tenancy caller recorded as an operator.
    /// </summary>
    private static bool InWorkBegunFor(IRequireAccess request)
        => RequestInHand.Current is { } hand
           && ReferenceEquals(hand.Request, request)
           && TenancyCallers.Current<TTenantId, TSeatId>() is { Actor: { Kind: TenancyActorKind.Operator } };
}
