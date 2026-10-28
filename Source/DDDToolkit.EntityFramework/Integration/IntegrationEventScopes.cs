using DDDToolkit.Access;

namespace DDDToolkit.EntityFramework.Integration;

/// <summary>The <see cref="IntegrationEventScope"/>s the toolkit ships.</summary>
public static class IntegrationEventScopes
{
    /// <summary>Begins system work for the flow of work. What it begins does not depend on the instance.</summary>
    private static readonly AccessManager Access = new(new CurrentUser());

    /// <summary>
    /// Runs the handler, its inbox read and its save as system work without a scope, which on Postgres is the host's
    /// system role, past row level security.
    /// <para>
    /// Where the host requires every flow of work to begin its user
    /// (<see cref="CurrentUserServiceCollectionExtensions.RequireExplicitCurrentUser"/>), this is the only way a module's
    /// handlers run as system work without a scope. So the choice is written down where the handlers are registered:
    /// <c>module.Around(IntegrationEventScopes.System)</c>. Prefer system work in the module's scope, which the policies
    /// hold, where there is one: <c>module.Around((services, message, contract) => services.GetRequiredService&lt;AccessManager&gt;().BeginSystem("shipping"))</c>.
    /// </para>
    /// </summary>
    public static IntegrationEventScope System { get; } = static (_, _, _) => Access.BeginSystem();
}
