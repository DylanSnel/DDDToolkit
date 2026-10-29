using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Access;

public abstract partial record AccessRequirement
{
    /// <summary>
    /// What <see cref="Own"/> answers: the request is about the caller's own <typeparamref name="TValue"/>,
    /// <see cref="Value"/>. Decided by the core's <see cref="CurrentUserAccessCheck"/>, which compares the value with what
    /// the <see cref="IOwnedByCurrentUser{TValue}"/> registered for <typeparamref name="TValue"/> says is the caller's.
    /// </summary>
    /// <typeparam name="TValue">What the request names: the caller's identity, a seat, a user id of the application's.</typeparam>
    [AccessCheckRegistration("services.AddOwnedByCurrentUser<TValue, THandle>(), with a small IOwnedByCurrentUser<TValue> that says what the caller's own value of the type is; a package registers its own (Tenancy's AddTenancy for its seat id), and a Guid, the caller's verified identity, needs none")]
    public sealed record Owned<TValue> : AccessRequirement, IOwnedRequirement
        where TValue : struct, IEquatable<TValue>
    {
        /// <summary>Made by <see cref="Own"/>, the one spelling a request writes.</summary>
        /// <param name="value">The value the request names.</param>
        internal Owned(TValue value) => Value = value;

        /// <summary>The value the request names, which the caller's own is compared with.</summary>
        public TValue Value { get; }

        /// <inheritdoc />
        bool IOwnedRequirement.CanBeAnswered(IServiceProvider? services) => OwnHandle<TValue>.In(services) is not null;

        /// <inheritdoc />
        bool IOwnedRequirement.IsTheCurrentUsers(ICurrentUser user, IServiceProvider? services)
            => (OwnHandle<TValue>.In(services) ?? throw new InvalidOperationException(
                    $"Nothing says what the caller's own {WrittenTypeNames.Of(typeof(TValue))} is. Register an IOwnedByCurrentUser<{WrittenTypeNames.Of(typeof(TValue))}> with services.AddOwnedByCurrentUser<{WrittenTypeNames.Of(typeof(TValue))}, THandle>(); a requirement nothing checks lets nobody through."))
                .Find(user) is { } own && own.Equals(Value);
    }
}

/// <summary>The core's view of <see cref="AccessRequirement.Owned{TValue}"/>, whatever the value's type, so its check reads no type argument.</summary>
internal interface IOwnedRequirement
{
    /// <summary>Whether something says what the caller's own value of the requirement's type is.</summary>
    /// <param name="services">The scope's services, where a handle is registered; <see langword="null"/> for none.</param>
    bool CanBeAnswered(IServiceProvider? services);

    /// <summary>Whether the requirement's value is the caller's own.</summary>
    /// <param name="user">Who is calling.</param>
    /// <param name="services">The scope's services, where a handle is registered; <see langword="null"/> for none.</param>
    /// <exception cref="InvalidOperationException">Nothing says what the caller's own value of the type is.</exception>
    bool IsTheCurrentUsers(ICurrentUser user, IServiceProvider? services);
}

/// <summary>
/// The core's own <see cref="IOwnedByCurrentUser{TValue}"/>: the caller's verified identity, the id its token names, for a
/// request about the caller itself. Nobody else has one: a caller who did not sign in, and the application's own work.
/// </summary>
internal sealed class CurrentUserIdentity : IOwnedByCurrentUser<Guid>
{
    /// <summary>The one there is: it keeps nothing.</summary>
    public static CurrentUserIdentity Instance { get; } = new();

    /// <inheritdoc />
    public Guid? Find(ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Id;
    }
}
