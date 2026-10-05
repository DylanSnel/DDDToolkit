using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// Decides Tenancy's requirements (<see cref="TenancyRequirement"/>) before a handler runs, for the requests of
/// the module it is registered for with <c>AddTenancyAccess</c>.
/// </summary>
/// <remarks>
/// Every case but two reads nothing: who is calling is kept with the flow of work, and is asked each time. A
/// key for the whole tenant, and a key at a unit, are each asked in one statement, over
/// <typeparamref name="TContext"/>, the context of the module the check was registered for: Tenancy's own, or a
/// module's that maps Tenancy's read model. So no module is checked over another module's context. The statement runs on a context of its own, from the
/// context's factory where the application registered one, since the requests of one scope may be checked side
/// by side; otherwise on the scope's own context.
/// <para>
/// It keeps nothing between two requests and nothing during one. Internal: an application names
/// <c>AddTenancyAccess</c>, never the check.
/// </para>
/// </remarks>
/// <param name="answers">Tenancy's answers about the current caller.</param>
/// <param name="options">Which token roles are operators'.</param>
/// <param name="callers">Who is calling, as the toolkit says: what says whether the caller is an operator.</param>
/// <param name="services">The services of the scope the check runs in, for the context a key is asked over.</param>
internal sealed class TenancyAccessCheck<TTenantId, TSeatId, TUnitId, TRoleId, TContext>(
    ITenancyAnswers<TTenantId, TSeatId, TUnitId, TRoleId> answers,
    TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId> options,
    ICallerAccessor callers,
    IServiceProvider services) : IAccessCheck
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TContext : DbContext
{
    /// <inheritdoc />
    public bool Decides(AccessRequirement requirement) => requirement is TenancyRequirement;

    /// <inheritdoc />
    /// <exception cref="RefusalException">The caller is not who the request requires.</exception>
    /// <exception cref="ArgumentException">The catalogue does not know the key the request requires.</exception>
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
                answers.RequireTenant();
                break;

            case TenancyRequirement.ForTheWholeTenant required:
                // The caller before anything is opened: nobody is refused as nobody, without a context made for it.
                answers.RequireTenant();
                if (!await HoldsForTheWholeTenantAsync(required.Key, cancellationToken).ConfigureAwait(false))
                {
                    throw TenancyRefusals.Of(TenancyRefusals.NotPermitted, ("Key", required.Key));
                }

                break;

            case TenancyRequirement.AtUnit<TUnitId> required:
                // As for the whole tenant: the caller first, then one statement.
                answers.RequireTenant();
                if (!await HoldsAtAsync(required.Key, required.Unit, cancellationToken).ConfigureAwait(false))
                {
                    throw TenancyRefusals.Of(TenancyRefusals.NotPermitted, ("Key", required.Key), ("Unit", required.Unit));
                }

                break;

            case TenancyRequirement.Operator:
                // The token's own role, never the Tenancy caller: an operator is nobody in every tenant.
                if (!options.IsOperator(callers.Current))
                {
                    throw TenancyRefusals.Of(TenancyRefusals.OperatorsOnly);
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
    private Task<bool> HoldsForTheWholeTenantAsync(string key, CancellationToken cancellationToken)
        => AskAsync(questions => questions.HoldsTenantWideAsync(key, cancellationToken), cancellationToken);

    /// <summary>One statement, on a context of its own where the application registered a factory for <typeparamref name="TContext"/>.</summary>
    private Task<bool> HoldsAtAsync(string key, TUnitId unit, CancellationToken cancellationToken)
        => AskAsync(questions => questions.HoldsAtAsync(key, unit, cancellationToken), cancellationToken);

    /// <summary>Asks Tenancy's questions over <typeparamref name="TContext"/>: a context of its own where the application registered a factory for it, and the scope's otherwise.</summary>
    private async Task<bool> AskAsync(Func<ITenancyQuestions<TTenantId, TSeatId, TUnitId, TRoleId>, Task<bool>> ask, CancellationToken cancellationToken)
    {
        if (services.GetService<IDbContextFactory<TContext>>() is not { } contexts)
        {
            return await ask(answers.Over(services.GetRequiredService<TContext>())).ConfigureAwait(false);
        }

        var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await ask(answers.Over(context)).ConfigureAwait(false);
        }
    }
}
