using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// A <see cref="TenancyActor{TSeatId}"/> without its id type, for code that cannot name it: the save that fills
/// the columns of <c>RecordsWhoChanged</c>, and the accessor that tells an event log who acted.
/// </summary>
public interface ITenancyActor
{
    /// <summary>What kind of actor it is.</summary>
    TenancyActorKind Kind { get; }

    /// <summary>The seat, boxed: a seat's own, the seat a token stands for, or the seat system work is done for; <see langword="null"/> when there is none.</summary>
    object? SeatId { get; }

    /// <summary>An operator's verified identity; <see langword="null"/> for every other kind.</summary>
    Guid? Operator { get; }

    /// <summary>The scope of the application's own work; <see langword="null"/> for a seat.</summary>
    string? Scope { get; }

    /// <summary>The actor as the toolkit's own records keep it: a kind and one id.</summary>
    ActedBy ToActedBy();
}
