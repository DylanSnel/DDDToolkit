using System.Reflection;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>A command or a query the host can handle, as its container knows it.</summary>
/// <param name="Type">The request's type.</param>
/// <param name="Response">What its handler answers with; <see cref="Unit"/> for a command that answers nothing.</param>
/// <param name="Handler">The class that handles it.</param>
/// <param name="IsQuery">Whether it is a query; a command otherwise.</param>
public sealed record HandledRequest(Type Type, Type Response, Type Handler, bool IsQuery)
{
    /// <summary>The module the request is of: the third part of its namespace.</summary>
    public string Module => Type.Namespace!.Split('.')[2];

    /// <inheritdoc />
    public override string ToString() => Type.Name;
}

/// <summary>
/// What the host registers, read from the host itself: every service of its container, and from them every
/// command and query it can handle. The architecture tests take their lists from here, so a request, a handler or
/// a service added to any module is held to the rules without being added to a test.
/// </summary>
/// <remarks>
/// The host is built once, without a database (<see cref="SampleFactory.WithoutDatabase"/>), and stopped again:
/// only its registrations are kept. They are copied before that host takes out the hosted services it cannot
/// start, so every hosted service the host registers is in the list, the start-up checks, the seeding and the
/// outbox pollers included.
/// </remarks>
public static class HostRegistrations
{
    private static readonly Lazy<IReadOnlyList<ServiceDescriptor>> Read = new(() =>
    {
        ServiceDescriptor[] registered = [];
        using var sample = SampleFactory.WithoutDatabase(services => registered = [.. services]);
        _ = sample.Server;
        return registered;
    });

    /// <summary>Every registration of the host, in the order it was made.</summary>
    public static IReadOnlyList<ServiceDescriptor> All => Read.Value;

    /// <summary>
    /// Every command and query the host handles: the mediator registers each handler under the closed handler
    /// interface of its request, which names the request and its response.
    /// </summary>
    public static IReadOnlyList<HandledRequest> Requests { get; } =
    [
        .. All
            .Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType && IsRequestHandler(descriptor.ServiceType.GetGenericTypeDefinition()))
            .Select(descriptor => new HandledRequest(
                descriptor.ServiceType.GetGenericArguments()[0],
                descriptor.ServiceType.GetGenericArguments()[1],
                HandlerOf(descriptor),
                descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
            .DistinctBy(request => request.Type)
            .OrderBy(request => request.Type.FullName, StringComparer.Ordinal),
    ];

    /// <summary>
    /// Every type the host registers of the sample, the toolkit, the Tenancy package or the mediator, as a service
    /// or as what implements one, a constructed type by its definition: what a route could ask the container for
    /// in place of sending a request. The sender is left out: a route may take that, and nothing else.
    /// </summary>
    public static IReadOnlySet<Type> Services { get; } = All
        .Where(descriptor => !descriptor.IsKeyedService)
        .SelectMany(descriptor => new[] { descriptor.ServiceType, descriptor.ImplementationType, descriptor.ImplementationInstance?.GetType() })
        .OfType<Type>()
        .Select(type => type.IsGenericType ? type.GetGenericTypeDefinition() : type)
        .Where(type => IsOurs(type.Assembly) && type != typeof(ISender))
        .ToHashSet();

    /// <summary>The handler interfaces of the mediator a request's handler is registered under.</summary>
    public static bool IsRequestHandler(Type definition)
        => definition == typeof(ICommandHandler<,>) || definition == typeof(IQueryHandler<,>) || definition == typeof(IRequestHandler<,>);

    /// <summary>Whether an assembly is the sample's, the toolkit's, the Tenancy package's or the mediator's.</summary>
    private static bool IsOurs(Assembly assembly)
        => assembly.GetName().Name is { } name
            && (name.StartsWith("Examples.Tenancy.", StringComparison.Ordinal) || name.StartsWith("DDDToolkit", StringComparison.Ordinal) || SampleLayout.IsMediator(name));

    /// <summary>
    /// The class behind a handler registration. The mediator registers a handler's interface with the handler's
    /// type, or with a factory that asks the container for it; either way the class is the one registered under
    /// its own type that implements the interface.
    /// </summary>
    private static Type HandlerOf(ServiceDescriptor descriptor)
        => descriptor.ImplementationType
            ?? All.Select(candidate => candidate.ServiceType)
                .First(candidate => candidate is { IsClass: true, IsAbstract: false } && descriptor.ServiceType.IsAssignableFrom(candidate));
}
