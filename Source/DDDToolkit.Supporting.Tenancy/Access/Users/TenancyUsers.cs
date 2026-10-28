using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// The Tenancy user of the current flow of work, which every manager, access question and tenant filter
/// reads. It is kept apart from the toolkit's current user, <c>ICurrentUser</c>: that one says which person or which system
/// is calling, this one which seat in which tenant, and system work there is never system work here.
/// <para>
/// It is stored without its id types, so a query filter that only compares the tenant can read it without
/// knowing the application's classes. It follows the flow the way an <see cref="AsyncLocal{T}"/> does: into
/// awaited calls and tasks started inside, not back out.
/// </para>
/// <para>
/// Nothing makes a user up. Outside any scope there is none, which every reader treats as nobody:
/// <see cref="TenantSelection{TTenantId, TSeatId}"/> begins a seat for a request, and
/// <see cref="TenancyWork"/> begins system work on purpose.
/// </para>
/// </summary>
public static class TenancyUsers
{
    private static readonly AsyncLocal<ITenancyUser?> AmbientUser = new();

    /// <summary>The user <see cref="Begin"/> made current for this flow of work, or <see langword="null"/> outside any.</summary>
    public static ITenancyUser? Ambient => AmbientUser.Value;

    /// <summary>
    /// Makes <paramref name="user"/> the Tenancy user for everything that runs until the result is
    /// disposed, and then the one before it again. Scopes nest.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="user"/> is null.</exception>
    public static IDisposable Begin(ITenancyUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var previous = AmbientUser.Value;
        AmbientUser.Value = user;
        return new Scope(previous);
    }

    /// <summary>
    /// Makes nobody the Tenancy user for everything that runs until the result is disposed, and then the one
    /// before it again: what runs inside acts in no tenant, whatever the flow of work around it began, as it
    /// would outside any scope. For a read that is no seat's and no tenant's, made in the middle of somebody's
    /// work: the tenant of the user around it must not travel with it.
    /// </summary>
    public static IDisposable BeginNone()
    {
        var previous = AmbientUser.Value;
        AmbientUser.Value = null;
        return new Scope(previous);
    }

    /// <summary>
    /// The current user with the application's ids; nobody, refused as not seated, outside any scope. An application
    /// asks the access manager its code takes, <c>access.CurrentTenant</c>, which reads this closed over its ids. Code that
    /// needs only what kind of user it is reads <see cref="Ambient"/>, which needs no id: none there is nobody, not seated.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current user was made with other id types: two Tenancy registrations with different ids.</exception>
    public static TenancyUser<TTenantId, TSeatId> Current<TTenantId, TSeatId>()
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        => Ambient switch
        {
            null => TenancyUser<TTenantId, TSeatId>.Nobody(TenancyRefusals.NotSeated),
            TenancyUser<TTenantId, TSeatId> user => user,
            var other => throw new InvalidOperationException(
                "The current Tenancy user is a " + other.GetType().Name + ", not a TenancyUser<" + typeof(TTenantId).Name + ", "
                + typeof(TSeatId).Name + ">: it was begun with other id types than the ones asked for."),
        };

    /// <summary>
    /// The tenant the current user acts in, boxed, for a seat or system work in a tenant; <see langword="null"/>
    /// for nobody and for system work outside any tenant, which a tenant filter then matches with nothing.
    /// </summary>
    public static object? CurrentTenantOrNull()
        => Ambient is { Kind: TenancyUserKind.Seat or TenancyUserKind.SystemInTenant } user ? user.TenantId : null;

    /// <summary>Puts back the user from before <see cref="Begin"/>, once.</summary>
    private sealed class Scope(ITenancyUser? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            AmbientUser.Value = previous;
        }
    }
}
