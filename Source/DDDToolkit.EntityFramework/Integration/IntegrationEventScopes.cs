using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;

namespace DDDToolkit.EntityFramework.Integration;

/// <summary>The <see cref="IntegrationEventScope"/>s the toolkit ships.</summary>
public static class IntegrationEventScopes
{
    /// <summary>
    /// Runs the handler, its inbox read and its save as the application itself, <see cref="Caller.System"/>,
    /// which on Postgres is the role the application logged in as, past row level security.
    /// <para>
    /// Where the host requires explicit callers (<see cref="CallerServiceCollectionExtensions.RequireExplicitCallers"/>)
    /// this is the only way a module's handlers run as the system, so the choice is written down where the
    /// handlers are registered: <c>module.Around(IntegrationEventScopes.System)</c>. Prefer a scope that
    /// begins a caller the policies hold, such as <c>Caller.SystemIn("shipping")</c>, where there is one.
    /// </para>
    /// </summary>
    public static IntegrationEventScope System { get; } = static (_, _, _) => Callers.Begin(Caller.System);
}
