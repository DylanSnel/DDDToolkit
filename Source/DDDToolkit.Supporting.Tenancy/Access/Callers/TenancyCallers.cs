using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// The Tenancy caller of the current flow of work, which every use case, access question and tenant filter
/// reads. It is kept apart from the toolkit's own <c>Callers</c>: that one says which person or which system
/// is calling, this one which seat in which tenant, and a system caller there is never a system caller here.
/// <para>
/// It is stored without its id types, so a query filter that only compares the tenant can read it without
/// knowing the application's classes. It follows the flow the way an <see cref="AsyncLocal{T}"/> does: into
/// awaited calls and tasks started inside, not back out.
/// </para>
/// <para>
/// Nothing makes a caller up. Outside any scope there is none, which every reader treats as nobody:
/// <see cref="TenantSelection{TTenantId, TSeatId}"/> begins a seat for a request, and
/// <see cref="TenancyWork"/> begins system work on purpose.
/// </para>
/// </summary>
public static class TenancyCallers
{
    private static readonly AsyncLocal<ITenancyCaller?> AmbientCaller = new();

    /// <summary>The caller <see cref="Begin"/> made current for this flow of work, or <see langword="null"/> outside any.</summary>
    public static ITenancyCaller? Ambient => AmbientCaller.Value;

    /// <summary>
    /// Makes <paramref name="caller"/> the Tenancy caller for everything that runs until the result is
    /// disposed, and then the one before it again. Scopes nest.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="caller"/> is null.</exception>
    public static IDisposable Begin(ITenancyCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        var previous = AmbientCaller.Value;
        AmbientCaller.Value = caller;
        return new Scope(previous);
    }

    /// <summary>
    /// Makes nobody the Tenancy caller for everything that runs until the result is disposed, and then the one
    /// before it again: what runs inside acts in no tenant, whatever the flow of work around it began, as it
    /// would outside any scope. For a read that is no seat's and no tenant's, made in the middle of somebody's
    /// work: the tenant of the caller around it must not travel with it.
    /// </summary>
    public static IDisposable BeginNone()
    {
        var previous = AmbientCaller.Value;
        AmbientCaller.Value = null;
        return new Scope(previous);
    }

    /// <summary>
    /// The current caller with the application's ids; nobody, refused as not seated, outside any scope. A project that
    /// sees the application's classes asks the same closed over them, through the class named after the module that
    /// declares them, <c>TenantsTenancy.CurrentCaller()</c>. Code that needs only what kind of caller it is reads
    /// <see cref="Ambient"/>, which needs no id: none there is nobody, not seated.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current caller was made with other id types: two Tenancy registrations with different ids.</exception>
    public static TenancyCaller<TTenantId, TSeatId> Current<TTenantId, TSeatId>()
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        => Ambient switch
        {
            null => TenancyCaller<TTenantId, TSeatId>.Nobody(TenancyRefusals.NotSeated),
            TenancyCaller<TTenantId, TSeatId> caller => caller,
            var other => throw new InvalidOperationException(
                "The current Tenancy caller is a " + other.GetType().Name + ", not a TenancyCaller<" + typeof(TTenantId).Name + ", "
                + typeof(TSeatId).Name + ">: it was begun with other id types than the ones asked for."),
        };

    /// <summary>
    /// The tenant the current caller acts in, boxed, for a seat or system work in a tenant; <see langword="null"/>
    /// for nobody and for system work outside any tenant, which a tenant filter then matches with nothing.
    /// </summary>
    public static object? CurrentTenantOrNull()
        => Ambient is { Kind: TenancyCallerKind.Seat or TenancyCallerKind.SystemInTenant } caller ? caller.TenantId : null;

    /// <summary>Puts back the caller from before <see cref="Begin"/>, once.</summary>
    private sealed class Scope(ITenancyCaller? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            AmbientCaller.Value = previous;
        }
    }
}
