using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.Access;

/// <summary>Registration of the access checks of a module.</summary>
public static class AccessCheckServiceCollectionExtensions
{
    /// <summary>
    /// Registers the set of checks the requests of one module are held to,
    /// <see cref="AccessChecks{TRequests}"/>, with whatever checks are added for the interface, before this
    /// call or after it:
    /// <code>
    /// services.AddAccessChecks&lt;IBillingRequest&gt;();
    /// </code>
    /// <see cref="AddAccessCheck{TRequests, TCheck}"/> calls it, so a module that adds a check has no need to.
    /// It is for whoever asks the set in front of the handlers: the pipeline behavior the generator writes for
    /// an interface marked <c>[AccessRequests]</c> registers the set with itself.
    /// <para>
    /// The set asks the core's <see cref="CallerAccessCheck"/> first, so the requirements that are about who is
    /// calling and nothing else work in every module, with no check added:
    /// <see cref="AccessRequirement.AllowAnonymous"/>, <see cref="AccessRequirement.SignedIn"/> and
    /// <see cref="AccessRequirement.RequiresSystemWork"/>. Who is calling is what the host's
    /// <see cref="ICallerAccessor"/> answers, and without one what <see cref="Callers.Begin"/> made current
    /// (<see cref="AmbientCallerAccessor"/>); system work counts only where trusted code began it. Anything else
    /// a request declares is stopped unless a check the module added decides it.
    /// </para>
    /// <para>
    /// Everything is registered per scope: the set, and <see cref="Checked{T}"/> for what a check keeps for a
    /// handler. Calling it more than once is harmless.
    /// </para>
    /// </summary>
    /// <typeparam name="TRequests">The module's request interface, which every command and query of the module implements.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddAccessChecks<TRequests>(this IServiceCollection services)
        where TRequests : class, IRequireAccess
    {
        ArgumentNullException.ThrowIfNull(services);

        // Made from the registrations kept for this interface alone, behind the core's check of who is calling. The
        // set's own constructor takes every check there is, so the container is never left to call it. No accessor
        // is registered here: one the host adds later, with TryAdd too, is the one asked.
        services.TryAddScoped(static provider => new AccessChecks<TRequests>(
            provider.GetServices<RegisteredAccessCheck<TRequests>>()
                .Select(static registered => registered.Check)
                .Prepend(new CallerAccessCheck(provider.GetService<ICallerAccessor>() ?? new AmbientCallerAccessor(provider.GetService<CallerOptions>())))));
        services.TryAddScoped(typeof(Checked<>));

        return services;
    }

    /// <summary>
    /// Adds <typeparamref name="TCheck"/> to the checks the requests of one module are held to: the set
    /// <see cref="AccessChecks{TRequests}"/> asks, in the order the checks were added.
    /// <code>
    /// services.AddAccessCheck&lt;IBillingRequest, BillingAccessCheck&gt;();
    /// </code>
    /// A package that ships requirements ships their check, and usually a registration of its own that calls
    /// this for the interface the module names. A check added for one interface is not asked about the requests
    /// of another: a module that uses the same check registers it for its own interface as well.
    /// <para>
    /// Everything is registered per scope: the check, unless it is registered already, the set, and
    /// <see cref="Checked{T}"/> for what a check keeps for a handler. Adding the same check for the same
    /// interface more than once is harmless.
    /// </para>
    /// </summary>
    /// <typeparam name="TRequests">The module's request interface, which every command and query of the module implements.</typeparam>
    /// <typeparam name="TCheck">The check, made by the container once per scope.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddAccessCheck<TRequests, TCheck>(this IServiceCollection services)
        where TRequests : class, IRequireAccess
        where TCheck : class, IAccessCheck
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<TCheck>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<RegisteredAccessCheck<TRequests>, RegisteredAccessCheck<TRequests, TCheck>>());

        return services.AddAccessChecks<TRequests>();
    }
}

/// <summary>
/// One check of the set registered for <typeparamref name="TRequests"/>. The container keeps the checks of a
/// module apart from those of every other by this type, since a check itself does not say whose it is.
/// </summary>
internal abstract class RegisteredAccessCheck<TRequests>
    where TRequests : class, IRequireAccess
{
    /// <summary>The check, as the scope made it.</summary>
    public abstract IAccessCheck Check { get; }
}

/// <summary>The registration of <typeparamref name="TCheck"/> for <typeparamref name="TRequests"/>: one per pair, however often it is added.</summary>
internal sealed class RegisteredAccessCheck<TRequests, TCheck>(TCheck check) : RegisteredAccessCheck<TRequests>
    where TRequests : class, IRequireAccess
    where TCheck : class, IAccessCheck
{
    /// <inheritdoc />
    public override IAccessCheck Check => check;
}
