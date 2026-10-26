using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Interceptors;

/// <summary>
/// Which scope's services the in-process handlers of a context's domain events are given, remembered per
/// context <em>and per rental</em>.
/// <para>
/// A context from a pool outlives every scope that uses it: the same instance serves one request, goes
/// back, and serves the next. So the scope cannot be kept on the context, and nothing tells the toolkit
/// when a context goes back. What ends a binding is the context's lease number
/// (<see cref="DbContextId.Lease"/>), which Entity Framework raises by one every time the instance is
/// rented: a binding made in an earlier rental is simply not found in the next. A context that is not
/// pooled has lease 0 for as long as it lives, so a binding holds until it is disposed.
/// </para>
/// <para>
/// The scope is held weakly. The table keeps an entry for as long as the context lives, which for a pooled
/// one is as long as the application runs, and a scope that ended must not be kept alive by the context it
/// once used.
/// </para>
/// <para>
/// What is bound is whatever provider the caller was handed, and that need not be the scope itself: a
/// container may give every registration a wrapper of its own that nothing else refers to. So the scope is
/// asked to keep it (<see cref="ScopeMarker.Keep"/>): the provider then lives exactly as long as the scope
/// does, and the binding with it.
/// </para>
/// </summary>
internal static class ScopeBinding
{
    private static readonly ConditionalWeakTable<DbContext, Rental> ByContext = new();

    /// <summary>Binds the rental <paramref name="context"/> is in now to <paramref name="services"/>, in place of any earlier binding.</summary>
    public static void Set(DbContext context, IServiceProvider services)
    {
        Marker(services)?.Keep(services);
        ByContext.AddOrUpdate(context, new Rental(context.ContextId.Lease, new WeakReference<IServiceProvider>(services)));
    }

    /// <summary>
    /// The services bound to the rental <paramref name="context"/> is in now; <see langword="null"/> when
    /// nothing was bound, when the binding is an earlier rental's, and when the scope is gone.
    /// </summary>
    public static IServiceProvider? Find(DbContext context)
        => ByContext.TryGetValue(context, out var rental)
           && rental.Lease == context.ContextId.Lease
           && rental.Services.TryGetTarget(out var services)
            ? services
            : null;

    /// <summary>
    /// The marker of the scope <paramref name="services"/> resolves from; <see langword="null"/> where it was
    /// never registered, and for the application's root services where the container validates scopes. The
    /// provider is then held weakly alone, as the caller's own reference keeps it.
    /// </summary>
    private static ScopeMarker? Marker(IServiceProvider services)
    {
        try
        {
            return services.GetService(typeof(ScopeMarker)) as ScopeMarker;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record Rental(int Lease, WeakReference<IServiceProvider> Services);
}

/// <summary>
/// A scoped service that exists to be asked for: resolving it from the application's root services fails
/// where scopes are validated, which is how <see cref="PublishDomainEventsInterceptor"/> tells a provider
/// that outlives every scope from a scope's own before it hands it to a handler.
/// <para>
/// The scope keeps its marker until it ends, so the marker is also where a provider bound to a context of
/// that scope is kept alive (<see cref="ScopeBinding"/>).
/// </para>
/// </summary>
internal sealed class ScopeMarker
{
    private readonly Lock _gate = new();
    private readonly List<IServiceProvider> _kept = [];

    /// <summary>Keeps <paramref name="services"/> alive for as long as this marker's scope lives. Keeping it again does nothing.</summary>
    public void Keep(IServiceProvider services)
    {
        lock (_gate)
        {
            if (!_kept.Exists(kept => ReferenceEquals(kept, services)))
            {
                _kept.Add(services);
            }
        }
    }
}
