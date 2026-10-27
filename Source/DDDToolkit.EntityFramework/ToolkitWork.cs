using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework;

/// <summary>
/// Who the toolkit's own background work runs as. Without <see cref="CurrentUserOptions.RequireExplicitCurrentUser"/>
/// nothing is begun, so a host's work runs as it always did: where nothing was begun, the current user answers
/// system work, and the handlers inherit whatever is current. With it, the toolkit's bookkeeping runs as system work
/// and the handlers it calls run with nobody begun, until something begins a user for them.
/// </summary>
internal static class ToolkitWork
{
    /// <summary>Begins for the flow of work. What it begins does not depend on the instance.</summary>
    private static readonly ICurrentUser Users = new CurrentUser();

    /// <summary>Whether the host requires every flow of work to say who it runs as.</summary>
    public static bool Required(IServiceProvider services)
        => services.GetService<CurrentUserOptions>()?.RequireExplicitCurrentUser == true;

    /// <summary>
    /// System work around the toolkit's own bookkeeping, reading and marking outbox rows, deleting old ones, receiving
    /// from a transport, when the host requires every flow of work to begin its user; otherwise nothing. The outbound
    /// conversions, the upcasters and the transport sinks run inside it too, a host's own included: they are part of
    /// publishing a row, not handlers of it.
    /// </summary>
    public static IDisposable? BeginBookkeeping(bool required) => required ? Users.BeginSystem() : null;

    /// <summary>Nobody begun around a handler the toolkit calls, when the host requires a begun user; otherwise nothing.</summary>
    public static IDisposable? BeginHandler(bool required) => required ? Users.BeginNone() : null;
}
