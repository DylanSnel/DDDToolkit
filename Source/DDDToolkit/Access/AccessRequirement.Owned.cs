using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Access;

public abstract partial record AccessRequirement
{
    /// <summary>
    /// What <see cref="Own"/> answers: the request is about the caller's own <typeparamref name="TValue"/>,
    /// <see cref="Value"/>. Decided by the core's <see cref="CallerAccessCheck"/>, which compares the value with what
    /// the <see cref="ICallersOwn{TValue}"/> registered for <typeparamref name="TValue"/> says is the caller's.
    /// </summary>
    /// <typeparam name="TValue">What the request names: the caller's identity, a seat, a user id of the application's.</typeparam>
    [AccessCheckRegistration("services.AddCallersOwn<TValue, TCallersOwn>(), with a small ICallersOwn<TValue> that says what the caller's own value of the type is; a package registers its own (Tenancy's AddTenancy for its seat id), and a Guid, the caller's verified identity, needs none")]
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
        bool IOwnedRequirement.IsTheCallers(Caller caller, IServiceProvider? services)
            => (OwnHandle<TValue>.In(services) ?? throw new InvalidOperationException(
                    $"Nothing says what the caller's own {WrittenTypeNames.Of(typeof(TValue))} is. Register an ICallersOwn<{WrittenTypeNames.Of(typeof(TValue))}> with services.AddCallersOwn<{WrittenTypeNames.Of(typeof(TValue))}, TCallersOwn>(); a requirement nothing checks lets nobody through."))
                .Find(caller) is { } own && own.Equals(Value);
    }
}

/// <summary>The core's view of <see cref="AccessRequirement.Owned{TValue}"/>, whatever the value's type, so its check reads no type argument.</summary>
internal interface IOwnedRequirement
{
    /// <summary>Whether something says what the caller's own value of the requirement's type is.</summary>
    /// <param name="services">The scope's services, where a handle is registered; <see langword="null"/> for none.</param>
    bool CanBeAnswered(IServiceProvider? services);

    /// <summary>Whether the requirement's value is the caller's own.</summary>
    /// <param name="caller">Who is calling.</param>
    /// <param name="services">The scope's services, where a handle is registered; <see langword="null"/> for none.</param>
    /// <exception cref="InvalidOperationException">Nothing says what the caller's own value of the type is.</exception>
    bool IsTheCallers(Caller caller, IServiceProvider? services);
}

/// <summary>
/// The core's own <see cref="ICallersOwn{TValue}"/>: the caller's verified identity, the id its token names, for a
/// request about the caller itself. Nobody else has one: a caller who did not sign in, and the application's own work.
/// </summary>
internal sealed class CallersIdentity : ICallersOwn<Guid>
{
    /// <summary>The one there is: it keeps nothing.</summary>
    public static CallersIdentity Instance { get; } = new();

    /// <inheritdoc />
    public Guid? Find(Caller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return caller.UserId;
    }
}
