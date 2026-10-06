using DDDToolkit.EntityFramework.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>Switches the expert hold on for a context.</summary>
public static class MemberHoldDbContextOptionsExtensions
{
    /// <summary>
    /// Holds every save of this context that changes a resource with members to what the access check of the
    /// request in hand read of it (<see cref="MemberHoldInterceptor"/>): one changed since the check is a lost
    /// race, and one no check read, outside the application's own work, is refused. The handlers write nothing
    /// for it. One line, after <c>UseDDDToolkit</c>, or after <c>UseDDDToolkitCore</c> for a context given the
    /// toolkit's base alone, so the save it holds is the one the domain event handlers changed as well:
    /// <code>
    /// services.AddDbContext&lt;FilingContext&gt;((serviceProvider, options) =&gt; options
    ///     .UseNpgsql(connectionString)
    ///     .UseDDDToolkit(serviceProvider)
    ///     .UseMemberHolds(serviceProvider));
    /// </code>
    /// Without it, a command's change is checked as the default path checks it: its request's requirement before
    /// the handler, the version its caller named when it loads (<c>ExpectVersion</c>), the version it loaded at
    /// when it saves, the aggregate's invariants, and, where the database checks every row, the database.
    /// <para>
    /// The same call serves a context pool: the interceptor is one instance for the application, and the hold it
    /// asks for is the one of the request in hand in the flow that saves, whichever scope the context is of.
    /// </para>
    /// <para>
    /// It is no part that a registration brings and <c>UseDDDToolkit</c> puts on every context: a host switches
    /// it on, per context. Like the parts' own calls it adds nothing the options already have, so a second call
    /// gives the interceptor once.
    /// </para>
    /// </summary>
    /// <param name="options">The context's options.</param>
    /// <param name="services">
    /// The provider handed to the options callback, of <c>AddDbContext</c> or of a context pool: the interceptor
    /// is registered with the resources, as a singleton, so a pool's root provider serves as well as a scope's.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// No resource with members is registered in <paramref name="services"/>, or neither <c>UseDDDToolkit</c> nor
    /// <c>UseDDDToolkitCore</c> was called on <paramref name="options"/> before: the hold would then run before the
    /// domain event handlers, and what they change in the same save would be held to nothing.
    /// </exception>
    public static DbContextOptionsBuilder UseMemberHolds(this DbContextOptionsBuilder options, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);

        var interceptor = services.GetService<MemberHoldInterceptor>()
            ?? throw new InvalidOperationException(
                "UseMemberHolds found no resource with members to hold a save to. Register them first, with the registration generated for each, "
                + "services.AddDocumentMembership<TContext>(rules), of DDDToolkit.Supporting.Membership.EntityFramework.");

        // Interceptors run in the order they were added, and the domain event handlers change what a save writes as
        // it begins: the hold goes after them, so it holds the whole save.
        var added = options.Options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        if (!added.Any(static existing => existing is PublishDomainEventsInterceptor))
        {
            throw new InvalidOperationException(
                "UseMemberHolds comes after UseDDDToolkit, or after UseDDDToolkitCore: the domain event handlers change what a save writes as it begins, and the hold "
                + "holds the whole save, what they changed included. Configure the context with options.UseDDDToolkit(serviceProvider).UseMemberHolds(serviceProvider).");
        }

        // Asked for twice, the hold is there once, as each part's own call adds nothing the options already have.
        return added.Any(static existing => existing is MemberHoldInterceptor) ? options : options.AddInterceptors(interceptor);
    }
}
