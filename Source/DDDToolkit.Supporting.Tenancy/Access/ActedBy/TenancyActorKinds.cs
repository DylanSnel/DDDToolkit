using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// The kinds Tenancy writes into a record that keeps who acted, next to the toolkit's own
/// (<see cref="ActedByKinds"/>): an event log's row, and the columns of <c>RecordsWhoChanged</c>.
/// </summary>
public static class TenancyActorKinds
{
    /// <summary>A person, through their seat; the id is the seat's.</summary>
    public const string Seat = "seat";

    /// <summary>An operator of the application; the id is the operator's verified identity.</summary>
    public const string Operator = "operator";

    /// <summary>The application's own work; the id is the scope it runs in. The toolkit's own kind.</summary>
    public const string System = ActedByKinds.System;

    /// <summary>A link that carries a token; the id is the seat the token stands for.</summary>
    public const string Token = "token";

    /// <summary>The kind as a record keeps it.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is none of the four.</exception>
    public static string Of(TenancyActorKind kind) => kind switch
    {
        TenancyActorKind.Seat => Seat,
        TenancyActorKind.Operator => Operator,
        TenancyActorKind.System => System,
        TenancyActorKind.Token => Token,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "An actor is a seat, an operator, the system or a token."),
    };
}
