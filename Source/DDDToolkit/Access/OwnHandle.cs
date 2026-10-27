using System.Collections.Concurrent;
using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Access;

/// <summary>
/// What says the caller's own value of one type, for a check that knows the type only as a <see cref="Type"/>: the owner
/// of an aggregate, whose type its storage says (<see cref="IAggregateOwners{TId}.OwnerType"/>). One per type, made the
/// first time it is asked for and kept.
/// </summary>
internal abstract class OwnHandle
{
    private static readonly ConcurrentDictionary<Type, OwnHandle?> Handles = new();

    /// <summary>
    /// The handle of <paramref name="type"/>, or <see langword="null"/> for a type no <see cref="ICallersOwn{TValue}"/> can
    /// be of: one that is no struct, an enum, a struct that is not <see cref="IEquatable{T}"/> of itself, or a nullable one.
    /// </summary>
    /// <param name="type">The type of the caller's own.</param>
    public static OwnHandle? Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return Handles.GetOrAdd(type, static each => each is { IsValueType: true, IsEnum: false }
                && Nullable.GetUnderlyingType(each) is null
                && typeof(IEquatable<>).MakeGenericType(each).IsAssignableFrom(each)
            ? (OwnHandle)Activator.CreateInstance(typeof(OwnHandle<>).MakeGenericType(each))!
            : null);
    }

    /// <summary>Whether something says what the caller's own value of the type is.</summary>
    /// <param name="services">The scope's services, where a handle is registered; <see langword="null"/> for none.</param>
    public abstract bool CanBeAnswered(IServiceProvider? services);

    /// <summary>The caller's own value of the type, boxed, or <see langword="null"/> where it has none.</summary>
    /// <param name="user">Who is calling.</param>
    /// <param name="services">The scope's services, where a handle is registered; <see langword="null"/> for none.</param>
    /// <exception cref="InvalidOperationException">Nothing says what the caller's own value of the type is.</exception>
    public abstract object? Find(ICurrentUser user, IServiceProvider? services);
}

/// <summary>The handle of <typeparamref name="TValue"/>: the <see cref="ICallersOwn{TValue}"/> registered for it, or the core's for a <see cref="Guid"/>.</summary>
/// <typeparam name="TValue">The type of the caller's own.</typeparam>
internal sealed class OwnHandle<TValue> : OwnHandle
    where TValue : struct, IEquatable<TValue>
{
    /// <summary>
    /// What says the caller's own <typeparamref name="TValue"/>: the one registered for it, or, for a <see cref="Guid"/>,
    /// the caller's verified identity; <see langword="null"/> where nothing does.
    /// </summary>
    /// <param name="services">The scope's services, where a handle is registered; <see langword="null"/> for none.</param>
    public static ICallersOwn<TValue>? In(IServiceProvider? services)
        => (ICallersOwn<TValue>?)services?.GetService(typeof(ICallersOwn<TValue>))
           ?? (typeof(TValue) == typeof(Guid) ? (ICallersOwn<TValue>)(object)CallersIdentity.Instance : null);

    /// <inheritdoc />
    public override bool CanBeAnswered(IServiceProvider? services) => In(services) is not null;

    /// <inheritdoc />
    public override object? Find(ICurrentUser user, IServiceProvider? services)
        => (In(services) ?? throw new InvalidOperationException(
                $"Nothing says what the caller's own {WrittenTypeNames.Of(typeof(TValue))} is. Register an ICallersOwn<{WrittenTypeNames.Of(typeof(TValue))}> with services.AddCallersOwn<{WrittenTypeNames.Of(typeof(TValue))}, TCallersOwn>(); a requirement nothing checks lets nobody through."))
            .Find(user);
}
