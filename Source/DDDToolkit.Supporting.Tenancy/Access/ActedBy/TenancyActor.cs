using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Who made a change Tenancy records. Ids only: a seat, an operator's verified identity, or the scope of the
/// application's own work. Never a name, and never an address.
/// <para>
/// Make one with <see cref="OfSeat"/>, <see cref="OfOperator"/>, <see cref="OfSystem"/> or <see cref="OfToken"/>,
/// which give each kind what it is named by. The Tenancy caller carries the actor of the work it stands for
/// (<see cref="TenancyCaller{TTenantId, TSeatId}.Actor"/>), so nothing works one out for itself: a request's seat,
/// and what <see cref="TenancyWork"/> began on purpose.
/// </para>
/// <para>
/// Every domain event of Tenancy's carries one as <c>By</c>, the actor of the caller whose command raised it, so
/// an event that is stored, kept in an event log or published says who made the change without anything else
/// being read.
/// </para>
/// </summary>
/// <typeparam name="TSeatId">The application's seat id.</typeparam>
/// <param name="Kind">What kind of actor it is.</param>
/// <param name="Seat">
/// The seat: a seat's own, the seat a token stands for, or, for the application's own work, the seat it is done
/// for, which is not who acted.
/// </param>
/// <param name="Operator">An operator's verified identity.</param>
/// <param name="Scope">The scope the application's own work runs in.</param>
public readonly record struct TenancyActor<TSeatId>(
    [property: JsonConverter(typeof(TenancyActorKindJsonConverter))] TenancyActorKind Kind,
    TSeatId? Seat = null,
    Guid? Operator = null,
    string? Scope = null)
    : ITenancyActor
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
{
    object? ITenancyActor.SeatId => Seat;

    /// <summary>A person, through <paramref name="seat"/>.</summary>
    public static TenancyActor<TSeatId> OfSeat(TSeatId seat) => new(TenancyActorKind.Seat, seat);

    /// <summary>
    /// An operator of the application, by the verified identity they signed in with, in the scope of the work that
    /// carries out what they asked. An operator holds no seat, so there is none to name.
    /// </summary>
    /// <param name="identity">The operator's verified identity.</param>
    /// <param name="scope">The scope of the work that acts for them.</param>
    /// <exception cref="ArgumentException"><paramref name="identity"/> is empty, or <paramref name="scope"/> is blank.</exception>
    public static TenancyActor<TSeatId> OfOperator(Guid identity, string scope)
    {
        if (identity == Guid.Empty)
        {
            throw new ArgumentException("An operator is named by a verified identity, and the empty one is nobody's.", nameof(identity));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return new(TenancyActorKind.Operator, Operator: identity, Scope: scope);
    }

    /// <summary>The application's own work in <paramref name="scope"/>.</summary>
    /// <param name="scope">The scope it runs in.</param>
    /// <param name="forSeat">The seat it is done for, if any: recorded next to the scope, and not who acted.</param>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is blank.</exception>
    public static TenancyActor<TSeatId> OfSystem(string scope, TSeatId? forSeat = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return new(TenancyActorKind.System, forSeat, Scope: scope);
    }

    /// <summary>A link that carries a token <paramref name="seat"/> made, in the scope of the work that answers it.</summary>
    /// <param name="seat">The seat the token stands for.</param>
    /// <param name="scope">The scope of the work that answers the link.</param>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is blank.</exception>
    public static TenancyActor<TSeatId> OfToken(TSeatId seat, string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return new(TenancyActorKind.Token, seat, Scope: scope);
    }

    /// <summary>
    /// The toolkit's own <see cref="ActedBy"/>: <c>seat</c> with the seat's id, <c>operator</c> with the identity,
    /// <c>system</c> with the scope, and <c>token</c> with the seat's id. An id is written as its value, with the
    /// invariant culture and a <see cref="Guid"/> as <c>D</c>, which is how a database writes the same value as
    /// text: a policy can compare the two.
    /// </summary>
    public ActedBy ToActedBy() => Kind switch
    {
        TenancyActorKind.Seat or TenancyActorKind.Token => new(TenancyActorKinds.Of(Kind), Seat is { } seat ? EntityIdText.Of(seat) : null),
        TenancyActorKind.Operator => new(TenancyActorKinds.Operator, Operator?.ToString("D", CultureInfo.InvariantCulture)),
        _ => new(TenancyActorKinds.System, Scope),
    };
}

/// <summary>An id's value as text, written the same wherever it runs.</summary>
internal static class EntityIdText
{
    /// <summary>How to write the value of each id type, found once per type.</summary>
    private static readonly ConcurrentDictionary<Type, Func<object, string>> Writers = new();

    private static readonly MethodInfo WriterOfValue = typeof(EntityIdText).GetMethod(nameof(Write), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// The value <paramref name="id"/> wraps, with the invariant culture: a <see cref="Guid"/> as <c>D</c>, a number
    /// in digits. An id that wraps no single value is written as it writes itself.
    /// </summary>
    public static string Of(object id)
        => Writers.GetOrAdd(id.GetType(), static type =>
        {
            var wrapped = type.GetInterfaces()
                .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEntityId<>))
                .Select(candidate => candidate.GetGenericArguments()[0])
                .ToArray();

            return wrapped.Length == 1
                ? WriterOfValue.MakeGenericMethod(wrapped[0]).CreateDelegate<Func<object, string>>()
                : static boxed => boxed.ToString() ?? string.Empty;
        })(id);

    private static string Write<TValue>(object id)
        => ((IEntityId<TValue>)id).Value switch
        {
            null => string.Empty,
            Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            var other => other.ToString() ?? string.Empty,
        };
}
