using DDDToolkit.Abstractions.Attributes;

namespace Examples.Tenancy.Tenants.Contracts.ValueObjects;

/// <summary>
/// Who changed a row last, as the row itself keeps it: the save that wrote it filled it in from the caller it ran
/// as, and the database holds a caller to itself there. A value object: two answers with the same
/// kind and seat are the same answer.
/// </summary>
/// <remarks>
/// Declared once, here, because every module that keeps who changed its rows answers the same thing, and what it
/// names is Tenancy's: a kind of actor and a seat. A project says it of its row, an inspection of the row that
/// recorded it. In GraphQL a value object is shareable by itself, so each module's schema shows the one type
/// <c>ChangedBy</c> and the gateway composes them without either saying more.
/// <para>
/// An id and never a name: what a seat is called is Tenancy's to say, by id. An operator is answered as the kind
/// alone: the work was carried out for a member of the application's own staff, whose identity is not a tenant's
/// to read.
/// </para>
/// </remarks>
/// <param name="Kind">What kind of actor it was: <c>seat</c>, <c>operator</c>, <c>system</c> or <c>token</c>.</param>
/// <param name="Seat">The seat, when a seat changed it.</param>
[ValueObject]
public partial record ChangedBy(string Kind, SeatId? Seat);
