namespace Examples.Tenancy.Tenants.Application.History;

/// <summary>
/// One row of a tenant's access history: an event that changed who may do what, with when it happened and who
/// made the change.
/// </summary>
/// <remarks>
/// It carries ids, keys and dates, and no name: who acted is a seat's id, which whoever shows the row asks the
/// directory about, and <see cref="Details"/> is the event as it was stored, which never holds what a tenant, a
/// unit, a seat or a role is called, or why a role was given. An operator is answered as the kind alone: the
/// identity a member of staff signs in with is not a tenant's to read, so the event's own word on who acted,
/// which carries it, is left out of <see cref="Details"/>.
/// </remarks>
/// <param name="Id">The event's id.</param>
/// <param name="Event">The event's stored name, such as <c>tenancy.organization-role-granted</c>.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="ByKind">What kind of actor made the change: <c>seat</c>, <c>operator</c>, <c>system</c> or <c>token</c>.</param>
/// <param name="BySeat">The seat that made it, when a seat did, or the seat a token stands for.</param>
/// <param name="Details">The event itself, as JSON: the ids, keys and dates it names, without who acted.</param>
public sealed record AccessHistoryEntry(Guid Id, string Event, DateTimeOffset OccurredAt, string ByKind, SeatId? BySeat, string Details);
