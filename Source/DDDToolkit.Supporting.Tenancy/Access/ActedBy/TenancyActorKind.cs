using System.Text.Json.Serialization;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// What kind of actor made a change Tenancy records. As JSON it is the word of <see cref="TenancyActorKinds"/>,
/// <c>seat</c>, <c>operator</c>, <c>system</c> or <c>token</c>: the word an event log and the columns of
/// <c>RecordsWhoChanged</c> keep as well (<see cref="TenancyActorKindJsonConverter"/>).
/// </summary>
[JsonConverter(typeof(TenancyActorKindJsonConverter))]
public enum TenancyActorKind
{
    /// <summary>A person, through their seat.</summary>
    Seat,

    /// <summary>An operator of the application, named by their verified identity: never a seat of any tenant.</summary>
    Operator,

    /// <summary>The application's own work, named by the scope it runs in.</summary>
    System,

    /// <summary>A link that carries a token a seat made: the seat it stands for.</summary>
    Token,
}
