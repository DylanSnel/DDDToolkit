using System.Collections.Concurrent;
using DDDToolkit.Composition;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DDDToolkit.EntityFramework;

/// <summary>
/// What <c>UseDDDToolkit</c> adds beyond the toolkit's own interceptors: every part the host's registrations brought
/// for a context's options (<see cref="ContextPart{TBuilder}"/> of <see cref="DbContextOptionsBuilder"/>), and the
/// one line that says, once per context type, which parts a context was given.
/// </summary>
/// <remarks>
/// The line is there for an application that upgrades within 3.x. <c>UseDDDToolkit</c> used to add the toolkit's
/// interceptors and nothing else, and now adds row level security as well where a registration brought it. A context
/// that was left without it on purpose is given it, and nothing in its code says so: the line does, by the context's
/// name, with the way back. It is information rather than a warning, because it is also what every context of a host
/// that relies on the one call is told, rightly. It is written once per context type, however many scopes build the
/// options, so the log is not filled with it.
/// </remarks>
internal sealed class ContextParts
{
    /// <summary>The context types the line was written for.</summary>
    private readonly ConcurrentDictionary<Type, bool> _reported = new();

    /// <summary>
    /// Applies to <paramref name="options"/> every part the registrations brought that belongs on it, in order, and
    /// writes the line for its context type the first time that type is given any.
    /// </summary>
    /// <param name="options">The context's options, which already hold the toolkit's interceptors.</param>
    /// <param name="services">The provider handed to the options callback.</param>
    public static void Apply(DbContextOptionsBuilder options, IServiceProvider services)
    {
        if (services.GetService<ContextParts<DbContextOptionsBuilder>>() is not { } parts)
        {
            return;
        }

        var applied = parts.ApplyTo(options, services);
        if (applied.Count > 0 && services.GetService<ContextParts>() is { } report)
        {
            report.Report(options.Options.ContextType, applied, services);
        }
    }

    private void Report(Type contextType, IReadOnlyList<ContextPart<DbContextOptionsBuilder>> applied, IServiceProvider services)
    {
        if (!_reported.TryAdd(contextType, true) || services.GetService<ILoggerFactory>() is not { } loggers)
        {
            return;
        }

        loggers.CreateLogger<ContextParts>().LogInformation(
            "UseDDDToolkit gave '{Context}' what the host's registrations bring, after the toolkit's own interceptors: {Parts}. " +
            "A context that should do without one is configured with UseDDDToolkitCore and the Use... calls of the parts it does want.",
            contextType.Name,
            string.Join(", ", applied.Select(part => part.Name)));
    }
}
