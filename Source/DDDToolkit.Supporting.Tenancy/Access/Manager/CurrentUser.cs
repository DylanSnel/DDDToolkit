using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Who acts: a signed-in user, somebody who has not signed in, or system work. It is a view over the toolkit's own
/// caller, which the host says, and it reads that caller each time it is asked. So inside
/// <c>access.BeginSystem()</c> it answers system work, and after the <c>using</c> the user again.
/// <code>
/// var admin = access.CurrentUser.Id
///     ?? throw new InvalidOperationException("[SignedIn] lets only a signed-in user through.");
/// </code>
/// </summary>
public sealed class CurrentUser
{
    private readonly IServiceProvider _services;
    private ICallerAccessor? _callers;

    /// <summary>A view over what the host's <see cref="ICallerAccessor"/> among <paramref name="services"/> answers.</summary>
    internal CurrentUser(IServiceProvider services) => _services = services;

    /// <summary>
    /// The signed-in user's id, the <c>sub</c> of their token. <see langword="null"/> for system work, for somebody who
    /// has not signed in, and for a token whose <c>sub</c> is no <see cref="Guid"/>.
    /// </summary>
    /// <exception cref="NoCallerException">Nobody is calling, and the host requires every flow of work to say who it runs as.</exception>
    public Guid? Id => IsSystem ? null : Caller.UserId;

    /// <summary>Whether a user signed in.</summary>
    /// <exception cref="NoCallerException">Nobody is calling, and the host requires every flow of work to say who it runs as.</exception>
    public bool IsAuthenticated => Caller.Kind == CallerKind.User;

    /// <summary>
    /// Whether this is system work: the application's own, or work begun on purpose, such as
    /// <c>access.BeginSystemIn(tenant)</c> or an operator's command.
    /// </summary>
    /// <exception cref="NoCallerException">Nobody is calling, and the host requires every flow of work to say who it runs as.</exception>
    public bool IsSystem => Caller.Kind is CallerKind.System or CallerKind.SystemIn;

    /// <summary>
    /// The role the user's token carries, <c>authenticated</c> or <c>anon</c>, or a role of its own that the identity
    /// provider gave the user. <see langword="null"/> for system work, and for a token without a role claim.
    /// </summary>
    /// <exception cref="NoCallerException">Nobody is calling, and the host requires every flow of work to say who it runs as.</exception>
    public string? Role => IsSystem ? null : Caller.Role;

    /// <summary>The toolkit's caller now, as the host's accessor answers it.</summary>
    private Caller Caller => (_callers ??= _services.GetService<ICallerAccessor>() ?? new AmbientCallerAccessor()).Current;
}
