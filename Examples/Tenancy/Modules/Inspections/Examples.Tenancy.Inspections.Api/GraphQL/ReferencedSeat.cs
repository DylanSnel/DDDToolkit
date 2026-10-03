using Examples.Tenancy.Tenants.Contracts.ValueObjects;
using HotChocolate;
using HotChocolate.Types.Composite;

namespace Examples.Tenancy.Inspections.Api.GraphQL;

/// <summary>
/// A seat as Inspections knows it: its key, and nothing else. Its name is Tenancy's to give, and the gateway asks
/// Tenancy for it when a client asks for more than the id.
/// </summary>
/// <param name="Id">The seat.</param>
[GraphQLName("Seat")]
[EntityKey("id")]
internal sealed record ReferencedSeat(SeatId Id);
