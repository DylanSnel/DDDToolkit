using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// Decides Tenancy's requirements (<see cref="TenancyRequirement"/>) before a handler runs, for the requests of
/// the module it is registered for with <c>AddTenancyAccess</c>. The operator cases are the package's own check's,
/// which <c>AddTenancyAccess</c> registers beside this one.
/// </summary>
/// <remarks>
/// Every case but two reads nothing: who is calling is kept with the flow of work, and is asked each time. A
/// permission for the whole tenant, and a permission at a unit, are each asked in one statement, over
/// <typeparamref name="TContext"/>, the context of the module the check was registered for: Tenancy's own, or a
/// module's that maps Tenancy's read model. So no module is checked over another module's context. The statement runs on a context of its own, from the
/// context's factory where the application registered one, since the requests of one scope may be checked side
/// by side; otherwise on the scope's own context.
/// <para>
/// It keeps nothing between two requests and nothing during one. Internal: an application names
/// <c>AddTenancyAccess</c>, never the check.
/// </para>
/// </remarks>
/// <param name="access">Tenancy's access manager, asked about the current caller and for the queries over the context.</param>
/// <param name="services">The services of the scope the check runs in, for the context a permission is asked over.</param>
internal sealed class TenancyAccessCheck<TTenantId, TSeatId, TUnitId, TRoleId, TContext>(
    TenancyAccessManager<TTenantId, TSeatId, TUnitId, TRoleId> access,
    IServiceProvider services) : IAccessCheck
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TContext : DbContext
{
    /// <inheritdoc />
    public bool Decides(AccessRequirement requirement)
        => requirement is TenancyRequirement and not TenancyRequirement.Operator
           && !(requirement.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(TenancyRequirement.OperatorIn<>));

    /// <inheritdoc />
    /// <exception cref="RefusalException">The caller is not who the request requires.</exception>
    /// <exception cref="ArgumentException">The catalogue does not know the permission the request requires.</exception>
    /// <exception cref="InvalidOperationException">
    /// The requirement is not one of Tenancy's; or it requires a tenant and the caller is system work outside any.
    /// </exception>
    public async ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(request);

        switch (requirement)
        {
            case TenancyRequirement.InTenant:
                access.CurrentTenant.Require();
                break;

            case TenancyRequirement.AtTenant required:
                // The caller before anything is opened: nobody is refused as nobody, without a context made for it.
                access.CurrentTenant.Require();
                if (!await HoldsAtTenantAsync(required.Permission, cancellationToken).ConfigureAwait(false))
                {
                    throw TenancyRefusals.Refuse(TenancyRefusals.NotPermitted, ("Permission", required.Permission));
                }

                break;

            case TenancyRequirement.AtUnit<TUnitId> required:
                // As for the whole tenant: the caller first, then one statement.
                access.CurrentTenant.Require();
                if (!await HoldsAtAsync(required.Permission, required.Unit, cancellationToken).ConfigureAwait(false))
                {
                    throw TenancyRefusals.Refuse(TenancyRefusals.NotPermitted, ("Permission", required.Permission), ("Unit", required.Unit));
                }

                break;

            default:
                // A case of Tenancy's this check cannot decide: a unit known by another type than the application's units.
                throw new InvalidOperationException(
                    $"{request.GetType().Name} declares '{requirement}', which Tenancy's access check does not decide: the application's units are known by "
                    + $"{typeof(TUnitId).Name}. A requirement nothing checks lets nobody through.");
        }
    }

    /// <summary>One statement, on a context of its own where the application registered a factory for <typeparamref name="TContext"/>.</summary>
    private Task<bool> HoldsAtTenantAsync(string permission, CancellationToken cancellationToken)
        => AskAsync(queries => queries.HoldsAtTenantAsync(permission, cancellationToken), cancellationToken);

    /// <summary>One statement, on a context of its own where the application registered a factory for <typeparamref name="TContext"/>.</summary>
    private Task<bool> HoldsAtAsync(string permission, TUnitId unit, CancellationToken cancellationToken)
        => AskAsync(queries => queries.HoldsAtAsync(permission, unit, cancellationToken), cancellationToken);

    /// <summary>Asks Tenancy's queries in <typeparamref name="TContext"/>: a context of its own where the application registered a factory for it, and the scope's otherwise.</summary>
    private async Task<bool> AskAsync(Func<ITenancyQueries<TTenantId, TSeatId, TUnitId, TRoleId>, Task<bool>> ask, CancellationToken cancellationToken)
    {
        if (services.GetService<IDbContextFactory<TContext>>() is not { } contexts)
        {
            return await ask(QueriesOver(services.GetRequiredService<TContext>())).ConfigureAwait(false);
        }

        var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await ask(QueriesOver(context)).ConfigureAwait(false);
        }
    }

    /// <summary>Tenancy's queries for the current caller over <paramref name="context"/>, at this moment.</summary>
    private ITenancyQueries<TTenantId, TSeatId, TUnitId, TRoleId> QueriesOver(TContext context)
        => access.In(new EfTenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>(context), EfQueryExecutor.Instance);
}
