using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// Tells a record that keeps who acted, an event log for one, what Tenancy knows of the caller: a seat as the seat,
/// an operator by its verified identity, the application's own work as the system with its scope, and a link's
/// token as the seat it stands for (<see cref="TenancyCaller{TTenantId, TSeatId}.Actor"/>). Where there is no
/// Tenancy caller, or the caller is nobody, the accessor it wraps answers: the toolkit's own caller.
/// <para>
/// <c>AddTenancy</c> puts it around the accessor that was registered before it, or around the toolkit's default.
/// It is a singleton that reads the ambient Tenancy caller each time it is asked, keeps nothing between two
/// answers and takes no scoped service, so it serves a context taken from a pool as it serves any other.
/// </para>
/// </summary>
/// <param name="inner">The accessor that answers where Tenancy has no actor to name.</param>
public sealed class TenancyActedByAccessor(IActedByAccessor inner) : IActedByAccessor
{
    private readonly IActedByAccessor _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <inheritdoc />
    public ActedBy Current => TenancyCallers.Ambient?.Actor is { } actor ? actor.ToActedBy() : _inner.Current;

    /// <summary>
    /// Puts this accessor around the one <paramref name="services"/> hold, the last one registered, or around the
    /// toolkit's default when they hold none. Once: a second call finds its own and leaves it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The accessor registered before is a scoped service.</exception>
    internal static void Decorate(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(Decorated)))
        {
            return;
        }

        var registered = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IActedByAccessor) && !descriptor.IsKeyedService);

        // This accessor is one for the application, and makes the one it wraps once, with the application's own
        // services: a scoped one would be made outside any scope, and answer for whatever it was first given.
        if (registered is { Lifetime: ServiceLifetime.Scoped })
        {
            throw new InvalidOperationException(
                $"{nameof(IActedByAccessor)} is registered as a scoped service, and AddTenancy puts {nameof(TenancyActedByAccessor)} around it as a singleton, " +
                "which would make it once, outside any scope. Register it as a singleton that reads who is acting each time it is asked, " +
                "from what follows the flow of work: the ambient caller, or the current request.");
        }

        services.AddSingleton<Decorated>();

        if (registered is not null)
        {
            services.Remove(registered);
        }

        services.AddSingleton<IActedByAccessor>(provider => new TenancyActedByAccessor(InnerOf(provider, registered)));
    }

    /// <summary>The accessor that was registered before, made as its registration says; the toolkit's default without one.</summary>
    private static IActedByAccessor InnerOf(IServiceProvider provider, ServiceDescriptor? registered)
        => registered switch
        {
            null => new CallerActedByAccessor(provider.GetService<ICallerAccessor>()),
            { ImplementationInstance: IActedByAccessor instance } => instance,
            { ImplementationFactory: { } factory } => (IActedByAccessor)factory(provider),
            _ => (IActedByAccessor)ActivatorUtilities.CreateInstance(provider, registered.ImplementationType!),
        };

    /// <summary>Marks a service collection whose accessor is wrapped already.</summary>
    private sealed class Decorated;
}
