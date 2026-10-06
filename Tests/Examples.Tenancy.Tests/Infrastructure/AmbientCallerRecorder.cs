using System.Collections.Concurrent;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// Stands in front of the host's caller accessor and remembers, each time it is asked who is calling while a
/// request is answered, which caller was ambient then: the caller the request began, rather than what the accessor
/// makes of the request. A route asks it, and so does every connection the request opens. It stands in front of
/// the seat directory as well, and remembers which Tenancy caller was current each time tenant selection looked
/// up a seat.
/// </summary>
/// <remarks>
/// What is asked outside a request is not remembered. The host asks there too: each outbox poller opens a
/// connection every few seconds, as the toolkit's system caller, in a flow of its own and at a moment no test
/// chooses. A test that reads what a request saw would otherwise read a poll that happened to fall inside it.
/// </remarks>
public sealed class AmbientCallerRecorder
{
    private readonly ConcurrentQueue<Caller?> _seen = new();
    private readonly ConcurrentQueue<ITenancyCaller?> _lookups = new();

    /// <summary>The ambient caller each time the accessor was asked since the last <see cref="Clear"/>, in order.</summary>
    public IReadOnlyList<Caller?> Seen => [.. _seen];

    /// <summary>The Tenancy caller current each time a seat was looked up in a tenant since the last <see cref="Clear"/>, in order.</summary>
    public IReadOnlyList<ITenancyCaller?> Lookups => [.. _lookups];

    /// <summary>Forgets what was seen so far.</summary>
    public void Clear()
    {
        _seen.Clear();
        _lookups.Clear();
    }

    /// <summary>
    /// Puts the recorder in front of the accessor and the seat directory the host registered. The host has
    /// registered both by the time a test's services run.
    /// </summary>
    public void AddTo(IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        var accessor = services.Last(descriptor => descriptor.ServiceType == typeof(ICallerAccessor));
        services.Remove(accessor);
        services.AddSingleton<ICallerAccessor>(provider => new Recording(
            (ICallerAccessor)Made(accessor, provider),
            provider.GetRequiredService<IHttpContextAccessor>(),
            _seen));

        var directory = services.Last(descriptor => descriptor.ServiceType == typeof(ISeatDirectory<TenantId, SeatId>));
        services.Remove(directory);
        services.Add(new ServiceDescriptor(
            typeof(ISeatDirectory<TenantId, SeatId>),
            provider => new RecordingDirectory((ISeatDirectory<TenantId, SeatId>)Made(directory, provider), _lookups),
            directory.Lifetime));
    }

    private static object Made(ServiceDescriptor descriptor, IServiceProvider provider)
        => descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);

    private sealed class Recording(ICallerAccessor inner, IHttpContextAccessor http, ConcurrentQueue<Caller?> seen) : ICallerAccessor
    {
        public Caller Current
        {
            get
            {
                // The request's own flow has its context; a poller's has none.
                if (http.HttpContext is not null)
                {
                    seen.Enqueue(Callers.Ambient);
                }

                return inner.Current;
            }
        }
    }

    private sealed class RecordingDirectory(ISeatDirectory<TenantId, SeatId> inner, ConcurrentQueue<ITenancyCaller?> lookups) : ISeatDirectory<TenantId, SeatId>
    {
        public Task<SeatOfCaller<TenantId, SeatId>?> FindAsync(Guid identity, string tenantSlug, CancellationToken cancellationToken)
        {
            lookups.Enqueue(TenancyCallers.Ambient);
            return inner.FindAsync(identity, tenantSlug, cancellationToken);
        }

        public Task<IReadOnlyList<SeatOfCaller<TenantId, SeatId>>> AllOfAsync(Guid identity, CancellationToken cancellationToken)
            => inner.AllOfAsync(identity, cancellationToken);

        public Task<IReadOnlyList<TView>> AllOfAsync<TSeat, TView>(Guid identity, Func<SeatOfCaller<TenantId, SeatId>, TSeat, TView> view, CancellationToken cancellationToken)
            where TSeat : class
            => inner.AllOfAsync(identity, view, cancellationToken);
    }
}
