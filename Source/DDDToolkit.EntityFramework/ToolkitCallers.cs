using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework;

/// <summary>
/// Who the toolkit's own background work runs as. Without <see cref="CallerOptions.RequireExplicitCallers"/>
/// nothing is begun, so a host's work runs exactly as it did: outside any caller its accessor answers the
/// system, and the handlers inherit whatever is current. With it, the toolkit's bookkeeping runs as the system
/// and the handlers it calls run with no caller, until something begins one for them.
/// </summary>
internal static class ToolkitCallers
{
    /// <summary>Whether the host requires every flow of work to say who it runs as.</summary>
    public static bool Required(IServiceProvider services)
        => services.GetService<CallerOptions>()?.RequireExplicitCallers == true;

    /// <summary>
    /// <see cref="Caller.System"/> around the toolkit's own bookkeeping, reading and marking outbox rows,
    /// deleting old ones, receiving from a transport, when explicit callers are required; otherwise nothing.
    /// The outbound conversions, the upcasters and the transport sinks run inside it too, a host's own
    /// included: they are part of publishing a row, not handlers of it.
    /// </summary>
    public static IDisposable? BeginBookkeeping(bool required) => required ? Callers.Begin(Caller.System) : null;

    /// <summary>No caller around a handler the toolkit calls, when explicit callers are required; otherwise nothing.</summary>
    public static IDisposable? BeginHandler(bool required) => required ? Callers.BeginNone() : null;
}
